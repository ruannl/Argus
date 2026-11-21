using System.Net;
using System.Text.Json;

namespace argus.Server.Tests;

public sealed class SalaryCycleContractTests(KitFactory factory) : TransactionTestBase(factory)
{
    private const string SalaryPath = "/v1/finance/salary-cycles";

    [Fact]
    public async Task ConfirmationPreviewListAndRemovalKeepSourceMetadataImmutable()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        Guid tagId = await SeedTagAsync(customerId, "Salary receipt tag");
        Guid transactionId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2025, 1, 25), "Employer payment", 10_000.00m, 1, [tagId]);
        JsonElement before = await GetTransactionAsync(session, transactionId);

        JsonElement empty = await ListAsync(session);
        Assert.Equal("0", empty.GetProperty("revision").GetString());
        Assert.Empty(empty.GetProperty("starts").EnumerateArray());

        JsonElement preview = await PreviewAsync(session, new
        {
            action = "confirm",
            transaction_id = transactionId,
            expected_transaction_version = 1,
        });
        Assert.Equal("confirm", preview.GetProperty("action").GetString());
        Assert.Equal("2025-01-25", preview.GetProperty("date").GetString());
        Assert.True(preview.GetProperty("retention_disclosure").GetBoolean());
        Assert.False(preview.GetProperty("already_confirmed").GetBoolean());
        Assert.NotEmpty(preview.GetProperty("affected_intervals").EnumerateArray());
        Assert.Equal("Employer payment", preview.GetProperty("source").GetProperty("description").GetString());
        Assert.Equal("0", (await ListAsync(session)).GetProperty("revision").GetString());

        JsonElement created = await ConfirmAsync(session, transactionId, 1, 0);
        Assert.Equal("created", created.GetProperty("status").GetString());
        Assert.Equal("1", created.GetProperty("revision").GetString());
        Guid boundaryId = created.GetProperty("id").GetGuid();

        JsonElement listed = await ListAsync(session);
        Assert.Equal("1", listed.GetProperty("revision").GetString());
        JsonElement boundary = listed.GetProperty("starts").EnumerateArray().Single();
        Assert.Equal(boundaryId, boundary.GetProperty("id").GetGuid());
        Assert.Equal("2025-01-25", boundary.GetProperty("start_date").GetString());

        JsonElement after = await GetTransactionAsync(session, transactionId);
        Assert.Equal(before.GetProperty("version").GetInt32(), after.GetProperty("version").GetInt32());
        Assert.Equal(before.GetProperty("amount").GetString(), after.GetProperty("amount").GetString());
        Assert.Equal(before.GetProperty("description").GetString(), after.GetProperty("description").GetString());
        Assert.Equal(before.GetProperty("transaction_date").GetString(), after.GetProperty("transaction_date").GetString());
        Assert.Equal(before.GetProperty("category").GetProperty("id").GetGuid(), after.GetProperty("category").GetProperty("id").GetGuid());
        Assert.Equal(
            before.GetProperty("merchant_tags").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()),
            after.GetProperty("merchant_tags").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));

        JsonElement removePreview = await PreviewAsync(session, new { action = "remove", start_id = boundaryId });
        Assert.Equal("remove", removePreview.GetProperty("action").GetString());
        Assert.True(removePreview.GetProperty("retention_disclosure").GetBoolean());
        Assert.NotEmpty(removePreview.GetProperty("warnings").EnumerateArray());

        JsonElement removed = await DeleteAsync(session, boundaryId, 1);
        Assert.Equal(boundaryId, removed.GetProperty("removed_id").GetGuid());
        Assert.Equal("2", removed.GetProperty("revision").GetString());
        Assert.Empty((await ListAsync(session)).GetProperty("starts").EnumerateArray());
        Assert.Equal("2", (await ListAsync(session)).GetProperty("revision").GetString());

        JsonElement readded = await ConfirmAsync(session, transactionId, 1, 2);
        Assert.Equal("created", readded.GetProperty("status").GetString());
        Assert.NotEqual(boundaryId, readded.GetProperty("id").GetGuid());
        Assert.Equal("3", readded.GetProperty("revision").GetString());
        Assert.Equal(1, (await GetTransactionAsync(session, transactionId)).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task EligibilityUsesOwnershipVisibilityAmountDateAndVersionOnly()
    {
        var (session, customerId) = await NewCustomerAsync();
        var (otherSession, otherCustomerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var otherAccountId = await CreateAccountAsync(otherSession);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var (_, otherExpenseId) = await DefaultCategoriesAsync(otherSession);
        Guid transferId = await CreateCategoryAsync(session, "Payroll transfers", "Transfer");
        var statementId = await SeedStatementAsync(customerId, accountId);
        var hiddenStatementId = await SeedStatementAsync(customerId, accountId, hidden: true);
        var otherStatementId = await SeedStatementAsync(otherCustomerId, otherAccountId);

        Guid positive = await SeedTransactionAsync(customerId, accountId, statementId, transferId, new DateOnly(2025, 2, 1), "Positive transfer", 500m, 1);
        Guid zero = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 2, 2), "Zero", 0m, 2);
        Guid negative = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 2, 3), "Negative", -500m, 3);
        Guid future = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2099, 1, 1), "Future", 500m, 4);
        Guid individualHidden = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 2, 4), "Individual hidden", 500m, 5, hidden: true);
        Guid parentHidden = await SeedTransactionAsync(customerId, accountId, hiddenStatementId, expenseId, new DateOnly(2025, 2, 5), "Parent hidden", 500m, 1);
        Guid foreign = await SeedTransactionAsync(otherCustomerId, otherAccountId, otherStatementId, otherExpenseId, new DateOnly(2025, 2, 6), "Foreign", 500m, 1);
        Guid unconfirmed = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 2, 7), "Unconfirmed category", 500m, 6, categoryConfirmed: false);

        foreach (Guid ineligible in new[] { zero, negative, future, individualHidden, parentHidden })
        {
            HttpResponseMessage response = await PreviewResponseAsync(session, new
            {
                action = "confirm",
                transaction_id = ineligible,
                expected_transaction_version = 1,
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("receipt_ineligible", await ErrorKindAsync(response));
        }

        foreach (Guid missing in new[] { foreign, Guid.NewGuid() })
        {
            HttpResponseMessage response = await PreviewResponseAsync(session, new
            {
                action = "confirm",
                transaction_id = missing,
                expected_transaction_version = 1,
            });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("not_found", await ErrorKindAsync(response));
        }

        HttpResponseMessage staleVersion = await PreviewResponseAsync(session, new
        {
            action = "confirm",
            transaction_id = positive,
            expected_transaction_version = 99,
        });
        Assert.Equal(HttpStatusCode.Conflict, staleVersion.StatusCode);
        Assert.Equal("version_conflict", await ErrorKindAsync(staleVersion));

        HttpResponseMessage staleRevision = await SendAsync(Post(
            SalaryPath,
            Json(new
            {
                transaction_id = positive,
                expected_transaction_version = 1,
                expected_revision = "7",
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, staleRevision.StatusCode);
        Assert.Equal("cycle_sequence_changed", await ErrorKindAsync(staleRevision));

        JsonElement created = await ConfirmAsync(session, positive, 1, 0);
        Assert.Equal("created", created.GetProperty("status").GetString());

        JsonElement unconfirmedCreated = await ConfirmAsync(session, unconfirmed, 1, 1);
        Assert.Equal("created", unconfirmedCreated.GetProperty("status").GetString());

        JsonElement transfer = await GetTransactionAsync(session, positive);
        Assert.Equal("Transfer", transfer.GetProperty("category").GetProperty("kind").GetString());
        JsonElement sourceUnconfirmed = await GetTransactionAsync(session, unconfirmed);
        Assert.False(sourceUnconfirmed.GetProperty("category_confirmed").GetBoolean());
    }

    [Fact]
    public async Task SameDateReceiptsShareOneBoundaryAndAStaleNoOpWritesNothing()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        Guid first = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 3, 25), "First receipt", 10_000m, 1);
        Guid second = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 3, 25), "Second receipt", 10_000m, 2);

        JsonElement created = await ConfirmAsync(session, first, 1, 0);
        Guid boundaryId = created.GetProperty("id").GetGuid();

        JsonElement preview = await PreviewAsync(session, new
        {
            action = "confirm",
            transaction_id = second,
            expected_transaction_version = 1,
        });
        Assert.True(preview.GetProperty("already_confirmed").GetBoolean());
        Assert.Equal("2025-03-25", preview.GetProperty("date").GetString());

        JsonElement noOp = await ConfirmAsync(session, second, 1, 0);
        Assert.Equal("already_confirmed", noOp.GetProperty("status").GetString());
        Assert.Equal(boundaryId, noOp.GetProperty("id").GetGuid());
        Assert.Equal("1", noOp.GetProperty("revision").GetString());

        JsonElement listed = await ListAsync(session);
        Assert.Single(listed.GetProperty("starts").EnumerateArray());
        Assert.Equal("1", listed.GetProperty("revision").GetString());
        Assert.Equal("Second receipt", (await GetTransactionAsync(session, second)).GetProperty("description").GetString());
    }

    [Fact]
    public async Task RemovalAndReselectionChangeOnlyBoundaryIdentityAndRetainDatesAfterSourceDeletion()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        DateOnly[] dates =
        [
            new(2025, 1, 25),
            new(2025, 2, 24),
            new(2025, 3, 25),
        ];
        Guid[] transactions = [];
        for (int index = 0; index < dates.Length; index++)
        {
            transactions = [.. transactions, await SeedTransactionAsync(
                customerId, accountId, statementId, expenseId, dates[index], $"Receipt {index}", 10_000m, index + 1)];
        }

        JsonElement first = await ConfirmAsync(session, transactions[0], 1, 0);
        JsonElement second = await ConfirmAsync(session, transactions[1], 1, 1);
        JsonElement third = await ConfirmAsync(session, transactions[2], 1, 2);
        Assert.Equal("3", third.GetProperty("revision").GetString());

        JsonElement removed = await DeleteAsync(session, second.GetProperty("id").GetGuid(), 3);
        Assert.Equal("2025-02-24", removed.GetProperty("removed_date").GetString());
        Assert.Equal("4", removed.GetProperty("revision").GetString());

        JsonElement readded = await ConfirmAsync(session, transactions[1], 1, 4);
        Assert.NotEqual(second.GetProperty("id").GetGuid(), readded.GetProperty("id").GetGuid());
        Assert.Equal("5", readded.GetProperty("revision").GetString());

        await ExecuteSqlAsync($"""
            DELETE FROM transaction_merchant_tags WHERE customer_id = '{customerId}';
            DELETE FROM transactions WHERE customer_id = '{customerId}' AND statement_import_id = '{statementId}';
            DELETE FROM statement_imports WHERE customer_id = '{customerId}' AND id = '{statementId}';
            """);

        JsonElement retained = await ListAsync(session);
        Assert.Equal(3, retained.GetProperty("starts").GetArrayLength());
        Assert.Equal("5", retained.GetProperty("revision").GetString());
        Assert.Equal(
            new[] { "2025-03-25", "2025-02-24", "2025-01-25" },
            retained.GetProperty("starts").EnumerateArray().Select(item => item.GetProperty("start_date").GetString()));

        HttpResponseMessage gone = await SendAsync(Get($"/v1/finance/transactions/{transactions[0]}", session: session));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        JsonElement removedAfterDelete = await DeleteAsync(session, first.GetProperty("id").GetGuid(), 5);
        Assert.Equal("6", removedAfterDelete.GetProperty("revision").GetString());
        Assert.Equal(2, (await ListAsync(session)).GetProperty("starts").GetArrayLength());
    }

    [Fact]
    public async Task SalaryListPagesWithAnExclusiveCursorAndRejectsMalformedQueries()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        Guid[] ids = [];
        for (int index = 0; index < 27; index++)
        {
            ids = [.. ids, await SeedTransactionAsync(
                customerId, accountId, statementId, expenseId,
                new DateOnly(2024, 1, 1).AddDays(index), $"Receipt {index}", 100m, index + 1)];
        }

        for (int index = 0; index < ids.Length; index++)
        {
            JsonElement created = await ConfirmAsync(session, ids[index], 1, index);
            Assert.Equal("created", created.GetProperty("status").GetString());
        }

        JsonElement firstPage = await ListAsync(session);
        Assert.Equal(25, firstPage.GetProperty("starts").GetArrayLength());
        string cursor = firstPage.GetProperty("next_before_date").GetString()!;
        Assert.Equal("2024-01-03", cursor);

        JsonElement secondPage = await ListAsync(session, cursor);
        Assert.Equal(2, secondPage.GetProperty("starts").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, secondPage.GetProperty("next_before_date").ValueKind);
        Guid[] firstIds = firstPage.GetProperty("starts").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Guid[] secondIds = secondPage.GetProperty("starts").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Assert.Empty(firstIds.Intersect(secondIds));

        foreach (string query in new[]
                 {
                     "?before_date=not-a-date",
                     "?before_date=2024-01-01&before_date=2024-01-02",
                     "?unknown=1",
                 })
        {
            HttpResponseMessage response = await SendAsync(Get(SalaryPath + query, session: session));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task SalaryRoutesAreNoStoreBoundedAndRejectInvalidBodies()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        Guid transactionId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 4, 1), "Receipt", 100m, 1);

        HttpResponseMessage list = await SendAsync(Get(SalaryPath, session: session));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True(list.Headers.CacheControl?.NoStore);

        HttpResponseMessage unauthorized = await SendAsync(Get(SalaryPath));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.True(unauthorized.Headers.CacheControl?.NoStore);

        HttpResponseMessage huge = await SendAsync(Post(
            SalaryPath + "/preview",
            "{\"action\":\"confirm\",\"padding\":\"" + new string('x', 20 * 1024) + "\"}",
            session: session));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);

        HttpResponseMessage unknown = await SendAsync(Post(
            SalaryPath + "/preview",
            Json(new { action = "confirm", transaction_id = transactionId, expected_transaction_version = 1, extra = "x" }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        HttpResponseMessage badAction = await SendAsync(Post(
            SalaryPath + "/preview",
            Json(new { action = "replace", transaction_id = transactionId, expected_transaction_version = 1 }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, badAction.StatusCode);

        HttpResponseMessage missingConfirm = await SendAsync(Post(
            SalaryPath,
            Json(new { transaction_id = transactionId, expected_transaction_version = 1, expected_revision = "0" }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, missingConfirm.StatusCode);

        HttpResponseMessage badRemove = await SendAsync(DeleteJson(
            SalaryPath + "/" + Guid.NewGuid(),
            Json(new { expected_revision = "0", confirm = false }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, badRemove.StatusCode);
    }

    private async Task<JsonElement> ListAsync(string session, string? beforeDate = null)
    {
        string path = SalaryPath + (beforeDate is null ? string.Empty : "?before_date=" + Uri.EscapeDataString(beforeDate));
        HttpResponseMessage response = await SendAsync(Get(path, session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private async Task<HttpResponseMessage> PreviewResponseAsync(string session, object body) =>
        await SendAsync(Post(SalaryPath + "/preview", Json(body), session: session));

    private async Task<JsonElement> PreviewAsync(string session, object body)
    {
        HttpResponseMessage response = await PreviewResponseAsync(session, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private async Task<JsonElement> ConfirmAsync(string session, Guid transactionId, int version, long revision)
    {
        HttpResponseMessage response = await SendAsync(Post(
            SalaryPath,
            Json(new
            {
                transaction_id = transactionId,
                expected_transaction_version = version,
                expected_revision = revision.ToString(),
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private async Task<JsonElement> DeleteAsync(string session, Guid id, long revision)
    {
        HttpResponseMessage response = await SendAsync(DeleteJson(
            SalaryPath + "/" + id,
            Json(new { expected_revision = revision.ToString(), confirm = true }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private async Task<JsonElement> GetTransactionAsync(string session, Guid id)
    {
        HttpResponseMessage response = await SendAsync(Get($"/v1/finance/transactions/{id}", session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await BodyAsync(response)).GetProperty("transaction");
    }

    private async Task<Guid> CreateCategoryAsync(string session, string name, string kind)
    {
        HttpResponseMessage response = await SendAsync(Post(
            "/v1/finance/categories",
            Json(new { display_name = name, kind }),
            session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }
}
