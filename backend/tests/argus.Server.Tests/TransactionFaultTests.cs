using System.Net;
using System.Text;
using System.Text.Json;
using argus.Server.Data;
using argus.Server.Transactions;
using Npgsql;

namespace argus.Server.Tests;

/// <summary>
/// Durable-effect failure evidence. Faults are injected only through the
/// test-only DI seam; no request or environment value can enable them.
/// </summary>
public sealed class TransactionFaultTests
{
    private sealed class StepFaultInjector(params string[] steps) : ITransactionFaultInjector
    {
        public void OnStep(string step)
        {
            if (steps.Contains(step, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("injected transaction fault");
            }
        }
    }

    private sealed class DatabaseErrorInjector(string canary) : ITransactionFaultInjector
    {
        public void OnStep(string step)
        {
            if (step != TransactionFaultPoints.AfterTransactionUpdate)
            {
                return;
            }

            // This deliberately produces a real PostgreSQL error whose
            // primary message contains a synthetic private-value canary.
            using var connection = new NpgsqlConnection(
                Db.BuildConnectionString(TestEnvironment.RequireTestDatabaseUrl()));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT CAST(@payload AS integer)";
            command.Parameters.AddWithValue("payload", canary);
            command.ExecuteNonQuery();
        }
    }

    private static HttpRequestMessage Post(string path, string json, string? session = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(Brand.ApplicationSecretHeader, TestEnvironment.ApiSecret);
        if (session is not null)
        {
            request.Headers.Add(Brand.SessionHeader, session);
        }
        return request;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>
    /// The caller must start the test host first (CreateClient) so migrations
    /// exist on a fresh database before these tables are truncated.
    /// </summary>
    private static async Task ResetAsync(KitFactory factory)
    {
        var db = factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "TRUNCATE import_review_rows, import_reviews, import_commit_receipts, import_quota_locks, " +
            "transaction_operation_items, transaction_operations, " +
            "transaction_merchant_tags, transactions, statement_imports, merchant_tags, " +
            "categories, financial_accounts, customers, sessions, email_verification_tokens, " +
            "password_reset_tokens CASCADE";
        await command.ExecuteNonQueryAsync();
        factory.Mailer.Clear();
    }

    private static async Task<string> NewSessionAsync(
        KitFactory factory,
        HttpClient client,
        string email)
    {
        var register = await client.SendAsync(Post(
            "/v1/register", JsonSerializer.Serialize(new
            {
                email,
                password = TestEnvironment.GoodPassword,
            })));
        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
        var verification = factory.Mailer.LastToken("verification", email);
        Assert.False(string.IsNullOrEmpty(verification));
        var verify = await client.SendAsync(Post(
            "/v1/verify", JsonSerializer.Serialize(new { token = verification })));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var login = await client.SendAsync(Post(
            "/v1/login", JsonSerializer.Serialize(new
            {
                email,
                password = TestEnvironment.GoodPassword,
            })));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await BodyAsync(login)).GetProperty("token").GetString()!;
    }

    private sealed record Workspace(
        Guid CustomerId,
        Guid AccountId,
        Guid SourceCategoryId,
        Guid TargetCategoryId,
        Guid TagId,
        Guid StatementId,
        Guid RowId);

