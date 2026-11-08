using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using argus.Server.Data;
using argus.Server.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace argus.Server.Tests;

public sealed class TransactionConcurrencyTests(KitFactory factory) : TransactionTestBase(factory)
{
    private const string PreparePath = "/v1/finance/transactions/operations/prepare";

    private Task<HttpResponseMessage> PrepareResponseAsync(string session,
                                                           Guid operationId,
                                                           object selection,
                                                           object changes) =>
        SendAsync(Post(
            PreparePath,
            Json(new { operation_id = operationId, selection, changes }),
            session: session));

    private async Task<JsonElement> PrepareAsync(string session,
                                                 Guid operationId,
                                                 object selection,
                                                 object changes)
    {
        var response = await PrepareResponseAsync(session, operationId, selection, changes);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await BodyAsync(response);
    }

    private static object Explicit(params (Guid Id, int Version)[] members) => new
    {
        mode = "explicit",
        transactions = members.Select(member => new
        {
            id = member.Id,
            expected_version = member.Version,
        }).ToArray(),
    };

    private static HttpRequestMessage ApplyRequest(string session, JsonElement operation)
    {
        var id = operation.GetProperty("operation").GetProperty("operation_id").GetGuid();

        return Post(
            $"/v1/finance/transactions/operations/{id}/apply",
            Json(new
            {
                confirmation_hash = operation.GetProperty("operation").GetProperty("confirmation_hash").GetString(),
                expected_selected_count = operation.GetProperty("operation").GetProperty("selected_count").GetInt32(),
                confirm = true,
            }),
            session: session);
    }

