using System.Net;
using System.Text.Json;

namespace argus.Server.Tests;

public sealed class ReportingCoverageTests(KitFactory factory) : TransactionTestBase(factory)
{
    private const string ReportPath = "/v1/finance/reports/query";

    [Fact]
    public async Task CoverageIsTheUnionAcrossSelectedAccountsAndIgnoresCategoryFilters()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountA = await CreateAccountAsync(session, "Account A");
        Guid accountB = await CreateAccountAsync(session, "Account B");
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid statementA = await SeedStatementAsync(customerId, accountA, label: "A period");
        Guid statementB = await SeedStatementAsync(customerId, accountB, label: "B period");
        Guid hiddenStatement = await SeedStatementAsync(customerId, accountB, hidden: true, label: "Hidden period");
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-02-15' WHERE id = '{statementA}';
            UPDATE statement_imports SET start_date = DATE '2025-02-10', end_date = DATE '2025-03-10' WHERE id = '{statementB}';
            UPDATE statement_imports SET start_date = DATE '2025-03-11', end_date = DATE '2025-03-31' WHERE id = '{hiddenStatement}';
            """);
        await SeedTransactionAsync(customerId, accountA, statementA, expenseId, new DateOnly(2025, 1, 15), "A row", -10m, 1);
        await SeedTransactionAsync(customerId, accountB, statementB, expenseId, new DateOnly(2025, 2, 20), "B row", -20m, 1);

        JsonElement union = await ReportAsync(session, CustomBody(new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), null, null));
        JsonElement unionCoverage = union.GetProperty("coverage").GetProperty("current");
        Assert.Equal(90, unionCoverage.GetProperty("total_days").GetInt64());
        Assert.Equal(69, unionCoverage.GetProperty("covered_days").GetInt64());
        Assert.Equal("76.7", unionCoverage.GetProperty("covered_percentage").GetString());
        Assert.True(unionCoverage.GetProperty("partial").GetBoolean());
        JsonElement[] unionGaps = unionCoverage.GetProperty("gaps").GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(unionGaps);
        Assert.Equal("2025-03-11", unionGaps[0].GetProperty("start_date").GetString());
        Assert.Equal("2025-03-31", unionGaps[0].GetProperty("end_date").GetString());

        JsonElement accountOnly = await ReportAsync(session, CustomBody(new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), accountA, null));
        JsonElement accountCoverage = accountOnly.GetProperty("coverage").GetProperty("current");
        Assert.Equal(46, accountCoverage.GetProperty("covered_days").GetInt64());
        JsonElement[] accountGaps = accountCoverage.GetProperty("gaps").GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(accountGaps);
        Assert.Equal("2025-02-16", accountGaps[0].GetProperty("start_date").GetString());
        Assert.Equal("2025-03-31", accountGaps[0].GetProperty("end_date").GetString());

        JsonElement filtered = await ReportAsync(session, CustomBody(new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), null, expenseId));
        Assert.Equal(
            unionCoverage.GetProperty("covered_days").GetInt64(),
            filtered.GetProperty("coverage").GetProperty("current").GetProperty("covered_days").GetInt64());
        Assert.Equal(
            unionCoverage.GetProperty("gaps").GetProperty("total").GetInt64(),
            filtered.GetProperty("coverage").GetProperty("current").GetProperty("gaps").GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task CoverageGapsPaginateWithoutOmittingNames()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid firstStatement = await SeedStatementAsync(customerId, accountId, label: "First");
        await ExecuteSqlAsync($"""
            DELETE FROM statement_imports WHERE customer_id = '{customerId}' AND id <> '{firstStatement}';
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-01' WHERE id = '{firstStatement}';
            """);
        await ExecuteSqlAsync($"""
            INSERT INTO statement_imports (
                id, customer_id, financial_account_id, source_type, statement_label,
                statement_fingerprint, fingerprint_occurrence, start_date, end_date,
                total_rows, imported_count, suppressed_count, skipped_count, created_at, updated_at)
            SELECT gen_random_uuid(), '{customerId}', '{accountId}', 'generic_csv',
                   'Coverage ' || series, 'coverage-' || series, 1,
                   DATE '2025-01-01' + (series * 2), DATE '2025-01-01' + (series * 2),
                   0, 0, 0, 0, NOW(), NOW()
            FROM generate_series(0, 29) AS series;
            """);
        await SeedTransactionAsync(customerId, accountId, firstStatement, expenseId, new DateOnly(2025, 1, 1), "Covered row", -10m, 1);

        JsonElement pageOne = await ReportAsync(session, new
        {
            view = "overview",
            period = new { mode = "custom", start_date = "2025-01-01", end_date = "2025-02-28" },
            coverage_page = 1,
        });
        JsonElement coverageOne = pageOne.GetProperty("coverage").GetProperty("current").GetProperty("gaps");
        Assert.Equal(29, coverageOne.GetProperty("total").GetInt64());
        Assert.Equal(25, coverageOne.GetProperty("items").GetArrayLength());
        Assert.True(coverageOne.GetProperty("has_more").GetBoolean());
        Assert.Equal(2, coverageOne.GetProperty("next_page").GetInt32());

        JsonElement pageTwo = await ReportAsync(session, new
        {
            view = "overview",
            period = new { mode = "custom", start_date = "2025-01-01", end_date = "2025-02-28" },
            coverage_page = 2,
        });
        JsonElement coverageTwo = pageTwo.GetProperty("coverage").GetProperty("current").GetProperty("gaps");
        Assert.Equal(4, coverageTwo.GetProperty("items").GetArrayLength());
        Assert.False(coverageTwo.GetProperty("has_more").GetBoolean());

        string[] starts = coverageOne.GetProperty("items").EnumerateArray()
            .Concat(coverageTwo.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("start_date").GetString()!)
            .ToArray();
        Assert.Equal(29, starts.Distinct().Count());
        Assert.Contains("2025-02-27", starts);
        Assert.DoesNotContain("2025-02-28", starts);
    }

    [Fact]
    public async Task UncoveredBucketsShowRecordedValuesWithoutClaimingZeroSpending()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid statement = await SeedStatementAsync(customerId, accountId, label: "January only");
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '2025-01-01', end_date = DATE '2025-01-31' WHERE id = '{statement}';
            """);
        await SeedTransactionAsync(customerId, accountId, statement, expenseId, new DateOnly(2025, 1, 10), "Covered", -10m, 1);
        await SeedTransactionAsync(customerId, accountId, statement, expenseId, new DateOnly(2025, 2, 10), "Recorded outside coverage", -25m, 2);

        JsonElement report = await ReportAsync(session, CustomBody(new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 28), null, null));
        JsonElement[] buckets = report.GetProperty("trend").EnumerateArray().ToArray();
        JsonElement february = buckets.Single(bucket =>
            string.CompareOrdinal(bucket.GetProperty("start_date").GetString(), "2025-02-10") <= 0 &&
            string.CompareOrdinal(bucket.GetProperty("end_date").GetString(), "2025-02-10") >= 0);
        Assert.Equal("uncovered", february.GetProperty("coverage").GetString());
        Assert.Equal("25.00", february.GetProperty("money_out").GetString());
        Assert.Equal(1, february.GetProperty("count").GetInt64());
        Assert.True(report.GetProperty("coverage").GetProperty("current").GetProperty("gaps").GetProperty("total").GetInt64() > 0);
    }

    [Fact]
    public async Task CoverageAtTheMaximumDateDoesNotInventAFinalGap()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        await ExecuteSqlAsync($"""
            UPDATE statement_imports SET start_date = DATE '9999-12-31', end_date = DATE '9999-12-31'
            WHERE id = '{statementId}';
            """);
        await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(9999, 12, 31), "Maximum date", -1m, 1);

        Assert.Equal("9999-12-31", await StringScalarAsync($"SELECT start_date::text FROM statement_imports WHERE id = '{statementId}'"));
        JsonElement report = await ReportAsync(session, CustomBody(new DateOnly(9999, 12, 31), new DateOnly(9999, 12, 31), null, null));
        JsonElement coverage = report.GetProperty("coverage").GetProperty("current");
        Assert.Equal(1, coverage.GetProperty("total_days").GetInt64());
        Assert.Equal(1, coverage.GetProperty("covered_days").GetInt64());
        Assert.Equal(0, coverage.GetProperty("gaps").GetProperty("total").GetInt64());
    }

    private async Task<JsonElement> ReportAsync(string session, object body)
    {
        HttpResponseMessage response = await SendAsync(Post(ReportPath, Json(body), session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private static object CustomBody(DateOnly start, DateOnly end, Guid? accountId, Guid? categoryId) =>
        new
        {
            view = "overview",
            period = new
            {
                mode = "custom",
                start_date = start.ToString("yyyy-MM-dd"),
                end_date = end.ToString("yyyy-MM-dd"),
            },
            filters = accountId is null && categoryId is null
                ? null
                : new
                {
                    financial_account_id = accountId,
                    category_id = categoryId,
                },
        };
}