    private static async Task<Workspace> SeedWorkspaceAsync(
        KitFactory factory,
        HttpClient client,
        string session)
    {
        var me = await client.SendAsync(Get("/v1/me", session));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var customerId = (await BodyAsync(me)).GetProperty("id").GetGuid();

        var accountResponse = await client.SendAsync(Post(
            "/v1/finance/accounts",
            JsonSerializer.Serialize(new { display_name = "Fault cheque" }),
            session));
        Assert.Equal(HttpStatusCode.Created, accountResponse.StatusCode);
        var accountId = (await BodyAsync(accountResponse)).GetProperty("id").GetGuid();

        var categoriesResponse = await client.SendAsync(Get("/v1/finance/categories", session));
        Assert.Equal(HttpStatusCode.OK, categoriesResponse.StatusCode);
        var categories = (await BodyAsync(categoriesResponse))
            .GetProperty("categories").EnumerateArray().ToArray();
        var sourceCategory = categories
            .First(category => category.GetProperty("kind").GetString() == "Expense")
            .GetProperty("id").GetGuid();

        var targetResponse = await client.SendAsync(Post(
            "/v1/finance/categories",
            JsonSerializer.Serialize(new { display_name = "Fault target", kind = "Expense" }),
            session));
        Assert.Equal(HttpStatusCode.Created, targetResponse.StatusCode);
        var targetCategory = (await BodyAsync(targetResponse)).GetProperty("id").GetGuid();

        var tagResponse = await client.SendAsync(Post(
            "/v1/finance/merchant-tags",
            JsonSerializer.Serialize(new { display_name = "Fault tag" }),
            session));
        Assert.Equal(HttpStatusCode.Created, tagResponse.StatusCode);
        var tagId = (await BodyAsync(tagResponse)).GetProperty("id").GetGuid();

        var db = factory.CreateDb();
        await using var connection = await db.OpenAsync();
        var statementId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        await using (var statement = connection.CreateCommand())
        {
            statement.CommandText = """
                INSERT INTO statement_imports (
                    id, customer_id, financial_account_id, source_type, statement_label,
                    statement_fingerprint, fingerprint_occurrence, start_date, end_date,
                    total_rows, imported_count, suppressed_count, skipped_count,
                    created_at, updated_at)
                VALUES (@id, @customer_id, @account_id, 'fnb_csv', 'Fault statement',
                        @fingerprint, 1, DATE '2024-01-01', DATE '2024-12-31',
                        0, 0, 0, 0, NOW(), NOW())
                """;
            statement.Parameters.AddWithValue("id", statementId);
            statement.Parameters.AddWithValue("customer_id", customerId);
            statement.Parameters.AddWithValue("account_id", accountId);
            statement.Parameters.AddWithValue("fingerprint", "fault-" + statementId.ToString("N"));
            await statement.ExecuteNonQueryAsync();
        }
        await using (var transaction = connection.CreateCommand())
        {
            transaction.CommandText = """
                INSERT INTO transactions (
                    id, customer_id, financial_account_id, statement_import_id,
                    transaction_date, description, normalized_description, amount,
                    category_id, source_type, source_row_number, identity_key,
                    occurrence_index, version, created_at, updated_at)
                VALUES (@id, @customer_id, @account_id, @statement_id,
                        DATE '2024-01-01', 'Fault row', 'fault row', -1.00,
                        @category_id, 'fnb_csv', 1, @identity_key, 1, 1, NOW(), NOW())
                """;
            transaction.Parameters.AddWithValue("id", rowId);
            transaction.Parameters.AddWithValue("customer_id", customerId);
            transaction.Parameters.AddWithValue("account_id", accountId);
            transaction.Parameters.AddWithValue("statement_id", statementId);
            transaction.Parameters.AddWithValue("category_id", sourceCategory);
            transaction.Parameters.AddWithValue("identity_key", "fault-" + rowId.ToString("N"));
            await transaction.ExecuteNonQueryAsync();
        }

        return new Workspace(
            customerId, accountId, sourceCategory, targetCategory, tagId, statementId, rowId);
    }

    private static HttpRequestMessage Get(string path, string session)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(Brand.ApplicationSecretHeader, TestEnvironment.ApiSecret);
        request.Headers.Add(Brand.SessionHeader, session);
        return request;
    }

