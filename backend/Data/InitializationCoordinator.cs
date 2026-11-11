using Microsoft.EntityFrameworkCore;

namespace argus.Server.Data;

/// <summary>
/// The only production schema/data apply entry. The asynchronous path serves
/// normal startup and the explicit seed command; the synchronous path serves
/// the internal migration-only command. Both execute the same ordered protocol.
/// </summary>
public sealed class InitializationCoordinator(string databaseUrl, IInitializationFaultInjector faultInjector)
{
    public const int LockKey1 = 0x41524755;
    public const int LockKey2 = 0x53494E49;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        InitializationLease? lease = null;
        Exception? failure = null;
        try
        {
            lease = await InitializationLease.AcquireAsync(databaseUrl, faultInjector, cancellationToken);

            await SchemaValidator.ValidateAsync(lease.Context, requireLedgers: false, cancellationToken);

            lease.TransactionInterceptor.BarriersEnabled = true;
            try
            {
                await lease.Context.Database.MigrateAsync(cancellationToken);
            }
            finally
            {
                lease.TransactionInterceptor.BarriersEnabled = false;
            }

            if (lease.PhysicalConnectionClosed)
            {
                throw new InvalidOperationException("initialization physical session closed during migration");
            }

            await SchemaValidator.ValidateAsync(lease.Context, requireLedgers: true, cancellationToken);
            await RequireLockAsync(lease, cancellationToken);

            await faultInjector.BeforeBackfillAsync(cancellationToken);
            await DataBackfills.RunAsync(lease.Context, faultInjector, cancellationToken);

            await SchemaValidator.ValidateAsync(lease.Context, requireLedgers: true, cancellationToken);
            await RequireLockAsync(lease, cancellationToken);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            if (lease is not null)
            {
                await lease.DisposeAsync();
                if (lease.CleanupError is not null && failure is null)
                {
                    throw new InvalidOperationException(
                        "initialization completed but advisory-lock cleanup failed",
                        lease.CleanupError);
                }
            }
        }
    }

    public void Initialize() => InitializeAsync().GetAwaiter().GetResult();

    private static async Task RequireLockAsync(InitializationLease lease, CancellationToken cancellationToken)
    {
        if (!await lease.IsLockHeldAsync(cancellationToken))
        {
            throw new InvalidOperationException("initialization advisory lock is no longer held by the initializer session");
        }

        int backendPid = await lease.CurrentBackendPidAsync(cancellationToken);
        if (backendPid != lease.BackendPid)
        {
            throw new InvalidOperationException("initialization continued on a replacement PostgreSQL session");
        }
    }
}
