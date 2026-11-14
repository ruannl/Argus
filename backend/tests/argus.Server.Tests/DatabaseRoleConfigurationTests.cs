using System.Collections;
using argus.Server.Configuration;
using argus.Server.Data;
using Npgsql;

namespace argus.Server.Tests;

public sealed class DatabaseRoleConfigurationTests
{
    [Fact]
    public async Task RolesMatchTheIsolationContract()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        await DatabaseRoleFixture.EnsureAsync(databaseUrl);

        await using var connection = await new TestDatabase(databaseUrl).OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT rolname, rolcanlogin, rolsuper, rolbypassrls, rolinherit, rolcreatedb, rolcreaterole
            FROM pg_roles
            WHERE rolname IN ('{DatabaseIsolationContract.OwnerRole}', '{DatabaseIsolationContract.MigratorRole}', '{DatabaseIsolationContract.RuntimeRole}')
            ORDER BY rolname
            """;
        var roles = new Dictionary<string, (bool Login, bool Super, bool Bypass, bool Inherit, bool CreateDb, bool CreateRole)>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                roles[reader.GetString(0)] = (
                    reader.GetBoolean(1),
                    reader.GetBoolean(2),
                    reader.GetBoolean(3),
                    reader.GetBoolean(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6));
            }
        }

        Assert.Equal(3, roles.Count);
        Assert.Equal((false, false, false, false, false, false), roles[DatabaseIsolationContract.OwnerRole]);
        Assert.Equal((true, false, false, false, false, false), roles[DatabaseIsolationContract.MigratorRole]);
        Assert.Equal((true, false, false, false, false, false), roles[DatabaseIsolationContract.RuntimeRole]);

        command.CommandText = $"""
            SELECT granted.rolname, am.set_option
            FROM pg_auth_members am
            JOIN pg_roles member ON member.oid = am.member
            JOIN pg_roles granted ON granted.oid = am.roleid
            WHERE member.rolname = '{DatabaseIsolationContract.MigratorRole}'
            """;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(DatabaseIsolationContract.OwnerRole, reader.GetString(0));
            Assert.True(reader.GetBoolean(1));
            Assert.False(await reader.ReadAsync());
        }

        command.CommandText = $"""
            SELECT COUNT(*)
            FROM pg_auth_members am
            JOIN pg_roles member ON member.oid = am.member
            WHERE member.rolname = '{DatabaseIsolationContract.RuntimeRole}'
            """;
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task RuntimeLoginSeesForcedRlsAndTwoPoliciesOnEveryProtectedTable()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schema = "rlsmeta_" + Guid.NewGuid().ToString("N");
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            string tables = string.Join(",", DatabaseIsolationContract.ProtectedTables.Select(table => $"'{table}'"));
            await using var connection = new NpgsqlConnection(
                PersistenceOptions.BuildConnectionString(DatabaseRoleFixture.RuntimeSchemaUrl(databaseUrl, schema)));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT current_user, session_user";
            await using (var reader = await command.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(DatabaseIsolationContract.RuntimeRole, reader.GetString(0));
                Assert.Equal(DatabaseIsolationContract.RuntimeRole, reader.GetString(1));
            }

            command.CommandText = $"""
                SELECT COUNT(*) FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema()
                  AND c.relname IN ({tables})
                  AND c.relrowsecurity AND c.relforcerowsecurity
                """;
            Assert.Equal(13L, Convert.ToInt64(await command.ExecuteScalarAsync()));

            command.CommandText = "SELECT COUNT(*) FROM pg_policies WHERE schemaname = current_schema()";
            Assert.Equal(26L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task RestrictedRuntimeLoginCannotAssumeOwnerOrCreateObjects()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        await DatabaseRoleFixture.EnsureAsync(databaseUrl);
        await using var connection = await new TestDatabase(DatabaseRoleFixture.RuntimeUrl(databaseUrl)).OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_user";
        Assert.Equal(DatabaseIsolationContract.RuntimeRole, (string)(await command.ExecuteScalarAsync())!);

        await AssertStateAsync(connection, "SET ROLE " + DatabaseIsolationContract.OwnerRole, "42501");
        await AssertStateAsync(connection, "CREATE TABLE public.runtime_probe_table (id integer)", "42501");
        await AssertStateAsync(connection, "CREATE ROLE runtime_probe_role", "42501");
        await AssertStateAsync(connection, "ALTER ROLE " + DatabaseIsolationContract.RuntimeRole + " NOSUPERUSER", "42501");
    }

    [Fact]
    public async Task MaintenanceGuardRequiresTheMigratorLoginActingAsOwner()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        await DatabaseRoleFixture.EnsureAsync(databaseUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MaintenanceProgram.ValidateMaintenanceRoleAsync(DatabaseRoleFixture.RuntimeUrl(databaseUrl)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MaintenanceProgram.ValidateMaintenanceRoleAsync(databaseUrl));

        string migratorUrl = DatabaseRoleFixture.AppendRole(
            DatabaseRoleFixture.MigratorUrl(databaseUrl),
            DatabaseIsolationContract.OwnerRole);
        await MaintenanceProgram.ValidateMaintenanceRoleAsync(migratorUrl);
    }

    [Fact]
    public async Task AppSettingsNeverReadsTestOrMaintenanceFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "argus-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? original = Directory.GetCurrentDirectory();
        var saved = ClearArgusEnvironment();
        try
        {
            Directory.SetCurrentDirectory(directory);
            File.WriteAllText(Path.Combine(directory, ".env"), """
                ARGUS_DATABASE_URL=postgres://argus_runtime:runtime-env-file@127.0.0.1:5432/argus
                ARGUS_API_SECRET=01234567890123456789012345678901
                ARGUS_FRONTEND_URL=http://127.0.0.1:3000
                ARGUS_RESEND_API_KEY=re_test_key_with_at_least_20_bytes
                ARGUS_RESEND_FROM=local@example.com
                ARGUS_BILLING_WEBHOOK_SECRET=01234567890123456789012345678901
                """);
            File.WriteAllText(Path.Combine(directory, ".env.test"), """
                ARGUS_DATABASE_URL=postgres://test-file-canary@127.0.0.1:5432/argus_test
                ARGUS_API_SECRET=test-file-secret-canary
                """);
            File.WriteAllText(Path.Combine(directory, ".env.maintenance"), """
                ARGUS_DATABASE_URL=postgres://migrator-file@127.0.0.1:5432/argus
                ARGUS_API_SECRET=maintenance-file-secret-canary
                ARGUS_SEED_DEV_PASSWORD=maintenance-seed-canary
                """);

            AppSettings settings = AppSettings.Load();
            Assert.Contains("runtime-env-file", settings.DatabaseUrl, StringComparison.Ordinal);
            Assert.DoesNotContain("test-file-canary", settings.DatabaseUrl, StringComparison.Ordinal);
            Assert.DoesNotContain("migrator-file", settings.DatabaseUrl, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-canary", settings.ApiSecret, StringComparison.Ordinal);

            MaintenanceSettings maintenance = MaintenanceSettings.Load();
            Assert.Contains("migrator-file", maintenance.DatabaseUrl, StringComparison.Ordinal);
            Assert.DoesNotContain("runtime-env-file", maintenance.DatabaseUrl, StringComparison.Ordinal);
            Assert.Equal("maintenance-seed-canary", maintenance.SeedDevPassword);
        }
        finally
        {
            Directory.SetCurrentDirectory(original!);
            RestoreArgusEnvironment(saved);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AssertStateAsync(NpgsqlConnection connection, string sql, string expectedState)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(expectedState, exception.SqlState);
    }

    private static Dictionary<string, string?> ClearArgusEnvironment()
    {
        var saved = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            string key = (string)entry.Key;
            if (!key.StartsWith("ARGUS_", StringComparison.Ordinal))
            {
                continue;
            }

            saved[key] = (string?)entry.Value;
            Environment.SetEnvironmentVariable(key, null);
        }

        return saved;
    }

    private static void RestoreArgusEnvironment(Dictionary<string, string?> saved)
    {
        foreach ((string key, string? value) in saved)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
