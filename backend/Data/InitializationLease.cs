using Microsoft.EntityFrameworkCore;

namespace argus.Server.Data;

/// <summary>
/// One explicitly opened, non-pooled, non-multiplexed EF session that owns the
/// fixed session advisory lock for the complete initialization protocol. The
/// physical session must not be replaced while protected work is in progress.
/// </summary>
public sealed class InitializationLease : IAsyncDisposable, IDisposable
{
    private readonly InitializationConnectionInterceptor _connectionInterceptor;
    private readonly IInitializationFaultInjector _faultInjector;
    private bool _disposed;

    private InitializationLease(ArgusDbContext context,
                                InitializationConnectionInterceptor connectionInterceptor,
                                InitializationTransactionInterceptor transactionInterceptor,
                                int backendPid,
                                IInitializationFaultInjector faultInjector)
    {
        Context = context;
        _connectionInterceptor = connectionInterceptor;
        TransactionInterceptor = transactionInterceptor;
        BackendPid = backendPid;
        _faultInjector = faultInjector;
    }

    public ArgusDbContext Context { get; }

    public InitializationTransactionInterceptor TransactionInterceptor { get; }

    public int BackendPid { get; }

    public Exception? CleanupError { get; private set; }

    public static async Task<InitializationLease> AcquireAsync(string databaseUrl,
                                                               IInitializationFaultInjector faultInjector,
                                                               CancellationToken cancellationToken)
    {
        var connectionInterceptor = new InitializationConnectionInterceptor();
        var transactionInterceptor = new InitializationTransactionInterceptor(faultInjector);
        var options = PersistenceOptions.Build(
            databaseUrl,
            initializer: true,
            interceptors: [connectionInterceptor, transactionInterceptor]);
        var context = new ArgusDbContext(options);
        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
            connectionInterceptor.LeaseAcquired = true;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int acquired = await context.Database
                    .SqlQueryRaw<int>(
                        "SELECT CASE WHEN pg_try_advisory_lock({0}, {1}) THEN 1 ELSE 0 END AS \"Value\"",
                        InitializationCoordinator.LockKey1,
                        InitializationCoordinator.LockKey2)
                    .SingleAsync(cancellationToken);
                await faultInjector.AfterLockAcquireAttemptAsync(acquired == 1, cancellationToken);
                if (acquired == 1)
                {
                    break;
                }

                await Task.Delay(100, cancellationToken);
            }

            int backendPid = await context.Database
                .SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"")
                .SingleAsync(cancellationToken);
            await faultInjector.AfterLeaseAcquiredAsync(backendPid, cancellationToken);

            return new InitializationLease(context, connectionInterceptor, transactionInterceptor, backendPid, faultInjector);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async Task<bool> IsLockHeldAsync(CancellationToken cancellationToken)
    {
        int count = await Context.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND classid = {0}::oid AND objid = {1}::oid AND objsubid = 2 AND pid = pg_backend_pid()",
                InitializationCoordinator.LockKey1,
                InitializationCoordinator.LockKey2)
            .SingleAsync(cancellationToken);

        return count == 1;
    }

    public async Task<int> CurrentBackendPidAsync(CancellationToken cancellationToken) =>
        await Context.Database
            .SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync(cancellationToken);

    public bool PhysicalConnectionClosed => _connectionInterceptor.PhysicalConnectionClosed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            if (!PhysicalConnectionClosed)
            {
                await _faultInjector.BeforeUnlockAsync(cleanup.Token);
                int unlocked = await Context.Database
                    .SqlQueryRaw<int>(
                        "SELECT CASE WHEN pg_advisory_unlock({0}, {1}) THEN 1 ELSE 0 END AS \"Value\"",
                        InitializationCoordinator.LockKey1,
                        InitializationCoordinator.LockKey2)
                    .SingleAsync(cleanup.Token);
                if (unlocked != 1)
                {
                    CleanupError = new InvalidOperationException("initialization advisory lock was not held during cleanup");
                }
            }
        }
        catch (Exception exception)
        {
            CleanupError = exception;
        }
        finally
        {
            await Context.DisposeAsync();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
