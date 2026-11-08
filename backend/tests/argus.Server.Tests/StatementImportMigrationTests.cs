using argus.Server.Data;
using argus.Server.Imports;
using Npgsql;

namespace argus.Server.Tests;

public sealed class StatementImportMigrationTests(KitFactory factory) : IClassFixture<KitFactory>
{
    private readonly KitFactory _factory = factory;

    [Fact]
    public async Task Populated0005UpgradesWithoutDataLoss()
    {
        var schema = "migration_" + Guid.NewGuid().ToString("N");
        var db = _factory.CreateDb();
        await using var connection = await db.OpenAsync();
        try
        {
            await using (var create = connection.CreateCommand())
            {
                create.CommandText = $"CREATE SCHEMA {schema}";
                await create.ExecuteNonQueryAsync();
            }

            await using (var use = connection.CreateCommand())
            {
                use.CommandText =
                    $"SET search_path TO {schema}; " +
                    "CREATE TABLE schema_migrations (name TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());";
                await use.ExecuteNonQueryAsync();
            }

            foreach (var (_, sql) in Migrations.All.Take(5))
            {
                await using var apply = connection.CreateCommand();
                apply.CommandText = sql;
                await apply.ExecuteNonQueryAsync();
            }

            var customerId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var statementId = Guid.NewGuid();
            var transactionId = Guid.NewGuid();
            var tagId = Guid.NewGuid();
            await using (var seed = connection.CreateCommand())
            {
                seed.CommandText = $"""
                    INSERT INTO customers (id, canonical_email, password_hash, created_at, updated_at)
                    VALUES ('{customerId}', 'migration@example.test', 'hash', NOW(), NOW());
                    INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
                    VALUES ('{accountId}', '{customerId}', 'Cheque', 'ZAR', NOW(), NOW());
                    INSERT INTO categories (id, customer_id, display_name, normalized_name, kind, is_system, is_default, created_at, updated_at)
                    VALUES ('{categoryId}', '{customerId}', 'Groceries', 'groceries', 'Expense', FALSE, FALSE, NOW(), NOW());
                    INSERT INTO statement_imports (
                        id, customer_id, financial_account_id, source_type, statement_label,
                        statement_fingerprint, start_date, end_date, created_at, updated_at)
                    VALUES (
                        '{statementId}', '{customerId}', '{accountId}', 'csv', 'statement',
                        'fingerprint-migration', DATE '2025-01-01', DATE '2025-01-31', NOW(), NOW());
                    INSERT INTO transactions (
                        id, customer_id, financial_account_id, statement_import_id, transaction_date,
                        description, normalized_description, amount, category_id, source_type,
                        source_row_number, identity_key, created_at, updated_at)
                    VALUES (
                        '{transactionId}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-05',
                        'Migration row', 'migration row', -12.34, '{categoryId}', 'csv',
                        1, 'migration-identity', NOW(), NOW());
                    INSERT INTO merchant_tags (id, customer_id, display_name, normalized_name, created_at, updated_at)
                    VALUES ('{tagId}', '{customerId}', 'Tag', 'tag', NOW(), NOW());
                    INSERT INTO transaction_merchant_tags (transaction_id, merchant_tag_id)
                    VALUES ('{transactionId}', '{tagId}');
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            await using (var migrate = connection.CreateCommand())
            {
                migrate.CommandText = Migrations.All[5].Sql;
                await migrate.ExecuteNonQueryAsync();
            }
            await Db.BackfillCanonicalIdentitiesAsync(connection);

            var canonicalKey = StatementIdentity.ComputeIdentityKey(
                new DateOnly(2025, 1, 5), -1234, "migration row");
            var storedKey = (string)(await ScalarAsync(
                connection, "SELECT identity_key FROM transactions"))!;
            Assert.Equal(canonicalKey, storedKey);
            var expectedFingerprint = StatementIdentity.ComputeStatementFingerprint(
                [
                    new ParsedStatementRow(
                        transactionId,
                        1,
                        null,
                        1,
                        new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            ["date"] = "2025-01-05",
                            ["description"] = "Migration row",
                            ["amount"] = "-12.34",
                        },
                        new DateOnly(2025, 1, 5),
                        "Migration row",
                        "migration row",
                        -1234,
                        [],
                        [],
                        null,
                        null),
                ],
                new DateOnly(2025, 1, 1),
                new DateOnly(2025, 1, 31));
            Assert.Equal(expectedFingerprint, (string)(await ScalarAsync(
                connection, "SELECT statement_fingerprint FROM statement_imports"))!);

            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM transactions")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM statement_imports")));
            Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(connection, "SELECT import_version FROM financial_accounts")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "SELECT identity_version FROM transactions")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "SELECT occurrence_index FROM transactions")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "SELECT fingerprint_occurrence FROM statement_imports")));
            Assert.Equal(customerId, (Guid)(await ScalarAsync(connection,
                "SELECT customer_id FROM transaction_merchant_tags"))!);

            // A second row with the same fallback identity and occurrence must
            // violate the new uniqueness constraint.
            var duplicate = Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, $"""
                INSERT INTO transactions (
                    id, customer_id, financial_account_id, statement_import_id, transaction_date,
                    description, normalized_description, amount, category_id, source_type,
                    source_row_number, identity_key, occurrence_index, created_at, updated_at)
                VALUES (
                    '{Guid.NewGuid()}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-05',
                    'Migration row', 'migration row', -12.34, '{categoryId}', 'csv',
                    2, '{canonicalKey}', 1, NOW(), NOW());
                """));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, (await duplicate).SqlState);

            // A tag link with a foreign owner is rejected by the composite FK.
            var foreignOwner = Guid.NewGuid();
            var foreignTagId = Guid.NewGuid();
            await ExecuteAsync(connection, $"""
                INSERT INTO customers (id, canonical_email, password_hash, created_at, updated_at)
                VALUES ('{foreignOwner}', 'foreign-migration@example.test', 'hash', NOW(), NOW());
                INSERT INTO merchant_tags (id, customer_id, display_name, normalized_name, created_at, updated_at)
                VALUES ('{foreignTagId}', '{foreignOwner}', 'Foreign tag', 'foreign tag', NOW(), NOW());
                """);
            var badLink = Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, $"""
                INSERT INTO transaction_merchant_tags (customer_id, transaction_id, merchant_tag_id)
                VALUES ('{foreignOwner}', '{transactionId}', '{foreignTagId}');
                """));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await badLink).SqlState);
        }
        finally
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP SCHEMA IF EXISTS {schema} CASCADE";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection,
                                                   string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection,
                                           string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
