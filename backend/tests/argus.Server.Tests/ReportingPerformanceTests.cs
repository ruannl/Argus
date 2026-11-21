using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit.Abstractions;

namespace argus.Server.Tests;

/// <summary>
/// Reproducible 20,000-row Overview measurement. It is excluded from the
/// default suite and runs in the separate performance step.
/// </summary>
[Trait("Category", "Performance")]
public sealed class ReportingPerformanceTests(KitFactory factory, ITestOutputHelper output)
    : TransactionTestBase(factory)
{
    private const string ReportPath = "/v1/finance/reports/query";
    private const int Rows = 20_000;
    private const int Warmups = 3;
    private const int SamplesPerMode = 10;

    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task TwentyThousandRowReportsMeetTheP95Target()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (systemId, expenseId) = await DefaultCategoriesAsync(session);
        Guid incomeId = await CreateCategoryAsync(session, "Performance income", "Income");
        Guid transferId = await CreateCategoryAsync(session, "Performance transfers", "Transfer");
        Guid statementId = await SeedStatementAsync(customerId, accountId, label: "Performance coverage");
        await ExecuteSqlAsync($"""
            UPDATE statement_imports
            SET start_date = DATE '2022-01-01', end_date = DATE '2026-12-31'
            WHERE id = '{statementId}';
            """);

        var tagIds = new List<Guid>();
        for (int index = 0; index < 8; index++)
        {
            tagIds.Add(await SeedTagAsync(customerId, $"Performance tag {index}"));
        }
        Guid[] tags = tagIds.ToArray();

        var seedWatch = Stopwatch.StartNew();
        await ExecuteSqlParamsAsync(
            """
            INSERT INTO transactions (
                id, customer_id, financial_account_id, statement_import_id,
                transaction_date, description, normalized_description, amount,
                category_id, category_confirmed, source_type, source_page, source_row_number,
                identity_key, occurrence_index, is_hidden_by_user, version,
                created_at, updated_at)
            SELECT
                gen_random_uuid(), @customer_id, @account_id, @statement_id,
                DATE '2022-01-01' + (series % 1795),
                'Performance merchant ' || (series % 500),
                'performance merchant ' || (series % 500),
                CASE
                    WHEN series % 10 = 0 THEN (1000 + (series % 700))::numeric(18, 2)
                    WHEN series % 20 = 0 THEN (-(5 + (series % 90)))::numeric(18, 2)
                    ELSE (-(10 + (series % 400)))::numeric(18, 2)
                END,
                CASE
                    WHEN series % 10 = 0 THEN @income_id
                    WHEN series % 20 = 0 THEN @transfer_id
                    WHEN series % 25 = 0 THEN @system_id
                    ELSE @expense_id
                END,
                TRUE, 'fnb_csv', NULL, series,
                'perf-report-' || series, 1, FALSE, 1, NOW(), NOW()
            FROM generate_series(1, @count) AS series
            """,
            ("customer_id", customerId),
            ("account_id", accountId),
            ("statement_id", statementId),
            ("income_id", incomeId),
            ("transfer_id", transferId),
            ("system_id", systemId),
            ("expense_id", expenseId),
            ("count", Rows));

        await ExecuteSqlParamsAsync(
            """
            INSERT INTO transaction_merchant_tags (customer_id, transaction_id, merchant_tag_id)
            SELECT t.customer_id, t.id, tag.tag_id
            FROM transactions t
            JOIN unnest(@tag_ids) WITH ORDINALITY AS tag(tag_id, ord)
              ON tag.ord = (t.source_row_number % 8) + 1
            WHERE t.customer_id = @customer_id AND t.source_row_number % 3 = 0
            ON CONFLICT DO NOTHING
            """,
            ("customer_id", customerId),
            ("tag_ids", tags));

        await ExecuteSqlParamsAsync(
            """
            INSERT INTO transaction_merchant_tags (customer_id, transaction_id, merchant_tag_id)
            SELECT t.customer_id, t.id, tag.tag_id
            FROM transactions t
            JOIN unnest(@tag_ids) WITH ORDINALITY AS tag(tag_id, ord)
              ON tag.ord = ((t.source_row_number + 3) % 8) + 1
            WHERE t.customer_id = @customer_id AND t.source_row_number % 4 = 0
            ON CONFLICT DO NOTHING
            """,
            ("customer_id", customerId),
            ("tag_ids", tags));

        await ExecuteSqlParamsAsync(
            """
            INSERT INTO salary_cycle_state (customer_id, revision, updated_at)
            VALUES (@customer_id, 2, NOW());
            INSERT INTO salary_cycle_starts (id, customer_id, start_date, created_at)
            VALUES
                (gen_random_uuid(), @customer_id, DATE '2026-01-25', NOW()),
                (gen_random_uuid(), @customer_id, DATE '2026-02-24', NOW());
            """,
            ("customer_id", customerId));
        seedWatch.Stop();

        var modes = new (string Name, object Body)[]
        {
            ("salary_cycle", new { view = "overview" }),
            ("calendar_last_three", new
            {
                view = "overview",
                period = new { mode = "calendar", preset = "last_three_months" },
            }),
            ("custom_multi_year", new
            {
                view = "overview",
                period = new { mode = "custom", start_date = "2024-01-01", end_date = "2026-12-31" },
            }),
        };

        foreach (var (name, body) in modes)
        {
            for (int warmup = 0; warmup < Warmups; warmup++)
            {
                HttpResponseMessage response = await SendAsync(Post(ReportPath, Json(body), session: session));
                string responseBody = await response.Content.ReadAsStringAsync();
                Assert.True(
                    response.StatusCode == HttpStatusCode.OK,
                    $"{name} warmup returned {(int)response.StatusCode}: {responseBody}\n" +
                    string.Join("\n", Factory.LogCapture.Snapshot()));
            }
        }

        var measurements = new List<(string Mode, double Milliseconds, long Bytes)>();
        foreach (var (name, body) in modes)
        {
            for (int sample = 0; sample < SamplesPerMode; sample++)
            {
                var watch = Stopwatch.StartNew();
                HttpResponseMessage response = await SendAsync(Post(ReportPath, Json(body), session: session));
                string payload = await response.Content.ReadAsStringAsync();
                watch.Stop();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using JsonDocument document = JsonDocument.Parse(payload);
                Assert.Equal("ready", document.RootElement.GetProperty("status").GetString());
                measurements.Add((name, watch.Elapsed.TotalMilliseconds, payload.Length));
            }
        }

        await using var connection = await Factory.CreateDb().OpenAsync();
        string postgresVersion = (string)(await PersistenceTestSupport.ScalarAsync(connection, "SHOW server_version"))!;
        _output.WriteLine(
            $"REPORT_PERF fixture_rows={Rows} seeded_in_ms={seedWatch.Elapsed.TotalMilliseconds:F0} " +
            $"os={RuntimeInformation.OSDescription} framework={RuntimeInformation.FrameworkDescription} " +
            $"processors={Environment.ProcessorCount} postgres={postgresVersion} " +
            $"warmups_per_mode={Warmups} samples_per_mode={SamplesPerMode}");

        foreach (var group in measurements.GroupBy(item => item.Mode, StringComparer.Ordinal))
        {
            double[] sorted = group.Select(item => item.Milliseconds).Order().ToArray();
            double p50 = Percentile(sorted, 0.50);
            double p95 = Percentile(sorted, 0.95);
            _output.WriteLine(
                $"REPORT_PERF mode={group.Key} samples={sorted.Length} p50_ms={p50:F1} p95_ms={p95:F1} " +
                $"max_ms={sorted[^1]:F1} avg_bytes={group.Average(item => item.Bytes):F0}");
        }

        double[] all = measurements.Select(item => item.Milliseconds).Order().ToArray();
        double overallP50 = Percentile(all, 0.50);
        double overallP95 = Percentile(all, 0.95);
        _output.WriteLine(
            $"REPORT_PERF overall samples={all.Length} p50_ms={overallP50:F1} p95_ms={overallP95:F1} max_ms={all[^1]:F1}");

        Assert.True(
            overallP95 < 2000,
            $"Overview p95 {overallP95:F1} ms missed the 2000 ms target; record the measurement and correct the query plan.");
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

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
