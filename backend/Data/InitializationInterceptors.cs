using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace argus.Server.Data;

/// <summary>
/// The initializer owns one explicitly opened physical session. Once the lease
/// is acquired, a second physical opening would be a different PostgreSQL
/// session and would not hold the advisory lock.
/// </summary>
public sealed class InitializationConnectionInterceptor : DbConnectionInterceptor
{
    public bool LeaseAcquired { get; set; }

    public bool PhysicalConnectionClosed { get; private set; }

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        if (LeaseAcquired)
        {
            throw new InvalidOperationException("initialization connection must not be reopened after the lease is acquired");
        }

        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
                                                                         ConnectionEventData eventData,
                                                                         InterceptionResult result,
                                                                         CancellationToken cancellationToken = default)
    {
        if (LeaseAcquired)
        {
            throw new InvalidOperationException("initialization connection must not be reopened after the lease is acquired");
        }

        return ValueTask.FromResult(result);
    }

    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (LeaseAcquired)
        {
            PhysicalConnectionClosed = true;
        }
    }

    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (LeaseAcquired)
        {
            PhysicalConnectionClosed = true;
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Calls the initialization fault seam after each EF migration commit. The
/// coordinator enables barriers only around the migrator call so the backfill
/// transaction is not mistaken for a migration commit.
/// </summary>
public sealed class InitializationTransactionInterceptor(IInitializationFaultInjector faultInjector) : DbTransactionInterceptor
{
    private int _commitOrdinal;

    public bool BarriersEnabled { get; set; }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (BarriersEnabled)
        {
            _commitOrdinal++;
            faultInjector.AfterMigrationCommitAsync(_commitOrdinal, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    public override async Task TransactionCommittedAsync(DbTransaction transaction,
                                                         TransactionEndEventData eventData,
                                                         CancellationToken cancellationToken = default)
    {
        if (BarriersEnabled)
        {
            _commitOrdinal++;
            await faultInjector.AfterMigrationCommitAsync(_commitOrdinal, cancellationToken);
        }
    }
}

/// <summary>
/// Design-time model and script generation must never touch a database. A bare
/// <c>dotnet ef database update</c> reaches this guard and fails before effects.
/// </summary>
public sealed class DenyDatabaseConnectionInterceptor : DbConnectionInterceptor
{
    private static InvalidOperationException Rejected() =>
        new("design-time database access is not permitted; model and script generation must stay offline");

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) => throw Rejected();

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
                                                                         ConnectionEventData eventData,
                                                                         InterceptionResult result,
                                                                         CancellationToken cancellationToken = default) => throw Rejected();
}

public sealed class DenyDatabaseCommandInterceptor : DbCommandInterceptor
{
    private static InvalidOperationException Rejected() =>
        new("design-time database access is not permitted; model and script generation must stay offline");

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
                                                                    CommandEventData eventData,
                                                                    InterceptionResult<DbDataReader> result) => throw Rejected();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
                                                                                     CommandEventData eventData,
                                                                                     InterceptionResult<DbDataReader> result,
                                                                                     CancellationToken cancellationToken = default) => throw Rejected();

    public override InterceptionResult<object> ScalarExecuting(DbCommand command,
                                                               CommandEventData eventData,
                                                               InterceptionResult<object> result) => throw Rejected();

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
                                                                               CommandEventData eventData,
                                                                               InterceptionResult<object> result,
                                                                               CancellationToken cancellationToken = default) => throw Rejected();

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command,
                                                              CommandEventData eventData,
                                                              InterceptionResult<int> result) => throw Rejected();

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
                                                                              CommandEventData eventData,
                                                                              InterceptionResult<int> result,
                                                                              CancellationToken cancellationToken = default) => throw Rejected();
}
