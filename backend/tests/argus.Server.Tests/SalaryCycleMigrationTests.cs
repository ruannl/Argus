using argus.Server.Data;
using argus.Server.Security;
using Npgsql;

namespace argus.Server.Tests;

public sealed class SalaryCycleMigrationTests
{
    [Fact]
    public async Task Prefix11ReplaysWithoutSalaryTablesAndPrefix12InstallsThem()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schema = "salary_mig_" + Guid.NewGuid().ToString("N");
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            string ownerUrl = DatabaseRoleFixture.MigrationSchemaUrl(databaseUrl, schema);
            await PersistenceTestSupport.RunEfToAsync(ownerUrl, MigrationCatalog.CustomerRowSecurityEfId);

            await using (var context = new ArgusDbContext(PersistenceOptions.Build(ownerUrl)))
            {
                InitializationState state = await SchemaValidator.ValidateAsync(context, requireLedgers: true, CancellationToken.None);
                Assert.Equal(11, state.EffectivePrefix);
            }

            await using (NpgsqlConnection connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = current_schema() AND c.relname IN ('salary_cycle_state', 'salary_cycle_starts')")));
                Assert.Equal(26L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM pg_policies WHERE schemaname = current_schema()")));
                await PersistenceTestSupport.ExecuteAsync(connection, "INSERT INTO data_migrations (name) VALUES ('0006_canonical_identity_backfill')");
            }

            InvalidOperationException prefixRefusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RuntimeDatabaseValidator.ValidateAsync(DatabaseRoleFixture.RuntimeSchemaUrl(databaseUrl, schema)));
            Assert.Contains("is not the latest supported prefix", prefixRefusal.Message, StringComparison.Ordinal);

            await PersistenceTestSupport.RunEfToAsync(ownerUrl, MigrationCatalog.SalaryCyclesEfId);

            await using (var context = new ArgusDbContext(PersistenceOptions.Build(ownerUrl)))
            {
                InitializationState state = await SchemaValidator.ValidateAsync(context, requireLedgers: true, CancellationToken.None);
                Assert.Equal(12, state.EffectivePrefix);
            }

            await using (NpgsqlConnection connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                Assert.Equal(2L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = current_schema() AND c.relname IN ('salary_cycle_state', 'salary_cycle_starts')")));
                Assert.Equal(30L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM pg_policies WHERE schemaname = current_schema()")));
            }
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task Prefix11ValidationRejectsARetainedPrefix12SalarySchema()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schema = "salary_rollback_" + Guid.NewGuid().ToString("N");
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            string ownerUrl = DatabaseRoleFixture.MigrationSchemaUrl(databaseUrl, schema);
            await PersistenceTestSupport.RunEfToAsync(ownerUrl, MigrationCatalog.SalaryCyclesEfId);
            await using (NpgsqlConnection connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                await PersistenceTestSupport.ExecuteAsync(
                    connection,
                    $"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '{MigrationCatalog.SalaryCyclesEfId}'");
            }

            await using var context = new ArgusDbContext(PersistenceOptions.Build(ownerUrl));
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SchemaValidator.ValidateAsync(context, requireLedgers: true, CancellationToken.None));
            Assert.Contains("unexpected table", exception.Message, StringComparison.Ordinal);
            Assert.Contains("salary_cycle_", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task PopulatedPrefix11UpgradesWithoutChangingFinancialRows()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schema = "salary_upgrade_" + Guid.NewGuid().ToString("N");
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            string ownerUrl = DatabaseRoleFixture.MigrationSchemaUrl(databaseUrl, schema);
            await PersistenceTestSupport.RunEfToAsync(ownerUrl, MigrationCatalog.CustomerRowSecurityEfId);
            await PersistenceTestSupport.SeedRepresentativeRowsAsync(schemaUrl);
            IReadOnlyList<string> before = (await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl))
                .Where(digest => !digest.StartsWith("__EFMigrationsHistory|", StringComparison.Ordinal))
                .ToArray();
            await PersistenceTestSupport.RunEfToAsync(ownerUrl, MigrationCatalog.SalaryCyclesEfId);
            IReadOnlyList<string> after = await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl);

