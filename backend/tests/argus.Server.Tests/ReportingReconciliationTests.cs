using System.Globalization;
using System.Net;
using System.Text.Json;

namespace argus.Server.Tests;

public sealed class ReportingReconciliationTests(KitFactory factory) : TransactionTestBase(factory)
{
    private const string ReportPath = "/v1/finance/reports/query";
    private const string QueryPath = "/v1/finance/transactions/query";

    [Fact]
    public async Task ListMoneyTotalsUseVisibleIncludedRowsAndKeepExistingCounts()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (systemId, expenseId) = await DefaultCategoriesAsync(session);
        Guid incomeId = await CreateCategoryAsync(session, "Money income", "Income");
        Guid transferId = await CreateCategoryAsync(session, "Money transfers", "Transfer");
        Guid excludedId = await CreateCategoryAsync(session, "Money excluded", "Excluded");
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        Guid hiddenStatementId = await SeedStatementAsync(customerId, accountId, hidden: true);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-31' WHERE id IN ('{statementId}', '{hiddenStatementId}');
            """);

        await SeedTransactionAsync(customerId, accountId, statementId, incomeId, new DateOnly(2025, 1, 2), "Income", 1000m, 1);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 3), "Expense", -300m, 2);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 4), "Refund", 50m, 3);
        await SeedTransactionAsync(customerId, accountId, statementId, systemId, new DateOnly(2025, 1, 5), "Uncategorised", -40m, 4);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 6), "Zero", 0m, 5);
        await SeedTransactionAsync(customerId, accountId, statementId, transferId, new DateOnly(2025, 1, 7), "Transfer", -500m, 6);
        await SeedTransactionAsync(customerId, accountId, statementId, excludedId, new DateOnly(2025, 1, 8), "Excluded", -100m, 7);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 9), "Hidden individual", -50m, 8, hidden: true);
        await SeedTransactionAsync(customerId, accountId, hiddenStatementId, incomeId, new DateOnly(2025, 1, 10), "Hidden parent", 70m, 1);

        JsonElement visible = await QueryBodyAsync(session, new { filters = new { visibility = "visible" }, page_size = 100 });
        Assert.Equal(9, visible.GetProperty("inventory_count").GetInt64());
        Assert.Equal(7, visible.GetProperty("matching_visible_count").GetInt64());
        Assert.Equal(2, visible.GetProperty("matching_hidden_count").GetInt64());
        Assert.Equal(7, visible.GetProperty("filtered_total").GetInt64());
        JsonElement visibleMoney = visible.GetProperty("money_totals");
        Assert.Equal("1050.00", visibleMoney.GetProperty("money_in").GetString());
        Assert.Equal("340.00", visibleMoney.GetProperty("money_out").GetString());
        Assert.Equal("710.00", visibleMoney.GetProperty("net").GetString());
        Assert.Equal(5, visibleMoney.GetProperty("included_count").GetInt64());
        Assert.Equal(1, visibleMoney.GetProperty("transfers").GetProperty("count").GetInt64());
        Assert.Equal("500.00", visibleMoney.GetProperty("transfers").GetProperty("absolute").GetString());
        Assert.Equal(1, visibleMoney.GetProperty("excluded").GetProperty("count").GetInt64());
        Assert.Equal("100.00", visibleMoney.GetProperty("excluded").GetProperty("absolute").GetString());

        JsonElement all = await QueryBodyAsync(session, new { filters = new { visibility = "all" }, page_size = 100 });
        Assert.Equal(9, all.GetProperty("filtered_total").GetInt64());
        Assert.Equal("710.00", all.GetProperty("money_totals").GetProperty("net").GetString());
        Assert.Equal(5, all.GetProperty("money_totals").GetProperty("included_count").GetInt64());

        JsonElement hidden = await QueryBodyAsync(session, new { filters = new { visibility = "hidden" }, page_size = 100 });
        Assert.Equal(2, hidden.GetProperty("filtered_total").GetInt64());
        Assert.Equal("0.00", hidden.GetProperty("money_totals").GetProperty("net").GetString());
        Assert.Equal(0, hidden.GetProperty("money_totals").GetProperty("included_count").GetInt64());

        JsonElement included = await QueryBodyAsync(session, new { filters = new { calculation_scope = "included" }, page_size = 100 });
        Assert.Equal(5, included.GetProperty("filtered_total").GetInt64());
        Assert.Equal("710.00", included.GetProperty("money_totals").GetProperty("net").GetString());

        JsonElement excludedExpense = await QueryBodyAsync(session, new
        {
            filters = new
            {
                calculation_scope = "included",
                excluded_category_ids = new[] { expenseId },
            },
            page_size = 100,
        });
        Assert.Equal(2, excludedExpense.GetProperty("filtered_total").GetInt64());
        Assert.Equal("960.00", excludedExpense.GetProperty("money_totals").GetProperty("net").GetString());
        Assert.Equal("40.00", excludedExpense.GetProperty("money_totals").GetProperty("money_out").GetString());

        JsonElement incomeOnly = await QueryBodyAsync(session, new
        {
            filters = new { category_kind = "Income" },
            page_size = 100,
        });
        Assert.Equal(0, incomeOnly.GetProperty("money_totals").GetProperty("transfers").GetProperty("count").GetInt64());
        Assert.Equal(0, incomeOnly.GetProperty("money_totals").GetProperty("excluded").GetProperty("count").GetInt64());
    }

    [Fact]
    public async Task EveryCategoryAndTagRankDescriptorReconcilesToItsFilteredRows()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (systemId, expenseId) = await DefaultCategoriesAsync(session);
        Guid incomeId = await CreateCategoryAsync(session, "Rank income", "Income");
        Guid travelId = await CreateCategoryAsync(session, "Rank travel", "Expense");
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-31' WHERE id = '{statementId}';
            """);
        Guid tagA = await SeedTagAsync(customerId, "Rank tag A");
        Guid tagB = await SeedTagAsync(customerId, "Rank tag B");
        Guid tagC = await SeedTagAsync(customerId, "Rank tag C");

        await SeedTransactionAsync(customerId, accountId, statementId, incomeId, new DateOnly(2025, 1, 2), "Income one", 500m, 1, [tagA]);
        await SeedTransactionAsync(customerId, accountId, statementId, incomeId, new DateOnly(2025, 1, 3), "Income two", 300m, 2);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 4), "Expense one", -200m, 3, [tagA, tagB]);
        await SeedTransactionAsync(customerId, accountId, statementId, travelId, new DateOnly(2025, 1, 5), "Travel", -100m, 4, [tagC]);
        await SeedTransactionAsync(customerId, accountId, statementId, systemId, new DateOnly(2025, 1, 6), "Uncategorised", -50m, 5);

        JsonElement report = await ReportAsync(session, CustomBody());
        JsonElement categoriesIn = report.GetProperty("categories").GetProperty("money_in");
        JsonElement categoriesOut = report.GetProperty("categories").GetProperty("money_out");
        await AssertCategoryRanksAsync(session, categoriesIn, "money_in");
        await AssertCategoryRanksAsync(session, categoriesOut, "money_out");

        JsonElement tagsIn = report.GetProperty("tags").GetProperty("money_in");
        JsonElement tagsOut = report.GetProperty("tags").GetProperty("money_out");
        await AssertTagRanksAsync(session, tagsIn, "money_in");
        await AssertTagRanksAsync(session, tagsOut, "money_out");

        decimal tagOutSum = tagsOut.GetProperty("ranks").EnumerateArray()
            .Sum(rank => decimal.Parse(rank.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal(500m, tagOutSum);
        Assert.Equal(350m, decimal.Parse(report.GetProperty("current").GetProperty("money").GetProperty("money_out").GetString()!, CultureInfo.InvariantCulture));
        Assert.True(tagOutSum > 350m);
    }

    [Fact]
    public async Task ActiveTagAndDirectionFiltersArePreservedInEveryDescriptor()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-31'
            WHERE id = '{statementId}';
            """);
        Guid focusTag = await SeedTagAsync(customerId, "Focus tag");
        Guid coTag = await SeedTagAsync(customerId, "Co tag");
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 5), "Both tags", -100m, 1, [focusTag, coTag]);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 6), "Co only", -50m, 2, [coTag]);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 7), "Inflow", 25m, 3, [focusTag]);

        JsonElement report = await ReportAsync(session, new
        {
            view = "overview",
            period = new { mode = "custom", start_date = "2025-01-01", end_date = "2025-01-31" },
            filters = new { merchant_tag = focusTag, direction = "out" },
        });

        JsonElement outTags = report.GetProperty("tags").GetProperty("money_out");
        JsonElement[] ranks = outTags.GetProperty("ranks").EnumerateArray().ToArray();
        Assert.Single(ranks);
        Assert.Equal(focusTag, ranks[0].GetProperty("id").GetGuid());
        Assert.Equal(focusTag.ToString("D"), ranks[0].GetProperty("descriptor").GetProperty("merchant_tag").GetString());
        Assert.Equal("out", ranks[0].GetProperty("descriptor").GetProperty("direction").GetString());
        Assert.Equal(0, outTags.GetProperty("other").GetProperty("total").GetInt64());

        JsonElement trend = report.GetProperty("trend").EnumerateArray().First();
        Assert.Equal("out", trend.GetProperty("money_out_descriptor").GetProperty("direction").GetString());
        Assert.Equal("out", trend.GetProperty("money_in_descriptor").GetProperty("direction").GetString());
    }

    [Fact]
    public async Task OtherTagsExpandIndividuallyWithoutAnAggregateAmount()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-31' WHERE id = '{statementId}';
            """);

        for (int index = 0; index < 7; index++)
        {
            Guid tagId = await SeedTagAsync(customerId, $"Expand tag {index}");
            await SeedTransactionAsync(
                customerId,
                accountId,
                statementId,
                expenseId,
                new DateOnly(2025, 1, index + 1),
                $"Tagged {index}",
                -(index + 1) * 10m,
                index + 1,
                [tagId]);
        }
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 20), "Untagged", -5m, 20);

        JsonElement report = await ReportAsync(session, CustomBody());
        JsonElement other = report.GetProperty("tags").GetProperty("money_out").GetProperty("other");
        Assert.Equal(2, other.GetProperty("total").GetInt64());
        Assert.Equal(2, other.GetProperty("items").GetArrayLength());
        Assert.False(other.TryGetProperty("amount", out _));
        Assert.Equal(1, other.GetProperty("page").GetInt32());
        Assert.False(other.GetProperty("has_more").GetBoolean());

        JsonElement[] allTagRanks = report.GetProperty("tags").GetProperty("money_out").GetProperty("all_ranks").EnumerateArray().ToArray();
        Assert.Equal(7, allTagRanks.Length);
        decimal[] allTagAmounts = allTagRanks
            .Select(rank => decimal.Parse(rank.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal(allTagAmounts.OrderByDescending(value => value).ToArray(), allTagAmounts);
        Assert.Equal(70m, allTagAmounts[0]);
        Assert.Equal(10m, allTagAmounts[^1]);

        JsonElement untagged = report.GetProperty("tags").GetProperty("money_out").GetProperty("untagged");
        Assert.Equal("5.00", untagged.GetProperty("amount").GetString());
        Assert.Equal(1, untagged.GetProperty("count").GetInt64());
        JsonElement untaggedRows = await QueryAsync(session, untagged.GetProperty("descriptor"));
        Assert.Equal("5.00", untaggedRows.GetProperty("money_totals").GetProperty("money_out").GetString());

        foreach (JsonElement item in other.GetProperty("items").EnumerateArray())
        {
            JsonElement rows = await QueryAsync(session, item.GetProperty("descriptor"));
            Assert.Equal(item.GetProperty("count").GetInt64(), rows.GetProperty("filtered_total").GetInt64());
            Assert.Equal(item.GetProperty("amount").GetString(), rows.GetProperty("money_totals").GetProperty("money_out").GetString());
        }
    }

    [Fact]
    public async Task GiantSumsRemainExactDecimalStrings()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid incomeId = await CreateCategoryAsync(session, "Giant income", "Income");
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-31' WHERE id = '{statementId}';
            """);
        await SeedTransactionAsync(customerId, accountId, statementId, incomeId, new DateOnly(2025, 1, 2), "Giant one", 9_999_999_999_999_999.99m, 1);
        await SeedTransactionAsync(customerId, accountId, statementId, incomeId, new DateOnly(2025, 1, 3), "Giant two", 9_999_999_999_999_999.99m, 2);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 4), "Tiny expense", -0.01m, 3);

        JsonElement report = await ReportAsync(session, CustomBody());
        JsonElement money = report.GetProperty("current").GetProperty("money");
        Assert.Equal("19999999999999999.98", money.GetProperty("money_in").GetString());
        Assert.Equal("0.01", money.GetProperty("money_out").GetString());
        Assert.Equal("19999999999999999.97", money.GetProperty("net").GetString());
    }

    [Fact]
    public async Task ComparisonRemainderPagesRetainAbsoluteOrderWithoutDuplicates()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-12-01', end_date = DATE '2026-02-28' WHERE id = '{statementId}';
            """);
        var categories = new List<Guid>();
        for (int index = 0; index < 30; index++)
        {
            Guid categoryId = await CreateCategoryAsync(session, $"Change category {index}", "Income");
            categories.Add(categoryId);
            await SeedTransactionAsync(customerId, accountId, statementId, categoryId, new DateOnly(2026, 1, 5), $"Prior {index}", 100m, index + 1);
            await SeedTransactionAsync(customerId, accountId, statementId, categoryId, new DateOnly(2026, 2, 5), $"Current {index}", 100m + (index + 1) * 10m, index + 31);
        }

        JsonElement first = await ReportAsync(session, new
        {
            view = "comparison",
            period = new { mode = "calendar", preset = "month", month = "2026-02" },
            category_changes_page = 0,
        });
        JsonElement initial = first.GetProperty("category_changes").GetProperty("money_in");
        Assert.Equal(10, initial.GetProperty("items").GetArrayLength());
        Assert.True(initial.GetProperty("has_more").GetBoolean());
        Assert.Equal(1, initial.GetProperty("next_page").GetInt32());
        decimal[] initialDeltas = initial.GetProperty("items").EnumerateArray()
            .Select(item => decimal.Parse(item.GetProperty("delta").GetString()!, CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal(initialDeltas.OrderByDescending(value => Math.Abs(value)).ToArray(), initialDeltas);
        Assert.Equal(300m, initialDeltas[0]);

        JsonElement second = await ReportAsync(session, new
        {
            view = "comparison",
            period = new { mode = "calendar", preset = "month", month = "2026-02" },
            category_changes_page = 1,
        });
        JsonElement remainder = second.GetProperty("category_changes").GetProperty("money_in");
        Assert.Equal(20, remainder.GetProperty("items").GetArrayLength());
        Assert.False(remainder.GetProperty("has_more").GetBoolean());

        Guid[] initialIds = initial.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Guid[] remainderIds = remainder.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Assert.Empty(initialIds.Intersect(remainderIds));
        Assert.Equal(30, initialIds.Concat(remainderIds).Distinct().Count());
    }

    private async Task AssertCategoryRanksAsync(string session, JsonElement direction, string directionName)
    {
        decimal expected;
        foreach (JsonElement rank in direction.GetProperty("ranks").EnumerateArray())
        {
            JsonElement rows = await QueryAsync(session, rank.GetProperty("descriptor"));
            Assert.Equal(rank.GetProperty("count").GetInt64(), rows.GetProperty("filtered_total").GetInt64());
            expected = decimal.Parse(rank.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture);
            Assert.Equal(expected, decimal.Parse(rows.GetProperty("money_totals").GetProperty(directionName).GetString()!, CultureInfo.InvariantCulture));
        }

        JsonElement? uncategorised = direction.GetProperty("uncategorised").ValueKind == JsonValueKind.Object
            ? direction.GetProperty("uncategorised")
            : null;
        if (uncategorised is not null)
        {
            JsonElement rows = await QueryAsync(session, uncategorised.Value.GetProperty("descriptor"));
            Assert.Equal(uncategorised.Value.GetProperty("count").GetInt64(), rows.GetProperty("filtered_total").GetInt64());
        }

        JsonElement other = direction.GetProperty("other");
        JsonElement otherRows = await QueryAsync(session, other.GetProperty("descriptor"));
        Assert.Equal(other.GetProperty("count").GetInt64(), otherRows.GetProperty("filtered_total").GetInt64());
        expected = decimal.Parse(other.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture);
        Assert.Equal(expected, decimal.Parse(otherRows.GetProperty("money_totals").GetProperty(directionName).GetString()!, CultureInfo.InvariantCulture));
    }

    private async Task AssertTagRanksAsync(string session, JsonElement direction, string directionName)
    {
        foreach (JsonElement rank in direction.GetProperty("ranks").EnumerateArray())
        {
            JsonElement rows = await QueryAsync(session, rank.GetProperty("descriptor"));
            Assert.Equal(rank.GetProperty("count").GetInt64(), rows.GetProperty("filtered_total").GetInt64());
            decimal expected = decimal.Parse(rank.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture);
            Assert.Equal(expected, decimal.Parse(rows.GetProperty("money_totals").GetProperty(directionName).GetString()!, CultureInfo.InvariantCulture));
        }
    }

    private async Task<JsonElement> ReportAsync(string session, object body)
    {
        HttpResponseMessage response = await SendAsync(Post(ReportPath, Json(body), session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    [Fact]
    public async Task PriorOnlyOtherCategoryUsesTheCurrentExclusionSet()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, baseExpenseId) = await DefaultCategoriesAsync(session);
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-02-28'
            WHERE id = '{statementId}';
            """);

        var categories = new List<Guid>();
        for (int index = 0; index < 6; index++)
        {
            Guid categoryId = await CreateCategoryAsync(session, $"Other membership {index}", "Expense");
            categories.Add(categoryId);
            await SeedTransactionAsync(customerId, accountId, statementId, categoryId, new DateOnly(2025, 1, 10), $"Prior {index}", -(60m - index * 10m), index + 1);
            await SeedTransactionAsync(customerId, accountId, statementId, categoryId, new DateOnly(2025, 2, 10), $"Current {index}", -(60m - index * 10m), index + 20);
        }
        Guid priorOnly = await CreateCategoryAsync(session, "Prior only other", "Expense");
        await SeedTransactionAsync(customerId, accountId, statementId, priorOnly, new DateOnly(2025, 1, 10), "Prior only", -15m, 40);

        JsonElement report = await ReportAsync(session, new
        {
            view = "overview",
            period = new { mode = "custom", start_date = "2025-02-01", end_date = "2025-02-28" },
        });
        JsonElement other = report.GetProperty("categories").GetProperty("money_out").GetProperty("other");
        Assert.Equal("10.00", other.GetProperty("amount").GetString());
        Assert.Equal("25.00", other.GetProperty("prior_amount").GetString());
    }

    [Fact]
    public async Task ExpenseGroupValueSubtractsInflowsFromOutflows()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        Guid categoryId = await CreateCategoryAsync(session, "Expense refund group", "Expense");
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-31'
            WHERE id = '{statementId}';
            """);
        await SeedTransactionAsync(customerId, accountId, statementId, categoryId, new DateOnly(2025, 1, 5), "Expense", -300m, 1);
        await SeedTransactionAsync(customerId, accountId, statementId, categoryId, new DateOnly(2025, 1, 6), "Refund", 50m, 2);

        JsonElement report = await ReportAsync(session, new
        {
            view = "category",
            period = new { mode = "custom", start_date = "2025-01-01", end_date = "2025-01-31" },
            filters = new { category_id = categoryId },
        });

        Assert.Equal("Expense", report.GetProperty("focus_group").GetProperty("kind").GetString());
        Assert.Equal("250.00", report.GetProperty("focus_group").GetProperty("net").GetString());
        Assert.Equal("-250.00", report.GetProperty("current").GetProperty("money").GetProperty("net").GetString());
    }

    private async Task<JsonElement> QueryBodyAsync(string session, object body)
    {
        HttpResponseMessage response = await SendAsync(Post(QueryPath, Json(body), session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private async Task<JsonElement> QueryAsync(string session, JsonElement descriptor)
    {
        var filters = new Dictionary<string, object?>();
        foreach (JsonProperty property in descriptor.EnumerateObject())
        {
            filters[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Array => property.Value.EnumerateArray().Select(item => item.GetGuid()).ToArray(),
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText(),
            };
        }

        HttpResponseMessage response = await SendAsync(Post(QueryPath, Json(new { filters }), session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
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

    private static object CustomBody() =>
        new
        {
            view = "overview",
            period = new
            {
                mode = "custom",
                start_date = "2025-01-01",
                end_date = "2025-01-31",
            },
        };
}
