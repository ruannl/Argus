using argus.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace argus.Server.Tests;

/// <summary>
/// Test-only persistence wiring. Raw Npgsql stays here for schema setup,
/// fixture creation and independent observation; production code no longer
/// executes commands through it.
/// </summary>
public static class PersistenceTestSupport
{
    public static string SchemaUrl(string databaseUrl, string schema)
    {
        string separator = databaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";

        return databaseUrl + separator + "search_path=" + Uri.EscapeDataString(schema);
    }

    public static async Task CreateSchemaAsync(string databaseUrl, string schema, CancellationToken cancellationToken = default)
    {
        await DatabaseRoleFixture.EnsureAsync(databaseUrl, cancellationToken);
        string identifier = DatabaseRoleFixture.ValidateSchemaIdentifier(schema);
        await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(databaseUrl));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE SCHEMA {identifier};
            GRANT USAGE, CREATE ON SCHEMA {identifier} TO {DatabaseIsolationContract.OwnerRole};
            GRANT USAGE ON SCHEMA {identifier} TO {DatabaseIsolationContract.RuntimeRole};
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Bounded test-only mirror of the planned legacy SQL-object transfer. It
    /// inventories only this schema's tables, sequences, views and functions
    /// and changes their SQL owner to argus_owner. It never touches row data.
    /// </summary>
    public static async Task TransferSchemaObjectsToOwnerAsync(string databaseUrl,
                                                               string schema,
                                                               CancellationToken cancellationToken = default)
    {
        string identifier = DatabaseRoleFixture.ValidateSchemaIdentifier(schema);
        await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(databaseUrl));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DO $transfer$
            DECLARE
                item record;
            BEGIN
                FOR item IN
                    SELECT c.relname, c.relkind
                    FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = '{identifier}'
                      AND c.relkind IN ('r', 'S', 'v', 'm', 'p', 'f')
                LOOP
                    EXECUTE format('ALTER %s %I.%I OWNER TO {DatabaseIsolationContract.OwnerRole}',
                        CASE item.relkind
                            WHEN 'S' THEN 'SEQUENCE'
                            WHEN 'v' THEN 'VIEW'
                            WHEN 'm' THEN 'MATERIALIZED VIEW'
                            WHEN 'p' THEN 'TABLE'
                            WHEN 'f' THEN 'FOREIGN TABLE'
                            ELSE 'TABLE'
                        END,
                        '{identifier}',
                        item.relname);
                END LOOP;

