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
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.ApplyLegacyAsync(schemaUrl, 5);
            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);

            Guid customerId = Guid.NewGuid();
            Guid accountId = Guid.NewGuid();
            Guid categoryId = Guid.NewGuid();
            Guid statementId = Guid.NewGuid();
            Guid transactionId = Guid.NewGuid();
            Guid tagId = Guid.NewGuid();
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

            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);

            var canonicalKey = StatementIdentity.ComputeIdentityKey(new DateOnly(2025, 1, 5), -1234, "migration row");
            var storedKey = (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT identity_key FROM transactions"))!;
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
            Assert.Equal(expectedFingerprint, (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT statement_fingerprint FROM statement_imports"))!);

            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM transactions")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM statement_imports")));
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT import_version FROM financial_accounts")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT identity_version FROM transactions")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT occurrence_index FROM transactions")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT fingerprint_occurrence FROM statement_imports")));
            Assert.Equal(customerId, (Guid)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT customer_id FROM transaction_merchant_tags"))!);

            // A second row with the same fallback identity and occurrence must
            // violate the new uniqueness constraint.
            var duplicate = Assert.ThrowsAsync<PostgresException>(() => PersistenceTestSupport.ExecuteAsync(connection, $"""
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
            Guid foreignOwner = Guid.NewGuid();
            Guid foreignTagId = Guid.NewGuid();
            await PersistenceTestSupport.ExecuteAsync(connection, $"""
                INSERT INTO customers (id, canonical_email, password_hash, created_at, updated_at)
                VALUES ('{foreignOwner}', 'foreign-migration@example.test', 'hash', NOW(), NOW());
                INSERT INTO merchant_tags (id, customer_id, display_name, normalized_name, created_at, updated_at)
                VALUES ('{foreignTagId}', '{foreignOwner}', 'Foreign tag', 'foreign tag', NOW(), NOW());
                """);
            var badLink = Assert.ThrowsAsync<PostgresException>(() => PersistenceTestSupport.ExecuteAsync(connection, $"""
                INSERT INTO transaction_merchant_tags (customer_id, transaction_id, merchant_tag_id)
                VALUES ('{foreignOwner}', '{transactionId}', '{foreignTagId}');
                """));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await badLink).SqlState);
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task PopulatedReviewUpgradesThroughCategorySuggestionWithoutDataLoss()
    {
        var schema = "migration_cat_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.ApplyLegacyAsync(schemaUrl, 8);
            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);

            Guid customerId = Guid.NewGuid();
            Guid accountId = Guid.NewGuid();
            Guid reviewId = Guid.NewGuid();
            Guid rowId = Guid.NewGuid();
            await PersistenceTestSupport.ExecuteAsync(connection, $"""
                INSERT INTO customers (id, canonical_email, password_hash, created_at, updated_at)
                VALUES ('{customerId}', 'category-migration@example.test', 'hash', NOW(), NOW());
                INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
                VALUES ('{accountId}', '{customerId}', 'Cheque', 'ZAR', NOW(), NOW());
                INSERT INTO import_reviews (
                    id, customer_id, financial_account_id, source_kind, parser_config,
                    start_date, end_date, version, status, expires_at, account_import_version,
                    duplicate_policy_version, statement_fingerprint, summary_hash,
                    total_rows, imported_count, suppressed_count, skipped_count,
                    possible_duplicate_count, exact_override_count, payload_bytes,
                    created_at, updated_at)
                VALUES (
                    '{reviewId}', '{customerId}', '{accountId}', 'csv', jsonb_build_object(),
                    DATE '2025-01-01', DATE '2025-01-31', 1, 'Prepared', NOW() + INTERVAL '1 day', 0,
                    1, 'fingerprint', 'hash', 1, 1, 0, 0, 0, 0, 100, NOW(), NOW());
                INSERT INTO import_review_rows (
                    row_id, review_id, customer_id, source_line, source_row_number,
                    raw_values, description, normalized_description,
                    validation_codes, warnings, created_at, updated_at)
                VALUES (
                    '{rowId}', '{reviewId}', '{customerId}', 1, 1,
                    jsonb_build_object(), 'Row', 'row', jsonb_build_array(), jsonb_build_array(), NOW(), NOW());
                """);

            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);

            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM import_reviews")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM import_review_rows")));
            Assert.True((bool)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT suggested_category_id IS NULL AND selected_category_id IS NULL AND category_decided = FALSE FROM import_review_rows"))!);
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }
}
