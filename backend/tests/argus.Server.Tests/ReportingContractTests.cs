using System.Net;
using System.Text.Json;
using argus.Server.Reporting;

namespace argus.Server.Tests;

/// <summary>
/// Synthetic full-stack reporting reconciliation. The API runs through the real
/// guarded host and restricted runtime login; no report total is computed by a
/// frontend or test helper.
/// </summary>
public sealed class ReportingContractTests(KitFactory factory) : TransactionTestBase(factory)
{
    private const string ReportPath = "/v1/finance/reports/query";
    private const string QueryPath = "/v1/finance/transactions/query";

    [Fact]
    public async Task CalendarReportReconcilesMoneyCategoriesTagsAndDrillDowns()
    {
        ReportFixture fixture = await CreateFixtureAsync();
        JsonElement report = await ReportAsync(fixture.Session, CustomBody(fixture.PeriodStart, fixture.PeriodEnd));

        Assert.Equal("ready", report.GetProperty("status").GetString());
        JsonElement current = report.GetProperty("current");
        JsonElement money = current.GetProperty("money");
        Assert.Equal("1250.00", money.GetProperty("money_in").GetString());
        Assert.Equal("340.00", money.GetProperty("money_out").GetString());
        Assert.Equal("910.00", money.GetProperty("net").GetString());
        Assert.Equal(6, money.GetProperty("included_count").GetInt64());
        Assert.Equal(1, money.GetProperty("zero_count").GetInt64());

        Assert.Equal(1, current.GetProperty("transfers").GetProperty("count").GetInt64());
        Assert.Equal("100.00", current.GetProperty("transfers").GetProperty("absolute").GetString());
        Assert.Equal(1, current.GetProperty("excluded").GetProperty("count").GetInt64());
        Assert.Equal("100.00", current.GetProperty("excluded").GetProperty("absolute").GetString());
        Assert.Equal(4, current.GetProperty("hidden").GetProperty("count").GetInt64());
        Assert.Equal("2787.00", current.GetProperty("hidden").GetProperty("absolute").GetString());

        JsonElement categoriesIn = report.GetProperty("categories").GetProperty("money_in");
        JsonElement categoriesOut = report.GetProperty("categories").GetProperty("money_out");
        decimal categoryInSum = SumRanks(categoriesIn);
        decimal categoryOutSum = SumRanks(categoriesOut);
        Assert.Equal(1250m, categoryInSum);
        Assert.Equal(340m, categoryOutSum);
        Assert.Equal(
            money.GetProperty("money_in").GetString(),
            TransactionAmount(categoryInSum));
        Assert.Equal(
            money.GetProperty("money_out").GetString(),
            TransactionAmount(categoryOutSum));
        Assert.Equal("40.00", categoriesOut.GetProperty("uncategorised").GetProperty("amount").GetString());

        JsonElement tagsIn = report.GetProperty("tags").GetProperty("money_in");
        JsonElement tagsOut = report.GetProperty("tags").GetProperty("money_out");
        Assert.True(report.GetProperty("tags").GetProperty("overlap_disclosure").GetBoolean());
        Assert.Equal(2, current.GetProperty("tagged_count").GetInt64());
        Assert.Equal(4, current.GetProperty("untagged_count").GetInt64());
        Assert.Equal("81.8", current.GetProperty("tag_coverage_percentage").GetString());

        JsonElement firstInRank = categoriesIn.GetProperty("ranks").EnumerateArray().First();
        JsonElement rankDrillDown = await QueryAsync(fixture.Session, firstInRank.GetProperty("descriptor"));
        Assert.Equal(firstInRank.GetProperty("count").GetInt64(), rankDrillDown.GetProperty("filtered_total").GetInt64());
        Assert.Equal(
            firstInRank.GetProperty("amount").GetString(),
            rankDrillDown.GetProperty("money_totals").GetProperty("money_in").GetString());

        JsonElement otherIn = categoriesIn.GetProperty("other");
        JsonElement otherDrillDown = await QueryAsync(fixture.Session, otherIn.GetProperty("descriptor"));
        Assert.Equal(otherIn.GetProperty("count").GetInt64(), otherDrillDown.GetProperty("filtered_total").GetInt64());

        JsonElement coverage = report.GetProperty("coverage").GetProperty("current");
        Assert.Equal("100.0", coverage.GetProperty("covered_percentage").GetString());
        Assert.False(coverage.GetProperty("partial").GetBoolean());
        Assert.Equal(0, coverage.GetProperty("gaps").GetProperty("total").GetInt64());

        JsonElement trend = report.GetProperty("trend").EnumerateArray().First();
        Assert.Equal("2025-01-01", trend.GetProperty("start_date").GetString());
        Assert.Equal("2025-01-07", trend.GetProperty("end_date").GetString());
        JsonElement trendDrillDown = await QueryAsync(fixture.Session, trend.GetProperty("money_in_descriptor"));
        Assert.Equal(trend.GetProperty("money_in").GetString(), trendDrillDown.GetProperty("money_totals").GetProperty("money_in").GetString());

        Assert.Single(tagsIn.GetProperty("ranks").EnumerateArray());
        JsonElement[] outRanks = tagsOut.GetProperty("ranks").EnumerateArray().ToArray();
        Assert.Equal(2, outRanks.Length);
        decimal tagOutSum = outRanks.Sum(rank => decimal.Parse(rank.GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(600m, tagOutSum);
        Assert.NotEqual(340m, tagOutSum);
    }

    [Fact]
    public async Task CategoryTrendReturnsBoundedSixMonthMoneyOutSeries()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid travelId = await CreateCategoryAsync(session, "Trend travel", "Expense");
        Guid otherAccountId = await CreateAccountAsync(session, "Savings");
        Guid otherCategoryId = await CreateCategoryAsync(session, "Trend other account", "Expense");
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        Guid otherStatementId = await SeedStatementAsync(customerId, otherAccountId, label: "Other account");
        DateOnly today = ReportingClock.Today(TimeProvider.System);
        DateOnly firstMonth = new(today.Year, today.Month, 1);
        firstMonth = firstMonth.AddMonths(-5);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports
            SET start_date = DATE '{firstMonth:yyyy-MM-dd}', end_date = DATE '{today:yyyy-MM-dd}'
            WHERE id IN ('{statementId}', '{otherStatementId}');
            """);

        DateOnly oldest = firstMonth.AddDays(1);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, oldest, "Trend oldest expense", -10m, 1);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, today, "Trend latest expense", -5m, 2);
        await SeedTransactionAsync(customerId, accountId, statementId, travelId, today, "Trend latest travel", -25m, 3);
        await SeedTransactionAsync(customerId, otherAccountId, otherStatementId, otherCategoryId, today, "Trend other account", -40m, 1);

        JsonElement report = await ReportAsync(session, CustomBody(firstMonth, today));
        JsonElement trend = report.GetProperty("category_trend");
        JsonElement months = trend.GetProperty("months");
        Assert.Equal(6, months.GetArrayLength());
        Assert.Equal(today.ToString("yyyy-MM-dd"), months[5].GetProperty("end_date").GetString());

        JsonElement[] categories = trend.GetProperty("categories").EnumerateArray().ToArray();
        Assert.Equal(3, categories.Length);
        JsonElement travel = Assert.Single(categories, category => category.GetProperty("id").GetGuid() == travelId);
        Assert.Equal("25.00", travel.GetProperty("money_out").GetString());
        JsonElement travelLast = travel.GetProperty("months")[5];
        Assert.Equal("25.00", travelLast.GetProperty("amount").GetString());
        Assert.Equal(1, travelLast.GetProperty("count").GetInt64());
        Assert.Equal("out", travelLast.GetProperty("descriptor").GetProperty("direction").GetString());

        JsonElement[] selectedAccount = (await ReportAsync(session, FilteredBody(firstMonth, today, accountId)))
            .GetProperty("category_trend").GetProperty("categories").EnumerateArray().ToArray();
        Assert.Equal(2, selectedAccount.Length);
        Assert.DoesNotContain(selectedAccount, category => category.GetProperty("id").GetGuid() == otherCategoryId);

        JsonElement[] otherFilters = (await ReportAsync(session, FilteredBody(firstMonth, today, accountId, travelId)))
            .GetProperty("category_trend").GetProperty("categories").EnumerateArray().ToArray();
        Assert.Equal(2, otherFilters.Length);
        Assert.Contains(otherFilters, category => category.GetProperty("id").GetGuid() == expenseId);
    }

    [Fact]
    public async Task OtherCategoriesUseTheReturnedExclusionSetAndNeverCombinedTagSemantics()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, baseExpenseId) = await DefaultCategoriesAsync(session);
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-31'
            WHERE id = '{statementId}';
            """);

        var categoryIds = new List<Guid>();
        for (int index = 0; index < 7; index++)
        {
            categoryIds.Add(await CreateCategoryAsync(session, $"Rank category {index}", "Expense"));
        }
        for (int index = 0; index < 7; index++)
        {
            await SeedTransactionAsync(
                customerId,
                accountId,
                statementId,
                categoryIds[index],
                new DateOnly(2025, 1, index + 1),
                $"Rank expense {index}",
                -(index + 1) * 10m,
                index + 1);
        }

        JsonElement report = await ReportAsync(session, CustomBody(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31)));
        JsonElement other = report.GetProperty("categories").GetProperty("money_out").GetProperty("other");
        Assert.Equal("30.00", other.GetProperty("amount").GetString());
        Assert.Equal(2, other.GetProperty("count").GetInt64());
        JsonElement[] otherRanks = report.GetProperty("categories").GetProperty("money_out").GetProperty("other_ranks").EnumerateArray().ToArray();
        Assert.Equal(2, otherRanks.Length);
        decimal[] allAmounts = report.GetProperty("categories").GetProperty("money_out").GetProperty("all_ranks").EnumerateArray()
            .Select(rank => decimal.Parse(rank.GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal(7, allAmounts.Length);
        Assert.Equal(allAmounts.OrderByDescending(value => value).ToArray(), allAmounts);
        Assert.Equal(30m, otherRanks.Sum(rank => decimal.Parse(rank.GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture)));
        Guid[] excluded = other.GetProperty("excluded_category_ids").EnumerateArray().Select(item => item.GetGuid()).ToArray();
        Assert.Equal(6, excluded.Length);

        JsonElement drillDown = await QueryAsync(session, other.GetProperty("descriptor"));
        Assert.Equal(2, drillDown.GetProperty("filtered_total").GetInt64());
        Assert.Equal("30.00", drillDown.GetProperty("money_totals").GetProperty("money_out").GetString());
        Assert.DoesNotContain(baseExpenseId, excluded);
    }

    [Fact]
    public async Task SalarySetupAndOpenCycleComparisonUseExactRanges()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-03-31'
            WHERE id = '{statementId}';
            """);

        JsonElement setup = await ReportAsync(session, new { view = "overview" });
        Assert.Equal("setup_required", setup.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, setup.GetProperty("period").ValueKind);
        Assert.Equal("no_salary_boundaries", setup.GetProperty("setup").GetProperty("reason").GetString());
        Assert.Contains("calendar", setup.GetProperty("setup").GetProperty("alternatives").EnumerateArray().Select(item => item.GetString()));

        JsonElement calendar = await ReportAsync(session, CalendarBody("month_to_date"));
        Assert.NotEqual("setup_required", calendar.GetProperty("status").GetString());

        Guid first = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 25), "First salary", 10_000m, 1);
        Guid second = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 2, 24), "Second salary", 10_000m, 2);
        await ConfirmSalaryAsync(session, first, 0);
        await ConfirmSalaryAsync(session, second, 1);

        JsonElement report = await ReportAsync(session, new { view = "overview" });
        JsonElement period = report.GetProperty("period");
        Assert.Equal("salary_cycle", period.GetProperty("mode").GetString());
        Assert.Equal("2025-02-24", period.GetProperty("start_date").GetString());
        Assert.True(period.GetProperty("is_open_salary_cycle").GetBoolean());
        JsonElement comparison = report.GetProperty("comparison");
        Assert.Equal("2025-01-25", comparison.GetProperty("period").GetProperty("start_date").GetString());
        Assert.Equal("2025-02-23", comparison.GetProperty("period").GetProperty("end_date").GetString());
        Assert.True(comparison.GetProperty("shorter_prior").GetBoolean());
        Assert.True(comparison.GetProperty("has_included_data").GetBoolean());
    }

    [Fact]
    public async Task HiddenDiagnosticPreservesAllKindsAndRestoresExactRows()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (systemId, expenseId) = await DefaultCategoriesAsync(session);
        Guid transferId = await CreateCategoryAsync(session, "Hidden transfers", "Transfer");
        Guid excludedId = await CreateCategoryAsync(session, "Hidden excluded", "Excluded");
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        Guid hiddenStatementId = await SeedStatementAsync(customerId, accountId, hidden: true);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-31'
            WHERE id = '{statementId}';
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-31'
            WHERE id = '{hiddenStatementId}';
            """);

        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 5), "Visible included", -10m, 1);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 6), "Hidden ordinary", -20m, 2, hidden: true);
        await SeedTransactionAsync(customerId, accountId, hiddenStatementId, transferId, new DateOnly(2025, 1, 7), "Hidden transfer", -30m, 1);
        await SeedTransactionAsync(customerId, accountId, hiddenStatementId, excludedId, new DateOnly(2025, 1, 8), "Hidden excluded", -40m, 2);
        await SeedTransactionAsync(customerId, accountId, statementId, systemId, new DateOnly(2025, 1, 9), "Visible uncategorised", -5m, 3);

        JsonElement report = await ReportAsync(session, CustomBody(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31)));
        JsonElement hidden = report.GetProperty("current").GetProperty("hidden");
        Assert.Equal(3, hidden.GetProperty("count").GetInt64());
        Assert.Equal("90.00", hidden.GetProperty("absolute").GetString());
        Assert.Equal("15.00", report.GetProperty("current").GetProperty("money").GetProperty("money_out").GetString());

        JsonElement restored = await QueryAsync(session, report.GetProperty("current").GetProperty("hidden_descriptor"));
        Assert.Equal(3, restored.GetProperty("filtered_total").GetInt64());

        JsonElement hiddenOnly = await ReportAsync(session, CustomBody(new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 8)));
        Assert.Equal("all_hidden", hiddenOnly.GetProperty("status").GetString());
        Assert.Equal(0, hiddenOnly.GetProperty("current").GetProperty("money").GetProperty("included_count").GetInt64());
    }

    [Fact]
    public async Task ComparisonOrdersAbsoluteDirectionalDeltaAndPaginatesRemainder()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-12-01', end_date = DATE '2026-02-28'
            WHERE id = '{statementId}';
            """);

        Guid decrease = await CreateCategoryAsync(session, "Decrease category", "Income");
        Guid increase = await CreateCategoryAsync(session, "Increase category", "Income");
        Guid priorOnly = await CreateCategoryAsync(session, "Prior only category", "Income");
        await SeedTransactionAsync(customerId, accountId, statementId, decrease, new DateOnly(2026, 1, 5), "Prior decrease", 600m, 1);
        await SeedTransactionAsync(customerId, accountId, statementId, decrease, new DateOnly(2026, 2, 5), "Current decrease", 100m, 2);
        await SeedTransactionAsync(customerId, accountId, statementId, increase, new DateOnly(2026, 1, 6), "Prior increase", 100m, 3);
        await SeedTransactionAsync(customerId, accountId, statementId, increase, new DateOnly(2026, 2, 6), "Current increase", 200m, 4);
        await SeedTransactionAsync(customerId, accountId, statementId, priorOnly, new DateOnly(2026, 1, 7), "Prior only", 50m, 5);

        JsonElement report = await ReportAsync(session, new
        {
            view = "comparison",
            period = new { mode = "calendar", preset = "month", month = "2026-02" },
        });

        Assert.Equal("comparison", report.GetProperty("view").GetString());
        JsonElement changes = report.GetProperty("category_changes");
        JsonElement inChanges = changes.GetProperty("money_in");
        JsonElement[] items = inChanges.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, items.Length);
        Assert.Equal(decrease, items[0].GetProperty("id").GetGuid());
        Assert.Equal("-500.00", items[0].GetProperty("delta").GetString());
        Assert.Equal("-83.3", items[0].GetProperty("delta_percentage").GetString());
        Assert.Equal(increase, items[1].GetProperty("id").GetGuid());
        Assert.Equal("100.00", items[1].GetProperty("delta").GetString());
        Assert.Equal(priorOnly, items[2].GetProperty("id").GetGuid());
        Assert.Equal("-50.00", items[2].GetProperty("delta").GetString());
        Assert.Equal("-100.0", items[2].GetProperty("delta_percentage").GetString());
        Assert.Equal("2026-02-01", items[0].GetProperty("current_descriptor").GetProperty("start_date").GetString());
        Assert.Equal("2026-01-01", items[0].GetProperty("prior_descriptor").GetProperty("start_date").GetString());
        Assert.Equal("2026-01-31", items[0].GetProperty("prior_descriptor").GetProperty("end_date").GetString());
        Assert.False(inChanges.GetProperty("has_more").GetBoolean());
        Assert.Equal(3, inChanges.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task ReportTransportRejectsMalformedBodiesAndGuardsAuthority()
    {
        var (session, _) = await NewCustomerAsync();

        HttpResponseMessage unauthorized = await SendAsync(Post(ReportPath, Json(new { })));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.True(unauthorized.Headers.CacheControl?.NoStore);

        HttpResponseMessage unknown = await SendAsync(Post(ReportPath, Json(new { view = "overview", extra = 1 }), session: session));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        HttpResponseMessage huge = await SendAsync(Post(
            ReportPath,
            "{\"padding\":\"" + new string('x', 20 * 1024) + "\"}",
            session: session));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);

        HttpResponseMessage mixed = await SendAsync(Post(
            ReportPath,
            Json(new { period = new { mode = "custom", start_date = "2026-01-01" } }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);

        HttpResponseMessage missingFocus = await SendAsync(Post(
            ReportPath,
            Json(new { view = "category" }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, missingFocus.StatusCode);

        HttpResponseMessage foreign = await SendAsync(Post(
            ReportPath,
            Json(new { view = "category", filters = new { category_id = Guid.NewGuid() } }),
            session: session));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        HttpResponseMessage ok = await SendAsync(Post(ReportPath, Json(new { view = "overview" }), session: session));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True(ok.Headers.CacheControl?.NoStore);
    }

    private async Task<ReportFixture> CreateFixtureAsync()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (systemId, expenseId) = await DefaultCategoriesAsync(session);
        Guid incomeId = await CreateCategoryAsync(session, "Salary income", "Income");
        Guid transferId = await CreateCategoryAsync(session, "Own transfers", "Transfer");
        Guid excludedId = await CreateCategoryAsync(session, "Excluded movements", "Excluded");
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        Guid hiddenStatementId = await SeedStatementAsync(customerId, accountId, hidden: true);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-31'
            WHERE id = '{statementId}';
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-31'
            WHERE id = '{hiddenStatementId}';
            """);

        Guid tagA = await SeedTagAsync(customerId, "Tag A");
        Guid tagB = await SeedTagAsync(customerId, "Tag B");

        await SeedTransactionAsync(customerId, accountId, statementId, incomeId, new DateOnly(2025, 1, 5), "Income one", 1000m, 1, [tagA]);
        await SeedTransactionAsync(customerId, accountId, statementId, incomeId, new DateOnly(2025, 1, 6), "Income two", 200m, 2);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 7), "Expense", -300m, 3, [tagA, tagB]);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 8), "Refund", 50m, 4);
        await SeedTransactionAsync(customerId, accountId, statementId, systemId, new DateOnly(2025, 1, 9), "Uncategorised", -40m, 5);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 10), "Zero row", 0m, 6);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 11), "Hidden individual", -999m, 7, hidden: true);
        await SeedTransactionAsync(customerId, accountId, hiddenStatementId, transferId, new DateOnly(2025, 1, 12), "Hidden transfer", -600m, 1);
        await SeedTransactionAsync(customerId, accountId, hiddenStatementId, excludedId, new DateOnly(2025, 1, 13), "Hidden excluded", -300m, 2);
        await SeedTransactionAsync(customerId, accountId, hiddenStatementId, incomeId, new DateOnly(2025, 1, 14), "Hidden parent income", 888m, 3);

        // Visible transfer/excluded rows in the main statement.
        await SeedTransactionAsync(customerId, accountId, statementId, transferId, new DateOnly(2025, 1, 15), "Visible transfer", -100m, 8);
        await SeedTransactionAsync(customerId, accountId, statementId, excludedId, new DateOnly(2025, 1, 16), "Visible excluded", -100m, 9);

        return new ReportFixture(
            session,
            customerId,
            accountId,
            statementId,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31));
    }

    private static object CustomBody(DateOnly start, DateOnly end) =>
        new
        {
            view = "overview",
            period = new
            {
                mode = "custom",
                start_date = start.ToString("yyyy-MM-dd"),
                end_date = end.ToString("yyyy-MM-dd"),
            },
        };

    private static object FilteredBody(DateOnly start,
                                       DateOnly end,
                                       Guid accountId,
                                       Guid? categoryId = null) =>
        new
        {
            view = "overview",
            period = new
            {
                mode = "custom",
                start_date = start.ToString("yyyy-MM-dd"),
                end_date = end.ToString("yyyy-MM-dd"),
            },
            filters = new { financial_account_id = accountId, category_id = categoryId },
        };

    private static object CalendarBody(string preset) =>
        new
        {
            view = "overview",
            period = new { mode = "calendar", preset },
        };

    private async Task<JsonElement> ReportAsync(string session, object body)
    {
        HttpResponseMessage response = await SendAsync(Post(ReportPath, Json(body), session: session));
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

    private async Task ConfirmSalaryAsync(string session, Guid transactionId, long revision)
    {
        HttpResponseMessage response = await SendAsync(Post(
            "/v1/finance/salary-cycles",
            Json(new
            {
                transaction_id = transactionId,
                expected_transaction_version = 1,
                expected_revision = revision.ToString(),
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private static decimal SumRanks(JsonElement direction)
    {
        decimal sum = 0m;
        foreach (JsonElement rank in direction.GetProperty("ranks").EnumerateArray())
        {
            sum += decimal.Parse(rank.GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        }
        JsonElement? uncategorised = direction.GetProperty("uncategorised").ValueKind == JsonValueKind.Object
            ? direction.GetProperty("uncategorised")
            : null;
        if (uncategorised is not null)
        {
            sum += decimal.Parse(uncategorised.Value.GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        }
        sum += decimal.Parse(direction.GetProperty("other").GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

        return sum;
    }

    private static string TransactionAmount(decimal amount) =>
        amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private sealed record ReportFixture(string Session,
                                        Guid CustomerId,
                                        Guid AccountId,
                                        Guid StatementId,
                                        DateOnly PeriodStart,
                                        DateOnly PeriodEnd);
}