            // Compare only rows that existed before; the new salary tables are
            // empty and therefore have no digest entry in the before list.
            foreach (string digest in before)
            {
                Assert.Contains(digest, after);
            }

            await using NpgsqlConnection connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM salary_cycle_state")));
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM salary_cycle_starts")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, """
                SELECT COUNT(*) FROM transactions t
                WHERE t.description = 'Preserved row' AND t.amount = -12.34
                """)));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task SalaryTablesHaveForcedRlsAndLeastPrivilegeRuntimeGrants()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schema = "salary_rls_" + Guid.NewGuid().ToString("N");
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            Guid customerA = Guid.NewGuid();
            Guid customerB = Guid.NewGuid();
            string sessionAHash = Tokens.HashToken(Tokens.NewToken());
            await using (NpgsqlConnection owner = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                await PersistenceTestSupport.ExecuteAsync(owner, $"""
                    INSERT INTO customers (id, canonical_email, password_hash, email_verified_at, subscriber_activated_at, created_at, updated_at)
                    VALUES
                        ('{customerA}', 'salary-a-{Guid.NewGuid():N}@example.test', 'hash', NOW(), NOW(), NOW(), NOW()),
                        ('{customerB}', 'salary-b-{Guid.NewGuid():N}@example.test', 'hash', NOW(), NOW(), NOW(), NOW());
                    INSERT INTO sessions (id, customer_id, token_hash, expires_at, last_seen_at, created_at)
                    VALUES ('{Guid.NewGuid()}', '{customerA}', '{sessionAHash}', NOW() + INTERVAL '1 hour', NOW(), NOW());
                    INSERT INTO salary_cycle_state (customer_id, revision, updated_at)
                    VALUES ('{customerA}', 1, NOW()), ('{customerB}', 1, NOW());
                    INSERT INTO salary_cycle_starts (id, customer_id, start_date, created_at)
                    VALUES
                        ('{Guid.NewGuid()}', '{customerA}', DATE '2025-01-25', NOW()),
                        ('{Guid.NewGuid()}', '{customerB}', DATE '2025-02-24', NOW());
                    """);
            }

            string runtimeUrl = DatabaseRoleFixture.RuntimeSchemaUrl(databaseUrl, schema);
            await using var runtime = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(runtimeUrl));
            await runtime.OpenAsync();

            await using (NpgsqlCommand privileges = runtime.CreateCommand())
            {
                privileges.CommandText = $"""
                    SELECT
                        has_table_privilege('{DatabaseIsolationContract.RuntimeRole}', 'salary_cycle_state', 'SELECT'),
                        has_table_privilege('{DatabaseIsolationContract.RuntimeRole}', 'salary_cycle_state', 'INSERT'),
                        has_table_privilege('{DatabaseIsolationContract.RuntimeRole}', 'salary_cycle_state', 'DELETE'),
                        has_column_privilege('{DatabaseIsolationContract.RuntimeRole}', 'salary_cycle_state', 'revision', 'UPDATE'),
                        has_column_privilege('{DatabaseIsolationContract.RuntimeRole}', 'salary_cycle_state', 'updated_at', 'UPDATE'),
                        has_column_privilege('{DatabaseIsolationContract.RuntimeRole}', 'salary_cycle_state', 'customer_id', 'UPDATE'),
                        has_table_privilege('{DatabaseIsolationContract.RuntimeRole}', 'salary_cycle_starts', 'SELECT'),
                        has_table_privilege('{DatabaseIsolationContract.RuntimeRole}', 'salary_cycle_starts', 'INSERT'),
                        has_table_privilege('{DatabaseIsolationContract.RuntimeRole}', 'salary_cycle_starts', 'DELETE'),
                        has_column_privilege('{DatabaseIsolationContract.RuntimeRole}', 'salary_cycle_starts', 'start_date', 'UPDATE'),
                        has_column_privilege('{DatabaseIsolationContract.RuntimeRole}', 'salary_cycle_starts', 'created_at', 'UPDATE')
                    """;
                await using NpgsqlDataReader reader = await privileges.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                bool[] expected =
                [
                    true, true, false, true, true, false,
                    true, true, true, false, false,
                ];
                for (int index = 0; index < expected.Length; index++)
                {
                    Assert.Equal(expected[index], reader.GetBoolean(index));
                }
            }

            await using (NpgsqlCommand tablePrivileges = runtime.CreateCommand())
            {
                tablePrivileges.CommandText = $"""
                    SELECT COUNT(*) FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = current_schema()
                      AND c.relname IN ('salary_cycle_state', 'salary_cycle_starts')
                      AND c.relrowsecurity AND c.relforcerowsecurity
                    """;
                Assert.Equal(2L, Convert.ToInt64(await tablePrivileges.ExecuteScalarAsync()));
            }

            await using NpgsqlTransaction transaction = await runtime.BeginTransactionAsync();
            await using (NpgsqlCommand authority = runtime.CreateCommand())
            {
                authority.Transaction = transaction;
                authority.CommandText = """
                    SELECT set_config('argus.customer_id', @customer, true),
                           set_config('argus.session_hash', @hash, true),
                           set_config('argus.transaction_id', pg_current_xact_id()::text, true)
                    """;
                authority.Parameters.AddWithValue("customer", customerA.ToString("D"));
                authority.Parameters.AddWithValue("hash", sessionAHash);
                await authority.ExecuteNonQueryAsync();
            }

            await using (NpgsqlCommand count = runtime.CreateCommand())
            {
                count.Transaction = transaction;
                count.CommandText = "SELECT COUNT(*)::int FROM salary_cycle_starts";
                Assert.Equal(1, Convert.ToInt32(await count.ExecuteScalarAsync()));
                count.CommandText = "SELECT COUNT(*)::int FROM salary_cycle_state";
                Assert.Equal(1, Convert.ToInt32(await count.ExecuteScalarAsync()));
            }

            await using (NpgsqlCommand forged = runtime.CreateCommand())
            {
                forged.Transaction = transaction;
                forged.CommandText = $"""
                    INSERT INTO salary_cycle_starts (id, customer_id, start_date, created_at)
                    VALUES ('{Guid.NewGuid()}', '{customerB}', DATE '2025-03-25', NOW())
                    """;
                PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => forged.ExecuteNonQueryAsync());
                Assert.Equal("42501", exception.SqlState);
            }

            await transaction.RollbackAsync();
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task SalaryTableCatalogMatchesTheContract()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schema = "salary_catalog_" + Guid.NewGuid().ToString("N");
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            await using NpgsqlConnection connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);

            Assert.Equal("uuid", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT format_type(a.atttypid, a.atttypmod) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = current_schema() AND c.relname = 'salary_cycle_state' AND a.attname = 'customer_id'"))!);
            Assert.Equal("bigint", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT format_type(a.atttypid, a.atttypmod) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = current_schema() AND c.relname = 'salary_cycle_state' AND a.attname = 'revision'"))!);
            Assert.Equal("date", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT format_type(a.atttypid, a.atttypmod) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = current_schema() AND c.relname = 'salary_cycle_starts' AND a.attname = 'start_date'"))!);

            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, """
                SELECT COUNT(*) FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = current_schema()
                  AND c.conname = 'salary_cycle_starts_customer_date_key' AND c.contype = 'u'
                """)));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, """
                SELECT COUNT(*) FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = current_schema()
                  AND c.conname = 'salary_cycle_starts_customer_id_id_key' AND c.contype = 'u'
                """)));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, """
                SELECT COUNT(*) FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = current_schema()
                  AND c.conname = 'salary_cycle_state_revision_check' AND c.contype = 'c'
                """)));
            Assert.Equal(2L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, """
                SELECT COUNT(*) FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = current_schema()
                  AND t.relname IN ('salary_cycle_state', 'salary_cycle_starts')
                  AND c.contype = 'f'
                  AND c.confdeltype = 'c'
                """)));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }
}
