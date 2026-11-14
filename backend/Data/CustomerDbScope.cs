using System.Data;
using argus.Server.Data.Entities;
using argus.Server.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace argus.Server.Data;

/// <summary>
/// The sole factory for customer-facing finance database units. One explicit
/// transaction pins the connection; admission locks the customer then the
/// session and binds transaction-local authority before any protected work.
/// </summary>
public sealed class CustomerDbScope : IAsyncDisposable
{
    private const string CustomerLockShared = """
        SELECT * FROM customers
        WHERE id = {0} AND email_verified_at IS NOT NULL AND subscriber_activated_at IS NOT NULL
        FOR SHARE
        """;

    private const string CustomerLockExclusive = """
        SELECT * FROM customers
        WHERE id = {0} AND email_verified_at IS NOT NULL AND subscriber_activated_at IS NOT NULL
        FOR UPDATE
        """;

    private const string SessionLock = """
        SELECT * FROM sessions
        WHERE customer_id = {0}
          AND token_hash = {1}
          AND revoked_at IS NULL
          AND expires_at > clock_timestamp()
          AND last_seen_at > clock_timestamp() - INTERVAL '30 minutes'
        FOR SHARE
        """;

    private readonly FinanceAuthority _authority;
    private readonly ArgusDbContext _context;
    private readonly IDbContextTransaction _transaction;
    private bool _committed;

    private CustomerDbScope(FinanceAuthority authority, ArgusDbContext context, IDbContextTransaction transaction)
    {
        _authority = authority;
        _context = context;
        _transaction = transaction;
    }

    public ArgusDbContext Context => _context;

    public FinanceAuthority Authority => _authority;

    public static Task<CustomerDbScope> OpenSharedAsync(IDbContextFactory<ArgusDbContext> contextFactory,
                                                        FinanceAuthority authority,
                                                        CancellationToken cancellationToken = default) =>
        OpenAsync(contextFactory, authority, IsolationLevel.ReadCommitted, exclusiveCustomerLock: false, workspaceTimeouts: false, cancellationToken);

    public static Task<CustomerDbScope> OpenExclusiveAsync(IDbContextFactory<ArgusDbContext> contextFactory,
                                                           FinanceAuthority authority,
                                                           CancellationToken cancellationToken = default) =>
        OpenAsync(contextFactory, authority, IsolationLevel.ReadCommitted, exclusiveCustomerLock: true, workspaceTimeouts: false, cancellationToken);

    public static async Task<CustomerDbScope> OpenAsync(IDbContextFactory<ArgusDbContext> contextFactory,
                                                        FinanceAuthority authority,
                                                        IsolationLevel isolationLevel,
                                                        bool exclusiveCustomerLock,
                                                        bool workspaceTimeouts,
                                                        CancellationToken cancellationToken = default)
    {
        ArgusDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = isolationLevel == IsolationLevel.ReadCommitted
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : await context.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
            var scope = new CustomerDbScope(authority, context, transaction);
            await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5000ms'", cancellationToken);
            await scope.AdmitAsync(exclusiveCustomerLock, cancellationToken);
            if (workspaceTimeouts)
            {
                await scope.SetWorkspaceTimeoutsAsync(cancellationToken);
            }
            else
            {
                await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = DEFAULT", cancellationToken);
            }

            return scope;
        }
        catch
        {
            Exception? cleanupFailure = null;
            if (transaction is not null)
            {
                try
                {
                    await transaction.DisposeAsync();
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }
            }

            try
            {
                await context.DisposeAsync();
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
            }

            if (cleanupFailure is not null)
            {
                throw new InvalidOperationException("customer database scope cleanup failed", cleanupFailure);
            }

            throw;
        }
    }

    public async Task SetWorkspaceTimeoutsAsync(CancellationToken cancellationToken = default) =>
        await _context.Database.ExecuteSqlRawAsync(
            $"SET LOCAL lock_timeout = '{TransactionLimits.LockTimeoutSeconds}s'; SET LOCAL statement_timeout = '{TransactionLimits.StatementTimeoutSeconds}s';",
            cancellationToken);

    /// <summary>
    /// Fresh wall-clock admission immediately before commit. The row locks
    /// serialize revocation/reset with admitted work; a time-driven expiry can
    /// still refuse this final check.
    /// </summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        bool valid = await _context.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1
                FROM customers c
                JOIN sessions s ON s.customer_id = c.id
                WHERE c.id = {_authority.CustomerId}
                  AND s.token_hash = {_authority.SessionHash}
                  AND s.revoked_at IS NULL
                  AND s.expires_at > clock_timestamp()
                  AND s.last_seen_at > clock_timestamp() - INTERVAL '30 minutes'
                  AND c.email_verified_at IS NOT NULL
                  AND c.subscriber_activated_at IS NOT NULL
            ) AS "Value"
            """).SingleAsync(cancellationToken);
        if (!valid)
        {
            await _transaction.RollbackAsync(cancellationToken);
            _committed = true;
            throw new FinanceAuthorityException();
        }

        await _transaction.CommitAsync(cancellationToken);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        Exception? failure = null;
        if (!_committed)
        {
            try
            {
                await _transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        try
        {
            await _transaction.DisposeAsync();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        try
        {
            await _context.DisposeAsync();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        if (failure is not null)
        {
            throw new InvalidOperationException("customer database scope cleanup failed", failure);
        }
    }

    private async Task AdmitAsync(bool exclusiveCustomerLock, CancellationToken cancellationToken)
    {
        string customerSql = exclusiveCustomerLock ? CustomerLockExclusive : CustomerLockShared;
        List<Customer> customers = await _context.Customers
            .FromSqlRaw(customerSql, _authority.CustomerId)
            .AsTracking()
            .ToListAsync(cancellationToken);
        if (customers.Count != 1)
        {
            throw new FinanceAuthorityException();
        }

        List<Session> sessions = await _context.Sessions
            .FromSqlRaw(SessionLock, _authority.CustomerId, _authority.SessionHash)
            .AsTracking()
            .ToListAsync(cancellationToken);
        if (sessions.Count != 1 || sessions[0].CustomerId != _authority.CustomerId)
        {
            throw new FinanceAuthorityException();
        }

        string customerText = _authority.CustomerId.ToString("D");
        await _context.Database
            .SqlQuery<string>($"SELECT set_config('argus.customer_id', {customerText}, true) AS \"Value\"")
            .SingleAsync(cancellationToken);
        await _context.Database
            .SqlQuery<string>($"SELECT set_config('argus.session_hash', {_authority.SessionHash}, true) AS \"Value\"")
            .SingleAsync(cancellationToken);
        await _context.Database
            .SqlQueryRaw<string>("SELECT set_config('argus.transaction_id', pg_current_xact_id()::text, true) AS \"Value\"")
            .SingleAsync(cancellationToken);

        string? helper = await _context.Database
            .SqlQueryRaw<string?>("SELECT argus_current_customer_id()::text AS \"Value\"")
            .SingleAsync(cancellationToken);
        if (!Guid.TryParse(helper, out Guid admitted) || admitted != _authority.CustomerId)
        {
            throw new FinanceAuthorityException();
        }
    }
}
