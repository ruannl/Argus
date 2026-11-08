using System.Globalization;
using System.Net;
using System.Text.Json;
using argus.Server.Data;
using argus.Server.Security;

namespace argus.Server.Tests;

/// <summary>
/// PostgreSQL isolation tests for the customer-scoped finance surface:
/// accounts, categories, merchant tags, cross-customer probing, concurrency,
/// and log privacy.
/// </summary>
public sealed class FinanceContractTests(KitFactory factory) : ApiTestBase(factory)
{
    private const string AccountsPath = "/v1/finance/accounts";
    private const string CategoriesPath = "/v1/finance/categories";
    private const string TagsPath = "/v1/finance/merchant-tags";

    // --- Helpers -----------------------------------------------------------

    private async Task<(string Session, Guid CustomerId)> NewCustomerAsync()
    {
        var email = UniqueEmail();
        var session = await RegisterVerifyLoginAsync(email);
        var me = await SendAsync(Get("/v1/me", session: session));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        return (session, (await BodyAsync(me)).GetProperty("id").GetGuid());
    }

    private async Task<Guid> CreateAccountAsync(string session,
                                                string displayName,
                                                string? institution = null)
    {
        var body = institution is null
            ? Json(new { display_name = displayName })
            : Json(new { display_name = displayName, institution });
        var response = await SendAsync(Post(AccountsPath, body, session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ListAccountsAsync(string session)
    {
        var response = await SendAsync(Get(AccountsPath, session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("accounts");
    }

    private async Task<JsonElement> ListCategoriesAsync(string session)
    {
        var response = await SendAsync(Get(CategoriesPath, session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("categories");
    }

    private async Task<JsonElement> ListTagsAsync(string session)
    {
        var response = await SendAsync(Get(TagsPath, session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("merchant_tags");
    }

    private static JsonElement? FindByName(JsonElement collection,
                                           string name)
    {
        foreach (var item in collection.EnumerateArray())
        {
            if (item.GetProperty("display_name").GetString() == name)
            {
                return item;
            }
        }
        return null;
    }

    private async Task<Guid> CategoryIdAsync(string session,
                                             string name)
    {
        var categories = await ListCategoriesAsync(session);
        var found = FindByName(categories, name);
        Assert.NotNull(found);
        return found!.Value.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CustomerIdByEmailAsync(string email)
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM customers WHERE canonical_email = @email";
        command.Parameters.AddWithValue("email", email);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> InsertTransactionAsync(Guid customerId,
                                                    Guid accountId,
                                                    Guid categoryId,
                                                    decimal amount = -100.00m)
    {
        var statementId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var amountText = amount.ToString("0.00", CultureInfo.InvariantCulture);
        await ExecuteSqlAsync($"""
            INSERT INTO statement_imports (
                id, customer_id, financial_account_id, source_type, statement_label,
                statement_fingerprint, start_date, end_date, created_at, updated_at
            )
            VALUES (
                '{statementId}', '{customerId}', '{accountId}', 'csv', 'statement',
                'fingerprint-{statementId}', DATE '2026-01-01', DATE '2026-01-31', NOW(), NOW()
            );

            INSERT INTO transactions (
                id, customer_id, financial_account_id, statement_import_id, transaction_date,
                description, normalized_description, amount, category_id, source_type,
                source_row_number, identity_key, created_at, updated_at
            )
            VALUES (
                '{transactionId}', '{customerId}', '{accountId}', '{statementId}', DATE '2026-01-05',
                'Test transaction', 'test transaction', {amountText}, '{categoryId}', 'csv',
                1, 'identity-{transactionId}', NOW(), NOW()
            );
            """);
        return transactionId;
    }

    private static HttpRequestMessage WithTestIp(HttpRequestMessage request,
                                                 string ip)
    {
        request.Headers.Add("X-Test-Remote-Ip", ip);
        return request;
    }

    // --- Accounts ----------------------------------------------------------

    [Fact]
    public async Task Accounts_SupportCreateListRenameInstitutionArchiveAndRestore()
    {
        var (session, _) = await NewCustomerAsync();

        var accountId = await CreateAccountAsync(session, "Cheque", "FNB");

        var accounts = await ListAccountsAsync(session);
        var created = FindByName(accounts, "Cheque");
        Assert.NotNull(created);
        Assert.Equal("ZAR", created!.Value.GetProperty("currency").GetString());
        Assert.Equal("FNB", created.Value.GetProperty("institution").GetString());
        Assert.False(created.Value.GetProperty("is_archived").GetBoolean());
        Assert.Equal(0L, created.Value.GetProperty("transaction_count").GetInt64());

        // The contract names are frozen snake_case.
        var names = created.Value.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();
        Assert.Equal(
            ["created_at", "currency", "display_name", "id", "institution", "is_archived", "transaction_count", "updated_at"],
            names);

        // Rename only: institution is preserved when omitted.
        var renamed = await SendAsync(Patch(
            $"{AccountsPath}/{accountId}",
            Json(new { display_name = "Main cheque" }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var renamedBody = await BodyAsync(renamed);
        Assert.Equal("Main cheque", renamedBody.GetProperty("display_name").GetString());
        Assert.Equal("FNB", renamedBody.GetProperty("institution").GetString());

        // Explicit null clears the institution.
        var cleared = await SendAsync(Patch(
            $"{AccountsPath}/{accountId}",
            Json(new { display_name = "Main cheque", institution = (string?)null }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await BodyAsync(cleared)).GetProperty("institution").ValueKind);

        // Set it again, then clear with an empty string.
        var set = await SendAsync(Patch(
            $"{AccountsPath}/{accountId}",
            Json(new { display_name = "Main cheque", institution = "Capitec" }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var clearedEmpty = await SendAsync(Patch(
            $"{AccountsPath}/{accountId}",
            Json(new { display_name = "Main cheque", institution = "" }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, clearedEmpty.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await BodyAsync(clearedEmpty)).GetProperty("institution").ValueKind);

        // Archive and restore are idempotent.
        var archived = await SendAsync(Post($"{AccountsPath}/{accountId}/archive", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.True((await BodyAsync(archived)).GetProperty("is_archived").GetBoolean());

        var archivedAgain = await SendAsync(Post($"{AccountsPath}/{accountId}/archive", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, archivedAgain.StatusCode);
        Assert.True((await BodyAsync(archivedAgain)).GetProperty("is_archived").GetBoolean());

        var restored = await SendAsync(Post($"{AccountsPath}/{accountId}/restore", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.False((await BodyAsync(restored)).GetProperty("is_archived").GetBoolean());

        var restoredAgain = await SendAsync(Post($"{AccountsPath}/{accountId}/restore", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, restoredAgain.StatusCode);
        Assert.False((await BodyAsync(restoredAgain)).GetProperty("is_archived").GetBoolean());
    }

    [Fact]
    public async Task Accounts_RejectInvalidNamesMalformedBodiesAndUnknownFields()
    {
        var (session, _) = await NewCustomerAsync();

        foreach (var name in new[] { "", "   ", new string('a', 81), "bad\u0000name", "bad\nname" })
        {
            var response = await SendAsync(Post(
                AccountsPath,
                Json(new { display_name = name }),
                session: session));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid_request", await ErrorKindAsync(response));
        }

        var unknownField = await SendAsync(Post(
            AccountsPath,
            Json(new { display_name = "Cheque", surprise = true }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, unknownField.StatusCode);

        var malformed = await SendAsync(Post(AccountsPath, "{not-json", session: session));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        var nonObject = await SendAsync(Post(AccountsPath, "[]", session: session));
        Assert.Equal(HttpStatusCode.BadRequest, nonObject.StatusCode);
    }

    // --- Categories --------------------------------------------------------

    [Fact]
    public async Task Categories_FirstUseSeedsDefaultsAndSystemBucket()
    {
        var (session, _) = await NewCustomerAsync();

        var categories = await ListCategoriesAsync(session);
        Assert.Equal(20, categories.GetArrayLength());

        var system = FindByName(categories, "Uncategorised");
        Assert.NotNull(system);
        Assert.True(system!.Value.GetProperty("is_system").GetBoolean());
        Assert.True(system.Value.GetProperty("is_default").GetBoolean());
        Assert.Equal("Uncategorised", system.Value.GetProperty("kind").GetString());
        Assert.False(system.Value.GetProperty("is_archived").GetBoolean());

        var expected = new Dictionary<string, string>
        {
            ["Groceries"] = "Expense",
            ["Dining and takeaways"] = "Expense",
            ["Transport"] = "Expense",
            ["Housing and rent"] = "Expense",
            ["Home loan"] = "Expense",
            ["Levies"] = "Expense",
            ["Utilities"] = "Expense",
            ["Health"] = "Expense",
            ["Education and school fees"] = "Expense",
            ["Shopping"] = "Expense",
            ["Entertainment"] = "Expense",
            ["Subscriptions"] = "Expense",
            ["Fees and charges"] = "Expense",
            ["Travel"] = "Expense",
            ["Other expenses"] = "Expense",
            ["Salary"] = "Income",
            ["Other income"] = "Income",
            ["Transfers"] = "Transfer",
            ["Excluded"] = "Excluded",
        };

        foreach (var (name, kind) in expected)
        {
            var category = FindByName(categories, name);
            Assert.NotNull(category);
            Assert.Equal(kind, category!.Value.GetProperty("kind").GetString());
            Assert.False(category.Value.GetProperty("is_system").GetBoolean());
            Assert.True(category.Value.GetProperty("is_default").GetBoolean());
        }

        // Repeated reads do not seed a second time.
        Assert.Equal(20, (await ListCategoriesAsync(session)).GetArrayLength());
    }

    [Fact]
    public async Task Categories_SeedingDoesNotResurrectRenamedOrDeletedDefaults()
    {
        var (session, customerId) = await NewCustomerAsync();
        _ = await ListCategoriesAsync(session);

        var groceriesId = await CategoryIdAsync(session, "Groceries");
        var travelId = await CategoryIdAsync(session, "Travel");

        var rename = await SendAsync(Patch(
            $"{CategoriesPath}/{groceriesId}",
            Json(new { display_name = "Supermarket", kind = "Expense" }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        var delete = await SendAsync(Delete($"{CategoriesPath}/{travelId}", session: session));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        // Re-invoking the seeder must not restore the renamed or deleted defaults.
        var store = new FinanceStore(Factory.CreateDb());
        await store.EnsureCategoriesSeededAsync(customerId);

        var categories = await ListCategoriesAsync(session);
        Assert.NotNull(FindByName(categories, "Supermarket"));
        Assert.Null(FindByName(categories, "Groceries"));
        Assert.Null(FindByName(categories, "Travel"));
        Assert.Equal(19, categories.GetArrayLength());
    }

    [Fact]
    public async Task Categories_SupportCustomCreateRenameKindChangeArchiveRestoreAndDelete()
    {
        var (session, _) = await NewCustomerAsync();
        _ = await ListCategoriesAsync(session);

        var create = await SendAsync(Post(
            CategoriesPath,
            Json(new { display_name = "Coffee", kind = "Expense" }),
            session: session));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await BodyAsync(create);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Coffee", created.GetProperty("display_name").GetString());
        Assert.Equal("Expense", created.GetProperty("kind").GetString());
        Assert.False(created.GetProperty("is_system").GetBoolean());
        Assert.False(created.GetProperty("is_default").GetBoolean());

        // Case and surrounding whitespace collide.
        var duplicate = await SendAsync(Post(
            CategoriesPath,
            Json(new { display_name = "  coffee  ", kind = "Income" }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("duplicate_name", await ErrorKindAsync(duplicate));

        // Rename succeeds, and renaming to a case variant of itself also succeeds.
        var rename = await SendAsync(Patch(
            $"{CategoriesPath}/{id}",
            Json(new { display_name = "Coffee shops", kind = "Expense" }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal("Coffee shops", (await BodyAsync(rename)).GetProperty("display_name").GetString());

        // Kind change without confirmation is rejected and leaves the kind intact.
        var unconfirmed = await SendAsync(Patch(
            $"{CategoriesPath}/{id}",
            Json(new { display_name = "Coffee shops", kind = "Income" }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal("kind_change_confirmation_required", await ErrorKindAsync(unconfirmed));
        Assert.Equal("Expense", FindByName(await ListCategoriesAsync(session), "Coffee shops")!
            .Value.GetProperty("kind").GetString());

        var confirmed = await SendAsync(Patch(
            $"{CategoriesPath}/{id}",
            Json(new { display_name = "Coffee shops", kind = "Income", confirm_kind_change = true }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("Income", (await BodyAsync(confirmed)).GetProperty("kind").GetString());

        // Archive and restore are idempotent.
        var archived = await SendAsync(Post($"{CategoriesPath}/{id}/archive", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.True((await BodyAsync(archived)).GetProperty("is_archived").GetBoolean());
        var archivedAgain = await SendAsync(Post($"{CategoriesPath}/{id}/archive", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, archivedAgain.StatusCode);
        var restored = await SendAsync(Post($"{CategoriesPath}/{id}/restore", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.False((await BodyAsync(restored)).GetProperty("is_archived").GetBoolean());
        var restoredAgain = await SendAsync(Post($"{CategoriesPath}/{id}/restore", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, restoredAgain.StatusCode);

        var delete = await SendAsync(Delete($"{CategoriesPath}/{id}", session: session));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        var deleted = await BodyAsync(delete);
        Assert.True(deleted.GetProperty("deleted").GetBoolean());
        Assert.Equal(0L, deleted.GetProperty("reassigned_transaction_count").GetInt64());
        Assert.Null(FindByName(await ListCategoriesAsync(session), "Coffee shops"));
    }

    [Fact]
    public async Task CategoryDelete_ReassignsTransactionsToUncategorised()
    {
        var (session, customerId) = await NewCustomerAsync();
        _ = await ListCategoriesAsync(session);
        var accountId = await CreateAccountAsync(session, "Cheque");
        var groceriesId = await CategoryIdAsync(session, "Groceries");

        await InsertTransactionAsync(customerId, accountId, groceriesId);
        await InsertTransactionAsync(customerId, accountId, groceriesId);

        var before = FindByName(await ListCategoriesAsync(session), "Groceries");
        Assert.Equal(2L, before!.Value.GetProperty("transaction_count").GetInt64());

        var delete = await SendAsync(Delete($"{CategoriesPath}/{groceriesId}", session: session));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        var body = await BodyAsync(delete);
        Assert.Equal(2L, body.GetProperty("reassigned_transaction_count").GetInt64());

        var categories = await ListCategoriesAsync(session);
        Assert.Null(FindByName(categories, "Groceries"));
        var uncategorised = FindByName(categories, "Uncategorised");
        Assert.Equal(2L, uncategorised!.Value.GetProperty("transaction_count").GetInt64());
        Assert.Equal(2L, await CountRowsAsync("transactions"));
    }

    [Fact]
    public async Task SystemUncategorised_IsImmutable()
    {
        var (session, _) = await NewCustomerAsync();
        var uncategorisedId = await CategoryIdAsync(session, "Uncategorised");

        var rename = await SendAsync(Patch(
            $"{CategoriesPath}/{uncategorisedId}",
            Json(new { display_name = "Everything", kind = "Expense" }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
        Assert.Equal("system_category_immutable", await ErrorKindAsync(rename));

        var kindChange = await SendAsync(Patch(
            $"{CategoriesPath}/{uncategorisedId}",
            Json(new { display_name = "Uncategorised", kind = "Income", confirm_kind_change = true }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, kindChange.StatusCode);
        Assert.Equal("system_category_immutable", await ErrorKindAsync(kindChange));

        var archive = await SendAsync(Post($"{CategoriesPath}/{uncategorisedId}/archive", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.BadRequest, archive.StatusCode);
        Assert.Equal("system_category_immutable", await ErrorKindAsync(archive));

        var restore = await SendAsync(Post($"{CategoriesPath}/{uncategorisedId}/restore", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.BadRequest, restore.StatusCode);

        var delete = await SendAsync(Delete($"{CategoriesPath}/{uncategorisedId}", session: session));
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Equal("system_category_immutable", await ErrorKindAsync(delete));

        foreach (var duplicateName in new[] { "  uncategorised  ", "Uncategorised" })
        {
            var duplicate = await SendAsync(Post(
                CategoriesPath,
                Json(new { display_name = duplicateName, kind = "Expense" }),
                session: session));
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal("duplicate_name", await ErrorKindAsync(duplicate));
        }

        Assert.NotNull(FindByName(await ListCategoriesAsync(session), "Uncategorised"));
    }

    [Fact]
    public async Task Categories_RejectInvalidKindAndOversizedNames()
    {
        var (session, _) = await NewCustomerAsync();

        var badKind = await SendAsync(Post(
            CategoriesPath,
            Json(new { display_name = "Coffee", kind = "Bogus" }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
        Assert.Equal("invalid_request", await ErrorKindAsync(badKind));

        var oversized = await SendAsync(Post(
            CategoriesPath,
            Json(new { display_name = new string('b', 81), kind = "Expense" }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
    }

    [Fact]
    public async Task CategoryMutations_MapDatabaseCheckViolationsToClientErrors()
    {
        var (session, _) = await NewCustomerAsync();
        _ = await ListCategoriesAsync(session);
        var groceriesId = await CategoryIdAsync(session, "Groceries");

        // A race the application validation cannot see: the database check
        // rejects the write, and the store must map 23514 to a 400 rather than
        // letting it become a 500.
        await ExecuteSqlAsync(
            "ALTER TABLE categories ADD CONSTRAINT test_23514_create CHECK (kind <> 'Expense') NOT VALID");
        try
        {
            var create = await SendAsync(Post(
                CategoriesPath,
                Json(new { display_name = "Violation", kind = "Expense" }),
                session: session));
            Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
            Assert.Equal("invalid_request", await ErrorKindAsync(create));
        }
        finally
        {
            await ExecuteSqlAsync("ALTER TABLE categories DROP CONSTRAINT IF EXISTS test_23514_create");
        }

        await ExecuteSqlAsync(
            $"ALTER TABLE categories ADD CONSTRAINT test_23514_archive CHECK (NOT is_archived OR id <> '{groceriesId}') NOT VALID");
        try
        {
            var archive = await SendAsync(Post(
                $"{CategoriesPath}/{groceriesId}/archive",
                string.Empty,
                session: session));
            Assert.Equal(HttpStatusCode.BadRequest, archive.StatusCode);
            Assert.Equal("system_category_immutable", await ErrorKindAsync(archive));
        }
        finally
        {
            await ExecuteSqlAsync("ALTER TABLE categories DROP CONSTRAINT IF EXISTS test_23514_archive");
        }
    }

    // --- Merchant tags -----------------------------------------------------

    [Fact]
    public async Task MerchantTags_SupportCreateRenameArchiveRestoreAndUniqueness()
    {
        var (session, _) = await NewCustomerAsync();

        var create = await SendAsync(Post(TagsPath, Json(new { display_name = "Woolworths" }), session: session));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await BodyAsync(create);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(0L, created.GetProperty("transaction_count").GetInt64());

        var names = created.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();
        Assert.Equal(["created_at", "display_name", "id", "is_archived", "transaction_count", "updated_at"], names);

        foreach (var duplicateName in new[] { "  woolworths ", "WOOLWORTHS" })
        {
            var duplicate = await SendAsync(Post(TagsPath, Json(new { display_name = duplicateName }), session: session));
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal("duplicate_name", await ErrorKindAsync(duplicate));
        }

        // Renaming the same tag to a different case of its own name succeeds.
        var selfRename = await SendAsync(Patch(
            $"{TagsPath}/{id}",
            Json(new { display_name = "woolworths" }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, selfRename.StatusCode);
        Assert.Equal("woolworths", (await BodyAsync(selfRename)).GetProperty("display_name").GetString());

        var other = await SendAsync(Post(TagsPath, Json(new { display_name = "Checkers" }), session: session));
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        var otherId = (await BodyAsync(other)).GetProperty("id").GetGuid();

        var collidingRename = await SendAsync(Patch(
            $"{TagsPath}/{otherId}",
            Json(new { display_name = "woolworths" }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, collidingRename.StatusCode);
        Assert.Equal("duplicate_name", await ErrorKindAsync(collidingRename));

        var archived = await SendAsync(Post($"{TagsPath}/{id}/archive", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.True((await BodyAsync(archived)).GetProperty("is_archived").GetBoolean());
        var archivedAgain = await SendAsync(Post($"{TagsPath}/{id}/archive", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, archivedAgain.StatusCode);
        var restored = await SendAsync(Post($"{TagsPath}/{id}/restore", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.False((await BodyAsync(restored)).GetProperty("is_archived").GetBoolean());
        var restoredAgain = await SendAsync(Post($"{TagsPath}/{id}/restore", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, restoredAgain.StatusCode);

        var delete = await SendAsync(Delete($"{TagsPath}/{id}", session: session));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        var deleted = await BodyAsync(delete);
        Assert.True(deleted.GetProperty("deleted").GetBoolean());
        Assert.Equal(0L, deleted.GetProperty("unassigned_transaction_count").GetInt64());

        // Another customer may use the same tag name independently.
        var (otherCustomerSession, _) = await NewCustomerAsync();
        var independent = await SendAsync(Post(
            TagsPath,
            Json(new { display_name = "Woolworths" }),
            session: otherCustomerSession));
        Assert.Equal(HttpStatusCode.Created, independent.StatusCode);
    }

    [Fact]
    public async Task MerchantTagDelete_RemovesAssignmentsAndKeepsTransactions()
    {
        var (session, customerId) = await NewCustomerAsync();
        _ = await ListCategoriesAsync(session);
        var accountId = await CreateAccountAsync(session, "Cheque");
        var groceriesId = await CategoryIdAsync(session, "Groceries");

        var create = await SendAsync(Post(TagsPath, Json(new { display_name = "Woolworths" }), session: session));
        var tagId = (await BodyAsync(create)).GetProperty("id").GetGuid();

        var first = await InsertTransactionAsync(customerId, accountId, groceriesId);
        var second = await InsertTransactionAsync(customerId, accountId, groceriesId);
        await ExecuteSqlAsync($"""
            INSERT INTO transaction_merchant_tags (customer_id, transaction_id, merchant_tag_id)
            VALUES ('{customerId}', '{first}', '{tagId}'), ('{customerId}', '{second}', '{tagId}');
            """);

        var before = FindByName(await ListTagsAsync(session), "Woolworths");
        Assert.Equal(2L, before!.Value.GetProperty("transaction_count").GetInt64());

        var delete = await SendAsync(Delete($"{TagsPath}/{tagId}", session: session));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(2L, (await BodyAsync(delete)).GetProperty("unassigned_transaction_count").GetInt64());

        Assert.Equal(2L, await CountRowsAsync("transactions"));
        Assert.Equal(0L, await CountRowsAsync("transaction_merchant_tags"));
        Assert.Null(FindByName(await ListTagsAsync(session), "Woolworths"));
        Assert.NotNull(FindByName(await ListCategoriesAsync(session), "Groceries"));
    }

    // --- Security ----------------------------------------------------------

    [Fact]
    public async Task CrossCustomer_ProbesReturnIdenticalNotFoundAndMutateNothing()
    {
        var (ownerSession, _) = await NewCustomerAsync();
        var (intruderSession, _) = await NewCustomerAsync();

        var accountId = await CreateAccountAsync(ownerSession, "Owner cheque");
        _ = await ListCategoriesAsync(ownerSession);
        var categoryCreate = await SendAsync(Post(
            CategoriesPath,
            Json(new { display_name = "Owner category", kind = "Expense" }),
            session: ownerSession));
        var categoryId = (await BodyAsync(categoryCreate)).GetProperty("id").GetGuid();
        var tagCreate = await SendAsync(Post(
            TagsPath,
            Json(new { display_name = "Owner tag" }),
            session: ownerSession));
        var tagId = (await BodyAsync(tagCreate)).GetProperty("id").GetGuid();

        var probes = new List<(HttpRequestMessage Foreign, HttpRequestMessage Random)>
        {
            (
                Patch($"{AccountsPath}/{accountId}", Json(new { display_name = "Hijacked" }), session: intruderSession),
                Patch($"{AccountsPath}/{Guid.NewGuid()}", Json(new { display_name = "Hijacked" }), session: intruderSession)),
            (
                Post($"{AccountsPath}/{accountId}/archive", string.Empty, session: intruderSession),
                Post($"{AccountsPath}/{Guid.NewGuid()}/archive", string.Empty, session: intruderSession)),
            (
                Post($"{AccountsPath}/{accountId}/restore", string.Empty, session: intruderSession),
                Post($"{AccountsPath}/{Guid.NewGuid()}/restore", string.Empty, session: intruderSession)),
            (
                Patch($"{CategoriesPath}/{categoryId}", Json(new { display_name = "Hijacked", kind = "Expense" }), session: intruderSession),
                Patch($"{CategoriesPath}/{Guid.NewGuid()}", Json(new { display_name = "Hijacked", kind = "Expense" }), session: intruderSession)),
            (
                Post($"{CategoriesPath}/{categoryId}/archive", string.Empty, session: intruderSession),
                Post($"{CategoriesPath}/{Guid.NewGuid()}/archive", string.Empty, session: intruderSession)),
            (
                Delete($"{CategoriesPath}/{categoryId}", session: intruderSession),
                Delete($"{CategoriesPath}/{Guid.NewGuid()}", session: intruderSession)),
            (
                Patch($"{TagsPath}/{tagId}", Json(new { display_name = "Hijacked" }), session: intruderSession),
                Patch($"{TagsPath}/{Guid.NewGuid()}", Json(new { display_name = "Hijacked" }), session: intruderSession)),
            (
                Post($"{TagsPath}/{tagId}/archive", string.Empty, session: intruderSession),
                Post($"{TagsPath}/{Guid.NewGuid()}/archive", string.Empty, session: intruderSession)),
            (
                Delete($"{TagsPath}/{tagId}", session: intruderSession),
                Delete($"{TagsPath}/{Guid.NewGuid()}", session: intruderSession)),
        };

        foreach (var (foreign, random) in probes)
        {
            var foreignResponse = await SendAsync(foreign);
            var randomResponse = await SendAsync(random);
            Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, randomResponse.StatusCode);
            Assert.Equal(await foreignResponse.Content.ReadAsStringAsync(), await randomResponse.Content.ReadAsStringAsync());
            Assert.Equal("not_found", await ErrorKindAsync(foreignResponse));
        }

        // The owner's data is untouched.
        var accounts = await ListAccountsAsync(ownerSession);
        var account = FindByName(accounts, "Owner cheque");
        Assert.NotNull(account);
        Assert.False(account!.Value.GetProperty("is_archived").GetBoolean());
        Assert.NotNull(FindByName(await ListCategoriesAsync(ownerSession), "Owner category"));
        Assert.NotNull(FindByName(await ListTagsAsync(ownerSession), "Owner tag"));
    }

    [Fact]
    public async Task FinanceRoutes_RequireTrustedCallerSessionAndActiveSubscription()
    {
        var (session, _) = await NewCustomerAsync();

        // Missing / wrong application secret.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Get(AccountsPath, secret: null, session: session))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Get(AccountsPath, secret: "wrong", session: session))).StatusCode);

        // Origin mismatch is bodyless 403 on every verb.
        var evilGet = await SendAsync(Get(AccountsPath, session: session, origin: "http://evil.test"));
        Assert.Equal(HttpStatusCode.Forbidden, evilGet.StatusCode);
        Assert.Equal(string.Empty, await evilGet.Content.ReadAsStringAsync());
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsync(Post(AccountsPath, Json(new { display_name = "X" }), session: session, origin: "http://evil.test"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsync(Patch($"{AccountsPath}/{Guid.NewGuid()}", Json(new { display_name = "X" }), session: session, origin: "http://evil.test"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsync(Delete($"{TagsPath}/{Guid.NewGuid()}", session: session, origin: "http://evil.test"))).StatusCode);

        // Missing or unknown session.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Get(AccountsPath))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Get(AccountsPath, session: "not-a-session"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await SendAsync(Delete($"{TagsPath}/{Guid.NewGuid()}", session: "not-a-session"))).StatusCode);

        // A session for an unverified customer is rejected.
        var unverifiedEmail = UniqueEmail();
        await RegisterAsync(unverifiedEmail);
        var unverifiedId = await CustomerIdByEmailAsync(unverifiedEmail);
        // Test-only deliberately invalid session. Production issuance requires
        // the hash verified by login and cannot mint by customer ID alone.
        var unverifiedToken = Tokens.NewToken();
        var now = DateTime.UtcNow;
        await using (var connection = await Factory.CreateDb().OpenAsync())
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO sessions (id, customer_id, token_hash, expires_at, revoked_at, last_seen_at, created_at)
                VALUES (@id, @customer, @hash, @expires, NULL, @now, @now)
                """;
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("customer", unverifiedId);
            insert.Parameters.AddWithValue("hash", Tokens.HashToken(unverifiedToken));
            insert.Parameters.AddWithValue("expires", now.Add(Store.SessionAbsoluteTtl));
            insert.Parameters.AddWithValue("now", now);
            await insert.ExecuteNonQueryAsync();
        }
        var unverified = await SendAsync(Get(AccountsPath, session: unverifiedToken));
        Assert.Equal(HttpStatusCode.Unauthorized, unverified.StatusCode);
        Assert.Equal("unauthorized", await ErrorKindAsync(unverified));
    }

    [Fact]
    public async Task FinanceRoutes_RateLimitEveryVerbPerIp()
    {
        var (session, _) = await NewCustomerAsync();
        const string ip = "203.0.113.42";

        HttpResponseMessage? last = null;
        for (var index = 0; index < 300; index++)
        {
            last?.Dispose();
            last = await SendAsync(WithTestIp(Get(AccountsPath, session: session), ip));
            Assert.Equal(HttpStatusCode.OK, last.StatusCode);
        }
        last?.Dispose();

        foreach (var request in new[]
        {
            Get(AccountsPath, session: session),
            Post(AccountsPath, Json(new { display_name = "X" }), session: session),
            Patch($"{AccountsPath}/{Guid.NewGuid()}", Json(new { display_name = "X" }), session: session),
            Delete($"{TagsPath}/{Guid.NewGuid()}", session: session),
        })
        {
            var response = await SendAsync(WithTestIp(request, ip));
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal("rate_limited", await ErrorKindAsync(response));
        }
    }

    // --- Concurrency -------------------------------------------------------

    [Fact]
    public async Task ConcurrentFirstUseSeeding_ProducesExactlyTwentyCategories()
    {
        var (session, customerId) = await NewCustomerAsync();

        var responses = await Task.WhenAll(
            SendAsync(Get(CategoriesPath, session: session)),
            SendAsync(Get(CategoriesPath, session: session)),
            SendAsync(Get(CategoriesPath, session: session)));

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(20, (await BodyAsync(response)).GetProperty("categories").GetArrayLength());
        }

        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), COUNT(DISTINCT normalized_name) FROM categories WHERE customer_id = @customer_id";
        command.Parameters.AddWithValue("customer_id", customerId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(20L, reader.GetInt64(0));
        Assert.Equal(20L, reader.GetInt64(1));
    }

    // --- Log privacy -------------------------------------------------------

    [Fact]
    public async Task UserSuppliedNamesNeverReachLogs_OnSuccessOrRejection()
    {
        var (session, _) = await NewCustomerAsync();

        // Success paths: create, rename, archive, restore for every entity type.
        var accountCreate = await SendAsync(Post(
            AccountsPath,
            Json(new { display_name = "CanaryAccount_9921", institution = "CanaryInst_9921" }),
            session: session));
        Assert.Equal(HttpStatusCode.Created, accountCreate.StatusCode);
        var accountId = (await BodyAsync(accountCreate)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Patch(
            $"{AccountsPath}/{accountId}",
            Json(new { display_name = "CanaryAccount_9921b", institution = "CanaryInst_9921b" }),
            session: session))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Post($"{AccountsPath}/{accountId}/archive", string.Empty, session: session))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Post($"{AccountsPath}/{accountId}/restore", string.Empty, session: session))).StatusCode);

        var categoryCreate = await SendAsync(Post(
            CategoriesPath,
            Json(new { display_name = "CanaryCategory_9921", kind = "Expense" }),
            session: session));
        Assert.Equal(HttpStatusCode.Created, categoryCreate.StatusCode);
        var categoryId = (await BodyAsync(categoryCreate)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Patch(
            $"{CategoriesPath}/{categoryId}",
            Json(new { display_name = "CanaryCategory_9921b", kind = "Expense" }),
            session: session))).StatusCode);

        var tagCreate = await SendAsync(Post(
            TagsPath,
            Json(new { display_name = "CanaryTag_9921" }),
            session: session));
        Assert.Equal(HttpStatusCode.Created, tagCreate.StatusCode);
        var tagId = (await BodyAsync(tagCreate)).GetProperty("id").GetGuid();

        // Rejection paths: unique violation (23505), invalid name, and system mutation.
        var duplicateCategory = await SendAsync(Post(
            CategoriesPath,
            Json(new { display_name = "canarycategory_9921b", kind = "Expense" }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, duplicateCategory.StatusCode);

        var duplicateTag = await SendAsync(Post(
            TagsPath,
            Json(new { display_name = "  CANARYTAG_9921 " }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, duplicateTag.StatusCode);

        var invalidName = await SendAsync(Post(
            CategoriesPath,
            Json(new { display_name = "CanaryCategory\u0000_9921", kind = "Expense" }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, invalidName.StatusCode);

        var uncategorisedId = await CategoryIdAsync(session, "Uncategorised");
        var systemMutation = await SendAsync(Patch(
            $"{CategoriesPath}/{uncategorisedId}",
            Json(new { display_name = "CanarySystem_9921", kind = "Expense" }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, systemMutation.StatusCode);

        // Clean-up paths also must stay quiet.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Delete($"{CategoriesPath}/{categoryId}", session: session))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Delete($"{TagsPath}/{tagId}", session: session))).StatusCode);

        // The capture must be live, then must contain no canary value.
        Assert.True(
            Factory.LogCapture.Contains("audit.finance_account_created"),
            "log capture is not receiving finance audit events");
        Assert.False(
            Factory.LogCapture.Contains("Canary"),
            string.Join("\n", Factory.LogCapture.Snapshot()));
    }
}