    private static async Task<JsonElement> PrepareAsync(
        HttpClient client,
        string session,
        Workspace workspace,
        Guid operationId)
    {
        var response = await client.SendAsync(Post(
            "/v1/finance/transactions/operations/prepare",
            JsonSerializer.Serialize(new
            {
                operation_id = operationId,
                selection = new
                {
                    mode = "explicit",
                    transactions = new[]
                    {
                        new { id = workspace.RowId, expected_version = 1 },
                    },
                },
                changes = new
                {
                    category_id = workspace.TargetCategoryId,
                    add_tag_ids = new[] { workspace.TagId },
                },
            }),
            session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await BodyAsync(response);
    }

    private static HttpRequestMessage ApplyRequest(
        Guid operationId,
        JsonElement prepared,
        string session) =>
        Post(
            $"/v1/finance/transactions/operations/{operationId}/apply",
            JsonSerializer.Serialize(new
            {
                confirmation_hash = prepared.GetProperty("operation")
                    .GetProperty("confirmation_hash").GetString(),
                expected_selected_count = 1,
                confirm = true,
            }),
            session);

    [Theory]
    [InlineData(TransactionFaultPoints.AfterTransactionUpdate)]
    [InlineData(TransactionFaultPoints.AfterTagLinkMutation)]
    [InlineData(TransactionFaultPoints.BeforeReceiptWrite)]
    public async Task ApplyFaultLeavesNoDurableEffect(string faultPoint)
    {
        using var factory = new KitFactory(null, new StepFaultInjector(faultPoint));
        using var client = factory.CreateClient();
        await ResetAsync(factory);
        var session = await NewSessionAsync(factory, client, $"fault-{Guid.NewGuid():N}@example.test");
        var workspace = await SeedWorkspaceAsync(factory, client, session);
        var operationId = Guid.NewGuid();
        var prepared = await PrepareAsync(client, session, workspace, operationId);

        var response = await client.SendAsync(ApplyRequest(operationId, prepared, session));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        Assert.Equal(1, await ScalarAsync(
            factory, $"SELECT version FROM transactions WHERE id = '{workspace.RowId}'"));
        Assert.Equal(
            workspace.SourceCategoryId,
            await ScalarGuidAsync(
                factory, $"SELECT category_id FROM transactions WHERE id = '{workspace.RowId}'"));
        Assert.Equal(1L, await ScalarAsync(
            factory, $"SELECT COUNT(*) FROM transaction_operation_items WHERE operation_id = '{operationId}'"));
        Assert.Equal(0L, await ScalarAsync(
            factory,
            $"SELECT COUNT(*) FROM transaction_merchant_tags WHERE transaction_id = '{workspace.RowId}'"));
        Assert.Equal("Prepared", await ScalarStringAsync(
            factory, $"SELECT status FROM transaction_operations WHERE operation_id = '{operationId}'"));
        Assert.Equal("Prepared", await ScalarStringAsync(
            factory,
            $"SELECT CASE WHEN applied_at IS NULL THEN 'Prepared' ELSE 'Applied' END FROM transaction_operations WHERE operation_id = '{operationId}'"));
    }

    [Fact]
    public async Task FaultAfterReceiptWriteRollsBackAllMembersAndTheReceipt()
    {
        using var factory = new KitFactory(null, new StepFaultInjector(TransactionFaultPoints.AfterReceiptWrite));
        using var client = factory.CreateClient();
        await ResetAsync(factory);
        var session = await NewSessionAsync(factory, client, $"receipt-fault-{Guid.NewGuid():N}@example.test");
        var workspace = await SeedWorkspaceAsync(factory, client, session);
        var secondRowId = Guid.NewGuid();
        var db = factory.CreateDb();
        await using (var connection = await db.OpenAsync())
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO transactions (
                    id, customer_id, financial_account_id, statement_import_id,
                    transaction_date, description, normalized_description, amount,
                    category_id, source_type, source_row_number, identity_key,
                    occurrence_index, version, created_at, updated_at)
                VALUES (@id, @customer, @account, @statement, DATE '2024-01-02',
                        'Second fault row', 'second fault row', -2.00,
                        @category, 'fnb_csv', 2, @identity, 1, 1, NOW(), NOW())
                """;
            insert.Parameters.AddWithValue("id", secondRowId);
            insert.Parameters.AddWithValue("customer", workspace.CustomerId);
            insert.Parameters.AddWithValue("account", workspace.AccountId);
            insert.Parameters.AddWithValue("statement", workspace.StatementId);
            insert.Parameters.AddWithValue("category", workspace.SourceCategoryId);
            insert.Parameters.AddWithValue("identity", "fault-" + secondRowId.ToString("N"));
            await insert.ExecuteNonQueryAsync();
        }

        var operationId = Guid.NewGuid();
        var prepare = await client.SendAsync(Post(
            "/v1/finance/transactions/operations/prepare",
            JsonSerializer.Serialize(new
            {
                operation_id = operationId,
                selection = new
                {
                    mode = "explicit",
                    transactions = new[]
                    {
                        new { id = workspace.RowId, expected_version = 1 },
                        new { id = secondRowId, expected_version = 1 },
                    },
                },
                changes = new
                {
                    category_id = workspace.TargetCategoryId,
                    add_tag_ids = new[] { workspace.TagId },
                },
            }),
            session));
        Assert.Equal(HttpStatusCode.Created, prepare.StatusCode);
        var prepared = (await BodyAsync(prepare)).GetProperty("operation");
        var apply = await client.SendAsync(Post(
            $"/v1/finance/transactions/operations/{operationId}/apply",
            JsonSerializer.Serialize(new
            {
                confirmation_hash = prepared.GetProperty("confirmation_hash").GetString(),
                expected_selected_count = 2,
                confirm = true,
            }),
            session));
        Assert.Equal(HttpStatusCode.InternalServerError, apply.StatusCode);

        Assert.Equal(2L, await ScalarAsync(factory, $"""
            SELECT COUNT(*) FROM transactions
            WHERE customer_id = '{workspace.CustomerId}' AND id IN ('{workspace.RowId}', '{secondRowId}')
              AND version = 1 AND category_id = '{workspace.SourceCategoryId}'
            """));
        Assert.Equal(0L, await ScalarAsync(factory, $"""
            SELECT COUNT(*) FROM transaction_merchant_tags
            WHERE transaction_id IN ('{workspace.RowId}', '{secondRowId}')
            """));
        Assert.Equal(2L, await ScalarAsync(factory, $"""
            SELECT COUNT(*) FROM transaction_operation_items
            WHERE customer_id = '{workspace.CustomerId}' AND operation_id = '{operationId}'
            """));
        Assert.Equal("Prepared", await ScalarStringAsync(factory, $"""
            SELECT status FROM transaction_operations
            WHERE customer_id = '{workspace.CustomerId}' AND operation_id = '{operationId}'
              AND applied_at IS NULL
            """));
    }

    [Fact]
    public async Task DatabaseExceptionDoesNotLeakSyntheticPrivateValueToResponseOrLogs()
    {
        const string canary = "synthetic-db-private-913712";
        using var factory = new KitFactory(null, new DatabaseErrorInjector(canary));
        using var client = factory.CreateClient();
        await ResetAsync(factory);
        var session = await NewSessionAsync(factory, client, $"db-error-{Guid.NewGuid():N}@example.test");
        var workspace = await SeedWorkspaceAsync(factory, client, session);
        var operationId = Guid.NewGuid();
        var prepared = await PrepareAsync(client, session, workspace, operationId);
        factory.LogCapture.Clear();

        var response = await client.SendAsync(ApplyRequest(operationId, prepared, session));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("{\"error\":\"internal_server_error\"}", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(factory.LogCapture.Contains("finance_operation_failed"));
        Assert.True(factory.LogCapture.Contains("22P02"));
        Assert.False(factory.LogCapture.Contains(canary));
        Assert.Equal(1L, await ScalarAsync(factory,
            $"SELECT version FROM transactions WHERE id = '{workspace.RowId}'"));
        Assert.Equal("Prepared", await ScalarStringAsync(factory,
            $"SELECT status FROM transaction_operations WHERE operation_id = '{operationId}'"));
    }

    [Fact]
    public async Task LostResponseAfterCommitReplaysAcrossANewHostWithoutRepeatEffects()
    {
        var operationId = Guid.NewGuid();
        JsonElement prepared;
        Workspace workspace;
        string session;
        string email = $"lost-{Guid.NewGuid():N}@example.test";

        using (var firstFactory = new KitFactory(
                   null,
                   new StepFaultInjector(TransactionFaultPoints.AfterCommit)))
        {
            using var firstClient = firstFactory.CreateClient();
            await ResetAsync(firstFactory);
            session = await NewSessionAsync(firstFactory, firstClient, email);
            workspace = await SeedWorkspaceAsync(firstFactory, firstClient, session);
            prepared = await PrepareAsync(firstClient, session, workspace, operationId);

            var lost = await firstClient.SendAsync(ApplyRequest(operationId, prepared, session));
            Assert.Equal(HttpStatusCode.InternalServerError, lost.StatusCode);

            Assert.Equal(2, await ScalarAsync(
                firstFactory, $"SELECT version FROM transactions WHERE id = '{workspace.RowId}'"));
            Assert.Equal(workspace.TargetCategoryId, await ScalarGuidAsync(
                firstFactory,
                $"SELECT category_id FROM transactions WHERE id = '{workspace.RowId}'"));
            Assert.Equal("Applied", await ScalarStringAsync(
                firstFactory,
                $"SELECT status FROM transaction_operations WHERE operation_id = '{operationId}'"));
            Assert.Equal(1L, await ScalarAsync(
                firstFactory,
                $"SELECT COUNT(*) FROM transaction_merchant_tags WHERE transaction_id = '{workspace.RowId}'"));
        }

        using var secondFactory = new KitFactory();
        using var secondClient = secondFactory.CreateClient();
        var replay = await secondClient.SendAsync(ApplyRequest(operationId, prepared, session));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await BodyAsync(replay);
        Assert.True(replayBody.GetProperty("replayed").GetBoolean());
        Assert.Equal("Applied", replayBody.GetProperty("status").GetString());

        Assert.Equal(2, await ScalarAsync(
            secondFactory, $"SELECT version FROM transactions WHERE id = '{workspace.RowId}'"));
        Assert.Equal(1L, await ScalarAsync(
            secondFactory,
            $"SELECT COUNT(*) FROM transactions WHERE customer_id = '{workspace.CustomerId}' AND version = 2"));
        Assert.Equal(0L, await ScalarAsync(
            secondFactory,
            $"SELECT COUNT(*) FROM transaction_operation_items WHERE operation_id = '{operationId}'"));

        using var delete = new HttpRequestMessage(
            HttpMethod.Delete, $"/v1/finance/imports/{workspace.StatementId}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    expected_version = 1,
                    expected_transaction_count = 1,
                    confirm = true,
                }),
                Encoding.UTF8,
                "application/json"),
        };
        delete.Headers.Add(Brand.ApplicationSecretHeader, TestEnvironment.ApiSecret);
        delete.Headers.Add(Brand.SessionHeader, session);
        var deleted = await secondClient.SendAsync(delete);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        // An already-applied receipt is the truth even after its members have
        // been deleted and its original expiry has passed.
        var db = secondFactory.CreateDb();
        await using (var connection = await db.OpenAsync())
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = """
                UPDATE transaction_operations
                SET captured_at = clock_timestamp() - interval '20 minutes',
                    expires_at = clock_timestamp() - interval '5 minutes'
                WHERE customer_id = @customer AND operation_id = @operation
                """;
            update.Parameters.AddWithValue("customer", workspace.CustomerId);
            update.Parameters.AddWithValue("operation", operationId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        var replayAfterDelete = await secondClient.SendAsync(ApplyRequest(operationId, prepared, session));
        Assert.Equal(HttpStatusCode.OK, replayAfterDelete.StatusCode);
        var receiptAfterDelete = await BodyAsync(replayAfterDelete);
        Assert.True(receiptAfterDelete.GetProperty("replayed").GetBoolean());
        Assert.Equal(1, receiptAfterDelete.GetProperty("changed_count").GetInt32());
        Assert.Equal(0L, await ScalarAsync(
            secondFactory, $"SELECT COUNT(*) FROM transactions WHERE id = '{workspace.RowId}'"));
    }

    private static async Task<long> ScalarAsync(KitFactory factory, string sql)
    {
        var db = factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<Guid> ScalarGuidAsync(KitFactory factory, string sql)
    {
        var db = factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string?> ScalarStringAsync(KitFactory factory, string sql)
    {
        var db = factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync() as string;
    }
}