                FOR item IN
                    SELECT p.oid::regprocedure AS signature
                    FROM pg_proc p
                    JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = '{identifier}'
                LOOP
                    EXECUTE format('ALTER FUNCTION %s OWNER TO {DatabaseIsolationContract.OwnerRole}', item.signature);
                END LOOP;
            END
            $transfer$;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task DropSchemaAsync(string databaseUrl, string schema, CancellationToken cancellationToken = default)
    {
        await using (var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(databaseUrl)))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS {schema} CASCADE";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Every unique schema URL is a distinct Npgsql pool. Clear its idle
        // connections before the next fixture, otherwise a long suite exhausts
        // the server's connection slots.
        ClearPool(SchemaUrl(databaseUrl, schema));
        ClearPool(DatabaseRoleFixture.RuntimeSchemaUrl(databaseUrl, schema));
        ClearPool(DatabaseRoleFixture.MigrationSchemaUrl(databaseUrl, schema));
    }

    private static void ClearPool(string url) =>
        NpgsqlConnection.ClearPool(new NpgsqlConnection(PersistenceOptions.BuildConnectionString(url)));

    public static async Task ApplyLegacyAsync(string schemaUrl,
                                              int migrationCount,
                                              bool canonicalMarkerPresent = false,
                                              CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(schemaUrl));
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                name TEXT PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE TABLE IF NOT EXISTS data_migrations (
                name TEXT PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """, cancellationToken);

        for (int index = 0; index < migrationCount; index++)
        {
            string legacyName = LegacyMigrationFixtures.LegacyNames[index];
            await ExecuteAsync(connection, GetLegacySql(index), cancellationToken);
            await using var record = connection.CreateCommand();
            record.CommandText = "INSERT INTO schema_migrations (name) VALUES (@name)";
            record.Parameters.AddWithValue("name", legacyName);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        if (canonicalMarkerPresent)
        {
            await ExecuteAsync(
                connection,
                $"INSERT INTO data_migrations (name) VALUES ('{LegacyMigrationFixtures.CanonicalBackfillMarker}')",
                cancellationToken);
        }
    }

    /// <summary>
    /// Prepared public schema for the in-process API host. The fixture owns
    /// role provisioning, the bounded object-owner transfer and the migration;
    /// the host itself starts as the restricted runtime login.
    /// </summary>
    public static async Task PreparePublicDatabaseAsync(string databaseUrl, CancellationToken cancellationToken = default)
    {
        await DatabaseRoleFixture.EnsureAsync(databaseUrl, cancellationToken);
        await using (var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(databaseUrl)))
        {
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(connection, $"""
                GRANT USAGE, CREATE ON SCHEMA public TO {DatabaseIsolationContract.OwnerRole};
                GRANT USAGE ON SCHEMA public TO {DatabaseIsolationContract.RuntimeRole};
                REVOKE CREATE ON SCHEMA public FROM PUBLIC;
                """, cancellationToken);
        }

        await TransferSchemaObjectsToOwnerAsync(databaseUrl, "public", cancellationToken);
        string ownerUrl = DatabaseRoleFixture.MigrationSchemaUrl(databaseUrl, "public");
        var options = PersistenceOptions.Build(ownerUrl);
        await using (var context = new ArgusDbContext(options))
        {
            bool existing = await context.Database
                .SqlQueryRaw<bool>("SELECT to_regclass('financial_accounts') IS NOT NULL AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (existing)
            {
                // The disposable public schema may already be at the latest EF
                // history from an earlier candidate revision. Reapply the
                // idempotent isolation SQL before the coordinator validates it,
                // so the current grant contract is present without re-running EF.
                // ExecuteSqlRawAsync uses composite formatting; escape the
                // function-body JSON braces on this test-only path.
                string sql = CustomerIsolationSql.BuildUpSql().Replace("{", "{{").Replace("}", "}}");
                await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            }
        }

        await new InitializationCoordinator(ownerUrl, NoopInitializationFaultInjector.Instance)
            .InitializeAsync(cancellationToken);
    }

    private static readonly object _transferGate = new();
    private static readonly HashSet<string> _transferredSchemas = new(StringComparer.Ordinal);

    public static async Task RunCoordinatorAsync(string schemaUrl,
                                                 IInitializationFaultInjector? faultInjector = null,
                                                 CancellationToken cancellationToken = default)
    {
        string schema = DatabaseRoleFixture.SchemaFromUrl(schemaUrl);
        string databaseUrl = DatabaseRoleFixture.DatabaseFromSchemaUrl(schemaUrl);
        await DatabaseRoleFixture.EnsureAsync(databaseUrl, cancellationToken);
        bool transfer;
        lock (_transferGate)
        {
            transfer = _transferredSchemas.Add(databaseUrl + "|" + schema);
        }
        if (transfer)
        {
            await TransferSchemaObjectsToOwnerAsync(databaseUrl, schema, cancellationToken);
        }
        string ownerUrl = DatabaseRoleFixture.MigrationSchemaUrl(databaseUrl, schema);
        await new InitializationCoordinator(ownerUrl, faultInjector ?? NoopInitializationFaultInjector.Instance)
            .InitializeAsync(cancellationToken);
    }

    public static async Task BackfillAsync(string databaseUrl, CancellationToken cancellationToken = default)
    {
        var options = PersistenceOptions.Build(databaseUrl);
        await using var context = new ArgusDbContext(options);
        await DataBackfills.RunAsync(context, NoopInitializationFaultInjector.Instance, cancellationToken);
    }

    public static async Task<NpgsqlConnection> OpenSchemaConnectionAsync(string schemaUrl,
                                                                         CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(schemaUrl));
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    public static async Task ExecuteAsync(NpgsqlConnection connection,
                                          string sql,
                                          CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<object?> ScalarAsync(NpgsqlConnection connection,
                                                  string sql,
                                                  CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync(cancellationToken);
    }

    public static async Task SeedRepresentativeRowsAsync(string schemaUrl,
                                                        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenSchemaConnectionAsync(schemaUrl, cancellationToken);
        Guid customerId = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        Guid statementId = Guid.NewGuid();
        Guid transactionId = Guid.NewGuid();
        await ExecuteAsync(connection, $"""
            INSERT INTO customers (id, canonical_email, password_hash, email_verified_at,
                                   subscriber_activated_at, created_at, updated_at)
            VALUES ('{customerId}', 'preserved-{Guid.NewGuid():N}@example.test', 'hash', NOW(), NOW(), NOW(), NOW());
            INSERT INTO financial_accounts (id, customer_id, display_name, institution, currency, created_at, updated_at)
            VALUES ('{accountId}', '{customerId}', 'Preserved account', 'FNB', 'ZAR', NOW(), NOW());
            INSERT INTO categories (id, customer_id, display_name, normalized_name, kind, is_system, is_default, created_at, updated_at)
            VALUES ('{categoryId}', '{customerId}', 'Groceries', 'groceries', 'Expense', FALSE, FALSE, NOW(), NOW());
            INSERT INTO statement_imports (
                id, customer_id, financial_account_id, source_type, statement_label,
                statement_fingerprint, start_date, end_date, created_at, updated_at)
            VALUES (
                '{statementId}', '{customerId}', '{accountId}', 'fnb_csv', 'Preserved statement',
                'preserved-fingerprint', DATE '2025-01-01', DATE '2025-01-31', NOW(), NOW());
            INSERT INTO transactions (
                id, customer_id, financial_account_id, statement_import_id, transaction_date,
                description, normalized_description, amount, category_id, source_type,
                source_row_number, identity_key, created_at, updated_at)
            VALUES (
                '{transactionId}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-05',
                'Preserved row', 'preserved row', -12.34, '{categoryId}', 'fnb_csv',
                1, 'preserved-key', NOW(), NOW());
            """, cancellationToken);
    }

    public static async Task SeedLegacy0005RowsAsync(string schemaUrl,
                                                     CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenSchemaConnectionAsync(schemaUrl, cancellationToken);
        Guid customerId = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        Guid statementId = Guid.NewGuid();
        Guid transactionId = Guid.NewGuid();
        await ExecuteAsync(connection, $"""
            INSERT INTO customers (id, canonical_email, password_hash, created_at, updated_at)
            VALUES ('{customerId}', 'legacy-preserved-{Guid.NewGuid():N}@example.test', 'hash', NOW(), NOW());
            INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
            VALUES ('{accountId}', '{customerId}', 'Legacy account', 'ZAR', NOW(), NOW());
            INSERT INTO categories (id, customer_id, display_name, normalized_name, kind, is_system, is_default, created_at, updated_at)
            VALUES ('{categoryId}', '{customerId}', 'Groceries', 'groceries', 'Expense', FALSE, FALSE, NOW(), NOW());
            INSERT INTO statement_imports (
                id, customer_id, financial_account_id, source_type, statement_label,
                statement_fingerprint, start_date, end_date, created_at, updated_at)
            VALUES (
                '{statementId}', '{customerId}', '{accountId}', 'fnb_csv', 'Legacy statement',
                'legacy-preserved-fingerprint', DATE '2025-01-01', DATE '2025-01-31', NOW(), NOW());
            INSERT INTO transactions (
                id, customer_id, financial_account_id, statement_import_id, transaction_date,
                description, normalized_description, amount, category_id, source_type,
                source_row_number, identity_key, created_at, updated_at)
            VALUES (
                '{transactionId}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-05',
                'Legacy preserved row', 'legacy preserved row', -12.34, '{categoryId}', 'fnb_csv',
                1, 'legacy-preserved-key', NOW(), NOW());
            """, cancellationToken);
    }

    public static async Task<LegacyBackfillFixture> SeedLegacyBackfillFixtureAsync(string schemaUrl,
                                                                                 CancellationToken cancellationToken = default)
    {
        await ApplyLegacyAsync(schemaUrl, 6, canonicalMarkerPresent: false, cancellationToken);
        await using var connection = await OpenSchemaConnectionAsync(schemaUrl, cancellationToken);

        Guid customerId = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        Guid statementId = Guid.NewGuid();
        Guid transactionId = Guid.NewGuid();
        await ExecuteAsync(connection, $"""
            INSERT INTO customers (id, canonical_email, password_hash, created_at, updated_at)
            VALUES ('{customerId}', 'backfill-{Guid.NewGuid():N}@example.test', 'hash', NOW(), NOW());
            INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
            VALUES ('{accountId}', '{customerId}', 'Cheque', 'ZAR', NOW(), NOW());
            INSERT INTO categories (id, customer_id, display_name, normalized_name, kind, is_system, is_default, created_at, updated_at)
            VALUES ('{categoryId}', '{customerId}', 'Groceries', 'groceries', 'Expense', FALSE, FALSE, NOW(), NOW());
            INSERT INTO statement_imports (
                id, customer_id, financial_account_id, source_type, statement_label,
                statement_fingerprint, start_date, end_date, created_at, updated_at)
            VALUES (
                '{statementId}', '{customerId}', '{accountId}', 'fnb_csv', 'Backfill statement',
                'legacy-fingerprint', DATE '2025-01-01', DATE '2025-01-31', NOW(), NOW());
            INSERT INTO transactions (
                id, customer_id, financial_account_id, statement_import_id, transaction_date,
                description, normalized_description, amount, category_id, source_type,
                source_row_number, identity_key, created_at, updated_at)
            VALUES (
                '{transactionId}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-05',
                'Backfill row', 'backfill row', -12.34, '{categoryId}', 'fnb_csv',
                1, 'legacy-key', NOW(), NOW());
            """, cancellationToken);

        return new LegacyBackfillFixture(customerId, accountId, categoryId, statementId, transactionId);
    }

    public static async Task<IReadOnlyList<string>> ReadDataDigestsAsync(string schemaUrl,
                                                                         CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenSchemaConnectionAsync(schemaUrl, cancellationToken);
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = current_schema()
                  AND table_type = 'BASE TABLE'
                ORDER BY table_name
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var digests = new List<string>();
        foreach (string table in tables)
        {
            object? value = await ScalarAsync(connection,
                $"SELECT COALESCE(md5(string_agg(to_jsonb(t)::text, '' ORDER BY to_jsonb(t)::text)), 'EMPTY') FROM \"{table}\" t",
                cancellationToken);
            digests.Add($"{table}|{Convert.ToString(value)}");
        }

        return digests;
    }

    public static async Task RunEfToAsync(string schemaUrl,
                                          string targetMigration,
                                          CancellationToken cancellationToken = default)
    {
        var options = PersistenceOptions.Build(schemaUrl);
        await using var context = new ArgusDbContext(options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration, cancellationToken);
    }

    public static async Task<CatalogSnapshot> ReadCatalogAsync(string schemaUrl,
                                                               CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(schemaUrl));
        await connection.OpenAsync(cancellationToken);

        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() AND table_type = 'BASE TABLE' ORDER BY table_name";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var columns = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT c.relname, a.attname, format_type(a.atttypid, a.atttypmod), NOT a.attnotnull, a.attgenerated::text
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema() AND c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
                ORDER BY c.relname, a.attnum
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add($"{reader.GetString(0)}.{reader.GetString(1)}|{reader.GetString(2)}|{reader.GetBoolean(3)}|{reader.GetString(4)}");
            }
        }

        var indexes = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT indexname FROM pg_indexes WHERE schemaname = current_schema() AND tablename <> '__EFMigrationsHistory' ORDER BY indexname";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                indexes.Add(reader.GetString(0));
            }
        }

        var constraints = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT conname
                FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = current_schema() AND t.relname <> '__EFMigrationsHistory' AND c.contype IN ('c', 'f')
                ORDER BY conname
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                constraints.Add(reader.GetString(0));
            }
        }

        return new CatalogSnapshot(tables, columns, indexes, constraints);
    }

    private static string GetLegacySql(int index) => index switch
    {
        0 => LegacyMigrationFixtures.M0001CustomerAccounts,
        1 => LegacyMigrationFixtures.M0002EmailVerificationTokens,
        2 => LegacyMigrationFixtures.M0003PasswordResetTokens,
        3 => LegacyMigrationFixtures.M0004FailedLoginLockout,
        4 => LegacyMigrationFixtures.M0005FinanceFoundation,
        5 => LegacyMigrationFixtures.M0006StatementImport,
        6 => LegacyMigrationFixtures.M0007TransactionWorkspace,
        7 => LegacyMigrationFixtures.M0008SuggestionLearning,
        8 => LegacyMigrationFixtures.M0009ReviewCategorySuggestion,
        9 => LegacyMigrationFixtures.M0010TransactionCategoryConfirmation,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}

public sealed class BarrierInitializationFaultInjector : IInitializationFaultInjector
{
    public enum BarrierPoint
    {
        AfterLockAcquireAttempt,
        AfterMigrationCommit,
        BeforeBackfill,
        AfterBackfillStaging,
        BeforeBackfillMarker,
    }

    private readonly BarrierPoint _point;
    private readonly int _commitOrdinal;

    public BarrierInitializationFaultInjector(BarrierPoint point, int commitOrdinal = 1)
    {
        _point = point;
        _commitOrdinal = commitOrdinal;
    }

    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int? BackendPid { get; private set; }

    public Task AfterLockAcquireAttemptAsync(bool acquired, CancellationToken cancellationToken) =>
        _point == BarrierPoint.AfterLockAcquireAttempt && acquired
            ? PauseAsync(cancellationToken)
            : Task.CompletedTask;

    public Task AfterLeaseAcquiredAsync(int backendPid, CancellationToken cancellationToken)
    {
        BackendPid = backendPid;

        return Task.CompletedTask;
    }

    public Task BeforeUnlockAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AfterMigrationCommitAsync(int commitOrdinal, CancellationToken cancellationToken) =>
        _point == BarrierPoint.AfterMigrationCommit && commitOrdinal == _commitOrdinal
            ? PauseAsync(cancellationToken)
            : Task.CompletedTask;

    public Task BeforeBackfillAsync(CancellationToken cancellationToken) =>
        _point == BarrierPoint.BeforeBackfill ? PauseAsync(cancellationToken) : Task.CompletedTask;

    public Task AfterBackfillStagingAsync(CancellationToken cancellationToken) =>
        _point == BarrierPoint.AfterBackfillStaging ? PauseAsync(cancellationToken) : Task.CompletedTask;

    public Task BeforeBackfillMarkerAsync(CancellationToken cancellationToken) =>
        _point == BarrierPoint.BeforeBackfillMarker ? PauseAsync(cancellationToken) : Task.CompletedTask;

    private async Task PauseAsync(CancellationToken cancellationToken)
    {
        Reached.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
    }
}


public sealed record CatalogSnapshot(
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> Indexes,
    IReadOnlyList<string> Constraints);


public sealed class FaultingInitializationFaultInjector : IInitializationFaultInjector
{
    public enum FaultPoint
    {
        AfterLockAcquireAttempt,
        BeforeUnlock,
    }

    private readonly FaultPoint _point;

    public FaultingInitializationFaultInjector(FaultPoint point)
    {
        _point = point;
    }

    public Task AfterLockAcquireAttemptAsync(bool acquired, CancellationToken cancellationToken) =>
        _point == FaultPoint.AfterLockAcquireAttempt
            ? Task.FromException(new InvalidOperationException("injected ambiguous lock acquisition"))
            : Task.CompletedTask;

    public Task AfterLeaseAcquiredAsync(int backendPid, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BeforeUnlockAsync(CancellationToken cancellationToken) =>
        _point == FaultPoint.BeforeUnlock
            ? Task.FromException(new InvalidOperationException("injected ambiguous unlock"))
            : Task.CompletedTask;

    public Task AfterMigrationCommitAsync(int commitOrdinal, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BeforeBackfillAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AfterBackfillStagingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BeforeBackfillMarkerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}


public sealed class CountingInitializationFaultInjector : IInitializationFaultInjector
{
    private int _attempts;

    public TaskCompletionSource FirstFailedAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int? BackendPid { get; private set; }

    public int Attempts => Volatile.Read(ref _attempts);

    public Task AfterLockAcquireAttemptAsync(bool acquired, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _attempts);
        if (!acquired)
        {
            FirstFailedAttempt.TrySetResult();
        }

        return Task.CompletedTask;
    }

    public Task AfterLeaseAcquiredAsync(int backendPid, CancellationToken cancellationToken)
    {
        BackendPid = backendPid;

        return Task.CompletedTask;
    }

    public Task BeforeUnlockAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AfterMigrationCommitAsync(int commitOrdinal, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BeforeBackfillAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AfterBackfillStagingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BeforeBackfillMarkerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}


public sealed record LegacyBackfillFixture(
    Guid CustomerId,
    Guid AccountId,
    Guid CategoryId,
    Guid StatementId,
    Guid TransactionId);
