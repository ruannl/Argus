using System.Net;
using System.Text;
using System.Text.Json;
using argus.Server.Data;
using argus.Server.Security;

namespace argus.Server.Tests;

public sealed class PersistenceModelTests
{
    [Fact]
    public async Task FreshAndLegacySchemasHaveTheSameCatalogManifest()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string freshSchema = "model_fresh_" + Guid.NewGuid().ToString("N");
        string legacySchema = "model_legacy_" + Guid.NewGuid().ToString("N");
        string freshUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, freshSchema);
        string legacyUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, legacySchema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, freshSchema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, legacySchema);
        try
        {
            await PersistenceTestSupport.RunCoordinatorAsync(freshUrl);
            await PersistenceTestSupport.ApplyLegacyAsync(legacyUrl, 10, canonicalMarkerPresent: true);
            await PersistenceTestSupport.RunCoordinatorAsync(legacyUrl);

            CatalogSnapshot fresh = await PersistenceTestSupport.ReadCatalogAsync(freshUrl);
            CatalogSnapshot legacy = await PersistenceTestSupport.ReadCatalogAsync(legacyUrl);

            Assert.Equal(fresh.Tables, legacy.Tables);
            Assert.Equal(fresh.Columns, legacy.Columns);
            Assert.Equal(fresh.Indexes, legacy.Indexes);
            Assert.Equal(fresh.Constraints, legacy.Constraints);
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, freshSchema);
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, legacySchema);
        }
    }

    [Fact]
    public async Task ModelKeepsLegacyShapeAndDeliberateMissingForeignKeys()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schema = "model_shape_" + Guid.NewGuid().ToString("N");
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);

            Assert.Equal(17L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, """
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = current_schema()
                  AND table_type = 'BASE TABLE'
                  AND table_name NOT IN ('schema_migrations', 'data_migrations', '__EFMigrationsHistory')
                """)));
            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM schema_migrations")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));

            Assert.Equal("ALWAYS", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT is_generated FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'transactions' AND column_name = 'merchant_key'"))!);
            Assert.Contains("argus_merchant_key", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT generation_expression FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'transactions' AND column_name = 'merchant_key'"))!, StringComparison.Ordinal);
            Assert.Equal("numeric(18,2)", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT format_type(a.atttypid, a.atttypmod) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = current_schema() AND c.relname = 'transactions' AND a.attname = 'amount'"))!);

            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, """
                SELECT COUNT(*) FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = current_schema() AND t.relname = 'transaction_operation_items' AND c.contype = 'f'
                """)));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, """
                SELECT COUNT(*) FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_class p ON p.oid = c.confrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = current_schema()
                  AND t.relname = 'transaction_operation_items'
                  AND p.relname = 'transaction_operations'
                  AND c.contype = 'f'
                """)));
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, """
                SELECT COUNT(*) FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_class p ON p.oid = c.confrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = current_schema()
                  AND t.relname IN ('transaction_operation_items', 'import_commit_receipts')
                  AND p.relname IN ('transactions', 'statement_imports')
                  AND c.contype = 'f'
                """)));

            string systemIndex = (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT indexdef FROM pg_indexes WHERE schemaname = current_schema() AND indexname = 'categories_customer_system_uidx'"))!;
            Assert.Contains("UNIQUE", systemIndex, StringComparison.Ordinal);
            Assert.Contains("WHERE (is_system = true)", systemIndex, StringComparison.Ordinal);
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() AND indexname = 'categories_customer_id_idx'")));
            string unconfirmedIndex = (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT indexdef FROM pg_indexes WHERE schemaname = current_schema() AND indexname = 'transactions_customer_unconfirmed_idx'"))!;
            Assert.Contains("WHERE (category_confirmed = false)", unconfirmedIndex, StringComparison.Ordinal);
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = current_schema() AND p.proname = 'argus_merchant_key'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task AdoptedRowsAreReadableThroughExistingApiAndForeignOwnersAreDenied()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schema = "model_api_" + Guid.NewGuid().ToString("N");
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        string? originalTestUrl = Environment.GetEnvironmentVariable("ARGUS_TEST_DATABASE_URL");
        try
        {
            await PersistenceTestSupport.ApplyLegacyAsync(schemaUrl, 10, canonicalMarkerPresent: true);
            await using (var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                Guid customerId = Guid.NewGuid();
                Guid accountId = Guid.NewGuid();
                Guid categoryId = Guid.NewGuid();
                Guid foreignCustomerId = Guid.NewGuid();
                Guid foreignAccountId = Guid.NewGuid();
                string rawToken = Tokens.NewToken();
                string tokenHash = Tokens.HashToken(rawToken);
                await PersistenceTestSupport.ExecuteAsync(connection, $"""
                    INSERT INTO customers (id, canonical_email, password_hash, email_verified_at,
                                           subscriber_activated_at, created_at, updated_at)
                    VALUES ('{customerId}', 'adopted-api@example.test', 'hash', NOW(), NOW(), NOW(), NOW()),
                           ('{foreignCustomerId}', 'foreign-api@example.test', 'hash', NOW(), NOW(), NOW(), NOW());
                    INSERT INTO financial_accounts (id, customer_id, display_name, institution, currency, created_at, updated_at)
                    VALUES ('{accountId}', '{customerId}', 'Adopted account', 'FNB', 'ZAR', NOW(), NOW()),
                           ('{foreignAccountId}', '{foreignCustomerId}', 'Foreign account', 'FNB', 'ZAR', NOW(), NOW());
                    INSERT INTO categories (id, customer_id, display_name, normalized_name, kind, is_system, is_default, created_at, updated_at)
                    VALUES ('{categoryId}', '{customerId}', 'Groceries', 'groceries', 'Expense', FALSE, FALSE, NOW(), NOW());
                    INSERT INTO sessions (id, customer_id, token_hash, expires_at, last_seen_at, created_at)
                    VALUES ('{Guid.NewGuid()}', '{customerId}', '{tokenHash}', NOW() + INTERVAL '1 hour', NOW(), NOW());
                    """);

                await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
                Environment.SetEnvironmentVariable("ARGUS_TEST_DATABASE_URL", schemaUrl);
                using var factory = new KitFactory();
                using HttpClient client = factory.CreateClient();

                using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
                meRequest.Headers.Add(Brand.ApplicationSecretHeader, TestEnvironment.ApiSecret);
                meRequest.Headers.Add(Brand.SessionHeader, rawToken);
                using HttpResponseMessage meResponse = await client.SendAsync(meRequest);
                Assert.True(meResponse.StatusCode == HttpStatusCode.OK, string.Join("\n", factory.LogCapture.Snapshot()));
                using JsonDocument meBody = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
                Assert.Equal("adopted-api@example.test", meBody.RootElement.GetProperty("email").GetString());

                using var accountsRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/finance/accounts");
                accountsRequest.Headers.Add(Brand.ApplicationSecretHeader, TestEnvironment.ApiSecret);
                accountsRequest.Headers.Add(Brand.SessionHeader, rawToken);
                using HttpResponseMessage accountsResponse = await client.SendAsync(accountsRequest);
                Assert.True(accountsResponse.StatusCode == HttpStatusCode.OK, string.Join("\n", factory.LogCapture.Snapshot()));
                using JsonDocument accountsBody = JsonDocument.Parse(await accountsResponse.Content.ReadAsStringAsync());
                Assert.Contains("Adopted account", accountsBody.RootElement.GetProperty("accounts").EnumerateArray().Select(item => item.GetProperty("display_name").GetString()));

                using var foreignRequest = new HttpRequestMessage(HttpMethod.Patch, $"/v1/finance/accounts/{foreignAccountId}")
                {
                    Content = new StringContent("{\"display_name\":\"Nope\"}", Encoding.UTF8, "application/json"),
                };
                foreignRequest.Headers.Add(Brand.ApplicationSecretHeader, TestEnvironment.ApiSecret);
                foreignRequest.Headers.Add(Brand.SessionHeader, rawToken);
                using HttpResponseMessage foreignResponse = await client.SendAsync(foreignRequest);
                Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
                using JsonDocument foreignBody = JsonDocument.Parse(await foreignResponse.Content.ReadAsStringAsync());
                Assert.Equal("not_found", foreignBody.RootElement.GetProperty("error").GetString());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("ARGUS_TEST_DATABASE_URL", originalTestUrl);
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }
}
