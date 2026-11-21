using argus.Server.Data;
using argus.Server.Reporting;
using argus.Server.Security;
using argus.Server.Transactions;
using Npgsql;

namespace argus.Server.Http;

/// <summary>
/// Guarded reporting transport. The strictly allowlisted body is bounded and
/// measured; every response is no-store and every failure is sanitized.
/// </summary>
public sealed class ReportApi(FinanceGuard guard,
                               ReportStore store,
                               RateLimiter rateLimiter,
                               ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("finance");

    public void Map(WebApplication app)
    {
        app.MapPost("/v1/finance/reports/query", QueryAsync);
    }

    private async Task<IResult> QueryAsync(HttpContext context, CancellationToken cancellationToken) =>
        await GuardedAsync(async () =>
        {
            (FinanceAuthority? authority, IResult? failure) = await guard.AuthenticateAsync(context, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            if (!rateLimiter.CheckReportRead(authority!.CustomerId))
            {
                return ReportError(ReportErrorCodes.RateLimited);
            }

            (ReportQueryRequest? request, IResult? bodyFailure) = await TransactionApi.ReadBodyAsync<ReportQueryRequest>(
                context.Request, ReportingLimits.ReportBodyBytes, cancellationToken);
            if (bodyFailure is not null)
            {
                return bodyFailure;
            }

            if (!ReportRequestParser.TryParseReportQuery(request, out ReportQueryPlan plan))
            {
                return Errors.InvalidRequest();
            }

            ReportResponse response = await store.QueryAsync(authority, plan, cancellationToken);
            return Results.Json(response);
        }, "reports_query");

    private async Task<IResult> GuardedAsync(Func<Task<IResult>> action, string operation)
    {
        try
        {
            return await action();
        }
        catch (ReportStoreException exception)
        {
            return ReportError(exception.Code);
        }
        catch (TransactionStoreException exception) when (exception.Code == TransactionErrorCodes.NotFound)
        {
            return ReportError(ReportErrorCodes.NotFound);
        }
        catch (FinanceAuthorityException)
        {
            return Errors.Unauthorized();
        }
        catch (PostgresException exception) when (IsRetryable(exception))
        {
            _logger.LogWarning("finance_operation_failed operation={Operation} error_class={ErrorClass} sql_state={SqlState}", operation, "postgres_retryable", exception.SqlState);
            return ReportError(ReportErrorCodes.ReportBusy);
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

    private static IResult ReportError(string code) =>
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
