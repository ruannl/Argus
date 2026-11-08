using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Xunit.Abstractions;

namespace argus.Server.Tests;

public sealed class TransactionMutationTests(KitFactory factory, ITestOutputHelper output)
    : TransactionTestBase(factory)
{
    private const string PreparePath = "/v1/finance/transactions/operations/prepare";

    private Task<HttpResponseMessage> PrepareResponseAsync(
        string session,
        Guid operationId,
        object selection,
        object changes) =>
        SendAsync(Post(
            PreparePath,
            Json(new { operation_id = operationId, selection, changes }),
            session: session));

    private async Task<JsonElement> PrepareAsync(
        string session,
        Guid operationId,
        object selection,
        object changes,
        HttpStatusCode expected = HttpStatusCode.Created)
    {
        var response = await PrepareResponseAsync(session, operationId, selection, changes);
        Assert.Equal(expected, response.StatusCode);
        return await BodyAsync(response);
    }

    private async Task<JsonElement> ApplyAsync(
        string session,
        JsonElement operation,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        var operationId = operation.GetProperty("operation").GetProperty("operation_id").GetGuid();
        var body = Json(new
        {
            confirmation_hash = operation.GetProperty("operation").GetProperty("confirmation_hash").GetString(),
            expected_selected_count = operation.GetProperty("operation").GetProperty("selected_count").GetInt32(),
            confirm = true,
        });
        var response = await SendAsync(Post(
            $"{PreparePath.Replace("/prepare", string.Empty)}/{operationId}/apply", body, session: session));
        Assert.Equal(expected, response.StatusCode);
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

    private static object AllMatching(object filters) => new { mode = "all_matching", filters };

    private async Task<Guid> CreateCategoryAsync(string session, string name)
    {
        var response = await SendAsync(Post(
            "/v1/finance/categories",
            Json(new { display_name = name, kind = "Expense" }),
            session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<long> VersionAsync(Guid transactionId) =>
        await ScalarAsync($"SELECT version FROM transactions WHERE id = '{transactionId}'");

    private async Task<string> ActionJsonAsync(Guid operationId) =>
        (await StringScalarAsync(
            $"SELECT action::text FROM transaction_operations WHERE operation_id = '{operationId}'"))!;

    private async Task<long> PreparedItemCountAsync(Guid operationId) =>
        await ScalarAsync(
            $"SELECT COUNT(*) FROM transaction_operation_items WHERE operation_id = '{operationId}'");

    [Fact]
    public async Task SingleEditUpdatesMetadataTraceablyAndChecksVersion()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var otherCategory = await CreateCategoryAsync(session, "Other expense");
        var statementId = await SeedStatementAsync(customerId, accountId);
        var keepTag = await CreateTagAsync(session, "Keep");
        var removeTag = await CreateTagAsync(session, "Remove");
        var addTag = await CreateTagAsync(session, "Add");
        var transactionId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 1, 5), "Source row", -12.34m, 3,
            tagIds: [keepTag, removeTag]);

        var sourceBefore = await SeededSourceAsync(transactionId);

        var first = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new
            {
                expected_version = 1,
                changes = new
                {
                    category_id = otherCategory,
                    add_tag_ids = new[] { addTag },
                    remove_tag_ids = new[] { removeTag },
                },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await BodyAsync(first);
        Assert.Equal(1, firstBody.GetProperty("selected_count").GetInt32());
        Assert.Equal(1, firstBody.GetProperty("changed_count").GetInt32());
        Assert.Equal(0, firstBody.GetProperty("unchanged_count").GetInt32());
        Assert.Equal(2, firstBody.GetProperty("transaction").GetProperty("version").GetInt32());
        Assert.Equal(otherCategory, firstBody.GetProperty("transaction").GetProperty("category").GetProperty("id").GetGuid());
        var tagNames = firstBody.GetProperty("transaction").GetProperty("merchant_tags")
            .EnumerateArray().Select(tag => tag.GetProperty("display_name").GetString()).ToArray();
        Assert.Equal(new[] { "Add", "Keep" }, tagNames);

        var noOp = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new
            {
                expected_version = 2,
                changes = new { category_id = otherCategory },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, noOp.StatusCode);
        var noOpBody = await BodyAsync(noOp);
        Assert.Equal(0, noOpBody.GetProperty("changed_count").GetInt32());
        Assert.Equal(2, noOpBody.GetProperty("transaction").GetProperty("version").GetInt32());

        var stale = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new
            {
                expected_version = 1,
                changes = new { category_id = expenseId },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("version_conflict", await ErrorKindAsync(stale));
        Assert.Equal(2, await VersionAsync(transactionId));
        Assert.Equal(otherCategory, (await ScalarGuidAsync(
            $"SELECT category_id FROM transactions WHERE id = '{transactionId}'")));

        var unconfirmed = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new
            {
                expected_version = 2,
                changes = new { is_hidden_by_user = true },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);

        var hidden = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new
            {
                expected_version = 2,
                changes = new { is_hidden_by_user = true },
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        var hiddenBody = await BodyAsync(hidden);
        Assert.True(hiddenBody.GetProperty("transaction").GetProperty("effective_hidden").GetBoolean());
        Assert.Equal("individual", hiddenBody.GetProperty("transaction").GetProperty("hidden_reason").GetString());

        await ExecuteSqlParamsAsync(
            "UPDATE statement_imports SET is_hidden = TRUE, hidden_at = NOW(), version = version + 1 WHERE id = @id",
            ("id", statementId));
        var blockedRestore = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new
            {
                expected_version = 3,
                changes = new { is_hidden_by_user = false },
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, blockedRestore.StatusCode);
        Assert.Equal("parent_hidden", await ErrorKindAsync(blockedRestore));
        var blockedBody = await BodyAsync(blockedRestore);
        var statementLinks = blockedBody.GetProperty("statements").EnumerateArray().ToArray();
        Assert.Single(statementLinks);
        Assert.Equal(statementId, statementLinks[0].GetProperty("id").GetGuid());
        Assert.Equal(3, await VersionAsync(transactionId));
        Assert.True((await ScalarAsync(
            $"SELECT is_hidden_by_user::int FROM transactions WHERE id = '{transactionId}'")) == 1);

        await ExecuteSqlParamsAsync(
            "UPDATE statement_imports SET is_hidden = FALSE, hidden_at = NULL, version = version + 1 WHERE id = @id",
            ("id", statementId));
        Assert.True((await ScalarAsync(
            $"SELECT is_hidden_by_user::int FROM transactions WHERE id = '{transactionId}'")) == 1);
        var restored = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new
            {
                expected_version = 3,
                changes = new { is_hidden_by_user = false },
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);

        var sourceAfter = await SeededSourceAsync(transactionId);
        Assert.Equal(sourceBefore, sourceAfter);
    }

    private async Task<string> SeededSourceAsync(Guid transactionId)
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT description, normalized_description, amount::text, financial_account_id,
                   statement_import_id, transaction_date::text, identity_key, occurrence_index,
                   identity_version, source_row_number, source_page
            FROM transactions WHERE id = @id
            """;
        command.Parameters.AddWithValue("id", transactionId);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return string.Join(
            '|',
            Enumerable.Range(0, 11).Select(index =>
                reader.IsDBNull(index) ? "<null>" : reader.GetValue(index).ToString()));
    }

    private async Task<Guid> ScalarGuidAsync(string sql)
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task SingleEditRejectsUnavailableTargetsAndInvalidAlgebra()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var archivedCategory = await CreateCategoryAsync(session, "Archived target");
        var archivedTag = await CreateTagAsync(session, "Archived tag");
        var statementId = await SeedStatementAsync(customerId, accountId);
        var transactionId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 1, 6), "Validation row", -1m, 1, tagIds: [archivedTag]);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Post(
            $"/v1/finance/categories/{archivedCategory}/archive", "{}", session: session))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Post(
            $"/v1/finance/merchant-tags/{archivedTag}/archive", "{}", session: session))).StatusCode);

        var categoryUnavailable = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new { expected_version = 1, changes = new { category_id = archivedCategory } }),
            session: session));
        Assert.Equal("category_unavailable", await ErrorKindAsync(categoryUnavailable));

        var tagUnavailable = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new { expected_version = 1, changes = new { add_tag_ids = new[] { archivedTag } } }),
            session: session));
        Assert.Equal("tag_unavailable", await ErrorKindAsync(tagUnavailable));

        var archivedRemoval = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new { expected_version = 1, changes = new { remove_tag_ids = new[] { archivedTag } } }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, archivedRemoval.StatusCode);

        var foreign = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new { expected_version = 2, changes = new { category_id = Guid.NewGuid() } }),
            session: session));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var overlap = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new
            {
                expected_version = 2,
                changes = new { add_tag_ids = new[] { archivedTag }, remove_tag_ids = new[] { archivedTag } },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);

        var emptyAction = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new { expected_version = 2, changes = new { add_tag_ids = Array.Empty<Guid>() } }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, emptyAction.StatusCode);

        var unknownField = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new
            {
                expected_version = 2,
                changes = new { description = "edited" },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, unknownField.StatusCode);

        var tags = new List<Guid>();
        for (var index = 0; index < 101; index++)
        {
            tags.Add(await SeedTagAsync(customerId, $"limit-{index:D3}"));
        }
        var denseTagId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 1, 7), "Dense row", -2m, 2,
            tagIds: tags.Take(100).ToArray());

        var overLimit = await SendAsync(Patch(
            $"/v1/finance/transactions/{denseTagId}",
            Json(new { expected_version = 1, changes = new { add_tag_ids = new[] { tags[100] } } }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, overLimit.StatusCode);
        Assert.Equal("tag_limit_exceeded", await ErrorKindAsync(overLimit));

        var reduce = await SendAsync(Patch(
            $"/v1/finance/transactions/{denseTagId}",
            Json(new { expected_version = 1, changes = new { remove_tag_ids = new[] { tags[0] } } }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, reduce.StatusCode);
    }

    [Fact]
    public async Task BulkExplicitAppliesExactlyOnceAndReplaysDurably()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (systemId, expenseId) = await DefaultCategoriesAsync(session);
        var targetCategory = await CreateCategoryAsync(session, "Bulk target");
        var statementId = await SeedStatementAsync(customerId, accountId);
        var addTag = await CreateTagAsync(session, "Bulk add");
        var unchangedTag = await CreateTagAsync(session, "Already there");

        var changedOne = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 2, 1), "Changed one", -10m, 1);
        var changedTwo = await SeedTransactionAsync(
            customerId, accountId, statementId, systemId,
            new DateOnly(2024, 2, 2), "Changed two", -20m, 2);
        var unchangedOne = await SeedTransactionAsync(
            customerId, accountId, statementId, targetCategory,
            new DateOnly(2024, 2, 3), "Unchanged one", -30m, 3, tagIds: [addTag]);
        var unchangedTwo = await SeedTransactionAsync(
            customerId, accountId, statementId, targetCategory,
            new DateOnly(2024, 2, 4), "Unchanged two", -40m, 4, tagIds: [addTag, unchangedTag]);

        var operationId = Guid.NewGuid();
        var changes = new { category_id = targetCategory, add_tag_ids = new[] { addTag } };
        var operation = await PrepareAsync(
            session,
            operationId,
            Explicit(
                (changedOne, 1),
                (changedTwo, 1),
                (unchangedOne, 1),
                (unchangedTwo, 1)),
            changes);

        var summary = operation.GetProperty("operation");
        Assert.True(operation.GetProperty("created").GetBoolean());
        Assert.Equal("Prepared", summary.GetProperty("status").GetString());
        Assert.Equal(4, summary.GetProperty("selected_count").GetInt32());
        Assert.Equal(2, summary.GetProperty("changed_count").GetInt32());
        Assert.Equal(2, summary.GetProperty("unchanged_count").GetInt32());
        Assert.Equal(
            summary.GetProperty("selected_count").GetInt32(),
            summary.GetProperty("changed_count").GetInt32() +
            summary.GetProperty("unchanged_count").GetInt32());
        Assert.False(string.IsNullOrEmpty(summary.GetProperty("confirmation_hash").GetString()));
        Assert.False(summary.GetProperty("action").GetProperty("stale").GetBoolean());
        Assert.Equal(1, await VersionAsync(changedOne));
        Assert.Equal(1, await VersionAsync(unchangedOne));

        var applied = await ApplyAsync(session, operation);
        Assert.Equal("Applied", applied.GetProperty("status").GetString());
        Assert.False(applied.GetProperty("replayed").GetBoolean());
        Assert.Equal(2, applied.GetProperty("changed_count").GetInt32());
        Assert.Equal(2, await VersionAsync(changedOne));
        Assert.Equal(2, await VersionAsync(changedTwo));
        Assert.Equal(1, await VersionAsync(unchangedOne));
        Assert.Equal(1, await VersionAsync(unchangedTwo));
        Assert.Equal(targetCategory, await ScalarGuidAsync(
            $"SELECT category_id FROM transactions WHERE id = '{changedOne}'"));
        Assert.Equal(1L, await ScalarAsync(
            $"SELECT COUNT(*) FROM transaction_merchant_tags WHERE transaction_id = '{changedOne}' AND merchant_tag_id = '{addTag}'"));
        Assert.Equal(0L, await PreparedItemCountAsync(operationId));
        Assert.Equal("{}", await ActionJsonAsync(operationId));

        var replay = await ApplyAsync(session, operation);
        Assert.True(replay.GetProperty("replayed").GetBoolean());
        Assert.Equal(2, await VersionAsync(changedOne));

        var wrongHash = await SendAsync(Post(
            $"/v1/finance/transactions/operations/{operationId}/apply",
            Json(new
            {
                confirmation_hash = "differing-hash",
                expected_selected_count = 4,
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, wrongHash.StatusCode);
        Assert.Equal("idempotency_conflict", await ErrorKindAsync(wrongHash));

        var replayPrepare = await PrepareAsync(
            session,
            operationId,
            Explicit(
                (changedOne, 1),
                (changedTwo, 1),
                (unchangedOne, 1),
                (unchangedTwo, 1)),
            changes,
            HttpStatusCode.OK);
        Assert.False(replayPrepare.GetProperty("created").GetBoolean());
        Assert.Equal("Applied", replayPrepare.GetProperty("operation").GetProperty("status").GetString());

        var conflict = await PrepareResponseAsync(
            session,
            operationId,
            Explicit((changedOne, 2)),
            new { category_id = expenseId });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("idempotency_conflict", await ErrorKindAsync(conflict));
        Assert.Equal(2, await VersionAsync(changedOne));
    }

    [Fact]
    public async Task AllMatchingCapturesExactFrozenScope()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        var first = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 3, 1), "Match one", -1m, 1);
        var second = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 3, 2), "Match two", -2m, 2);
        var third = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 3, 3), "Match three", -3m, 3);

        var operation = await PrepareAsync(
            session,
            Guid.NewGuid(),
            AllMatching(new { financial_account_id = accountId }),
            new { is_hidden_by_user = true });
        var summary = operation.GetProperty("operation");
        Assert.Equal(3, summary.GetProperty("selected_count").GetInt32());
        Assert.Equal(3, summary.GetProperty("changed_count").GetInt32());

        var late = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 3, 4), "Late match", -4m, 4);

        var applied = await ApplyAsync(session, operation);
        Assert.Equal(3, applied.GetProperty("selected_count").GetInt32());
        Assert.Equal(3, applied.GetProperty("changed_count").GetInt32());
        Assert.Equal(2, await VersionAsync(first));
        Assert.Equal(2, await VersionAsync(second));
        Assert.True((await ScalarAsync(
            $"SELECT is_hidden_by_user::int FROM transactions WHERE id = '{third}'")) == 1);
        Assert.True((await ScalarAsync(
            $"SELECT is_hidden_by_user::int FROM transactions WHERE id = '{late}'")) == 0);

        var visible = await BodyAsync(await SendAsync(Post(
            "/v1/finance/transactions/query",
            Json(new { filters = new { financial_account_id = accountId } }),
            session: session)));
        Assert.Equal(1, visible.GetProperty("transactions").GetArrayLength());
        Assert.Equal(4, visible.GetProperty("inventory_count").GetInt64());
        Assert.Equal(3, visible.GetProperty("matching_hidden_count").GetInt64());
    }

    [Fact]
    public async Task ApplyRejectsStaleMembersAndChangedClassification()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (systemId, expenseId) = await DefaultCategoriesAsync(session);
        var targetCategory = await CreateCategoryAsync(session, "Changing target");
        var statementId = await SeedStatementAsync(customerId, accountId);
        var first = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 4, 1), "Stale row", -1m, 1);

        var versionChanged = await PrepareAsync(
            session,
            Guid.NewGuid(),
            Explicit((first, 1)),
            new { category_id = targetCategory });
        var patch = await SendAsync(Patch(
            $"/v1/finance/transactions/{first}",
            Json(new { expected_version = 1, changes = new { is_hidden_by_user = true }, confirm = true }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var staleApply = await SendAsync(Post(
            $"/v1/finance/transactions/operations/{versionChanged.GetProperty("operation").GetProperty("operation_id").GetGuid()}/apply",
            Json(new
            {
                confirmation_hash = versionChanged.GetProperty("operation").GetProperty("confirmation_hash").GetString(),
                expected_selected_count = 1,
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, staleApply.StatusCode);
        Assert.Equal("selection_stale", await ErrorKindAsync(staleApply));
        var staleBody = await BodyAsync(staleApply);
        Assert.Equal(1, staleBody.GetProperty("stale_count").GetInt32());

        var second = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 4, 2), "Classification row", -2m, 2);
        var classification = await PrepareAsync(
            session,
            Guid.NewGuid(),
            Explicit((second, 1)),
            new { category_id = targetCategory });
        var rename = await SendAsync(Patch(
            $"/v1/finance/categories/{targetCategory}",
            Json(new { display_name = "Renamed target", kind = "Expense" }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        var classificationApply = await SendAsync(Post(
            $"/v1/finance/transactions/operations/{classification.GetProperty("operation").GetProperty("operation_id").GetGuid()}/apply",
            Json(new
            {
                confirmation_hash = classification.GetProperty("operation").GetProperty("confirmation_hash").GetString(),
                expected_selected_count = 1,
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, classificationApply.StatusCode);
        Assert.Equal("classification_changed", await ErrorKindAsync(classificationApply));
        Assert.Equal(1, await VersionAsync(second));

        var third = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 4, 3), "Deleted row", -3m, 3);
        var deleted = await PrepareAsync(
            session,
            Guid.NewGuid(),
            Explicit((third, 1)),
            new { category_id = systemId });
        var deleteStatement = await SendAsync(DeleteJson(
            $"/v1/finance/imports/{statementId}",
            Json(new { expected_version = 1, expected_transaction_count = 3, confirm = true }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, deleteStatement.StatusCode);
        var deletedApply = await SendAsync(Post(
            $"/v1/finance/transactions/operations/{deleted.GetProperty("operation").GetProperty("operation_id").GetGuid()}/apply",
            Json(new
            {
                confirmation_hash = deleted.GetProperty("operation").GetProperty("confirmation_hash").GetString(),
                expected_selected_count = 1,
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, deletedApply.StatusCode);
        Assert.Equal("selection_stale", await ErrorKindAsync(deletedApply));
        Assert.Equal(0L, await ScalarAsync($"SELECT COUNT(*) FROM transactions WHERE id = '{third}'"));
    }

    [Fact]
    public async Task ParentHiddenRestoreIsRefusedButClassificationIsAllowed()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var targetCategory = await CreateCategoryAsync(session, "Hidden target");
        var statementId = await SeedStatementAsync(customerId, accountId, hidden: true);
        var rowId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 5, 1), "Hidden child", -1m, 1, hidden: true, version: 1);

        var restore = await PrepareResponseAsync(
            session,
            Guid.NewGuid(),
            Explicit((rowId, 1)),
            new { is_hidden_by_user = false });
        Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);
        Assert.Equal("parent_hidden", await ErrorKindAsync(restore));
        Assert.Contains(
            statementId,
            (await BodyAsync(restore)).GetProperty("statements").EnumerateArray()
                .Select(link => link.GetProperty("id").GetGuid()));

        var classify = await PrepareAsync(
            session,
            Guid.NewGuid(),
            Explicit((rowId, 1)),
            new { category_id = targetCategory });
        var applied = await ApplyAsync(session, classify);
        Assert.Equal(1, applied.GetProperty("changed_count").GetInt32());
        Assert.Equal(2, await VersionAsync(rowId));
        Assert.True((await ScalarAsync(
            $"SELECT is_hidden_by_user::int FROM transactions WHERE id = '{rowId}'")) == 1);
        Assert.Equal(targetCategory, await ScalarGuidAsync(
            $"SELECT category_id FROM transactions WHERE id = '{rowId}'"));
    }

    [Fact]
    public async Task AbandonExpiryQuotaAndIdempotentReplay()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 6, 1), "Lifecycle row", -1m, 1);

        var abandonedId = Guid.NewGuid();
        var abandoned = await PrepareAsync(
            session, abandonedId, Explicit((rowId, 1)),
            new { category_id = expenseId });
        var abandon = await SendAsync(Post(
            $"/v1/finance/transactions/operations/{abandonedId}/abandon", "{}", session: session));
        Assert.Equal(HttpStatusCode.OK, abandon.StatusCode);
        Assert.Equal("Abandoned", (await BodyAsync(abandon)).GetProperty("status").GetString());
        var abandonedApply = await SendAsync(Post(
            $"/v1/finance/transactions/operations/{abandonedId}/apply",
            Json(new
            {
                confirmation_hash = abandoned.GetProperty("operation").GetProperty("confirmation_hash").GetString(),
                expected_selected_count = 1,
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.Gone, abandonedApply.StatusCode);
        Assert.Equal("operation_abandoned", await ErrorKindAsync(abandonedApply));
        var abandonAgain = await SendAsync(Post(
            $"/v1/finance/transactions/operations/{abandonedId}/abandon", "{}", session: session));
        Assert.Equal(HttpStatusCode.OK, abandonAgain.StatusCode);
        Assert.Equal("Abandoned", (await BodyAsync(abandonAgain)).GetProperty("status").GetString());

        var expiringId = Guid.NewGuid();
        var expiring = await PrepareAsync(
            session, expiringId, Explicit((rowId, 1)),
            new { category_id = expenseId });
        await ExecuteSqlParamsAsync(
            "UPDATE transaction_operations SET captured_at = NOW() - interval '1 hour', expires_at = NOW() - interval '59 minutes' WHERE operation_id = @id",
            ("id", expiringId));
        var status = await SendAsync(Get(
            $"/v1/finance/transactions/operations/{expiringId}", session: session));
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal("Expired", (await BodyAsync(status)).GetProperty("status").GetString());
        var expiredApply = await SendAsync(Post(
            $"/v1/finance/transactions/operations/{expiringId}/apply",
            Json(new
            {
                confirmation_hash = expiring.GetProperty("operation").GetProperty("confirmation_hash").GetString(),
                expected_selected_count = 1,
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.Gone, expiredApply.StatusCode);
        Assert.Equal("{}", await ActionJsonAsync(expiringId));
        Assert.Equal(0L, await PreparedItemCountAsync(expiringId));

        for (var index = 0; index < 5; index++)
        {
            await PrepareAsync(
                session,
                Guid.NewGuid(),
                Explicit((rowId, 1)),
                new { category_id = expenseId });
        }
        var quota = await PrepareResponseAsync(
            session,
            Guid.NewGuid(),
            Explicit((rowId, 1)),
            new { category_id = expenseId });
        Assert.Equal(HttpStatusCode.TooManyRequests, quota.StatusCode);
        Assert.Equal("operation_quota", await ErrorKindAsync(quota));

        await ExecuteSqlParamsAsync(
            """
            UPDATE transaction_operations
            SET status = 'Expired', terminal_at = NOW(), action = '{}'::jsonb,
                target_stamps = '{}'::jsonb
            WHERE operation_id = (
                SELECT operation_id FROM transaction_operations
                WHERE status = 'Prepared' ORDER BY created_at LIMIT 1)
            """);
        var afterExpiry = await PrepareResponseAsync(
            session,
            Guid.NewGuid(),
            Explicit((rowId, 1)),
            new { category_id = expenseId });
        Assert.Equal(HttpStatusCode.Created, afterExpiry.StatusCode);
    }

    [Fact]
    public async Task ExplicitSelectionBoundsAreEnforced()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 7, 1), "Bounds row", -1m, 1);

        var tooMany = Enumerable.Range(0, 1001)
            .Select(index => new { id = Guid.NewGuid(), expected_version = 1 })
            .ToArray();
        var oversized = await SendAsync(Post(
            PreparePath,
            Json(new
            {
                operation_id = Guid.NewGuid(),
                selection = new { mode = "explicit", transactions = tooMany },
                changes = new { category_id = expenseId },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);

        var empty = await SendAsync(Post(
            PreparePath,
            Json(new
            {
                operation_id = Guid.NewGuid(),
                selection = new { mode = "explicit", transactions = Array.Empty<object>() },
                changes = new { category_id = expenseId },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var noMatches = await SendAsync(Post(
            PreparePath,
            Json(new
            {
                operation_id = Guid.NewGuid(),
                selection = new { mode = "all_matching", filters = new { search = "no-such-row" } },
                changes = new { category_id = expenseId },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, noMatches.StatusCode);
        Assert.Equal("empty_selection", await ErrorKindAsync(noMatches));

        var missing = await SendAsync(Post(
            PreparePath,
            Json(new
            {
                operation_id = Guid.NewGuid(),
                selection = new
                {
                    mode = "explicit",
                    transactions = new[] { new { id = Guid.NewGuid(), expected_version = 1 } },
                },
                changes = new { category_id = expenseId },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var stale = await SendAsync(Post(
            PreparePath,
            Json(new
            {
                operation_id = Guid.NewGuid(),
                selection = new
                {
                    mode = "explicit",
                    transactions = new[] { new { id = rowId, expected_version = 99 } },
                },
                changes = new { category_id = expenseId },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("selection_stale", await ErrorKindAsync(stale));
        Assert.Equal(1, await VersionAsync(rowId));
    }

    [Fact]
    public async Task ForeignOwnerMatrixRejectsEveryNewRouteWithoutEffects()
    {
        var (ownerSession, ownerId) = await NewCustomerAsync();
        var ownerAccount = await CreateAccountAsync(ownerSession, "Owner account");
        var (_, ownerCategory) = await DefaultCategoriesAsync(ownerSession);
        var ownerStatement = await SeedStatementAsync(ownerId, ownerAccount);
        var ownerRow = await SeedTransactionAsync(
            ownerId, ownerAccount, ownerStatement, ownerCategory,
            new DateOnly(2024, 7, 1), "Owner synthetic row", -1m);

        var (foreignSession, foreignId) = await NewCustomerAsync();
        var foreignAccount = await CreateAccountAsync(foreignSession, "Foreign account");
        var (_, foreignCategory) = await DefaultCategoriesAsync(foreignSession);
        var foreignTag = await CreateTagAsync(foreignSession, "Foreign synthetic tag");
        var foreignStatement = await SeedStatementAsync(foreignId, foreignAccount);
        var foreignRow = await SeedTransactionAsync(
            foreignId, foreignAccount, foreignStatement, foreignCategory,
            new DateOnly(2024, 7, 1), "Foreign synthetic row", -2m);

        var sharedId = Guid.NewGuid();
        var ownerOperation = await PrepareAsync(
            ownerSession, sharedId, Explicit((ownerRow, 1)), new { is_hidden_by_user = true });
        var foreignShared = await PrepareAsync(
            foreignSession, sharedId, Explicit((foreignRow, 1)), new { is_hidden_by_user = true });
        var foreignOnlyId = Guid.NewGuid();
        var foreignOnly = await PrepareAsync(
            foreignSession, foreignOnlyId, Explicit((foreignRow, 1)), new { is_hidden_by_user = true });

        const string searchCanary = "synthetic-private-search-7129";
        Factory.LogCapture.Clear();
        var responses = new List<HttpResponseMessage>
        {
            await SendAsync(Get($"/v1/finance/transactions/{foreignRow}", session: ownerSession)),
            await SendAsync(Patch($"/v1/finance/transactions/{foreignRow}",
                Json(new { expected_version = 1, changes = new { category_id = ownerCategory } }),
                session: ownerSession)),
            await SendAsync(Patch($"/v1/finance/transactions/{ownerRow}",
                Json(new { expected_version = 1, changes = new { category_id = foreignCategory } }),
                session: ownerSession)),
            await SendAsync(Patch($"/v1/finance/transactions/{ownerRow}",
                Json(new { expected_version = 1, changes = new { add_tag_ids = new[] { foreignTag } } }),
                session: ownerSession)),
            await PrepareResponseAsync(ownerSession, Guid.NewGuid(),
                Explicit((ownerRow, 1), (foreignRow, 1)), new { is_hidden_by_user = true }),
            await PrepareResponseAsync(ownerSession, Guid.NewGuid(),
                AllMatching(new { financial_account_id = foreignAccount }), new { is_hidden_by_user = true }),
            await SendAsync(Get($"/v1/finance/transactions/operations/{foreignOnlyId}", session: ownerSession)),
            await SendAsync(Post($"/v1/finance/transactions/operations/{foreignOnlyId}/apply",
                Json(new
                {
                    confirmation_hash = foreignOnly.GetProperty("operation").GetProperty("confirmation_hash").GetString(),
                    expected_selected_count = 1,
                    confirm = true,
                }), session: ownerSession)),
            await SendAsync(Post($"/v1/finance/transactions/operations/{foreignOnlyId}/abandon",
                "{}", session: ownerSession)),
            await SendAsync(Get($"/v1/finance/transactions/{Guid.NewGuid()}", session: ownerSession)),
        };

        foreach (var filters in new object[]
                 {
                     new { financial_account_id = foreignAccount, search = searchCanary },
                     new { statement_import_id = foreignStatement, search = searchCanary },
                     new { category_id = foreignCategory, search = searchCanary },
                     new { merchant_tag = foreignTag, search = searchCanary },
                 })
        {
            responses.Add(await SendAsync(Post(
                "/v1/finance/transactions/query", Json(new { filters }), session: ownerSession)));
        }

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal("{\"error\":\"not_found\"}", await response.Content.ReadAsStringAsync());
        }

        var copiedHash = await SendAsync(Post(
            $"/v1/finance/transactions/operations/{sharedId}/apply",
            Json(new
            {
                confirmation_hash = foreignShared.GetProperty("operation").GetProperty("confirmation_hash").GetString(),
                expected_selected_count = 1,
                confirm = true,
            }), session: ownerSession));
        Assert.Equal(HttpStatusCode.Conflict, copiedHash.StatusCode);
        Assert.Equal("idempotency_conflict", await ErrorKindAsync(copiedHash));
        Assert.True(copiedHash.Headers.CacheControl?.NoStore);

        var successfulQuery = await SendAsync(Post(
            "/v1/finance/transactions/query", Json(new { filters = new { search = searchCanary } }),
            session: ownerSession));
        Assert.Equal(HttpStatusCode.OK, successfulQuery.StatusCode);
        var malformed = await SendAsync(Post(
            "/v1/finance/transactions/query",
            Json(new { filters = new { description = searchCanary } }), session: ownerSession));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var deniedBeforeBody = await SendAsync(Post(
            "/v1/finance/transactions/query", new string('x', 17 * 1024),
            secret: null, session: ownerSession));
        Assert.Equal(HttpStatusCode.Unauthorized, deniedBeforeBody.StatusCode);
        Assert.True(deniedBeforeBody.Headers.CacheControl?.NoStore);
        Assert.False(Factory.LogCapture.Contains(searchCanary));

        Assert.Equal(1L, await VersionAsync(ownerRow));
        Assert.Equal(1L, await VersionAsync(foreignRow));
        Assert.Equal(0L, await ScalarAsync($"""
            SELECT COUNT(*) FROM transaction_operations
            WHERE customer_id = '{ownerId}' AND operation_id NOT IN ('{sharedId}')
            """));
        Assert.Equal("Prepared", await StringScalarAsync($"""
            SELECT status FROM transaction_operations
            WHERE customer_id = '{foreignId}' AND operation_id = '{foreignOnlyId}'
            """));
        var applied = await ApplyAsync(ownerSession, ownerOperation);
        Assert.Equal("Applied", applied.GetProperty("status").GetString());
        Assert.Equal(1L, await VersionAsync(foreignRow));
        Assert.Equal(0L, await ScalarAsync($"""
            SELECT is_hidden_by_user::int FROM transactions WHERE id = '{foreignRow}'
            """));
    }

    [Fact]
    public async Task FiftyThousandMemberBulkCommitsAtomically()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (systemId, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        var denseTags = new[]
        {
            await CreateTagAsync(session, "Dense A"),
            await CreateTagAsync(session, "Dense B"),
            await CreateTagAsync(session, "Dense C"),
        };
        var addTag = await CreateTagAsync(session, "Bulk add tag");

        var seedWatch = Stopwatch.StartNew();
        await SeedBulkTransactionsAsync(customerId, accountId, statementId, expenseId, systemId, 50_000);
        seedWatch.Stop();
        var denseWatch = Stopwatch.StartNew();
        await ExecuteSqlParamsAsync(
            """
            INSERT INTO transaction_merchant_tags (customer_id, transaction_id, merchant_tag_id)
            SELECT @customer_id, t.id, tag.tag_id
            FROM transactions t
            CROSS JOIN unnest(@tag_ids) AS tag(tag_id)
            WHERE t.customer_id = @customer_id AND t.statement_import_id = @statement_id
            ON CONFLICT DO NOTHING
            """,
            ("customer_id", customerId),
            ("statement_id", statementId),
            ("tag_ids", denseTags));
        denseWatch.Stop();
        var addWatch = Stopwatch.StartNew();
        await ExecuteSqlParamsAsync(
            """
            INSERT INTO transaction_merchant_tags (customer_id, transaction_id, merchant_tag_id)
            SELECT @customer_id, t.id, @add_tag
            FROM transactions t
            WHERE t.customer_id = @customer_id AND t.statement_import_id = @statement_id
              AND t.source_row_number % 2 = 0
            ON CONFLICT DO NOTHING
            """,
            ("customer_id", customerId),
            ("statement_id", statementId),
            ("add_tag", addTag));
        addWatch.Stop();
        output.WriteLine(
            "P3-N5 setup seed_ms={0} dense_links_ms={1} add_links_ms={2}",
            seedWatch.ElapsedMilliseconds,
            denseWatch.ElapsedMilliseconds,
            addWatch.ElapsedMilliseconds);

        var prepareWatch = Stopwatch.StartNew();
        var operation = await PrepareAsync(
            session,
            Guid.NewGuid(),
            AllMatching(new { financial_account_id = accountId }),
            new { category_id = expenseId, add_tag_ids = new[] { addTag } });
        prepareWatch.Stop();
        var summary = operation.GetProperty("operation");
        Assert.Equal(50_000, summary.GetProperty("selected_count").GetInt32());
        Assert.Equal(25_000, summary.GetProperty("changed_count").GetInt32());
        Assert.Equal(25_000, summary.GetProperty("unchanged_count").GetInt32());

        var applyWatch = Stopwatch.StartNew();
        var applied = await ApplyAsync(session, operation);
        applyWatch.Stop();
        Assert.Equal(25_000, applied.GetProperty("changed_count").GetInt32());
        output.WriteLine(
            "P3-N5 prepare_ms={0} apply_ms={1} selected={2} changed={3}",
            prepareWatch.ElapsedMilliseconds,
            applyWatch.ElapsedMilliseconds,
            summary.GetProperty("selected_count").GetInt32(),
            applied.GetProperty("changed_count").GetInt32());

        Assert.Equal(25_000L, await ScalarAsync(
            $"""
            SELECT COUNT(*) FROM transactions
            WHERE customer_id = '{customerId}' AND statement_import_id = '{statementId}'
              AND category_id = '{expenseId}' AND version = 2
            """));
        Assert.Equal(25_000L, await ScalarAsync(
            $"""
            SELECT COUNT(*) FROM transactions
            WHERE customer_id = '{customerId}' AND statement_import_id = '{statementId}'
              AND version = 1
            """));
        Assert.Equal(50_000L, await ScalarAsync(
            $"""
            SELECT COUNT(*) FROM transaction_merchant_tags
            WHERE customer_id = '{customerId}' AND merchant_tag_id = '{addTag}'
            """));

        var replayWatch = Stopwatch.StartNew();
        var replay = await ApplyAsync(session, operation);
        replayWatch.Stop();
        Assert.True(replay.GetProperty("replayed").GetBoolean());
        output.WriteLine("P3-N5 replay_ms={0}", replayWatch.ElapsedMilliseconds);
        Assert.Equal(50_000L, await ScalarAsync(
            $"""
            SELECT COUNT(*) FROM transaction_merchant_tags
            WHERE customer_id = '{customerId}' AND merchant_tag_id = '{addTag}'
            """));
    }

    [Fact]
    public async Task AllMatchingAboveThePublicBoundIsRejectedWholly()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (systemId, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        await SeedBulkTransactionsAsync(customerId, accountId, statementId, systemId, expenseId, 50_001);

        var response = await SendAsync(Post(
            PreparePath,
            Json(new
            {
                operation_id = Guid.NewGuid(),
                selection = new { mode = "all_matching", filters = new { financial_account_id = accountId } },
                changes = new { category_id = expenseId },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("selection_too_large", await ErrorKindAsync(response));
        Assert.Equal(0L, await ScalarAsync(
            $"SELECT COUNT(*) FROM transaction_operations WHERE customer_id = '{customerId}'"));
        Assert.Equal(0L, await ScalarAsync(
            $"SELECT COUNT(*) FROM transactions WHERE customer_id = '{customerId}' AND version > 1"));
    }

    private async Task SeedBulkTransactionsAsync(
        Guid customerId,
        Guid accountId,
        Guid statementId,
        Guid categoryA,
        Guid categoryB,
        int count)
    {
        await ExecuteSqlParamsAsync(
            """
            INSERT INTO transactions (
                id, customer_id, financial_account_id, statement_import_id,
                transaction_date, description, normalized_description, amount,
                category_id, source_type, source_page, source_row_number,
                identity_key, occurrence_index, is_hidden_by_user, version,
                created_at, updated_at)
            SELECT
                gen_random_uuid(), @customer_id, @account_id, @statement_id,
                DATE '2024-01-01' + (series % 300),
                'Bulk row ' || series,
                'bulk row ' || series,
                (-(1 + (series % 100)))::numeric(18, 2),
                CASE WHEN series % 2 = 0 THEN @category_a ELSE @category_b END,
                'fnb_csv', NULL, series,
                'bulk-' || series, 1, FALSE, 1, NOW(), NOW()
            FROM generate_series(1, @count) AS series
            """,
            ("customer_id", customerId),
            ("account_id", accountId),
            ("statement_id", statementId),
            ("category_a", categoryA),
            ("category_b", categoryB),
            ("count", count));
    }
}
