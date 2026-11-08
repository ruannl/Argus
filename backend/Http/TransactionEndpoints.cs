using argus.Server.Data;
using argus.Server.Security;
using argus.Server.Transactions;
using Npgsql;

namespace argus.Server.Http;

/// <summary>
/// Guarded transaction workspace transport. Search text only ever arrives in a
/// bounded request body; responses are no-store at the application middleware.
/// </summary>
public sealed class TransactionApi(FinanceGuard guard,
                                   TransactionStore store,
                                   RateLimiter rateLimiter,
                                   ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("finance");

    public void Map(WebApplication app)
    {
        app.MapPost("/v1/finance/transactions/query", QueryAsync);
        app.MapPatch("/v1/finance/transactions/{id:guid}", PatchAsync);
        app.MapGet("/v1/finance/transactions/{id:guid}", GetAsync);

        app.MapPost("/v1/finance/transactions/operations/prepare", PrepareAsync);
        app.MapGet("/v1/finance/transactions/operations/{id:guid}", StatusAsync);
        app.MapPost("/v1/finance/transactions/operations/{id:guid}/apply", ApplyAsync);
        app.MapPost("/v1/finance/transactions/operations/{id:guid}/abandon", AbandonAsync);
    }

    private Task<(Guid? CustomerId, IResult? Failure)> AuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken) =>
        guard.AuthenticateAsync(context, cancellationToken);

    private async Task<IResult> GuardedAsync(Func<Task<IResult>> action, string operation)
    {
        try
        {
            return await action();
        }
        catch (TransactionStoreException exception)
        {
            return TransactionError(exception);
        }
        catch (PostgresException exception) when (IsRetryable(exception))
        {
            _logger.LogWarning(
                "finance_operation_failed operation={Operation} error_class={ErrorClass} sql_state={SqlState}",
                operation, "postgres_retryable", exception.SqlState);
            return TransactionError(TransactionErrorCodes.OperationBusy);
        }
        catch (PostgresException exception)
        {
            _logger.LogError(
                "finance_operation_failed operation={Operation} error_class={ErrorClass} sql_state={SqlState}",
                operation, "postgres", exception.SqlState);
            return Errors.Internal();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "finance_operation_failed operation={Operation} error_class={ErrorClass}",
                operation, exception.GetType().Name);
            return Errors.Internal();
        }
    }

    private static bool IsRetryable(PostgresException exception) =>
        exception.SqlState is
            PostgresErrorCodes.SerializationFailure or
            PostgresErrorCodes.DeadlockDetected or
            PostgresErrorCodes.LockNotAvailable or
            PostgresErrorCodes.QueryCanceled;

    // --- Reads -------------------------------------------------------------

    private Task<IResult> QueryAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var (request, bodyFailure) = await ReadBodyAsync<TransactionQueryRequest>(
                context.Request, TransactionLimits.QueryBodyBytes, cancellationToken);
            if (bodyFailure is not null)
            {
                return bodyFailure;
            }

            if (!TransactionRequestParser.TryParseQuery(
                    request!, out var plan, out _))
            {
                return Errors.InvalidRequest();
            }

            var page = await store.QueryAsync(customerId!.Value, plan, cancellationToken);
            return Results.Json(page);
        }, "transactions_query");

    private Task<IResult> GetAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var detail = await store.GetAsync(customerId!.Value, id.Value, cancellationToken);
            return Results.Json(detail);
        }, "transactions_detail");

    // --- Mutations and operations ------------------------------------------

    private Task<IResult> PatchAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var (request, bodyFailure) = await ReadBodyAsync<TransactionPatchRequest>(
                context.Request, TransactionLimits.MutationBodyBytes, cancellationToken);
            if (bodyFailure is not null)
            {
                return bodyFailure;
            }

            if (request!.ExpectedVersion is null || request.ExpectedVersion < 1 ||
                !TransactionRequestParser.TryParseAction(request.Changes, out var action))
            {
                return Errors.InvalidRequest();
            }
            if (action.IsHiddenByUser is not null && request.Confirm != true)
            {
                return Errors.InvalidRequest();
            }

            var result = await store.PatchAsync(
                customerId!.Value, id.Value, request.ExpectedVersion.Value, action,
                cancellationToken);
            return Results.Json(result);
        }, "transactions_patch");

    private Task<IResult> PrepareAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }
            if (!rateLimiter.CheckTransactionPreparation(customerId!.Value))
            {
                return TransactionError(TransactionErrorCodes.OperationQuota);
            }

            var (request, bodyFailure) = await ReadBodyAsync<TransactionPrepareRequest>(
                context.Request, TransactionLimits.MutationBodyBytes, cancellationToken);
            if (bodyFailure is not null)
            {
                return bodyFailure;
            }

            if (request!.OperationId is null || request.OperationId == Guid.Empty ||
                !TransactionRequestParser.TryParseSelection(request.Selection, out var selection) ||
                !TransactionRequestParser.TryParseAction(request.Changes, out var action))
            {
                return Errors.InvalidRequest();
            }

            var result = await store.PrepareAsync(
                customerId!.Value, request.OperationId.Value, selection, action,
                cancellationToken);
            return Results.Json(result, statusCode: result.Created ? 201 : 200);
        }, "transactions_prepare");

    private Task<IResult> StatusAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var result = await store.StatusAsync(customerId!.Value, id.Value, cancellationToken);
            return Results.Json(result);
        }, "transactions_operation_status");

    private Task<IResult> ApplyAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var (request, bodyFailure) = await ReadBodyAsync<TransactionApplyRequest>(
                context.Request, TransactionLimits.MutationBodyBytes, cancellationToken);
            if (bodyFailure is not null)
            {
                return bodyFailure;
            }
            if (request!.Confirm != true ||
                string.IsNullOrWhiteSpace(request.ConfirmationHash) ||
                request.ExpectedSelectedCount is null ||
                request.ExpectedSelectedCount < 0)
            {
                return Errors.InvalidRequest();
            }

            var result = await store.ApplyAsync(
                customerId!.Value,
                id.Value,
                request.ConfirmationHash,
                request.ExpectedSelectedCount.Value,
                cancellationToken);
            return Results.Json(result);
        }, "transactions_operation_apply");

    private Task<IResult> AbandonAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            var id = RouteGuid(context);
            if (id is null)
            {
                return Errors.NotFound();
            }

            var (_, bodyFailure) = await ReadBodyAsync<EmptyBodyRequest>(
                context.Request, TransactionLimits.MutationBodyBytes, cancellationToken);
            if (bodyFailure is not null)
            {
                return bodyFailure;
            }

            var result = await store.AbandonAsync(customerId!.Value, id.Value, cancellationToken);
            return Results.Json(result);
        }, "transactions_operation_abandon");

    // --- Helpers -----------------------------------------------------------

    private static Guid? RouteGuid(HttpContext context) =>
        context.Request.RouteValues.TryGetValue("id", out var value) &&
        Guid.TryParse(value?.ToString(), out var id)
            ? id
            : null;

    internal static async Task<(T? Value, IResult? Failure)> ReadBodyAsync<T>(
        HttpRequest request,
        int maxBytes,
        CancellationToken cancellationToken)
        where T : class
    {
        if (request.ContentLength is > 0 && request.ContentLength > maxBytes)
        {
            return (null, TransactionError(TransactionErrorCodes.RequestTooLarge));
        }

        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > maxBytes)
            {
                return (null, TransactionError(TransactionErrorCodes.RequestTooLarge));
            }
            output.Write(buffer, 0, read);
        }

        if (output.Length == 0)
        {
            return (null, Errors.InvalidRequest());
        }

        var parsed = JsonBody.ParseBytes<T>(output.ToArray());
        if (!parsed.Parsed)
        {
            return (null, Errors.InvalidRequest());
        }
        return (parsed.Value, null);
    }

    internal static IResult TransactionError(TransactionStoreException exception)
    {
        var payload = new Dictionary<string, object?> { ["error"] = exception.Code };
        foreach (var (key, value) in exception.Details)
        {
            payload[key] = value;
        }
        return Results.Json(payload, statusCode: StatusFor(exception.Code));
    }

    internal static IResult TransactionError(string code) => Results.Json(
        new { error = code },
        statusCode: StatusFor(code));

    private static int StatusFor(string code) => code switch
    {
        TransactionErrorCodes.NotFound => 404,
        TransactionErrorCodes.RequestTooLarge => 413,
        TransactionErrorCodes.OperationExpired or
            TransactionErrorCodes.OperationAbandoned => 410,
        TransactionErrorCodes.VersionConflict or
            TransactionErrorCodes.SelectionStale or
            TransactionErrorCodes.ClassificationChanged or
            TransactionErrorCodes.ParentHidden or
            TransactionErrorCodes.IdempotencyConflict or
            TransactionErrorCodes.OperationBusy => 409,
        TransactionErrorCodes.OperationQuota or
            TransactionErrorCodes.RateLimited => 429,
        _ => 400,
    };
}