    [Fact]
    public async Task CleanupPreservesUnexpiredCrossPairsWithSharedOperationIds()
    {
        var (firstSession, firstCustomer) = await NewCustomerAsync();
        var firstAccount = await CreateAccountAsync(firstSession, "First cleanup account");
        var (_, firstCategory) = await DefaultCategoriesAsync(firstSession);
        var firstStatement = await SeedStatementAsync(firstCustomer, firstAccount);
        var firstRow = await SeedTransactionAsync(firstCustomer, firstAccount, firstStatement, firstCategory, new DateOnly(2024, 1, 1), "First cleanup row", -1m);

        var (secondSession, secondCustomer) = await NewCustomerAsync();
        var secondAccount = await CreateAccountAsync(secondSession, "Second cleanup account");
        var (_, secondCategory) = await DefaultCategoriesAsync(secondSession);
        var secondStatement = await SeedStatementAsync(secondCustomer, secondAccount);
        var secondRow = await SeedTransactionAsync(secondCustomer, secondAccount, secondStatement, secondCategory, new DateOnly(2024, 1, 1), "Second cleanup row", -2m);

        Guid firstOperationId = Guid.NewGuid();
        Guid secondOperationId = Guid.NewGuid();
        await PrepareAsync(firstSession, firstOperationId, Explicit((firstRow, 1)), new { is_hidden_by_user = true });
        var firstLive = await PrepareAsync(firstSession, secondOperationId, Explicit((firstRow, 1)), new { is_hidden_by_user = true });
        var secondLive = await PrepareAsync(secondSession, firstOperationId, Explicit((secondRow, 1)), new { is_hidden_by_user = true });
        await PrepareAsync(secondSession, secondOperationId, Explicit((secondRow, 1)), new { is_hidden_by_user = true });

        var firstPayloadBefore = await StringScalarAsync($"""
            SELECT action::text || '|' || target_stamps::text || '|' || confirmation_hash
            FROM transaction_operations
            WHERE customer_id = '{firstCustomer}' AND operation_id = '{secondOperationId}'
            """);
        var secondPayloadBefore = await StringScalarAsync($"""
            SELECT action::text || '|' || target_stamps::text || '|' || confirmation_hash
            FROM transaction_operations
            WHERE customer_id = '{secondCustomer}' AND operation_id = '{firstOperationId}'
            """);
        Assert.NotNull(firstPayloadBefore);
        Assert.NotNull(secondPayloadBefore);

        await ExecuteSqlParamsAsync("""
            UPDATE transaction_operations
            SET captured_at = clock_timestamp() - interval '20 minutes',
                expires_at = clock_timestamp() - interval '5 minutes'
            WHERE (customer_id = @first_customer AND operation_id = @first_operation)
               OR (customer_id = @second_customer AND operation_id = @second_operation)
            """,
            ("first_customer", firstCustomer), ("first_operation", firstOperationId),
            ("second_customer", secondCustomer), ("second_operation", secondOperationId));

        await Factory.Services.GetRequiredService<TransactionStore>().CleanupExpiredAsync();

        Assert.Equal("Expired", await StringScalarAsync($"""
            SELECT status FROM transaction_operations
            WHERE customer_id = '{firstCustomer}' AND operation_id = '{firstOperationId}'
            """));
        Assert.Equal("Expired", await StringScalarAsync($"""
            SELECT status FROM transaction_operations
            WHERE customer_id = '{secondCustomer}' AND operation_id = '{secondOperationId}'
            """));
        Assert.Equal("Prepared", await StringScalarAsync($"""
            SELECT status FROM transaction_operations
            WHERE customer_id = '{firstCustomer}' AND operation_id = '{secondOperationId}'
            """));
        Assert.Equal("Prepared", await StringScalarAsync($"""
            SELECT status FROM transaction_operations
            WHERE customer_id = '{secondCustomer}' AND operation_id = '{firstOperationId}'
            """));
        Assert.Equal(firstPayloadBefore, await StringScalarAsync($"""
            SELECT action::text || '|' || target_stamps::text || '|' || confirmation_hash
            FROM transaction_operations
            WHERE customer_id = '{firstCustomer}' AND operation_id = '{secondOperationId}'
            """));
        Assert.Equal(secondPayloadBefore, await StringScalarAsync($"""
            SELECT action::text || '|' || target_stamps::text || '|' || confirmation_hash
            FROM transaction_operations
            WHERE customer_id = '{secondCustomer}' AND operation_id = '{firstOperationId}'
            """));
        Assert.Equal(1L, await ScalarAsync($"""
            SELECT COUNT(*) FROM transaction_operation_items
            WHERE customer_id = '{firstCustomer}' AND operation_id = '{secondOperationId}'
            """));
        Assert.Equal(1L, await ScalarAsync($"""
            SELECT COUNT(*) FROM transaction_operation_items
            WHERE customer_id = '{secondCustomer}' AND operation_id = '{firstOperationId}'
            """));

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(ApplyRequest(firstSession, firstLive))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(ApplyRequest(secondSession, secondLive))).StatusCode);
        Assert.Equal(1L, await ScalarAsync($"SELECT is_hidden_by_user::int FROM transactions WHERE id = '{firstRow}'"));
        Assert.Equal(1L, await ScalarAsync($"SELECT is_hidden_by_user::int FROM transactions WHERE id = '{secondRow}'"));
    }

    [Fact]
    public async Task CleanupDoesNotBlockOrAlterAnInFlightApplyForALiveCrossPair()
    {
        var (firstSession, firstCustomer) = await NewCustomerAsync();
        var firstAccount = await CreateAccountAsync(firstSession, "First apply account");
        var (_, firstCategory) = await DefaultCategoriesAsync(firstSession);
        var firstStatement = await SeedStatementAsync(firstCustomer, firstAccount);
        var firstRow = await SeedTransactionAsync(firstCustomer, firstAccount, firstStatement, firstCategory, new DateOnly(2024, 1, 1), "Live apply row", -1m);
        var (secondSession, secondCustomer) = await NewCustomerAsync();
        var secondAccount = await CreateAccountAsync(secondSession, "Second apply account");
        var (_, secondCategory) = await DefaultCategoriesAsync(secondSession);
        var secondStatement = await SeedStatementAsync(secondCustomer, secondAccount);
        var secondRow = await SeedTransactionAsync(secondCustomer, secondAccount, secondStatement, secondCategory, new DateOnly(2024, 1, 1), "Second apply row", -2m);

        Guid expiredFirstId = Guid.NewGuid();
        Guid liveId = Guid.NewGuid();
        await PrepareAsync(firstSession, expiredFirstId, Explicit((firstRow, 1)), new { is_hidden_by_user = true });
        var liveOperation = await PrepareAsync(firstSession, liveId, Explicit((firstRow, 1)), new { is_hidden_by_user = true });
        await PrepareAsync(secondSession, liveId, Explicit((secondRow, 1)), new { is_hidden_by_user = true });
        await ExecuteSqlParamsAsync("""
            UPDATE transaction_operations
            SET captured_at = clock_timestamp() - interval '20 minutes',
                expires_at = clock_timestamp() - interval '5 minutes'
            WHERE (customer_id = @first_customer AND operation_id = @first_operation)
               OR (customer_id = @second_customer AND operation_id = @second_operation)
            """,
            ("first_customer", firstCustomer), ("first_operation", expiredFirstId),
            ("second_customer", secondCustomer), ("second_operation", liveId));

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        int blockerPid;
        await using (var lockHeader = blocker.CreateCommand())
        {
            lockHeader.Transaction = blockerTransaction;
            lockHeader.CommandText = """
                SELECT 1 FROM transaction_operations
                WHERE customer_id = @customer AND operation_id = @operation FOR UPDATE
                """;
            lockHeader.Parameters.AddWithValue("customer", firstCustomer);
            lockHeader.Parameters.AddWithValue("operation", liveId);
            Assert.NotNull(await lockHeader.ExecuteScalarAsync());
        }
        await using (var pid = blocker.CreateCommand())
        {
            pid.Transaction = blockerTransaction;
            pid.CommandText = "SELECT pg_backend_pid()";
            blockerPid = (int)(await pid.ExecuteScalarAsync())!;
        }

        await using var observer = await db.OpenAsync();
        var applyTask = SendAsync(ApplyRequest(firstSession, liveOperation));
        var cleanupTask = Task.CompletedTask;
        try
        {
            await WaitForBlockedRequestAsync(observer, blockerPid);
            cleanupTask = Factory.Services.GetRequiredService<TransactionStore>().CleanupExpiredAsync();
            await cleanupTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(1L, await ScalarAsync($"""
                SELECT COUNT(*) FROM transaction_operation_items
                WHERE customer_id = '{firstCustomer}' AND operation_id = '{liveId}'
                """));
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
            await cleanupTask;
        }

        var response = await applyTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Applied", (await BodyAsync(response)).GetProperty("status").GetString());
        Assert.Equal(2L, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{firstRow}'"));
    }

    [Theory]
    [InlineData("category")]
    [InlineData("tag")]
    [InlineData("transaction")]
    public async Task ExpiryAfterLaterLockWaitRejectsAValidConfirmation(string lockedResource)
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, sourceCategoryId) = await DefaultCategoriesAsync(session);
        var targetResponse = await SendAsync(Post("/v1/finance/categories", Json(new { display_name = "Expiry lock target", kind = "Expense" }), session: session));
        Assert.Equal(HttpStatusCode.Created, targetResponse.StatusCode);
        var targetId = (await BodyAsync(targetResponse)).GetProperty("id").GetGuid();
        var tagId = await CreateTagAsync(session, "Expiry lock tag");
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, sourceCategoryId, new DateOnly(2024, 1, 3), "Expiry lock row", -3m);
        Guid operationId = Guid.NewGuid();
        var operation = await PrepareAsync(session, operationId, Explicit((rowId, 1)), new { category_id = targetId, add_tag_ids = new[] { tagId } });

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        var lockTable = lockedResource switch
        {
            "category" => "categories",
            "tag" => "merchant_tags",
            "transaction" => "transactions",
            _ => throw new InvalidOperationException("Unknown test lock"),
        };
        var lockId = lockedResource switch
        {
            "category" => targetId,
            "tag" => tagId,
            _ => rowId,
        };
        int blockerPid;
        await using (var lockCommand = blocker.CreateCommand())
        {
            lockCommand.Transaction = blockerTransaction;
            lockCommand.CommandText = $"SELECT 1 FROM {lockTable} WHERE customer_id = @customer AND id = @id FOR UPDATE";
            lockCommand.Parameters.AddWithValue("customer", customerId);
            lockCommand.Parameters.AddWithValue("id", lockId);
            Assert.NotNull(await lockCommand.ExecuteScalarAsync());
        }
        await using (var pid = blocker.CreateCommand())
        {
            pid.Transaction = blockerTransaction;
            pid.CommandText = "SELECT pg_backend_pid()";
            blockerPid = (int)(await pid.ExecuteScalarAsync())!;
        }

        var (confirmationHash, expiresAt) = await SetCoherentShortExpiryAsync(customerId, operationId, rowId, targetId, tagId, operation.GetProperty("operation").GetProperty("confirmation_hash").GetString()!);
        await using var observer = await db.OpenAsync();
        var applyTask = SendAsync(Post($"/v1/finance/transactions/operations/{operationId}/apply", Json(new { confirmation_hash = confirmationHash, expected_selected_count = 1, confirm = true }), session: session));

        try
        {
            await WaitForBlockedRequestAsync(observer, blockerPid);
            Assert.False(applyTask.IsCompleted);
            await WaitForDatabaseTimeAsync(observer, expiresAt);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }

        var response = await applyTask;
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("operation_expired", await ErrorKindAsync(response));
        Assert.Equal(1L, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
        Assert.Equal(sourceCategoryId, (await ScalarGuidAsync($"SELECT category_id FROM transactions WHERE id = '{rowId}'"))!.Value);
        Assert.Equal(0L, await ScalarAsync($"SELECT COUNT(*) FROM transaction_merchant_tags WHERE transaction_id = '{rowId}'"));
        Assert.Equal("Expired", await StringScalarAsync($"""
            SELECT status FROM transaction_operations
            WHERE customer_id = '{customerId}' AND operation_id = '{operationId}'
            """));
        Assert.Equal(0L, await ScalarAsync($"""
            SELECT COUNT(*) FROM transaction_operations
            WHERE customer_id = '{customerId}' AND operation_id = '{operationId}'
              AND (status = 'Applied' OR applied_at IS NOT NULL)
            """));
    }

    private async Task<(string Hash, DateTime ExpiresAt)> SetCoherentShortExpiryAsync(Guid customerId,
                                                                                      Guid operationId,
                                                                                      Guid rowId,
                                                                                      Guid targetId,
                                                                                      Guid tagId,
                                                                                      string originalHash)
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        DateTime capturedAt;
        DateTime originalExpiresAt;
        DateTime databaseNow;
        string targetJson;
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = """
                SELECT captured_at, expires_at, target_stamps::text, clock_timestamp()
                FROM transaction_operations
                WHERE customer_id = @customer AND operation_id = @operation
                """;
            select.Parameters.AddWithValue("customer", customerId);
            select.Parameters.AddWithValue("operation", operationId);
            await using var reader = await select.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            capturedAt = reader.GetFieldValue<DateTime>(0);
            originalExpiresAt = reader.GetFieldValue<DateTime>(1);
            targetJson = reader.GetString(2);
            databaseNow = reader.GetFieldValue<DateTime>(3);
        }

        TransactionAction action = new TransactionAction(targetId, [tagId], [], null);
        var storeType = typeof(TransactionStore);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var targetStamp = storeType.GetMethod("DeserializeTargets", flags)!.Invoke(null, [targetJson])!;
        var targetCanonical = (string)storeType.GetMethod("TargetsCanonical", flags)!.Invoke(null, [targetStamp])!;
        (Guid Id, int Version)[] members = new (Guid Id, int Version)[] { (rowId, 1) };
        var memberDigest = (string)storeType.GetMethod("MembersDigest", flags)!.Invoke(null, [members])!;
        string Hash(DateTime expiry) => (string)storeType.GetMethod("ConfirmationHash", flags)!
            .Invoke(null,
                [customerId, operationId, "explicit", memberDigest, action, targetCanonical,
                    capturedAt, expiry, 1, 1, 0])!;

        // Establish that the fixture uses the same valid confirmation identity
        // before changing its expiry for a bounded lock-wait test.
        Assert.Equal(originalHash, Hash(originalExpiresAt));
        var shortExpiry = databaseNow.AddSeconds(3);
        var shortHash = Hash(shortExpiry);
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = """
                UPDATE transaction_operations
                SET expires_at = @expiry, confirmation_hash = @hash
                WHERE customer_id = @customer AND operation_id = @operation
                """;
            update.Parameters.AddWithValue("expiry", shortExpiry);
            update.Parameters.AddWithValue("hash", shortHash);
            update.Parameters.AddWithValue("customer", customerId);
            update.Parameters.AddWithValue("operation", operationId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        return (shortHash, shortExpiry);
    }

    private static async Task WaitForBlockedRequestAsync(NpgsqlConnection observer, int blockerPid, long requiredCount = 1)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(2))
        {
            await using var command = observer.CreateCommand();
            command.CommandText = """
                WITH RECURSIVE blocked(pid) AS (
                    SELECT pid FROM pg_stat_activity
                    WHERE @blocker = ANY(pg_blocking_pids(pid)) AND state = 'active'
                    UNION
                    SELECT activity.pid FROM pg_stat_activity activity
                    JOIN blocked ON blocked.pid = ANY(pg_blocking_pids(activity.pid))
                    WHERE activity.state = 'active'
                )
                SELECT COUNT(*) FROM blocked
                """;
            command.Parameters.AddWithValue("blocker", blockerPid);
            if ((long)(await command.ExecuteScalarAsync())! >= requiredCount)
            {
                return;
            }

            await Task.Delay(25);
        }
        Assert.Fail($"The expected {requiredCount} requests did not reach the database lock wait.");
    }

    private static async Task<int> LockCustomerForTestAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid customerId)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT 1 FROM customers WHERE id = @customer FOR UPDATE";
            command.Parameters.AddWithValue("customer", customerId);
            Assert.NotNull(await command.ExecuteScalarAsync());
        }
        await using var pid = connection.CreateCommand();
        pid.Transaction = transaction;
        pid.CommandText = "SELECT pg_backend_pid()";

        return (int)(await pid.ExecuteScalarAsync())!;
    }

    private static async Task<int> LockFinanceRowForTestAsync(NpgsqlConnection connection,
                                                              NpgsqlTransaction transaction,
                                                              Guid customerId,
                                                              Guid id,
                                                              string table)
    {
        if (table is not ("financial_accounts" or "merchant_tags"))
        {
            throw new InvalidOperationException("Unknown test lock table");
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"SELECT 1 FROM {table} WHERE customer_id = @customer AND id = @id FOR UPDATE";
            command.Parameters.AddWithValue("customer", customerId);
            command.Parameters.AddWithValue("id", id);
            Assert.NotNull(await command.ExecuteScalarAsync());
        }
        await using var pid = connection.CreateCommand();
        pid.Transaction = transaction;
        pid.CommandText = "SELECT pg_backend_pid()";

        return (int)(await pid.ExecuteScalarAsync())!;
    }

    // Waits on the database wall clock instead of a client monotonic timer.
    // Hosts running this suite step their clock backward periodically (NTP or
    // VM time sync), which can end a fixed client bound before the database
    // clock reaches the expiry. pg_sleep_until re-checks the database clock
    // and sleeps longer after a backward step. The one-second margin covers
    // the gap between this wait returning and the blocked request re-checking
    // expiry.
    private static async Task WaitForDatabaseTimeAsync(NpgsqlConnection observer, DateTime expiresAt)
    {
        await using var command = observer.CreateCommand();
        command.CommandText = "SELECT pg_sleep_until(@expiry)";
        command.CommandTimeout = 60;
        command.Parameters.AddWithValue("expiry", expiresAt.AddSeconds(1));
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ConcurrentAppliesOfOneOperationReturnOneReceiptWithoutDuplicateEffects()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid target = Guid.NewGuid();
        var categoryResponse = await SendAsync(Post("/v1/finance/categories", Json(new { display_name = "Concurrent target", kind = "Expense" }), session: session));
        target = (await BodyAsync(categoryResponse)).GetProperty("id").GetGuid();
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2024, 1, 1), "Concurrent row", -1m, 1);

        var operation = await PrepareAsync(session, Guid.NewGuid(), Explicit((rowId, 1)), new { category_id = target });

        var responses = await Task.WhenAll(SendAsync(ApplyRequest(session, operation)), SendAsync(ApplyRequest(session, operation)));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(BodyAsync));
        Assert.Equal(1, bodies.Count(body => !body.GetProperty("replayed").GetBoolean()));
        Assert.Equal(1, bodies.Count(body => body.GetProperty("replayed").GetBoolean()));
        Assert.Equal(2, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
        Assert.Equal(1L, await ScalarAsync($"SELECT COUNT(*) FROM transaction_operations WHERE customer_id = '{customerId}' AND operation_id = '{operation.GetProperty("operation").GetProperty("operation_id").GetGuid()}'"));
    }

    [Fact]
    public async Task AbandonAndApplyRaceLeavesOneTerminalTruth()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var categoryResponse = await SendAsync(Post("/v1/finance/categories", Json(new { display_name = "Race target", kind = "Expense" }), session: session));
        var target = (await BodyAsync(categoryResponse)).GetProperty("id").GetGuid();
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2024, 1, 2), "Race row", -2m, 1);

        var operation = await PrepareAsync(session, Guid.NewGuid(), Explicit((rowId, 1)), new { category_id = target });
        var operationId = operation.GetProperty("operation").GetProperty("operation_id").GetGuid();

        var applyTask = SendAsync(ApplyRequest(session, operation));
        var abandonTask = SendAsync(Post($"/v1/finance/transactions/operations/{operationId}/abandon", "{}", session: session));
        var applyResponse = await applyTask;
        var abandonResponse = await abandonTask;

        var applyBody = await BodyAsync(applyResponse);
        var abandonBody = await BodyAsync(abandonResponse);
        if (applyResponse.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal("Applied", applyBody.GetProperty("status").GetString());
            Assert.Equal("Applied", abandonBody.GetProperty("status").GetString());
            Assert.Equal(2, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
            Assert.Equal(target, (Guid)(await ScalarGuidAsync($"SELECT category_id FROM transactions WHERE id = '{rowId}'"))!);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Gone, applyResponse.StatusCode);
            Assert.Equal("operation_abandoned", await ErrorKindAsync(applyResponse));
            Assert.Equal("Abandoned", abandonBody.GetProperty("status").GetString());
            Assert.Equal(1, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
            Assert.Equal(expenseId, (Guid)(await ScalarGuidAsync($"SELECT category_id FROM transactions WHERE id = '{rowId}'"))!);
        }
        Assert.InRange((int)abandonResponse.StatusCode, 200, 299);
    }

    [Fact]
    public async Task ExpiryCrossingAccountLockWaitRefusesWholly()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var categoryResponse = await SendAsync(Post("/v1/finance/categories", Json(new { display_name = "Expiry target", kind = "Expense" }), session: session));
        var target = (await BodyAsync(categoryResponse)).GetProperty("id").GetGuid();
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2024, 1, 3), "Expiry row", -3m, 1);
        var operation = await PrepareAsync(session, Guid.NewGuid(), Explicit((rowId, 1)), new { category_id = target });
        var operationId = operation.GetProperty("operation").GetProperty("operation_id").GetGuid();

        var db = Factory.CreateDb();
        await using var lockConnection = await db.OpenAsync();
        await using var lockTransaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.Transaction = lockTransaction;
            lockCommand.CommandText = """
                SELECT 1 FROM financial_accounts
                WHERE customer_id = @customer_id AND id = @id FOR UPDATE
                """;
            lockCommand.Parameters.AddWithValue("customer_id", customerId);
            lockCommand.Parameters.AddWithValue("id", accountId);
            Assert.NotNull(await lockCommand.ExecuteScalarAsync());
        }

        var applyTask = SendAsync(ApplyRequest(session, operation));
        await Task.Delay(750);

        await ExecuteSqlParamsAsync(
            """
            UPDATE transaction_operations
            SET captured_at = NOW() - interval '1 hour',
                expires_at = NOW() - interval '59 minutes'
            WHERE customer_id = @customer_id AND operation_id = @operation_id
            """,
            ("customer_id", customerId),
            ("operation_id", operationId));

        await lockTransaction.RollbackAsync();

        var response = await applyTask;
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("operation_expired", await ErrorKindAsync(response));
        Assert.Equal(1, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
        Assert.Equal(expenseId, (Guid)(await ScalarGuidAsync($"SELECT category_id FROM transactions WHERE id = '{rowId}'"))!);
        Assert.Equal("Expired", (await StringScalarAsync($"SELECT status FROM transaction_operations WHERE operation_id = '{operationId}'")));
        Assert.Equal(0L, await ScalarAsync($"SELECT COUNT(*) FROM transaction_operation_items WHERE operation_id = '{operationId}'"));
    }

    [Fact]
    public async Task SameIdPreparationsConvergeAndDifferentBodiesConflict()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2024, 1, 4), "Prepare race row", -4m, 1);
        Guid operationId = Guid.NewGuid();
        var selection = Explicit((rowId, 1));
        var changes = new { category_id = expenseId };

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        var blockerPid = await LockCustomerForTestAsync(blocker, blockerTransaction, customerId);
        await using var observer = await db.OpenAsync();
        var first = PrepareResponseAsync(session, operationId, selection, changes);
        var second = PrepareResponseAsync(session, operationId, selection, changes);
        try
        {
            await WaitForBlockedRequestAsync(observer, blockerPid, requiredCount: 2);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }
        var bodies = await Task.WhenAll(first, second);
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Created }, bodies.Select(body => body.StatusCode).OrderBy(status => (int)status).ToArray());
        Assert.Equal(1L, await ScalarAsync($"SELECT COUNT(*) FROM transaction_operations WHERE operation_id = '{operationId}'"));
        Assert.Equal(1L, await ScalarAsync($"SELECT COUNT(*) FROM transaction_operation_items WHERE operation_id = '{operationId}'"));

        Guid differentId = Guid.NewGuid();
        await using var secondBlocker = await db.OpenAsync();
        await using var secondBlockerTransaction = await secondBlocker.BeginTransactionAsync();
        var secondBlockerPid = await LockCustomerForTestAsync(secondBlocker, secondBlockerTransaction, customerId);
        var differentFirst = PrepareResponseAsync(session, differentId, selection, changes);
        var differentSecond = PrepareResponseAsync(session, differentId, selection, new { is_hidden_by_user = true });
        try
        {
            await WaitForBlockedRequestAsync(observer, secondBlockerPid, requiredCount: 2);
        }
        finally
        {
            await secondBlockerTransaction.RollbackAsync();
        }
        var differentResponses = await Task.WhenAll(differentFirst, differentSecond);
        Assert.Equal(1, differentResponses.Count(response => response.StatusCode == HttpStatusCode.Created));
        var conflict = Assert.Single(differentResponses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("idempotency_conflict", await ErrorKindAsync(conflict));
        Assert.Equal(1L, await ScalarAsync($"SELECT COUNT(*) FROM transaction_operations WHERE customer_id = '{customerId}' AND operation_id = '{differentId}'"));
        Assert.Equal(1L, await ScalarAsync($"SELECT COUNT(*) FROM transaction_operation_items WHERE customer_id = '{customerId}' AND operation_id = '{differentId}'"));
        Assert.Equal(1L, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
    }

    [Fact]
    public async Task QuotaBoundarySerializesConcurrentPreparations()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2024, 1, 5), "Quota race row", -5m, 1);
        for (var index = 0; index < 4; index++)
        {
            await PrepareAsync(session, Guid.NewGuid(), Explicit((rowId, 1)), new { category_id = expenseId });
        }

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        var blockerPid = await LockCustomerForTestAsync(blocker, blockerTransaction, customerId);
        await using var observer = await db.OpenAsync();
        var first = PrepareResponseAsync(session, Guid.NewGuid(), Explicit((rowId, 1)), new { category_id = expenseId });
        var second = PrepareResponseAsync(session, Guid.NewGuid(), Explicit((rowId, 1)), new { category_id = expenseId });
        try
        {
            await WaitForBlockedRequestAsync(observer, blockerPid, requiredCount: 2);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }
        var responses = await Task.WhenAll(first, second);
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.Equal(5L, await ScalarAsync($"SELECT COUNT(*) FROM transaction_operations WHERE customer_id = '{customerId}' AND status = 'Prepared'"));
        Assert.Equal(5L, await ScalarAsync($"SELECT COUNT(*) FROM transaction_operation_items WHERE customer_id = '{customerId}'"));
    }

    [Fact]
    public async Task ReversedCrossAccountSelectionsDoNotDeadlock()
    {
        var (session, customerId) = await NewCustomerAsync();
        var firstAccount = await CreateAccountAsync(session, "Cheque");
        var secondAccount = await CreateAccountAsync(session, "Savings");
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var categoryResponse = await SendAsync(Post("/v1/finance/categories", Json(new { display_name = "Cross account target", kind = "Expense" }), session: session));
        var target = (await BodyAsync(categoryResponse)).GetProperty("id").GetGuid();
        var firstStatement = await SeedStatementAsync(customerId, firstAccount);
        var secondStatement = await SeedStatementAsync(customerId, secondAccount);
        var firstRow = await SeedTransactionAsync(customerId, firstAccount, firstStatement, expenseId, new DateOnly(2024, 1, 6), "First account row", -6m, 1);
        var secondRow = await SeedTransactionAsync(customerId, secondAccount, secondStatement, expenseId, new DateOnly(2024, 1, 7), "Second account row", -7m, 1);

        var firstOperation = await PrepareAsync(session, Guid.NewGuid(), Explicit((firstRow, 1), (secondRow, 1)), new { category_id = target });
        var reversedOperation = await PrepareAsync(session, Guid.NewGuid(), Explicit((secondRow, 1), (firstRow, 1)), new { category_id = target });

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        var blockerPid = await LockFinanceRowForTestAsync(blocker, blockerTransaction, customerId, firstAccount, "financial_accounts");
        await using var observer = await db.OpenAsync();
        var firstApply = SendAsync(ApplyRequest(session, firstOperation));
        var secondApply = SendAsync(ApplyRequest(session, reversedOperation));
        try
        {
            await WaitForBlockedRequestAsync(observer, blockerPid, requiredCount: 2);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }
        var responses = await Task.WhenAll(firstApply, secondApply);
        Assert.DoesNotContain(responses, response => response.StatusCode == HttpStatusCode.InternalServerError);
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2L, await ScalarAsync($"SELECT COUNT(*) FROM transactions WHERE customer_id = '{customerId}' AND version = 2"));
        Assert.Equal(0L, await ScalarAsync($"SELECT COUNT(*) FROM transactions WHERE customer_id = '{customerId}' AND version = 1"));
        Assert.Equal(2L, await ScalarAsync($"SELECT COUNT(*) FROM transactions WHERE customer_id = '{customerId}' AND category_id = '{target}'"));
    }

    [Fact]
    public async Task ConcurrentSingleEditsAllowExactlyOneVersionWinner()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var targetResponse = await SendAsync(Post("/v1/finance/categories", Json(new { display_name = "Single race target", kind = "Expense" }), session: session));
        var target = (await BodyAsync(targetResponse)).GetProperty("id").GetGuid();
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2024, 2, 1), "Single race row", -1m, 1);

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        int blockerPid;
        await using (var lockCommand = blocker.CreateCommand())
        {
            lockCommand.Transaction = blockerTransaction;
            lockCommand.CommandText = """
                SELECT 1 FROM transactions
                WHERE customer_id = @customer AND id = @row
                FOR UPDATE
                """;
            lockCommand.Parameters.AddWithValue("customer", customerId);
            lockCommand.Parameters.AddWithValue("row", rowId);
            Assert.NotNull(await lockCommand.ExecuteScalarAsync());
        }
        await using (var pid = blocker.CreateCommand())
        {
            pid.Transaction = blockerTransaction;
            pid.CommandText = "SELECT pg_backend_pid()";
            blockerPid = (int)(await pid.ExecuteScalarAsync())!;
        }

        await using var observer = await db.OpenAsync();
        var first = SendAsync(Patch($"/v1/finance/transactions/{rowId}", Json(new { expected_version = 1, changes = new { category_id = target } }), session: session));
        var second = SendAsync(Patch($"/v1/finance/transactions/{rowId}", Json(new { expected_version = 1, changes = new { is_hidden_by_user = true }, confirm = true, }), session: session));
        try
        {
            await WaitForBlockedRequestAsync(observer, blockerPid, requiredCount: 2);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }
        var responses = await Task.WhenAll(first, second);
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(2, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
    }

    [Fact]
    public async Task SingleEditAndBulkApplyCompeteOnTheOriginalVersion()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, sourceCategoryId) = await DefaultCategoriesAsync(session);
        var targetResponse = await SendAsync(Post("/v1/finance/categories", Json(new { display_name = "Single bulk target", kind = "Expense" }), session: session));
        var targetId = (await BodyAsync(targetResponse)).GetProperty("id").GetGuid();
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, sourceCategoryId, new DateOnly(2024, 2, 1), "Single bulk row", -1m);
        var operation = await PrepareAsync(session, Guid.NewGuid(), Explicit((rowId, 1)), new { category_id = targetId });

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        var blockerPid = await LockFinanceRowForTestAsync(blocker, blockerTransaction, customerId, accountId, "financial_accounts");
        await using var observer = await db.OpenAsync();
        var single = SendAsync(Patch($"/v1/finance/transactions/{rowId}", Json(new { expected_version = 1, changes = new { is_hidden_by_user = true }, confirm = true }), session: session));
        var bulk = SendAsync(ApplyRequest(session, operation));
        try
        {
            await WaitForBlockedRequestAsync(observer, blockerPid, requiredCount: 2);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }

        var singleResponse = await single;
        var bulkResponse = await bulk;
        Assert.Equal(1, new[] { singleResponse, bulkResponse }.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, new[] { singleResponse, bulkResponse }.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(2L, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
        if (singleResponse.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal("selection_stale", await ErrorKindAsync(bulkResponse));
            Assert.Equal(1L, await ScalarAsync($"SELECT is_hidden_by_user::int FROM transactions WHERE id = '{rowId}'"));
            Assert.Equal(sourceCategoryId, (await ScalarGuidAsync($"SELECT category_id FROM transactions WHERE id = '{rowId}'"))!.Value);
        }
        else
        {
            Assert.Equal("version_conflict", await ErrorKindAsync(singleResponse));
            Assert.Equal(0L, await ScalarAsync($"SELECT is_hidden_by_user::int FROM transactions WHERE id = '{rowId}'"));
            Assert.Equal(targetId, (await ScalarGuidAsync($"SELECT category_id FROM transactions WHERE id = '{rowId}'"))!.Value);
        }
    }

    [Fact]
    public async Task TagDeletionAndBulkApplyStayCoherent()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var tagId = await CreateTagAsync(session, "Race tag");
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2024, 2, 2), "Tag race row", -2m, 1);
        var operation = await PrepareAsync(session, Guid.NewGuid(), Explicit((rowId, 1)), new { add_tag_ids = new[] { tagId } });

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        var blockerPid = await LockFinanceRowForTestAsync(blocker, blockerTransaction, customerId, tagId, "merchant_tags");
        await using var observer = await db.OpenAsync();
        var applyTask = SendAsync(ApplyRequest(session, operation));
        var deleteTask = SendAsync(Delete($"/v1/finance/merchant-tags/{tagId}", session: session));
        try
        {
            await WaitForBlockedRequestAsync(observer, blockerPid, requiredCount: 2);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }
        var applyResponse = await applyTask;
        var deleteResponse = await deleteTask;

        Assert.DoesNotContain(new[] { applyResponse.StatusCode, deleteResponse.StatusCode }, status => status == HttpStatusCode.InternalServerError);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.Equal(0L, await ScalarAsync($"SELECT COUNT(*) FROM merchant_tags WHERE id = '{tagId}'"));
        Assert.Equal(0L, await ScalarAsync($"SELECT COUNT(*) FROM transaction_merchant_tags WHERE merchant_tag_id = '{tagId}'"));
        if (applyResponse.StatusCode == HttpStatusCode.OK)
        {
            // Apply adds the tag and deletion then removes it, so each real
            // metadata change advances the version once.
            Assert.Equal(3, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, applyResponse.StatusCode);
            Assert.Equal(1, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TagDeletionAndApplyBothOrderingsHaveCorrectVersions(bool applyFirst)
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, categoryId) = await DefaultCategoriesAsync(session);
        var tagId = await CreateTagAsync(session, "Ordered race tag");
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, categoryId, new DateOnly(2024, 2, 2), "Ordered tag row", -2m);
        var operation = await PrepareAsync(session, Guid.NewGuid(), Explicit((rowId, 1)), new { add_tag_ids = new[] { tagId } });

        if (applyFirst)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(ApplyRequest(session, operation))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(Delete($"/v1/finance/merchant-tags/{tagId}", session: session))).StatusCode);
            Assert.Equal(3L, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(Delete($"/v1/finance/merchant-tags/{tagId}", session: session))).StatusCode);
            var rejected = await SendAsync(ApplyRequest(session, operation));
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Equal("classification_changed", await ErrorKindAsync(rejected));
            Assert.Equal(1L, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
        }
        Assert.Equal(0L, await ScalarAsync($"SELECT COUNT(*) FROM transaction_merchant_tags WHERE transaction_id = '{rowId}'"));
    }

    [Fact]
    public async Task UncategorizedAssignmentAndCategoryDeletionShareForeignKeyLocksSafely()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (systemId, sourceCategoryId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, sourceCategoryId, new DateOnly(2024, 2, 4), "Foreign key race row", -4m);
        var operation = await PrepareAsync(session, Guid.NewGuid(), Explicit((rowId, 1)), new { category_id = systemId });

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        int blockerPid;
        await using (var command = blocker.CreateCommand())
        {
            command.Transaction = blockerTransaction;
            command.CommandText = "SELECT 1 FROM transactions WHERE customer_id = @customer AND id = @id FOR UPDATE";
            command.Parameters.AddWithValue("customer", customerId);
            command.Parameters.AddWithValue("id", rowId);
            Assert.NotNull(await command.ExecuteScalarAsync());
        }
        await using (var pid = blocker.CreateCommand())
        {
            pid.Transaction = blockerTransaction;
            pid.CommandText = "SELECT pg_backend_pid()";
            blockerPid = (int)(await pid.ExecuteScalarAsync())!;
        }
        await using var observer = await db.OpenAsync();
        var deletion = SendAsync(Delete($"/v1/finance/categories/{sourceCategoryId}", session: session));
        var apply = SendAsync(ApplyRequest(session, operation));
        try
        {
            await WaitForBlockedRequestAsync(observer, blockerPid, requiredCount: 2);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }

        var deletionResponse = await deletion;
        var applyResponse = await apply;
        Assert.Equal(HttpStatusCode.OK, deletionResponse.StatusCode);
        Assert.Contains(applyResponse.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        if (applyResponse.StatusCode == HttpStatusCode.Conflict)
        {
            Assert.Equal("selection_stale", await ErrorKindAsync(applyResponse));
        }
        Assert.Equal(systemId, (await ScalarGuidAsync($"SELECT category_id FROM transactions WHERE id = '{rowId}'"))!.Value);
        Assert.Equal(2L, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'"));
        Assert.Equal(0L, await ScalarAsync($"SELECT COUNT(*) FROM categories WHERE id = '{sourceCategoryId}'"));
    }

    [Fact]
    public async Task ParentVisibilityAndBulkApplyStayCoherent()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var targetResponse = await SendAsync(Post("/v1/finance/categories", Json(new { display_name = "Visibility race target", kind = "Expense" }), session: session));
        var target = (await BodyAsync(targetResponse)).GetProperty("id").GetGuid();
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2024, 2, 3), "Visibility race row", -3m, 1);
        var operation = await PrepareAsync(session, Guid.NewGuid(), Explicit((rowId, 1)), new { category_id = target });

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        int blockerPid;
        await using (var lockCommand = blocker.CreateCommand())
        {
            lockCommand.Transaction = blockerTransaction;
            lockCommand.CommandText = """
                SELECT 1 FROM financial_accounts
                WHERE customer_id = @customer AND id = @account
                FOR UPDATE
                """;
            lockCommand.Parameters.AddWithValue("customer", customerId);
            lockCommand.Parameters.AddWithValue("account", accountId);
            Assert.NotNull(await lockCommand.ExecuteScalarAsync());
        }
        await using (var pid = blocker.CreateCommand())
        {
            pid.Transaction = blockerTransaction;
            pid.CommandText = "SELECT pg_backend_pid()";
            blockerPid = (int)(await pid.ExecuteScalarAsync())!;
        }

        await using var observer = await db.OpenAsync();
        var applyTask = SendAsync(ApplyRequest(session, operation));
        var hideTask = SendAsync(Post($"/v1/finance/imports/{statementId}/hide", Json(new { expected_version = 1, expected_transaction_count = 1, confirm = true }), session: session));
        try
        {
            await WaitForBlockedRequestAsync(observer, blockerPid, requiredCount: 2);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }
        var applyResponse = await applyTask;
        var hideResponse = await hideTask;

        Assert.Equal(HttpStatusCode.OK, hideResponse.StatusCode);
        var version = await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'");
        if (applyResponse.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal(3, version);
            Assert.Equal(target, (Guid)(await ScalarGuidAsync($"SELECT category_id FROM transactions WHERE id = '{rowId}'"))!);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, applyResponse.StatusCode);
            Assert.Equal(2, version);
            Assert.Equal(expenseId, (Guid)(await ScalarGuidAsync($"SELECT category_id FROM transactions WHERE id = '{rowId}'"))!);
        }
        Assert.Equal(1L, await ScalarAsync($"SELECT is_hidden::int FROM statement_imports WHERE id = '{statementId}'"));
    }

    [Fact]
    public async Task ImportCommitAndBulkTagApplyStayCoherent()
    {
        var (session, customerId) = await NewCustomerAsync();
        var importAccount = await CreateAccountAsync(session, "Import account");
        var workspaceAccount = await CreateAccountAsync(session, "Workspace account");
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var tagId = await CreateTagAsync(session, "Shared import tag");
        var workspaceStatement = await SeedStatementAsync(customerId, workspaceAccount);
        var workspaceRow = await SeedTransactionAsync(customerId, workspaceAccount, workspaceStatement, expenseId, new DateOnly(2024, 3, 1), "Workspace tag row", -1m, 1);

        var file = StatementFixtureBuilder.BuildFnbCsv(1, chequeAccount: false).Bytes;
        var options = Json(new { financial_account_id = importAccount, source_kind = "fnb", start_date = "2024-01-01", end_date = "2024-01-31", currency = "ZAR", confirm_zar = true, });
        var reviewResponse = await SendAsync(MultipartPost("/v1/finance/imports/reviews", file, options, session: session));
        Assert.Equal(HttpStatusCode.Created, reviewResponse.StatusCode);
        var review = await BodyAsync(reviewResponse);
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var reviewVersion = review.GetProperty("summary").GetProperty("version").GetInt32();
        var rowId = review.GetProperty("page").GetProperty("rows")[0].GetProperty("row_id").GetGuid();

        var patch = await SendAsync(Patch($"/v1/finance/imports/reviews/{reviewId}", Json(new { expected_review_version = reviewVersion, decisions = new[] { new { row_id = rowId, selected_tag_ids = new[] { tagId } }, }, }), session: session));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await BodyAsync(patch);
        var commitBody = Json(new { expected_review_version = patched.GetProperty("version").GetInt32(), confirmed_summary_hash = patched.GetProperty("summary_hash").GetString(), });

        var operation = await PrepareAsync(session, Guid.NewGuid(), Explicit((workspaceRow, 1)), new { add_tag_ids = new[] { tagId } });

        var db = Factory.CreateDb();
        await using var lockConnection = await db.OpenAsync();
        await using var lockTransaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.Transaction = lockTransaction;
            lockCommand.CommandText = """
                SELECT 1 FROM merchant_tags
                WHERE customer_id = @customer_id AND id = @tag_id
                FOR UPDATE
                """;
            lockCommand.Parameters.AddWithValue("customer_id", customerId);
            lockCommand.Parameters.AddWithValue("tag_id", tagId);
            Assert.NotNull(await lockCommand.ExecuteScalarAsync());
        }
        int blockerPid;
        await using (var pid = lockConnection.CreateCommand())
        {
            pid.Transaction = lockTransaction;
            pid.CommandText = "SELECT pg_backend_pid()";
            blockerPid = (int)(await pid.ExecuteScalarAsync())!;
        }

        await using var observer = await db.OpenAsync();
        var commitTask = SendAsync(Post($"/v1/finance/imports/reviews/{reviewId}/commit", commitBody, session: session));
        var applyTask = SendAsync(ApplyRequest(session, operation));
        try
        {
            await WaitForBlockedRequestAsync(observer, blockerPid, requiredCount: 2);
        }
        finally
        {
            await lockTransaction.RollbackAsync();
        }

        var commitResponse = await commitTask;
        var applyResponse = await applyTask;
        Assert.Equal(HttpStatusCode.Created, commitResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);
        Assert.Equal(1L, await ScalarAsync($"SELECT COUNT(*) FROM transaction_merchant_tags WHERE merchant_tag_id = '{tagId}' AND transaction_id = '{workspaceRow}'"));
        Assert.Equal(1L, await ScalarAsync(
            $"""
            SELECT COUNT(*) FROM transaction_merchant_tags j
            JOIN transactions t ON t.id = j.transaction_id
            WHERE t.customer_id = '{customerId}' AND t.financial_account_id = '{importAccount}'
              AND j.merchant_tag_id = '{tagId}'
            """));
        Assert.Equal(2, await ScalarAsync($"SELECT version FROM transactions WHERE id = '{workspaceRow}'"));
    }

    [Fact]
    public async Task CategoryDeletionAndBulkApplyStayCoherent()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, sourceCategory) = await DefaultCategoriesAsync(session);
        var targetResponse = await SendAsync(Post("/v1/finance/categories", Json(new { display_name = "Separation target", kind = "Expense" }), session: session));
        var target = (await BodyAsync(targetResponse)).GetProperty("id").GetGuid();
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(customerId, accountId, statementId, sourceCategory, new DateOnly(2024, 1, 8), "Deletion row", -8m, 1);
        var operation = await PrepareAsync(session, Guid.NewGuid(), Explicit((rowId, 1)), new { category_id = target });

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        int blockerPid;
        await using (var lockCommand = blocker.CreateCommand())
        {
            lockCommand.Transaction = blockerTransaction;
            lockCommand.CommandText = """
                SELECT 1 FROM transactions
                WHERE customer_id = @customer AND id = @row
                FOR UPDATE
                """;
            lockCommand.Parameters.AddWithValue("customer", customerId);
            lockCommand.Parameters.AddWithValue("row", rowId);
            Assert.NotNull(await lockCommand.ExecuteScalarAsync());
        }
        await using (var pid = blocker.CreateCommand())
        {
            pid.Transaction = blockerTransaction;
            pid.CommandText = "SELECT pg_backend_pid()";
            blockerPid = (int)(await pid.ExecuteScalarAsync())!;
        }

        await using var observer = await db.OpenAsync();
        var applyTask = SendAsync(ApplyRequest(session, operation));
        var deleteTask = SendAsync(Delete($"/v1/finance/categories/{sourceCategory}", session: session));
        try
        {
            await WaitForBlockedRequestAsync(observer, blockerPid, requiredCount: 2);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }
        var applyResponse = await applyTask;
        var deleteResponse = await deleteTask;

        Assert.DoesNotContain(new[] { applyResponse.StatusCode, deleteResponse.StatusCode }, status => status == HttpStatusCode.InternalServerError);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Guid finalCategory = (Guid)(await ScalarGuidAsync($"SELECT category_id FROM transactions WHERE id = '{rowId}'"))!;
        var version = await ScalarAsync($"SELECT version FROM transactions WHERE id = '{rowId}'");
        if (applyResponse.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal(target, finalCategory);
            Assert.Equal(2, version);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, applyResponse.StatusCode);
            Assert.Equal(0L, await ScalarAsync($"SELECT COUNT(*) FROM categories WHERE customer_id = '{customerId}' AND id = '{sourceCategory}'"));
            Assert.Equal((Guid)(await ScalarGuidAsync($"SELECT id FROM categories WHERE customer_id = '{customerId}' AND is_system = TRUE"))!, finalCategory);
        }
    }

    private async Task<Guid?> ScalarGuidAsync(string sql)
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync() as Guid?;
    }
}
