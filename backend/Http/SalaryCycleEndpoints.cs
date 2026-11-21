using argus.Server.Data;
using argus.Server.Reporting;
using argus.Server.Security;
using Npgsql;

namespace argus.Server.Http;

/// <summary>
/// Guarded salary-cycle reporting-date transport. Bodies are bounded and
/// measured even without Content-Length; no response is cached and no private
/// receipt search enters a URL.
/// </summary>
public sealed class SalaryCycleApi(FinanceGuard guard,
                                    SalaryCycleStore store,
                                    RateLimiter rateLimiter,
                                    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("finance");

    public void Map(WebApplication app)
    {
        app.MapGet("/v1/finance/salary-cycles", ListAsync);
        app.MapPost("/v1/finance/salary-cycles/preview", PreviewAsync);
        app.MapPost("/v1/finance/salary-cycles", ConfirmAsync);
        app.MapDelete("/v1/finance/salary-cycles/{id:guid}", RemoveAsync);
    }

    private async Task<IResult> ListAsync(HttpContext context, CancellationToken cancellationToken) =>
        await GuardedAsync(async () =>
        {
            (FinanceAuthority? authority, IResult? failure) = await guard.AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            if (!rateLimiter.CheckReportRead(authority!.CustomerId))
            {
                return SalaryError(ReportErrorCodes.RateLimited);
            }

            DateOnly? beforeDate = null;
            foreach (var key in context.Request.Query.Keys)
            {
                if (!string.Equals(key, "before_date", StringComparison.Ordinal) ||
                    context.Request.Query[key].Count != 1)
                {
                    return Errors.InvalidRequest();
                }
            }

            if (context.Request.Query.TryGetValue("before_date", out var values))
            {
                string? raw = values.SingleOrDefault();
                if (string.IsNullOrEmpty(raw) || !ReportRequestParser.TryParseDate(raw, out beforeDate) || beforeDate is null)
                {
                    return Errors.InvalidRequest();
                }
            }

            SalaryCycleListResponse result = await store.ListAsync(authority, beforeDate, cancellationToken);
            return Results.Json(result);
        }, "salary_cycles_list");

    private async Task<IResult> PreviewAsync(HttpContext context, CancellationToken cancellationToken) =>
        await GuardedAsync(async () =>
        {
            (FinanceAuthority? authority, IResult? failure) = await guard.AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            if (!rateLimiter.CheckSalaryMutation(authority!.CustomerId))
            {
                return SalaryError(ReportErrorCodes.RateLimited);
            }

            (SalaryCyclePreviewRequest? request, IResult? bodyFailure) = await TransactionApi.ReadBodyAsync<SalaryCyclePreviewRequest>(
                context.Request, ReportingLimits.SalaryBodyBytes, cancellationToken);
            if (bodyFailure is not null)
            {
                return bodyFailure;
            }

            SalaryCyclePreviewResponse result = await store.PreviewAsync(authority, request!, cancellationToken);
            return Results.Json(result);
        }, "salary_cycles_preview");

    private async Task<IResult> ConfirmAsync(HttpContext context, CancellationToken cancellationToken) =>
        await GuardedAsync(async () =>
        {
            (FinanceAuthority? authority, IResult? failure) = await guard.AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            if (!rateLimiter.CheckSalaryMutation(authority!.CustomerId))
            {
                return SalaryError(ReportErrorCodes.RateLimited);
            }

            (SalaryCycleConfirmRequest? request, IResult? bodyFailure) = await TransactionApi.ReadBodyAsync<SalaryCycleConfirmRequest>(
                context.Request, ReportingLimits.SalaryBodyBytes, cancellationToken);
            if (bodyFailure is not null)
            {
                return bodyFailure;
            }

            SalaryCycleConfirmResponse result = await store.ConfirmAsync(authority, request!, cancellationToken);
            return Results.Json(result);
        }, "salary_cycles_confirm");

    private async Task<IResult> RemoveAsync(HttpContext context, CancellationToken cancellationToken) =>
        await GuardedAsync(async () =>
        {
            (FinanceAuthority? authority, IResult? failure) = await guard.AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            if (!rateLimiter.CheckSalaryMutation(authority!.CustomerId))
            {
                return SalaryError(ReportErrorCodes.RateLimited);
            }

            if (!context.Request.RouteValues.TryGetValue("id", out object? value) ||
                !Guid.TryParse(value?.ToString(), out Guid id))
            {
                return Errors.NotFound();
            }

            (SalaryCycleRemoveRequest? request, IResult? bodyFailure) = await TransactionApi.ReadBodyAsync<SalaryCycleRemoveRequest>(
                context.Request, ReportingLimits.SalaryBodyBytes, cancellationToken);
            if (bodyFailure is not null)
            {
                return bodyFailure;
            }

            SalaryCycleRemoveResponse result = await store.RemoveAsync(authority, id, request!, cancellationToken);
            return Results.Json(result);
        }, "salary_cycles_remove");

    private async Task<IResult> GuardedAsync(Func<Task<IResult>> action, string operation)
    {
        try
        {
            return await action();
        }
        catch (SalaryCycleStoreException exception)
        {
            return SalaryError(exception.Code);
        }
        catch (FinanceAuthorityException)
        {
            return Errors.Unauthorized();
        }
        catch (PostgresException exception) when (IsRetryable(exception))
        {
            _logger.LogWarning("finance_operation_failed operation={Operation} error_class={ErrorClass} sql_state={SqlState}", operation, "postgres_retryable", exception.SqlState);
            return SalaryError(ReportErrorCodes.ReportBusy);
        }
        catch (PostgresException exception)
        {
            _logger.LogError("finance_operation_failed operation={Operation} error_class={ErrorClass} sql_state={SqlState}", operation, "postgres", exception.SqlState);
            return Errors.Internal();
        }
        catch (Exception exception) when (PersistenceErrors.FindPostgresException(exception) is { } postgres)
        {
            _logger.LogError("finance_operation_failed operation={Operation} error_class={ErrorClass} sql_state={SqlState}", operation, "postgres", postgres.SqlState);
            return Errors.Internal();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "finance_operation_failed operation={Operation} error_class={ErrorClass}", operation, exception.GetType().Name);
            return Errors.Internal();
        }
    }

    private static bool IsRetryable(PostgresException exception) =>
        exception.SqlState is
            PostgresErrorCodes.SerializationFailure or
            PostgresErrorCodes.DeadlockDetected or
            PostgresErrorCodes.LockNotAvailable or
            PostgresErrorCodes.QueryCanceled;

    private static IResult SalaryError(string code) =>
        Results.Json(new { error = code }, statusCode: StatusFor(code));

    private static int StatusFor(string code) => code switch
    {
        ReportErrorCodes.NotFound => 404,
        ReportErrorCodes.RequestTooLarge => 413,
        ReportErrorCodes.ReceiptIneligible => 422,
        ReportErrorCodes.VersionConflict or
            ReportErrorCodes.CycleSequenceChanged or
            ReportErrorCodes.ReportBusy => 409,
        ReportErrorCodes.RateLimited => 429,
        ReportErrorCodes.Unauthorized => 401,
        _ => 400,
    };
}
