using System.Net;
using System.Text.Json;
using argus.Server.Reporting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace argus.Server.Tests;

public sealed class ReportingConcurrencyTests
{
    [Fact]
    public async Task OneReportSnapshotKeepsTotalsRanksAndVisibilityCoherentThroughConcurrentChanges()
    {
        var injector = new BarrierReportFaultInjector();
        using var factory = new KitFactory(null, null, injector);
        using WebApplicationFactory<Program> datedHost = factory.WithReportingDate(new DateOnly(2025, 1, 15));
        using HttpClient client = datedHost.CreateClient();
        var db = factory.CreateDb();

        string email = $"report-race-{Guid.NewGuid():N}@example.test";
        string session = await RegisterAndLoginAsync(client, factory, email);
        Guid customerId;
        using (HttpResponseMessage me = await SendAsync(client, Get("/v1/me", session)))
        {
            customerId = (await BodyAsync(me)).GetProperty("id").GetGuid();
        }
        Guid accountId = await CreateAccountAsync(client, session);
        Guid expenseId = await CreateCategoryAsync(client, session, "Snapshot expense", "Expense");
        Guid incomeId = await CreateCategoryAsync(client, session, "Snapshot income", "Income");
        Guid statementId = Guid.NewGuid();
        Guid priorStatementId = Guid.NewGuid();
        await using (Npgsql.NpgsqlConnection connection = await db.OpenAsync())
        {
            await PersistenceTestSupport.ExecuteAsync(connection, $"""
                INSERT INTO statement_imports (
                    id, customer_id, financial_account_id, source_type, statement_label,
                    statement_fingerprint, start_date, end_date, created_at, updated_at)
                VALUES
                    ('{statementId}', '{customerId}', '{accountId}', 'generic_csv', 'Snapshot statement',
                     'snapshot-{Guid.NewGuid():N}', DATE '2025-01-01', DATE '2025-01-31', NOW(), NOW()),
                    ('{priorStatementId}', '{customerId}', '{accountId}', 'generic_csv', 'Prior statement',
                     'snapshot-{Guid.NewGuid():N}', DATE '2024-12-01', DATE '2024-12-15', NOW(), NOW());
                INSERT INTO transactions (
                    id, customer_id, financial_account_id, statement_import_id, transaction_date,
                    description, normalized_description, amount, category_id, category_confirmed,
                    source_type, source_row_number, identity_key, created_at, updated_at)
                VALUES
                    ('{Guid.NewGuid()}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-05',
                     'Snapshot expense row', 'snapshot expense row', -100.00, '{expenseId}', TRUE,
                     'generic_csv', 1, 'snapshot-1', NOW(), NOW()),
                    ('{Guid.NewGuid()}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-06',
                     'Snapshot income row', 'snapshot income row', 250.00, '{incomeId}', TRUE,
                     'generic_csv', 2, 'snapshot-2', NOW(), NOW()),
                    ('{Guid.NewGuid()}', '{customerId}', '{accountId}', '{priorStatementId}', DATE '2024-12-15',
                     'Prior expense row', 'prior expense row', -80.00, '{expenseId}', TRUE,
                     'generic_csv', 1, 'snapshot-prior-1', NOW(), NOW());
                """);
        }
        var body = new
        {
            view = "overview",
            period = new { mode = "custom", start_date = "2025-01-01", end_date = "2025-01-31" },
        };

        Task<HttpResponseMessage> reportTask = SendAsync(client, Post("/v1/finance/reports/query", Json(body), session));
        Assert.True(injector.Reached.Wait(TimeSpan.FromSeconds(10)), "the report did not reach the snapshot barrier");

        // Concurrently change the visible statement, move one transaction to a
        // different category and add a new transaction. The next report must
        // see all of it; this in-flight one must not.
        await using (Npgsql.NpgsqlConnection connection = await db.OpenAsync())
        {
            await PersistenceTestSupport.ExecuteAsync(connection, $"""
                UPDATE statement_imports SET is_hidden = TRUE, hidden_at = NOW(), version = version + 1
                WHERE id = '{statementId}';
                UPDATE transactions SET category_id = '{incomeId}', version = version + 1
                WHERE customer_id = '{customerId}' AND statement_import_id = '{statementId}' AND amount < 0;
                INSERT INTO transactions (
                    id, customer_id, financial_account_id, statement_import_id, transaction_date,
                    description, normalized_description, amount, category_id, category_confirmed,
                    source_type, source_row_number, identity_key, created_at, updated_at)
                VALUES ('{Guid.NewGuid()}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-07',
                        'Concurrent new row', 'concurrent new row', -25.00, '{expenseId}', TRUE,
                        'generic_csv', 3, 'snapshot-3', NOW(), NOW());
                """);
        }

        injector.Release.Set();
        HttpResponseMessage reportResponse = await reportTask;
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        JsonElement report = await BodyAsync(reportResponse);
        Assert.Equal("ready", report.GetProperty("status").GetString());
        JsonElement current = report.GetProperty("current");
        Assert.Equal("250.00", current.GetProperty("money").GetProperty("money_in").GetString());
        Assert.Equal("100.00", current.GetProperty("money").GetProperty("money_out").GetString());
        Assert.Equal(2, current.GetProperty("money").GetProperty("included_count").GetInt64());
        decimal categoryOut = report.GetProperty("categories").GetProperty("money_out").GetProperty("ranks")
            .EnumerateArray()
            .Sum(rank => decimal.Parse(rank.GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(100m, categoryOut);
        Assert.Equal(0, current.GetProperty("hidden").GetProperty("count").GetInt64());
        AssertTrend(report, expenseId, "covered", "available", "100.00", "increase", "25.0");

        JsonElement fresh = await ReportAsync(client, session, body);
        Assert.Equal("0.00", fresh.GetProperty("current").GetProperty("money").GetProperty("money_in").GetString());
        Assert.Equal("0.00", fresh.GetProperty("current").GetProperty("money").GetProperty("money_out").GetString());
        Assert.Equal(0, fresh.GetProperty("current").GetProperty("money").GetProperty("included_count").GetInt64());
        Assert.Equal(3, fresh.GetProperty("current").GetProperty("hidden").GetProperty("count").GetInt64());
        AssertTrend(fresh, expenseId, "uncovered", "incomplete_coverage", "0.00", "incomplete_coverage", null);
    }

    private static void AssertTrend(JsonElement report,
                                    Guid expenseId,
                                    string coverage,
                                    string availability,
                                    string currentAmount,
                                    string state,
                                    string? percentage)
    {
        JsonElement trend = report.GetProperty("category_trend");
        JsonElement latest = trend.GetProperty("latest_comparison");
        Assert.Equal("2025-01-15", latest.GetProperty("current_period").GetProperty("end_date").GetString());
        Assert.Equal("2024-12-15", latest.GetProperty("prior_period").GetProperty("end_date").GetString());
        Assert.Equal(coverage, latest.GetProperty("current_coverage").GetString());
        Assert.Equal("covered", latest.GetProperty("prior_coverage").GetString());
        Assert.True(latest.GetProperty("has_prior_included_data").GetBoolean());
        Assert.Equal(availability, latest.GetProperty("availability").GetString());

        JsonElement category = Assert.Single(trend.GetProperty("categories").EnumerateArray());
        Assert.Equal(expenseId, category.GetProperty("id").GetGuid());
        JsonElement change = category.GetProperty("latest_change");
        Assert.Equal(currentAmount, change.GetProperty("current_amount").GetString());
        Assert.Equal("80.00", change.GetProperty("prior_amount").GetString());
        Assert.Equal(state, change.GetProperty("state").GetString());
        Assert.Equal(percentage, change.GetProperty("change_percentage").GetString());
    }

    private sealed class BarrierReportFaultInjector : IReportFaultInjector
    {
        public ManualResetEventSlim Reached { get; } = new(false);

        public ManualResetEventSlim Release { get; } = new(false);

        public void OnStep(ReportFaultPoints point)
        {
            if (point != ReportFaultPoints.AfterTotals)
            {
                return;
            }

            Reached.Set();
            Release.Wait(TimeSpan.FromSeconds(10));
        }
    }

    private static async Task<string> RegisterAndLoginAsync(HttpClient client, KitFactory factory, string email)
    {
        var password = TestEnvironment.GoodPassword;
        using (HttpResponseMessage register = await SendAsync(client, Post("/v1/register", Json(new { email, password }))))
        {
            Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
        }
        string token = factory.Mailer.LastToken("verification", email)!;
        using (HttpResponseMessage verify = await SendAsync(client, Post("/v1/verify", Json(new { token }))))
        {
            Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        }
        using HttpResponseMessage login = await SendAsync(client, Post("/v1/login", Json(new { email, password })));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return (await BodyAsync(login)).GetProperty("token").GetString()!;
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string session)
    {
        using HttpResponseMessage response = await SendAsync(client, Post("/v1/finance/accounts", Json(new { display_name = "Snapshot account" }), session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, string session, string name, string kind)
    {
        using HttpResponseMessage response = await SendAsync(client, Post("/v1/finance/categories", Json(new { display_name = name, kind }), session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReportAsync(HttpClient client, string session, object body)
    {
        using HttpResponseMessage response = await SendAsync(client, Post("/v1/finance/reports/query", Json(body), session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request) =>
        await client.SendAsync(request);

    private static HttpRequestMessage Get(string path, string? session = null) =>
        Request(HttpMethod.Get, path, null, session);

    private static HttpRequestMessage Post(string path, string json, string? session = null) =>
        Request(HttpMethod.Post, path, json, session);

    private static HttpRequestMessage Request(HttpMethod method, string path, string? json, string? session)
    {
        var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }
        request.Headers.Add("X-81e1a205-ec1c-4e03-b785-9748db1e7677-Application-Secret", TestEnvironment.ApiSecret);
        if (session is not null)
        {
            request.Headers.Add("X-81e1a205-ec1c-4e03-b785-9748db1e7677-Session", session);
        }
        return request;
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
