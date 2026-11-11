namespace argus.Server.Data;

/// <summary>
/// Deterministic test seam for the initialization protocol. Production
/// registers the no-op implementation; tests may pause at a durable boundary
/// without changing the protocol itself.
/// </summary>
public interface IInitializationFaultInjector
{
    Task AfterLockAcquireAttemptAsync(bool acquired, CancellationToken cancellationToken);

    Task AfterLeaseAcquiredAsync(int backendPid, CancellationToken cancellationToken);

    Task BeforeUnlockAsync(CancellationToken cancellationToken);

    Task AfterMigrationCommitAsync(int commitOrdinal, CancellationToken cancellationToken);

    Task BeforeBackfillAsync(CancellationToken cancellationToken);

    Task AfterBackfillStagingAsync(CancellationToken cancellationToken);

    Task BeforeBackfillMarkerAsync(CancellationToken cancellationToken);
}

public sealed class NoopInitializationFaultInjector : IInitializationFaultInjector
{
    public static readonly NoopInitializationFaultInjector Instance = new();

    private NoopInitializationFaultInjector()
    {
    }

    public Task AfterLockAcquireAttemptAsync(bool acquired, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AfterLeaseAcquiredAsync(int backendPid, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BeforeUnlockAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AfterMigrationCommitAsync(int commitOrdinal, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BeforeBackfillAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AfterBackfillStagingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BeforeBackfillMarkerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
