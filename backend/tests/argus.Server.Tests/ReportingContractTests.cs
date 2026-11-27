using System.Net;
using System.Text.Json;
using argus.Server.Reporting;
using Microsoft.AspNetCore.Mvc.Testing;

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
    public async Task LatestCategoryChangeUsesMatchedSlicesRatherThanOverviewOrFullPriorCell()
    {
        using WebApplicationFactory<Program> datedHost = Factory.WithReportingDate(new DateOnly(2024, 3, 15));
        using HttpClient reporting = datedHost.CreateClient();
        (string session, Guid customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        (Guid systemId, Guid expenseId) = await DefaultCategoriesAsync(session);
        Guid incomeId = await CreateCategoryAsync(session, "Archived income history", "Income");
        Guid transferId = await CreateCategoryAsync(session, "Matched transfer", "Transfer");
        Guid excludedId = await CreateCategoryAsync(session, "Matched excluded", "Excluded");
        Guid statement = await SeedStatementAsync(customerId, accountId);
        Guid hiddenStatement = await SeedStatementAsync(customerId, accountId, hidden: true);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2024-02-01', end_date = DATE '2024-02-15' WHERE id = '{statement}';
            UPDATE categories SET is_archived = TRUE WHERE id = '{incomeId}';
            """);
        Guid currentStatement = await SeedStatementAsync(customerId, accountId, label: "Matched current");
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2024-03-01', end_date = DATE '2024-03-15' WHERE id = '{currentStatement}';
            """);

        await SeedTransactionAsync(customerId, accountId, statement, expenseId, new DateOnly(2024, 2, 1), "Prior first day", -60m, 1);
        await SeedTransactionAsync(customerId, accountId, statement, expenseId, new DateOnly(2024, 2, 15), "Prior last matched day", -40m, 2);
        await SeedTransactionAsync(customerId, accountId, statement, expenseId, new DateOnly(2024, 2, 16), "Prior outside slice", -900m, 3);
        await SeedTransactionAsync(customerId, accountId, currentStatement, expenseId, new DateOnly(2024, 3, 15), "Current last day", -120m, 1);
        await SeedTransactionAsync(customerId, accountId, currentStatement, expenseId, new DateOnly(2024, 3, 10), "Refund", 30m, 2);
        await SeedTransactionAsync(customerId, accountId, currentStatement, incomeId, new DateOnly(2024, 3, 1), "Income-kind outflow", -80m, 3);
        await SeedTransactionAsync(customerId, accountId, statement, incomeId, new DateOnly(2024, 2, 1), "Prior income-kind outflow", -100m, 4);
        await SeedTransactionAsync(customerId, accountId, statement, systemId, new DateOnly(2024, 2, 15), "Prior uncategorised", -100m, 5);
        await SeedTransactionAsync(customerId, accountId, currentStatement, expenseId, new DateOnly(2024, 3, 8), "Child hidden", -1000m, 4, hidden: true);
        await SeedTransactionAsync(customerId, accountId, hiddenStatement, expenseId, new DateOnly(2024, 3, 9), "Parent hidden", -1000m, 1);
        await SeedTransactionAsync(customerId, accountId, currentStatement, transferId, new DateOnly(2024, 3, 4), "Not spending", -500m, 5);
        await SeedTransactionAsync(customerId, accountId, currentStatement, excludedId, new DateOnly(2024, 3, 5), "Excluded", -600m, 6);

        object selection = CustomBody(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        JsonElement report = await ReportAsync(session, selection, reporting);
        Assert.Equal("2024-01-31", report.GetProperty("period").GetProperty("end_date").GetString());
        JsonElement trend = report.GetProperty("category_trend");
        JsonElement latest = trend.GetProperty("latest_comparison");
        AssertLatestRanges(latest, "2024-03-01", "2024-03-15", "2024-02-01", "2024-02-15", partial: true, shorter: false);
        Assert.Equal("Same period last month", latest.GetProperty("label").GetString());
        Assert.Equal("available", latest.GetProperty("availability").GetString());
        Assert.Equal("covered", latest.GetProperty("current_coverage").GetString());
        Assert.Equal("covered", latest.GetProperty("prior_coverage").GetString());
        Assert.True(latest.GetProperty("has_prior_included_data").GetBoolean());
        Assert.Equal(JsonValueKind.Null, latest.GetProperty("unavailable_reason").ValueKind);

        JsonElement[] categories = trend.GetProperty("categories").EnumerateArray().ToArray();
        Assert.Equal(new[] { expenseId, incomeId, systemId }, categories.Select(category => category.GetProperty("id").GetGuid()));
        Assert.True(categories[1].GetProperty("is_archived").GetBoolean());
        AssertLatestChange(categories[0], "120.00", "100.00", "increase", "20.0");
        AssertLatestChange(categories[1], "80.00", "100.00", "decrease", "-20.0");
        AssertLatestChange(categories[2], "0.00", "100.00", "decrease", "-100.0");
        Assert.Equal("1120.00", categories[0].GetProperty("money_out").GetString());
        Assert.Equal(4, categories[0].GetProperty("count").GetInt64());
        JsonElement priorCell = categories[0].GetProperty("months")[4];
        Assert.Equal("1000.00", priorCell.GetProperty("amount").GetString());
        Assert.Equal(3, priorCell.GetProperty("count").GetInt64());
        JsonElement descriptor = priorCell.GetProperty("descriptor");
        Assert.Equal("2024-02-29", descriptor.GetProperty("end_date").GetString());
        Assert.Equal("partial", trend.GetProperty("months")[4].GetProperty("coverage").GetString());
        JsonElement drilldown = await QueryAsync(session, descriptor);
        Assert.Equal("1000.00", drilldown.GetProperty("money_totals").GetProperty("money_out").GetString());
        Assert.Equal(3, drilldown.GetProperty("filtered_total").GetInt64());
    }

    [Fact]
    public async Task LatestCategoryStatesSeparateIncludedPriorRowsFromCategoryZeros()
    {
        using WebApplicationFactory<Program> datedHost = Factory.WithReportingDate(new DateOnly(2024, 3, 15));
        using HttpClient reporting = datedHost.CreateClient();
        (string session, Guid customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        (Guid _, Guid expenseId) = await DefaultCategoriesAsync(session);
        Guid priorOnlyId = await CreateCategoryAsync(session, "Prior after slice", "Expense");
        Guid incomeId = await CreateCategoryAsync(session, "Prior evidence income", "Income");
        Guid equalId = await CreateCategoryAsync(session, "Equal comparison", "Expense");
        Guid tinyId = await CreateCategoryAsync(session, "Tiny change", "Expense");
        Guid statement = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2024-02-01', end_date = DATE '2024-03-15' WHERE id = '{statement}';
            """);
        await SeedTransactionAsync(customerId, accountId, statement, expenseId, new DateOnly(2024, 3, 15), "Current spending", -120m, 1);
        await SeedTransactionAsync(customerId, accountId, statement, priorOnlyId, new DateOnly(2024, 2, 16), "Outside prior slice", -100m, 2);

        object selection = CalendarBody("month_to_date");
        async Task<JsonElement> TrendAsync() => (await ReportAsync(session, selection, reporting)).GetProperty("category_trend");
        JsonElement noData = await TrendAsync();
        JsonElement latest = noData.GetProperty("latest_comparison");
        Assert.Equal("unavailable", latest.GetProperty("availability").GetString());
        Assert.Equal("no_prior_data", latest.GetProperty("unavailable_reason").GetString());
        Assert.False(latest.GetProperty("has_prior_included_data").GetBoolean());
        AssertLatestChange(TrendCategory(noData, expenseId), "120.00", "0.00", "unavailable", null);
        AssertLatestChange(TrendCategory(noData, priorOnlyId), "0.00", "0.00", "unavailable", null);

        Guid priorIncome = await SeedTransactionAsync(customerId, accountId, statement, incomeId, new DateOnly(2024, 2, 15), "Positive prior evidence", 50m, 3);
        JsonElement positive = await TrendAsync();
        Assert.True(positive.GetProperty("latest_comparison").GetProperty("has_prior_included_data").GetBoolean());
        AssertLatestChange(TrendCategory(positive, expenseId), "120.00", "0.00", "new_spending", null);
        AssertLatestChange(TrendCategory(positive, priorOnlyId), "0.00", "0.00", "no_spending", null);

        await ExecuteSqlAsync($"DELETE FROM transactions WHERE id = '{priorIncome}'");
        await SeedTransactionAsync(customerId, accountId, statement, incomeId, new DateOnly(2024, 2, 1), "Zero prior evidence", 0m, 4);
        JsonElement zero = await TrendAsync();
        Assert.True(zero.GetProperty("latest_comparison").GetProperty("has_prior_included_data").GetBoolean());
        AssertLatestChange(TrendCategory(zero, expenseId), "120.00", "0.00", "new_spending", null);
        AssertLatestChange(TrendCategory(zero, priorOnlyId), "0.00", "0.00", "no_spending", null);

        await SeedTransactionAsync(customerId, accountId, statement, equalId, new DateOnly(2024, 2, 10), "Prior equal amount", -100m, 5);
        await SeedTransactionAsync(customerId, accountId, statement, equalId, new DateOnly(2024, 3, 10), "Current equal amount", -100m, 6);
        await SeedTransactionAsync(customerId, accountId, statement, tinyId, new DateOnly(2024, 2, 10), "Prior tiny baseline", -100m, 7);
        await SeedTransactionAsync(customerId, accountId, statement, tinyId, new DateOnly(2024, 3, 10), "Current tiny decrease", -99.99m, 8);
        JsonElement withSpending = await TrendAsync();
        AssertLatestChange(TrendCategory(withSpending, equalId), "100.00", "100.00", "unchanged", "0.0");
        AssertLatestChange(TrendCategory(withSpending, tinyId), "99.99", "100.00", "decrease", "0.0");
    }

    [Theory]
    [InlineData(2, 29, "2024-02-01", "2024-01-01", "2024-01-31", false, false)]
    [InlineData(3, 1, "2024-03-01", "2024-02-01", "2024-02-01", true, false)]
    [InlineData(3, 30, "2024-03-01", "2024-02-01", "2024-02-29", true, true)]
    [InlineData(3, 31, "2024-03-01", "2024-02-01", "2024-02-29", false, false)]
    public async Task CalendarTrendDistinguishesElapsedAndCompletedMonths(int month,
                                                                        int day,
                                                                        string currentStart,
                                                                        string priorStart,
                                                                        string priorEnd,
                                                                        bool partial,
                                                                        bool shorter)
    {
        DateOnly today = new(2024, month, day);
        using WebApplicationFactory<Program> datedHost = Factory.WithReportingDate(today);
        using HttpClient reporting = datedHost.CreateClient();
        (string session, Guid customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        (Guid _, Guid expenseId) = await DefaultCategoriesAsync(session);
        Guid statement = await SeedStatementAsync(customerId, accountId);
        await SeedTransactionAsync(customerId, accountId, statement, expenseId, today, "Latest month", -1m, 1);

        JsonElement report = await ReportAsync(session, CalendarBody("previous_month"), reporting);
        Assert.Equal(priorStart, report.GetProperty("period").GetProperty("start_date").GetString());
        JsonElement trend = report.GetProperty("category_trend");
        JsonElement latest = trend.GetProperty("latest_comparison");
        AssertLatestRanges(latest, currentStart, today.ToString("yyyy-MM-dd"), priorStart, priorEnd, partial, shorter);
        Assert.Equal(partial ? "Same period last month" : "Previous calendar month", latest.GetProperty("label").GetString());
        Assert.Equal(partial, trend.GetProperty("months")[5].GetProperty("is_partial").GetBoolean());
    }

    [Fact]
    public async Task CategoryComparisonUsesSelectedConfirmedSalaryCycleAndItsPredecessor()
    {
        using WebApplicationFactory<Program> datedHost = Factory.WithReportingDate(new DateOnly(2025, 3, 3));
        using HttpClient reporting = datedHost.CreateClient();
        (string session, Guid customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        (Guid _, Guid expenseId) = await DefaultCategoriesAsync(session);
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-25', end_date = DATE '2025-03-03' WHERE id = '{statementId}';
            """);
        Guid jan = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 1, 25), "First salary", 1000m, 1);
        Guid feb = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 2, 24), "Second salary", 1000m, 2);
        Guid mar = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 3, 2), "Third salary", 1000m, 3);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 2, 23), "First cycle last day", -100m, 4);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 3, 1), "Short cycle last day", -80m, 5);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 3, 3), "Open cycle through today", -120m, 6);

        object overview = new { view = "overview" };
        JsonElement setup = await ReportAsync(session, overview, reporting);
        Assert.Equal(JsonValueKind.Null, setup.GetProperty("category_trend").ValueKind);

        await ConfirmSalaryAsync(session, jan, 0);
        JsonElement first = (await ReportAsync(session, overview, reporting)).GetProperty("category_trend");
        JsonElement firstComparison = first.GetProperty("latest_comparison");
        Assert.Equal("2025-01-25", firstComparison.GetProperty("current_period").GetProperty("start_date").GetString());
        Assert.Equal(JsonValueKind.Null, firstComparison.GetProperty("prior_period").ValueKind);
        Assert.Equal(JsonValueKind.Null, firstComparison.GetProperty("prior_coverage").ValueKind);
        Assert.Equal("unavailable", firstComparison.GetProperty("availability").GetString());
        Assert.Equal("insufficient_salary_boundaries", firstComparison.GetProperty("unavailable_reason").GetString());
        AssertLatestChange(TrendCategory(first, expenseId), "300.00", null, "unavailable", null);

        Guid middle = await ConfirmSalaryAsync(session, feb, 1);
        await ConfirmSalaryAsync(session, mar, 2);
        JsonElement openReport = await ReportAsync(session, overview, reporting);
        JsonElement open = openReport.GetProperty("category_trend");
        JsonElement columns = open.GetProperty("months");
        JsonElement expense = TrendCategory(open, expenseId);
        Assert.Equal(3, columns.GetArrayLength());
        Assert.Equal("insufficient_salary_boundaries", columns[0].GetProperty("comparison").GetProperty("unavailable_reason").GetString());
        AssertLatestRanges(columns[1].GetProperty("comparison"), "2025-02-24", "2025-03-01", "2025-01-25", "2025-02-23", partial: false, shorter: false);
        AssertLatestRanges(columns[2].GetProperty("comparison"), "2025-03-02", "2025-03-03", "2025-02-24", "2025-03-01", partial: true, shorter: false);
        AssertCategoryChange(expense.GetProperty("months")[0].GetProperty("change"), "100.00", null, "unavailable", null);
        AssertCategoryChange(expense.GetProperty("months")[1].GetProperty("change"), "80.00", "100.00", "decrease", "-20.0");
        AssertCategoryChange(expense.GetProperty("months")[2].GetProperty("change"), "120.00", "80.00", "increase", "50.0");
        AssertLatestChange(expense, "120.00", "80.00", "increase", "50.0");
        // The separate Overview comparison keeps its existing elapsed-day rule.
        Assert.Equal("2025-02-25", openReport.GetProperty("comparison").GetProperty("period").GetProperty("end_date").GetString());

        await ExecuteSqlAsync($"UPDATE statement_imports SET end_date = DATE '2025-03-02' WHERE id = '{statementId}'");
        JsonElement gap = (await ReportAsync(session, overview, reporting)).GetProperty("category_trend");
        Assert.Equal("available", gap.GetProperty("months")[1].GetProperty("comparison").GetProperty("availability").GetString());
        Assert.Equal("incomplete_coverage", gap.GetProperty("months")[2].GetProperty("comparison").GetProperty("availability").GetString());
        JsonElement gapCells = TrendCategory(gap, expenseId).GetProperty("months");
        AssertCategoryChange(gapCells[1].GetProperty("change"), "80.00", "100.00", "decrease", "-20.0");
        AssertCategoryChange(gapCells[2].GetProperty("change"), "120.00", "80.00", "incomplete_coverage", null);

        JsonElement closed = (await ReportAsync(session, new
        {
            view = "overview",
            period = new { mode = "salary_cycle", cycle_start_id = middle },
        }, reporting)).GetProperty("category_trend");
        Assert.Equal(2, closed.GetProperty("months").GetArrayLength());
        AssertLatestRanges(closed.GetProperty("latest_comparison"), "2025-02-24", "2025-03-01", "2025-01-25", "2025-02-23", partial: false, shorter: false);
        AssertLatestChange(TrendCategory(closed, expenseId), "80.00", "100.00", "decrease", "-20.0");
    }

    [Fact]
    public async Task EarliestDisplayedMonthComparesWithAnUndisplayedPredecessorWithoutAddingRows()
    {
        using WebApplicationFactory<Program> datedHost = Factory.WithReportingDate(new DateOnly(2024, 7, 15));
        using HttpClient reporting = datedHost.CreateClient();
        (string session, Guid customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        (Guid _, Guid expenseId) = await DefaultCategoriesAsync(session);
        Guid priorOnlyId = await CreateCategoryAsync(session, "Outside visible history", "Expense");
        Guid statement = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"UPDATE statement_imports SET start_date = DATE '2024-01-01', end_date = DATE '2024-07-15' WHERE id = '{statement}'");
        await SeedTransactionAsync(customerId, accountId, statement, expenseId, new DateOnly(2024, 1, 31), "Undisplayed prior", -100m, 1);
        await SeedTransactionAsync(customerId, accountId, statement, priorOnlyId, new DateOnly(2024, 1, 31), "Undisplayed category", -300m, 2);
        await SeedTransactionAsync(customerId, accountId, statement, expenseId, new DateOnly(2024, 2, 29), "Earliest displayed month", -120m, 3);

        JsonElement trend = (await ReportAsync(session, CalendarBody("month_to_date"), reporting)).GetProperty("category_trend");
        JsonElement months = trend.GetProperty("months");
        Assert.Equal(6, months.GetArrayLength());
        AssertLatestRanges(months[0].GetProperty("comparison"), "2024-02-01", "2024-02-29", "2024-01-01", "2024-01-31", partial: false, shorter: false);
        JsonElement category = Assert.Single(trend.GetProperty("categories").EnumerateArray());
        Assert.Equal(expenseId, category.GetProperty("id").GetGuid());
        Assert.Equal("120.00", category.GetProperty("money_out").GetString());
        Assert.Equal(1, category.GetProperty("count").GetInt64());
        JsonElement earliest = category.GetProperty("months")[0];
        Assert.Equal("2024-02-29", earliest.GetProperty("descriptor").GetProperty("end_date").GetString());
        AssertCategoryChange(earliest.GetProperty("change"), "120.00", "100.00", "increase", "20.0");
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
    public async Task SalaryCycleChartsUseConfirmedCycleBuckets()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        DateOnly today = ReportingClock.Today(TimeProvider.System);
        DateOnly firstStart = today.AddMonths(-3);
        DateOnly secondStart = today.AddMonths(-2);
        DateOnly thirdStart = today.AddMonths(-1);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports
            SET start_date = DATE '{firstStart.AddDays(-10):yyyy-MM-dd}', end_date = DATE '{today:yyyy-MM-dd}'
            WHERE id = '{statementId}';
            """);

        Guid firstSalary = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, firstStart, "First salary", 10_000m, 1);
        Guid secondSalary = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, secondStart, "Second salary", 10_000m, 2);
        Guid thirdSalary = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, thirdStart, "Third salary", 10_000m, 3);
        await ConfirmSalaryAsync(session, firstSalary, 0);
        await ConfirmSalaryAsync(session, secondSalary, 1);
        await ConfirmSalaryAsync(session, thirdSalary, 2);

        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, firstStart.AddDays(1), "First cycle expense", -10m, 4);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, secondStart.AddDays(1), "Second cycle expense", -20m, 5);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, thirdStart.AddDays(1), "Third cycle expense", -30m, 6);

        JsonElement report = await ReportAsync(session, new { view = "overview" });
        JsonElement cycleTrend = report.GetProperty("salary_cycle_trend");
        Assert.Equal(3, cycleTrend.GetArrayLength());
        Assert.Equal(firstStart.ToString("yyyy-MM-dd"), cycleTrend[0].GetProperty("start_date").GetString());
        Assert.Equal(secondStart.AddDays(-1).ToString("yyyy-MM-dd"), cycleTrend[0].GetProperty("end_date").GetString());
        Assert.Equal("10.00", cycleTrend[0].GetProperty("money_out").GetString());
        Assert.Equal(secondStart.ToString("yyyy-MM-dd"), cycleTrend[1].GetProperty("start_date").GetString());
        Assert.Equal(thirdStart.AddDays(-1).ToString("yyyy-MM-dd"), cycleTrend[1].GetProperty("end_date").GetString());
        Assert.Equal("20.00", cycleTrend[1].GetProperty("money_out").GetString());
        Assert.Equal(thirdStart.ToString("yyyy-MM-dd"), cycleTrend[2].GetProperty("start_date").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd"), cycleTrend[2].GetProperty("end_date").GetString());
        Assert.Equal("30.00", cycleTrend[2].GetProperty("money_out").GetString());

        JsonElement categoryTrend = report.GetProperty("category_trend");
        JsonElement months = categoryTrend.GetProperty("months");
        Assert.Equal(3, months.GetArrayLength());
        Assert.Equal(firstStart.ToString("yyyy-MM-dd"), months[0].GetProperty("start_date").GetString());
        Assert.Equal(thirdStart.ToString("yyyy-MM-dd"), months[2].GetProperty("start_date").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd"), months[2].GetProperty("end_date").GetString());
        Assert.True(months[2].GetProperty("is_partial").GetBoolean());
        JsonElement expense = Assert.Single(
            categoryTrend.GetProperty("categories").EnumerateArray(),
            category => category.GetProperty("id").GetGuid() == expenseId);
        JsonElement[] cells = expense.GetProperty("months").EnumerateArray().ToArray();
        Assert.Equal(3, cells.Length);
        Assert.Equal("10.00", cells[0].GetProperty("amount").GetString());
        Assert.Equal("20.00", cells[1].GetProperty("amount").GetString());
        Assert.Equal("30.00", cells[2].GetProperty("amount").GetString());
        Assert.Equal(firstStart.ToString("yyyy-MM-dd"), cells[0].GetProperty("descriptor").GetProperty("start_date").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd"), cells[2].GetProperty("descriptor").GetProperty("end_date").GetString());
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

    private async Task<JsonElement> ReportAsync(string session, object body, HttpClient? reporting = null)
    {
        using HttpRequestMessage request = Post(ReportPath, Json(body), session: session);
        using HttpResponseMessage response = await (reporting ?? Client).SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private static void AssertLatestRanges(JsonElement latest,
                                           string currentStart,
                                           string currentEnd,
                                           string priorStart,
                                           string priorEnd,
                                           bool partial,
                                           bool shorter)
    {
        JsonElement current = latest.GetProperty("current_period");
        JsonElement prior = latest.GetProperty("prior_period");
        Assert.Equal(currentStart, current.GetProperty("start_date").GetString());
        Assert.Equal(currentEnd, current.GetProperty("end_date").GetString());
        Assert.Equal(partial, current.GetProperty("is_partial").GetBoolean());
        Assert.Equal(priorStart, prior.GetProperty("start_date").GetString());
        Assert.Equal(priorEnd, prior.GetProperty("end_date").GetString());
        Assert.False(prior.GetProperty("is_partial").GetBoolean());
        Assert.Equal(shorter, latest.GetProperty("shorter_prior").GetBoolean());
    }

    private static void AssertLatestChange(JsonElement category, string? current, string? prior, string state, string? percentage) =>
        AssertCategoryChange(category.GetProperty("latest_change"), current, prior, state, percentage);

    private static void AssertCategoryChange(JsonElement change, string? current, string? prior, string state, string? percentage)
    {
        Assert.Equal(current, change.GetProperty("current_amount").GetString());
        Assert.Equal(prior, change.GetProperty("prior_amount").GetString());
        Assert.Equal(state, change.GetProperty("state").GetString());
        Assert.Equal(percentage, change.GetProperty("change_percentage").GetString());
    }

    private static JsonElement TrendCategory(JsonElement trend, Guid categoryId) =>
        Assert.Single(trend.GetProperty("categories").EnumerateArray(), category => category.GetProperty("id").GetGuid() == categoryId);

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

    private async Task<Guid> ConfirmSalaryAsync(string session, Guid transactionId, long revision)
    {
        using HttpResponseMessage response = await SendAsync(Post(
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

        return (await BodyAsync(response)).GetProperty("id").GetGuid();
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
