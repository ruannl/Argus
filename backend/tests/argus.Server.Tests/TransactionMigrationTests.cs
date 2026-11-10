using argus.Server.Data;
using Npgsql;

namespace argus.Server.Tests;

public sealed class TransactionMigrationTests(KitFactory factory) : IClassFixture<KitFactory>
{
    private readonly KitFactory _factory = factory;

    [Fact]
    public async Task Populated0006UpgradesThroughSuggestionLearningAndKeepsManifestRowsWithoutLiveCascades()
    {
        var schema = "txn_migration_" + Guid.NewGuid().ToString("N");
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
                use.CommandText = $"SET search_path TO {schema}; " + "CREATE TABLE schema_migrations (" + "name TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());";
                await use.ExecuteNonQueryAsync();
            }

            foreach (var (_, sql) in Migrations.All.Take(6))
            {
                await using var apply = connection.CreateCommand();
                apply.CommandText = sql;
                await apply.ExecuteNonQueryAsync();
            }

            Guid customerId = Guid.NewGuid();
            Guid accountId = Guid.NewGuid();
            Guid categoryId = Guid.NewGuid();
            Guid systemCategoryId = Guid.NewGuid();
            Guid statementId = Guid.NewGuid();
            Guid transactionId = Guid.NewGuid();
            await using (var seed = connection.CreateCommand())
            {
                seed.CommandText = $"""
                    INSERT INTO customers (id, canonical_email, password_hash, created_at, updated_at)
                    VALUES ('{customerId}', 'txn-migration@example.test', 'hash', NOW(), NOW());
                    INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
                    VALUES ('{accountId}', '{customerId}', 'Cheque', 'ZAR', NOW(), NOW());
                    INSERT INTO categories (
                        id, customer_id, display_name, normalized_name, kind, is_system, is_default,
                        created_at, updated_at)
                    VALUES
                        ('{categoryId}', '{customerId}', 'Groceries', 'groceries', 'Expense', FALSE, FALSE, NOW(), NOW()),
                        ('{systemCategoryId}', '{customerId}', 'Uncategorised', 'uncategorised',
                         'Uncategorised', TRUE, TRUE, NOW(), NOW());
                    INSERT INTO statement_imports (
                        id, customer_id, financial_account_id, source_type, statement_label,
                        statement_fingerprint, start_date, end_date, created_at, updated_at)
                    VALUES (
                        '{statementId}', '{customerId}', '{accountId}', 'fnb_csv', 'statement',
                        'fingerprint', DATE '2025-01-01', DATE '2025-01-31', NOW(), NOW());
                    INSERT INTO transactions (
                        id, customer_id, financial_account_id, statement_import_id, transaction_date,
                        description, normalized_description, amount, category_id, source_type,
                        source_row_number, identity_key, created_at, updated_at)
                    VALUES (
                        '{transactionId}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-05',
                        'Migration row', 'migration row', -12.34, '{categoryId}', 'fnb_csv',
                        1, 'migration-identity', NOW(), NOW());
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            await using (var apply = connection.CreateCommand())
            {
                apply.CommandText = Migrations.All[6].Sql;
                await apply.ExecuteNonQueryAsync();
            }

            await using (var applyLearning = connection.CreateCommand())
            {
                applyLearning.CommandText = Migrations.All[7].Sql;
                await applyLearning.ExecuteNonQueryAsync();
            }

            await using (var applyCategorySuggestion = connection.CreateCommand())
            {
                applyCategorySuggestion.CommandText = Migrations.All[8].Sql;
                await applyCategorySuggestion.ExecuteNonQueryAsync();
            }

            await using (var applyCategoryConfirmation = connection.CreateCommand())
            {
                applyCategoryConfirmation.CommandText = Migrations.All[9].Sql;
                await applyCategoryConfirmation.ExecuteNonQueryAsync();
            }

            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM transactions")));
            Assert.True((bool)(await ScalarAsync(connection, "SELECT category_confirmed FROM transactions"))!);
            Assert.Equal(3L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM information_schema.columns " + $"WHERE table_schema = '{schema}' AND table_name = 'import_review_rows' " + "AND column_name IN ('suggested_category_id', 'selected_category_id', 'category_decided')")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM pg_indexes " + $"WHERE schemaname = '{schema}' AND indexname = 'transactions_customer_unconfirmed_idx'")));
            Assert.Equal("migration row", (string)(await ScalarAsync(connection, "SELECT merchant_key FROM transactions"))!);
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM pg_indexes " + $"WHERE schemaname = '{schema}' AND indexname = 'transactions_customer_merchant_key_idx'")));
            Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM merchant_aliases")));
            Assert.Equal(3L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM pg_constraint c " + "JOIN pg_class t ON t.oid = c.conrelid " + "JOIN pg_namespace n ON n.oid = t.relnamespace " + $"WHERE n.nspname = '{schema}' AND t.relname = 'merchant_aliases' AND c.contype = 'f'")));
            string aliasTagFk = (string)(await ScalarAsync(connection, "SELECT pg_get_constraintdef(c.oid) " + "FROM pg_constraint c " + "JOIN pg_class t ON t.oid = c.conrelid " + "JOIN pg_namespace n ON n.oid = t.relnamespace " + $"WHERE n.nspname = '{schema}' AND t.relname = 'merchant_aliases' " + "AND c.conname = 'merchant_aliases_merchant_tag_id_fkey'"))!;
            Assert.Contains("ON DELETE CASCADE", aliasTagFk);
            string aliasCategoryFk = (string)(await ScalarAsync(connection, "SELECT pg_get_constraintdef(c.oid) " + "FROM pg_constraint c " + "JOIN pg_class t ON t.oid = c.conrelid " + "JOIN pg_namespace n ON n.oid = t.relnamespace " + $"WHERE n.nspname = '{schema}' AND t.relname = 'merchant_aliases' " + "AND c.conname = 'merchant_aliases_category_id_fkey'"))!;
            Assert.Contains("ON DELETE SET NULL", aliasCategoryFk);
            Assert.Equal("migration-identity", (string)(await ScalarAsync(connection, "SELECT identity_key FROM transactions"))!);
            Assert.Equal("-12.34", (string)(await ScalarAsync(connection, "SELECT amount::text FROM transactions"))!);
            Assert.Equal("fingerprint", (string)(await ScalarAsync(connection, "SELECT statement_fingerprint FROM statement_imports"))!);
            Assert.Equal(2L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM information_schema.tables " + $"WHERE table_schema = '{schema}' AND table_name IN " + "('transaction_operations', 'transaction_operation_items')")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM pg_indexes " + $"WHERE schemaname = '{schema}' AND indexname = 'transactions_customer_date_idx'")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM pg_indexes " + $"WHERE schemaname = '{schema}' AND indexname = 'transactions_customer_amount_idx'")));

            Guid operationId = Guid.NewGuid();
            var stamp = DateTime.UtcNow;
            await using (var header = connection.CreateCommand())
            {
                header.CommandText = """
                    INSERT INTO transaction_operations (
                        customer_id, operation_id, status, selection_mode, request_digest,
                        confirmation_hash, action, target_stamps, selected_count,
                        changed_count, unchanged_count, captured_at, expires_at,
                        created_at, updated_at)
                    VALUES (
                        @customer_id, @operation_id, 'Prepared', 'explicit', 'digest',
                        'confirmation', '{"category_id": null, "add_tag_ids": [],
                                          "remove_tag_ids": [], "is_hidden_by_user": true}'::jsonb,
                        '{}'::jsonb, 1, 1, 0, @captured_at, @expires_at, NOW(), NOW())
                    """;
                header.Parameters.AddWithValue("customer_id", customerId);
                header.Parameters.AddWithValue("operation_id", operationId);
                header.Parameters.AddWithValue("captured_at", stamp);
                header.Parameters.AddWithValue("expires_at", stamp.AddMinutes(15));
                await header.ExecuteNonQueryAsync();
            }
            await using (var item = connection.CreateCommand())
            {
                item.CommandText = """
                    INSERT INTO transaction_operation_items (
                        customer_id, operation_id, transaction_id, transaction_version,
                        financial_account_id, statement_import_id, ordinal)
                    VALUES (@customer_id, @operation_id, @transaction_id, 1,
                            @account_id, @statement_id, 0)
                    """;
                item.Parameters.AddWithValue("customer_id", customerId);
                item.Parameters.AddWithValue("operation_id", operationId);
                item.Parameters.AddWithValue("transaction_id", transactionId);
                item.Parameters.AddWithValue("account_id", accountId);
                item.Parameters.AddWithValue("statement_id", statementId);
                await item.ExecuteNonQueryAsync();
            }

            // The manifest deliberately has no live-transaction foreign key:
            // deleting a captured member must leave the frozen scope intact.
            await using (var deleteTransaction = connection.CreateCommand())
            {
                deleteTransaction.CommandText = $"DELETE FROM transactions WHERE id = '{transactionId}'";
                await deleteTransaction.ExecuteNonQueryAsync();
            }
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM transaction_operation_items")));

            await using (var deleteHeader = connection.CreateCommand())
            {
                deleteHeader.CommandText = $"""
                    DELETE FROM transaction_operations
                    WHERE customer_id = '{customerId}' AND operation_id = '{operationId}'
                    """;
                await deleteHeader.ExecuteNonQueryAsync();
            }
            Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM transaction_operation_items")));
        }
        finally
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP SCHEMA IF EXISTS {schema} CASCADE";
            await drop.ExecuteNonQueryAsync();
            await using var reset = connection.CreateCommand();
            reset.CommandText = "SET search_path TO public";
            await reset.ExecuteNonQueryAsync();
        }
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync();
    }
}
