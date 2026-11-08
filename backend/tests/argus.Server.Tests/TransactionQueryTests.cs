using System.Diagnostics;
using System.Net;
using System.Text.Json;
using argus.Server.Finance;
using Xunit.Abstractions;

namespace argus.Server.Tests;

public sealed class TransactionQueryTests(KitFactory factory,
                                          ITestOutputHelper output)
    : TransactionTestBase(factory)
{
    private const string QueryPath = "/v1/finance/transactions/query";

    private async Task<HttpResponseMessage> QueryResponseAsync(string session,
                                                               object body) =>
        await SendAsync(Post(QueryPath, Json(body), session: session));

    private async Task<JsonElement> QueryAsync(string session,
                                               object? body = null)
    {
        var response = await QueryResponseAsync(session, body ?? new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    private static JsonElement[] Rows(JsonElement page) =>
        page.GetProperty("transactions").EnumerateArray().ToArray();

    private static long Count(JsonElement page,
                              string name) =>
        page.GetProperty(name).GetInt64();

    [Fact]
    public async Task CountsSeparateInventoryVisibleAndHiddenMatches()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (systemId, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        var hiddenStatementId = await SeedStatementAsync(customerId, accountId, hidden: true);

        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 2, 1), "Visible one", -10.00m, 1);
        await SeedTransactionAsync(
            customerId, accountId, statementId, systemId,
            new DateOnly(2024, 2, 2), "Visible two", 0m, 2);
        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 2, 3), "Visible three", 9999999999999999.99m, 3);
        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 2, 4), "Individual hidden", -1.00m, 4, hidden: true);
        await SeedTransactionAsync(
            customerId, accountId, hiddenStatementId, expenseId,
            new DateOnly(2024, 2, 5), "Parent hidden", -2.00m, 1);

        var visible = await QueryAsync(session);
        Assert.Equal(5, Count(visible, "inventory_count"));
        Assert.Equal(3, Count(visible, "matching_visible_count"));
        Assert.Equal(2, Count(visible, "matching_hidden_count"));
        Assert.Equal(5, Count(visible, "matching_total"));
        Assert.Equal(3, Count(visible, "filtered_total"));
        Assert.Equal(3, Rows(visible).Length);
        Assert.All(Rows(visible), row => Assert.False(row.GetProperty("effective_hidden").GetBoolean()));

        var hidden = await QueryAsync(session, new
        {
            filters = new { visibility = "hidden" },
        });
        Assert.Equal(2, Rows(hidden).Length);
        Assert.Equal(2, Count(hidden, "filtered_total"));
        var reasons = Rows(hidden)
            .Select(row => row.GetProperty("hidden_reason").GetString())
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "individual", "statement" }, reasons);

        var all = await QueryAsync(session, new
        {
            filters = new { visibility = "all" },
        });
        Assert.Equal(5, Rows(all).Length);
        Assert.Equal(5, Count(all, "filtered_total"));
        Assert.Equal(5, Count(all, "inventory_count"));
    }

    [Fact]
    public async Task AmountsAreExactInvariantStringsAndDirectionFollowsSign()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);

        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 3, 1), "Negative", -12.30m, 1);
        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 3, 2), "Zero", 0m, 2);
        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 3, 3), "Extreme", 9999999999999999.99m, 3);
        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 3, 4), "Positive", 7.5m, 4);

        var page = await QueryAsync(session, new { sort_by = "amount", sort_direction = "asc" });
        var rows = Rows(page);
        Assert.Equal(new[] { "out", "zero", "in", "in" }, rows.Select(r => r.GetProperty("direction").GetString()));

        var byDescription = Rows(await QueryAsync(session, new
        {
            sort_by = "description",
            sort_direction = "asc",
        })).ToDictionary(
            row => row.GetProperty("description").GetString()!,
            row => row.GetProperty("amount").GetString());

        Assert.Equal("-12.30", byDescription["Negative"]);
        Assert.Equal("0.00", byDescription["Zero"]);
        Assert.Equal("9999999999999999.99", byDescription["Extreme"]);
        Assert.Equal("7.50", byDescription["Positive"]);
        Assert.All(rows, row => Assert.Equal("ZAR", row.GetProperty("currency").GetString()));
        Assert.Contains(
            rows,
            row => row.GetProperty("amount").ValueKind == JsonValueKind.String);
    }

    [Fact]
    public async Task SearchIsOneEscapedLiteralSubstringAcrossDescriptionOrTag()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        var cafeTag = await CreateTagAsync(session, "Cafe");
        var discountTag = await CreateTagAsync(session, "Discount");
        var archivedTag = await CreateTagAsync(session, "Old Label");

        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 4, 1), "CAFE 100% discount", -10m, 1);
        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 4, 2), "Weekly shop", -20m, 2, tagIds: [cafeTag]);
        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 4, 3), "literal _ underscore", -30m, 3);
        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 4, 4), "Old label history", -40m, 4, tagIds: [archivedTag]);

        var archive = await SendAsync(Post(
            $"/v1/finance/merchant-tags/{archivedTag}/archive", "{}", session: session));
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        var percent = Rows(await QueryAsync(session, new
        {
            filters = new { search = "100%" },
        }));
        Assert.Single(percent);
        Assert.Equal("CAFE 100% discount", percent[0].GetProperty("description").GetString());

        var underscore = Rows(await QueryAsync(session, new
        {
            filters = new { search = "_" },
        }));
        Assert.Single(underscore);
        Assert.Equal("literal _ underscore", underscore[0].GetProperty("description").GetString());

        var byTag = Rows(await QueryAsync(session, new
        {
            filters = new { search = "cafe" },
        }));
        Assert.Equal(2, byTag.Length);
        Assert.Equal(
            new[] { "CAFE 100% discount", "Weekly shop" },
            byTag.Select(row => row.GetProperty("description").GetString()).Order(StringComparer.Ordinal).ToArray());

        var archivedTagSearch = Rows(await QueryAsync(session, new
        {
            filters = new { search = "old label" },
        }));
        Assert.Single(archivedTagSearch);

        var wholeSubstring = Rows(await QueryAsync(session, new
        {
            filters = new { search = "cafe 100" },
        }));
        Assert.Single(wholeSubstring);

        var missing = Rows(await QueryAsync(session, new
        {
            filters = new { search = "no-such-value" },
        }));
        Assert.Empty(missing);
        Assert.Equal(4, Count(await QueryAsync(session), "inventory_count"));
        Assert.All(new[] { discountTag, cafeTag }, id => Assert.NotEqual(Guid.Empty, id));
    }

    [Fact]
    public async Task FiltersCombineAcrossDimensions()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var otherAccountId = await CreateAccountAsync(session, "Savings");
        var (systemId, expenseId) = await DefaultCategoriesAsync(session);
        var tagA = await CreateTagAsync(session, "Tag A");
        var tagB = await CreateTagAsync(session, "Tag B");
        var statementId = await SeedStatementAsync(customerId, accountId);
        var otherStatementId = await SeedStatementAsync(customerId, otherAccountId);

        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 5, 1), "Jan first", -10m, 1, tagIds: [tagA]);
        await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 5, 2), "Jan second", -20m, 2, tagIds: [tagA, tagB]);
        await SeedTransactionAsync(
            customerId, accountId, statementId, systemId,
            new DateOnly(2024, 5, 3), "Untagged income", 30m, 3);
        await SeedTransactionAsync(
            customerId, otherAccountId, otherStatementId, expenseId,
            new DateOnly(2024, 6, 1), "Other account", -40m, 1, tagIds: [tagB]);

        async Task<int> CountAsync(object filters) =>
            Rows(await QueryAsync(session, new { filters })).Length;

        Assert.Equal(2, await CountAsync(new
        {
            start_date = "2024-05-01",
            end_date = "2024-05-02",
        }));
        Assert.Equal(2, await CountAsync(new
        {
            financial_account_id = accountId,
            start_date = "2024-05-02",
        }));
        Assert.Equal(1, await CountAsync(new
        {
            statement_import_id = otherStatementId,
        }));
        Assert.Equal(1, await CountAsync(new { category_id = systemId }));
        Assert.Equal(2, await CountAsync(new { merchant_tag = tagA }));
        Assert.Equal(1, await CountAsync(new { merchant_tag = "untagged" }));
        Assert.Equal(1, await CountAsync(new { category_kind = "Uncategorised" }));
        Assert.Equal(1, await CountAsync(new { direction = "in" }));
        Assert.Equal(3, await CountAsync(new { direction = "out" }));
        Assert.Equal(0, await CountAsync(new
        {
            category_id = systemId,
            category_kind = "Expense",
        }));
        Assert.Equal(3, await CountAsync(new { financial_account_id = accountId }));
    }

    [Fact]
    public async Task ArchivedAccountsCategoriesAndTagsRemainHistoryFilters()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var tagId = await CreateTagAsync(session, "Archived label");
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 7, 1), "Archived history", -5m, 1, tagIds: [tagId]);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Post(
            $"/v1/finance/accounts/{accountId}/archive", "{}", session: session))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Post(
            $"/v1/finance/categories/{expenseId}/archive", "{}", session: session))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Post(
            $"/v1/finance/merchant-tags/{tagId}/archive", "{}", session: session))).StatusCode);

        var rows = Rows(await QueryAsync(session, new
        {
            filters = new
            {
                financial_account_id = accountId,
                category_id = expenseId,
                merchant_tag = tagId,
            },
        }));
        var row = Assert.Single(rows);
        Assert.True(row.GetProperty("financial_account").GetProperty("is_archived").GetBoolean());
        Assert.True(row.GetProperty("category").GetProperty("is_archived").GetBoolean());
        Assert.True(row.GetProperty("merchant_tags")[0].GetProperty("is_archived").GetBoolean());

        var detail = await SendAsync(Get($"/v1/finance/transactions/{rowId}", session: session));
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal("Archived history", (await BodyAsync(detail)).GetProperty("transaction").GetProperty("description").GetString());
    }

    [Fact]
    public async Task PagingSortingAndValidationAreWhitelisted()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        var firstId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 8, 1), "Alpha", -100m, 1);
        var secondId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 8, 1), "Alpha", 9m, 2);

        var ascending = Rows(await QueryAsync(session, new
        {
            sort_by = "amount",
            sort_direction = "asc",
        }));
        Assert.Equal(new[] { "Alpha", "Alpha" }, ascending.Select(row => row.GetProperty("description").GetString()));
        Assert.Equal("-100.00", ascending[0].GetProperty("amount").GetString());
        Assert.Equal("9.00", ascending[1].GetProperty("amount").GetString());

        var tied = Rows(await QueryAsync(session, new
        {
            sort_by = "description",
            sort_direction = "asc",
        }));
        Assert.Equal(
            new[] { firstId, secondId }.Select(id => id.ToString()).Order(StringComparer.Ordinal).ToArray(),
            tied.Select(row => row.GetProperty("id").GetGuid().ToString()).Order(StringComparer.Ordinal).ToArray());

        var page = await QueryAsync(session, new { page = 2, page_size = 25 });
        Assert.Empty(Rows(page));
        Assert.Equal(2, Count(page, "inventory_count"));
        Assert.Equal(2, Count(page, "matching_visible_count"));

        Assert.Equal(HttpStatusCode.BadRequest, (await QueryResponseAsync(session, new
        {
            page_size = 30,
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await QueryResponseAsync(session, new
        {
            sort_by = "created_at",
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await QueryResponseAsync(session, new
        {
            filters = new { start_date = "2024-09-01", end_date = "2024-08-01" },
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await QueryResponseAsync(session, new
        {
            filters = new { start_date = "2024-13-01" },
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await QueryResponseAsync(session, new
        {
            filters = new { direction = "sideways" },
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await QueryResponseAsync(session, new
        {
            unknown_field = "x",
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await QueryResponseAsync(session, new
        {
            filters = new { search = new string('a', 201) },
        })).StatusCode);
    }

    [Fact]
    public async Task ForeignIdentifiersAreIndistinguishableAndMixedFiltersAreDenied()
    {
        var (session, customerId) = await NewCustomerAsync();
        var (_, otherCustomerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        var rowId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 9, 1), "Owned row", -1m, 1);

        var foreignAccount = Guid.NewGuid();
        var foreignStatement = Guid.NewGuid();
        var foreignCategory = Guid.NewGuid();
        var foreignTag = Guid.NewGuid();

        foreach (var filters in new object[]
                 {
                     new { financial_account_id = foreignAccount },
                     new { statement_import_id = foreignStatement },
                     new { category_id = foreignCategory },
                     new { merchant_tag = foreignTag },
                 })
        {
            var response = await QueryResponseAsync(session, new { filters });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("not_found", await ErrorKindAsync(response));
        }

        var mixed = await QueryResponseAsync(session, new
        {
            filters = new
            {
                financial_account_id = accountId,
                category_id = foreignCategory,
            },
        });
        Assert.Equal(HttpStatusCode.NotFound, mixed.StatusCode);

        var foreignDetail = await SendAsync(Get(
            $"/v1/finance/transactions/{Guid.NewGuid()}", session: session));
        Assert.Equal(HttpStatusCode.NotFound, foreignDetail.StatusCode);

        var ownedDetail = await SendAsync(Get($"/v1/finance/transactions/{rowId}", session: session));
        Assert.Equal(HttpStatusCode.OK, ownedDetail.StatusCode);

        var rows = Rows(await QueryAsync(session));
        Assert.Single(rows);
        Assert.NotEqual(Guid.Empty, otherCustomerId);
    }

    [Fact]
    public async Task DetailProjectionIncludesSourceAndOrderedTags()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId, label: "Detail statement");
        var zebra = await CreateTagAsync(session, "Zebra");
        var alpha = await CreateTagAsync(session, "Alpha");
        var rowId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 10, 1), "Detail row", -42.10m, 7,
            tagIds: [zebra, alpha], sourcePage: 3);

        var response = await SendAsync(Get($"/v1/finance/transactions/{rowId}", session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await BodyAsync(response);
        var transaction = body.GetProperty("transaction");

        Assert.Equal(rowId, transaction.GetProperty("id").GetGuid());
        Assert.Equal("2024-10-01", transaction.GetProperty("transaction_date").GetString());
        Assert.Equal("Detail row", transaction.GetProperty("description").GetString());
        Assert.Equal("-42.10", transaction.GetProperty("amount").GetString());
        Assert.Equal("out", transaction.GetProperty("direction").GetString());
        Assert.Equal("ZAR", transaction.GetProperty("currency").GetString());
        Assert.Equal(accountId, transaction.GetProperty("financial_account").GetProperty("id").GetGuid());
        Assert.Equal(expenseId, transaction.GetProperty("category").GetProperty("id").GetGuid());
        Assert.Equal("Expense", transaction.GetProperty("category").GetProperty("kind").GetString());
        Assert.Equal(statementId, transaction.GetProperty("statement_import").GetProperty("id").GetGuid());
        Assert.Equal("Detail statement", transaction.GetProperty("statement_import").GetProperty("statement_label").GetString());
        Assert.Equal(
            ["Alpha", "Zebra"],
            transaction.GetProperty("merchant_tags").EnumerateArray()
                .Select(tag => tag.GetProperty("display_name").GetString()));
        Assert.Equal(3, body.GetProperty("source_page").GetInt32());
        Assert.Equal(7, body.GetProperty("source_row_number").GetInt32());
        Assert.False(transaction.GetProperty("effective_hidden").GetBoolean());
        Assert.Equal("none", transaction.GetProperty("hidden_reason").GetString());
    }

    [Fact]
    public async Task SuggestionsAreBoundedWholeTokenMatchesExcludingAssignedTags()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);

        var tagIds = new List<Guid>();
        for (var index = 0; index < 22; index++)
        {
            tagIds.Add(await CreateTagAsync(session, $"t{index:D2}"));
        }
        var description = string.Join(' ', Enumerable.Range(0, 22).Select(index => $"t{index:D2}"));
        var assigned = tagIds[0];
        var rowId = await SeedTransactionAsync(
            customerId, accountId, statementId, expenseId,
            new DateOnly(2024, 11, 1), description, -1m, 1, tagIds: [assigned]);

        var response = await SendAsync(Get($"/v1/finance/transactions/{rowId}", session: session));
        var body = await BodyAsync(response);
        var suggestions = body.GetProperty("suggested_tags").EnumerateArray().ToArray();
        Assert.Equal(20, suggestions.Length);
        Assert.True(body.GetProperty("has_more_suggestions").GetBoolean());
        Assert.DoesNotContain(suggestions, tag => tag.GetProperty("id").GetGuid() == assigned);
        Assert.Equal(
            suggestions.Select(tag => tag.GetProperty("display_name").GetString()).Order().ToArray(),
            suggestions.Select(tag => tag.GetProperty("display_name").GetString()).ToArray());
    }

    [Fact]
    public async Task TagSuggestionAlgorithmIsUnchangedWholeTokenSequenceMatching()
    {
        var tags = new (Guid Id, string[] Tokens)[]
        {
            (Guid.NewGuid(), ["cafe"]),
            (Guid.NewGuid(), ["cafe", "nero"]),
            (Guid.NewGuid(), ["nero"]),
        };

        var matches = TagSuggestions.Match("the cafe nero visit", tags).ToArray();
        Assert.Equal(new[] { tags[0].Id, tags[1].Id, tags[2].Id }, matches);

        var reversed = TagSuggestions.Match("nero cafe", tags).ToArray();
        Assert.Equal(new[] { tags[0].Id, tags[2].Id }, reversed);

        Assert.Empty(TagSuggestions.Match(string.Empty, tags));
        Assert.Equal(new[] { tags[2].Id }, TagSuggestions.Match("café nero", tags));
    }

    [Fact]
    public async Task GuardAndNoStoreCoverSuccessAndFailure()
    {
        var (session, _) = await NewCustomerAsync();

        var unauthorized = await SendAsync(Post(QueryPath, Json(new { })));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var badSecret = await SendAsync(Post(
            QueryPath, Json(new { }), secret: "wrong-secret"));
        Assert.Equal(HttpStatusCode.Unauthorized, badSecret.StatusCode);

        var badOrigin = await SendAsync(Post(
            QueryPath, Json(new { }), session: session, origin: "https://evil.example"));
        Assert.Equal(HttpStatusCode.Forbidden, badOrigin.StatusCode);
        Assert.Equal(string.Empty, await badOrigin.Content.ReadAsStringAsync());

        var ok = await QueryResponseAsync(session, new { });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("no-store", ok.Headers.CacheControl?.ToString());

        var invalid = await QueryResponseAsync(session, new { page = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("no-store", invalid.Headers.CacheControl?.ToString());

        var oversized = await SendAsync(Post(
            QueryPath, Json(new { filters = new { search = new string('a', 17 * 1024) } }),
            session: session));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal("request_too_large", await ErrorKindAsync(oversized));
    }

    [Fact]
    public async Task TwentyThousandRowMeasurementRecordsTimingsAndPlan()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (systemId, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        var denseTags = new[]
        {
            await CreateTagAsync(session, "Perf tag A"),
            await CreateTagAsync(session, "Perf tag B"),
        };
        await SeedPerformanceRowsAsync(
            customerId, accountId, statementId, expenseId, 20_000, 1);
        await ExecuteSqlParamsAsync(
            """
            INSERT INTO transaction_merchant_tags (customer_id, transaction_id, merchant_tag_id)
            SELECT @customer_id, t.id, tag.tag_id
            FROM transactions t
            CROSS JOIN unnest(@tag_ids) AS tag(tag_id)
            WHERE t.customer_id = @customer_id AND t.statement_import_id = @statement_id
              AND t.source_row_number % 5 = 0
            ON CONFLICT DO NOTHING
            """,
            ("customer_id", customerId),
            ("statement_id", statementId),
            ("tag_ids", denseTags));

        var hiddenStatementId = await SeedStatementAsync(customerId, accountId, hidden: true);
        await SeedPerformanceRowsAsync(
            customerId, accountId, hiddenStatementId, systemId, 1_000, 20_001);

        var (otherSession, otherCustomerId) = await NewCustomerAsync();
        var otherAccount = await CreateAccountAsync(otherSession, "Other customer account");
        var otherStatement = await SeedStatementAsync(otherCustomerId, otherAccount);
        var (_, otherExpenseId) = await DefaultCategoriesAsync(otherSession);
        await SeedPerformanceRowsAsync(
            otherCustomerId, otherAccount, otherStatement, otherExpenseId, 5_000, 1);

        async Task<(long Elapsed, JsonElement Body)> MeasureAsync(object body)
        {
            var watch = Stopwatch.StartNew();
            var response = await QueryResponseAsync(session, body);
            watch.Stop();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (watch.ElapsedMilliseconds, await BodyAsync(response));
        }

        var visible = await MeasureAsync(new { page = 1, page_size = 25 });
        var search = await MeasureAsync(new
        {
            filters = new { search = "bulk row 19999" },
            page = 1,
            page_size = 25,
        });
        var filtered = await MeasureAsync(new
        {
            filters = new { category_id = expenseId, direction = "out" },
            sort_by = "amount",
            sort_direction = "asc",
            page = 1,
            page_size = 100,
        });
        var hidden = await MeasureAsync(new
        {
            filters = new { visibility = "hidden" },
            page = 1,
            page_size = 50,
        });

        Assert.Equal(21_000, visible.Body.GetProperty("inventory_count").GetInt64());
        Assert.Equal(20_000, visible.Body.GetProperty("matching_visible_count").GetInt64());
        Assert.Equal(1_000, hidden.Body.GetProperty("filtered_total").GetInt64());
        Assert.True(search.Body.GetProperty("filtered_total").GetInt64() >= 1);

        output.WriteLine(
            "P3-20K dataset_owned=21000 dataset_other=5000 visible_ms={0} search_ms={1} filtered_amount_ms={2} hidden_ms={3}",
            visible.Elapsed,
            search.Elapsed,
            filtered.Elapsed,
            hidden.Elapsed);

        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var explain = connection.CreateCommand();
        explain.CommandText = """
            EXPLAIN (ANALYZE, BUFFERS)
            SELECT t.id, t.version, t.transaction_date, t.description, t.amount,
                   a.id, a.display_name, a.is_archived,
                   c.id, c.display_name, c.kind, c.is_system, c.is_archived,
                   s.id, s.statement_label, s.source_type, s.is_hidden,
                   t.is_hidden_by_user, t.source_page, t.source_row_number
            FROM transactions t
            JOIN financial_accounts a
              ON a.customer_id = t.customer_id AND a.id = t.financial_account_id
            JOIN categories c
              ON c.customer_id = t.customer_id AND c.id = t.category_id
            JOIN statement_imports s
              ON s.customer_id = t.customer_id AND s.id = t.statement_import_id
            WHERE t.customer_id = @customer_id
              AND NOT (s.is_hidden OR t.is_hidden_by_user)
            ORDER BY t.transaction_date DESC, t.id DESC
            LIMIT 25
            """;
        explain.Parameters.AddWithValue("customer_id", customerId);
        await using var reader = await explain.ExecuteReaderAsync();
        var planLines = new List<string>();
        while (await reader.ReadAsync())
        {
            planLines.Add(reader.GetString(0));
        }
        output.WriteLine("P3-20K plan: {0}", string.Join(" | ", planLines.Take(12)));
    }

    private async Task SeedPerformanceRowsAsync(Guid customerId,
                                                Guid accountId,
                                                Guid statementId,
                                                Guid categoryId,
                                                int count,
                                                int firstRow)
    {
        await ExecuteSqlParamsAsync(
            """
            INSERT INTO transactions (
                id, customer_id, financial_account_id, statement_import_id,
                transaction_date, description, normalized_description, amount,
                category_id, source_type, source_row_number, identity_key,
                occurrence_index, is_hidden_by_user, version, created_at, updated_at)
            SELECT
                gen_random_uuid(), @customer_id, @account_id, @statement_id,
                DATE '2024-01-01' + (series % 300),
                'Bulk row ' || series,
                'bulk row ' || series,
                (-(1 + (series % 1000)))::numeric(18, 2),
                @category_id, 'fnb_csv', series, 'perf-' || series,
                1, FALSE, 1, NOW(), NOW()
            FROM generate_series(@first_row, @first_row + @count - 1) AS series
            """,
            ("customer_id", customerId),
            ("account_id", accountId),
            ("statement_id", statementId),
            ("category_id", categoryId),
            ("first_row", firstRow),
            ("count", count));
    }

    [Fact]
    public async Task EmptyInventoryIsNotAClaimAboutFinanceData()
    {
        var (session, _) = await NewCustomerAsync();

        var page = await QueryAsync(session);
        Assert.Empty(Rows(page));
        Assert.Equal(0, Count(page, "inventory_count"));
        Assert.Equal(0, Count(page, "matching_visible_count"));
        Assert.Equal(0, Count(page, "matching_hidden_count"));
        Assert.Equal(0, Count(page, "filtered_total"));
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(25, page.GetProperty("page_size").GetInt32());
        Assert.Equal("date", page.GetProperty("sort_by").GetString());
        Assert.Equal("desc", page.GetProperty("sort_direction").GetString());
    }
}
