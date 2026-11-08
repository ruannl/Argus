using argus.Server.Configuration;
using argus.Server.Data;
using argus.Server.Security;
using Npgsql;

namespace argus.Server.Http;

/// <summary>
/// The single shared finance/import guard. It proves the trusted frontend
/// caller, then independently authorizes the acting user's session, then
/// applies the dedicated finance rate-limit bucket. Import routes reuse this
/// exact chain so their authority cannot drift from the entity surface.
/// </summary>
public sealed class FinanceGuard(AppSettings settings, Store store, RateLimiter rateLimiter)
{
    public async Task<(Guid? CustomerId, IResult? Failure)> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!Trust.OriginValid(context, settings))
        {
            return (null, Errors.OriginRejected());
        }

        if (!Trust.ApplicationSecretValid(context, settings))
        {
            return (null, Errors.Unauthorized());
        }

        string? rawToken = Trust.SessionToken(context);
        if (rawToken is null)
        {
            return (null, Errors.Unauthorized());
        }

        var customerId = await store.ValidateAndTouchSessionAsync(Tokens.HashToken(rawToken), cancellationToken);
        if (customerId is null)
        {
            return (null, Errors.Unauthorized());
        }

        var customer = await store.FindCustomerByIdAsync(customerId.Value, cancellationToken);
        if (customer is null || customer.EmailVerifiedAt is null || customer.SubscriberActivatedAt is null)
        {
            return (null, Errors.Unauthorized());
        }

        string? peerIp = context.Connection.RemoteIpAddress?.ToString();
        if (peerIp is not null && !rateLimiter.CheckFinanceIp(peerIp))
        {
            return (null, Errors.RateLimited());
        }

        return (customerId, null);
    }

    /// <summary>
    /// Converts an unexpected persistence failure into a sanitized log line
    /// plus the frozen 500 body. PostgreSQL exceptions are never logged whole:
    /// their Detail can echo user-supplied names or financial values.
    /// </summary>
    public static async Task<IResult> GuardedAsync(Func<Task<IResult>> action, string operation, ILogger logger)
    {
        try
        {
            return await action();
        }
        catch (PostgresException exception)
        {
            logger.LogError("finance_operation_failed operation={Operation} error_class={ErrorClass} sql_state={SqlState}", operation, "postgres", exception.SqlState);
            return Errors.Internal();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "finance_operation_failed operation={Operation} error_class={ErrorClass}", operation, exception.GetType().Name);
            return Errors.Internal();
        }
    }
}
