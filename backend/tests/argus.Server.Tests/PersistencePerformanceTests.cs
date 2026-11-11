using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using argus.Server.Data;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlTypes;
using Xunit.Abstractions;

namespace argus.Server.Tests;

/// <summary>
/// Test-only before/after persistence measurement harness. It observes Npgsql
/// activities, not EF or application counters, so the same method can run on
/// the legacy implementation and the converted one.
/// </summary>
[Trait("Category", "Performance")]
public sealed class PersistencePerformanceTests(KitFactory factory, ITestOutputHelper output)
    : TransactionTestBase(factory)
{
    private const string ReviewsPath = "/v1/finance/imports/reviews";
    private const string QueryPath = "/v1/finance/transactions/query";
    private const int ReadRounds = 3;
    private const int ReadSamplesPerRound = 30;
    private const int WriteRounds = 3;
    private const int WriteSamplesPerRound = 10;
    private const int Warmups = 5;
    private const int SmallImportRows = 100;
    private const int MediumImportRows = 10_000;
    private const int LargeQueryRows = 20_000;
    private const int LeanBulkRows = 50_000;

    private static readonly object _genericImportConfig = new
    {
        format = "generic",
        header_line = 1,
        delimiter = ",",
        date_format = "yyyy-MM-dd",
        decimal_separator = ".",
        thousands_separator = "none",
        amount_mode = "signed",
        date_column = 0,
        description_column = 1,
        amount_column = 2,
        direction = "normal",
    };

    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task ActivitySourceCalibrationProbe()
    {
        await using var connection = await Factory.CreateDb().OpenAsync();
        using var counter = new NpgsqlActivityCounter();

        async Task CommandAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        await CommandAsync("SELECT 1");
        await CommandAsync("BEGIN");
        await CommandAsync("SAVEPOINT probe");
        await CommandAsync("SELECT 2");
        await CommandAsync("ROLLBACK TO SAVEPOINT probe");
        await CommandAsync("COMMIT");
        await CommandAsync("SELECT 3; SELECT 4;");
        await CommandAsync("CREATE TEMP TABLE activity_probe (value integer)");
        await using (var copy = await connection.BeginBinaryImportAsync("COPY activity_probe (value) FROM STDIN (FORMAT BINARY)"))
        {
            await copy.StartRowAsync();
            await copy.WriteAsync(1, NpgsqlDbType.Integer);
            await copy.CompleteAsync();
        }

        await Task.Delay(50);
        ActivityCounts counts = counter.Counts();
        Assert.Equal(1, counts.Begin);
        Assert.Equal(1, counts.Commit);
        Assert.Equal(1, counts.Rollback);
        Assert.Equal(1, counts.Savepoint);
        Assert.Equal(1, counts.Copy);
        Assert.Equal(2, counts.Select);
        Assert.Equal(1, counts.Batch);

        foreach (ActivitySnapshot item in counter.Snapshot())
        {
            string tags = string.Join(
                ", ",
                item.Tags.Select(tag => $"{tag.Key}={Truncate(tag.Value)}"));
            _output.WriteLine(
                $"CALIBRATION activity name={item.OperationName} source={item.SourceName} duration_ms={item.Duration.TotalMilliseconds:F3} tags=[{tags}]");
        }
        _output.WriteLine(
            $"CALIBRATION counts select={counts.Select} insert={counts.Insert} update={counts.Update} delete={counts.Delete} copy={counts.Copy} begin={counts.Begin} commit={counts.Commit} rollback={counts.Rollback} savepoint={counts.Savepoint} batch={counts.Batch} other={counts.Other} opens={counts.Opens}");
    }

    [Fact]
    public async Task BaselineReadPaths()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid categoryId = await CreateCategoryAsync(session, "Perf category");
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        Guid tagA = await CreateTagAsync(session, "Perf tag A");
        Guid tagB = await CreateTagAsync(session, "Perf tag B");
        await SeedPerformanceRowsAsync(customerId, accountId, statementId, expenseId, LargeQueryRows, 1);
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
            ("tag_ids", new[] { tagA, tagB }));

        Guid historyId = await SeedTransactionAsync(
            customerId,
            accountId,
            statementId,
            categoryId,
            new DateOnly(2023, 12, 20),
            "Bulk row historical",
            -9.99m,
            sourceRowNumber: 100_000,
            tagIds: [tagA],
            version: 1);
        await ExecuteSqlParamsAsync(
            """
            INSERT INTO merchant_aliases (
                id, customer_id, display_alias, normalized_alias, merchant_tag_id, category_id,
                created_at, updated_at)
            VALUES (@id, @customer_id, @alias, @normalized, @tag_id, @category_id, NOW(), NOW())
            """,
            ("id", Guid.NewGuid()),
            ("customer_id", customerId),
            ("alias", "Bulk row alias"),
            ("normalized", "bulk row alias"),
            ("tag_id", tagA),
            ("category_id", categoryId));
        await ExecuteSqlAsync($"UPDATE transactions SET category_confirmed = TRUE WHERE id = '{historyId}'");

        var fanOutTags = new List<Guid> { tagA, tagB };
        for (var index = 0; index < 18; index++)
        {
            fanOutTags.Add(await CreateTagAsync(session, $"Perf fan tag {index:D2}"));
        }
        for (var index = 0; index < fanOutTags.Count; index++)
        {
            await SeedTransactionAsync(
                customerId,
                accountId,
                statementId,
                categoryId,
                new DateOnly(2023, 11, 1).AddDays(index),
                $"Bulk row fan {index:D2}",
                -(20m + index),
                sourceRowNumber: 100_001 + index,
                tagIds: [fanOutTags[index]],
                version: 1);
        }
        await ExecuteSqlAsync($"UPDATE transactions SET category_confirmed = TRUE WHERE customer_id = '{customerId}' AND source_row_number >= 100_001");

        Guid detailId = Guid.Parse((await StringScalarAsync(
            "SELECT id::text FROM transactions WHERE customer_id = '" + customerId + "' ORDER BY source_row_number LIMIT 1"))!);

        using var counter = new NpgsqlActivityCounter();
        var auth = new List<PerformanceSample>();
        var accounts = new List<PerformanceSample>();
        var categories = new List<PerformanceSample>();
        var query25 = new List<PerformanceSample>();
        var query100 = new List<PerformanceSample>();
        var search = new List<PerformanceSample>();
        var detail = new List<PerformanceSample>();
        var imports = new List<PerformanceSample>();

        for (var warmup = 1; warmup <= Warmups; warmup++)
        {
            string warmIp = SampleIp(0, warmup);
            await SendAsync(Get("/v1/me", session: session));
            await SendAsync(FinanceGet("/v1/finance/accounts", session, warmIp));
            await SendAsync(FinanceGet("/v1/finance/categories", session, warmIp));
            await SendAsync(FinancePost(QueryPath, Json(new { page = 1, page_size = 25 }), session, warmIp));
            await SendAsync(FinancePost(QueryPath, Json(new { page = 1, page_size = 100 }), session, warmIp));
            await SendAsync(FinancePost(QueryPath, Json(new { filters = new { search = "bulk row 19999" }, page = 1, page_size = 25 }), session, warmIp));
            await SendAsync(FinanceGet($"/v1/finance/transactions/{detailId}", session, warmIp));
            await SendAsync(FinanceGet("/v1/finance/imports?limit=50", session, warmIp));
        }

        for (var detailWarmup = 0; detailWarmup < 40; detailWarmup++)
        {
            await SendAsync(FinanceGet($"/v1/finance/transactions/{detailId}", session, SampleIp(0, detailWarmup % 30 + 1)));
        }

        for (var round = 1; round <= ReadRounds; round++)
        {
            for (var index = 1; index <= ReadSamplesPerRound; index++)
            {
                auth.Add(await MeasureCallAsync("auth.me", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(Get("/v1/me", session: session));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK);
                }));

                string ip = SampleIp(round, index);
                accounts.Add(await MeasureCallAsync("finance.accounts", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinanceGet("/v1/finance/accounts", session, ip));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK && body.GetProperty("accounts").GetArrayLength() >= 1);
                }));
                categories.Add(await MeasureCallAsync("finance.categories", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinanceGet("/v1/finance/categories", session, ip));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK && body.GetProperty("categories").GetArrayLength() >= 20);
                }));
                query25.Add(await MeasureCallAsync("transactions.query25", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePost(QueryPath, Json(new { page = 1, page_size = 25 }), session, ip));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK && body.GetProperty("transactions").GetArrayLength() == 25);
                }));
                query100.Add(await MeasureCallAsync("transactions.query100", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePost(QueryPath, Json(new { page = 1, page_size = 100 }), session, ip));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK && body.GetProperty("transactions").GetArrayLength() == 100);
                }));
                search.Add(await MeasureCallAsync("transactions.search", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePost(
                        QueryPath,
                        Json(new { filters = new { search = "bulk row 19999" }, page = 1, page_size = 25 }),
                        session,
                        ip));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK && body.GetProperty("filtered_total").GetInt64() >= 1);
                }));
                detail.Add(await MeasureCallAsync("transactions.detail", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinanceGet($"/v1/finance/transactions/{detailId}", session, ip));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK);
                }));
                imports.Add(await MeasureCallAsync("imports.history", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinanceGet("/v1/finance/imports?limit=50", session, ip));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK);
                }));
            }
        }

        Emit(auth);
        Emit(accounts);
        Emit(categories);
        Emit(query25);
        Emit(query100);
        Emit(search);
        Emit(detail);
        Emit(imports);
        Assert.All(auth.Concat(accounts).Concat(categories).Concat(query25).Concat(query100).Concat(search).Concat(detail).Concat(imports), sample => Assert.True(sample.Correct));
    }

    [Fact]
    public async Task BaselineSmallImportLifecycle()
    {
        using var counter = new NpgsqlActivityCounter();
        var create = new List<PerformanceSample>();
        var patch = new List<PerformanceSample>();
        var refresh = new List<PerformanceSample>();
        var commit = new List<PerformanceSample>();
        var replay = new List<PerformanceSample>();
        var customers = new List<(string Session, Guid CustomerId, Guid AccountId)>();
        for (var customer = 0; customer < 3; customer++)
        {
            var (session, customerId) = await NewCustomerAsync();
            Guid accountId = await CreateAccountAsync(session, $"Perf small {customer}");
            customers.Add((session, customerId, accountId));
        }

        for (var round = 1; round <= WriteRounds; round++)
        {
            for (var index = 1; index <= WriteSamplesPerRound; index++)
            {
                (string session, Guid customerId, Guid accountId) = customers[(round * WriteSamplesPerRound + index) % customers.Count];
                string ip = SampleIp(round, index);
                byte[] csv = BuildGenericCsv(SmallImportRows, round * 100 + index);
                JsonElement review = default;
                create.Add(await MeasureCallAsync("import.small.create", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(MultipartPost(
                        ReviewsPath,
                        csv,
                        Json(new
                        {
                            financial_account_id = accountId,
                            source_kind = "generic_csv",
                            parser_config = _genericImportConfig,
                            start_date = "2025-01-01",
                            end_date = "2025-01-31",
                            currency = "ZAR",
                            confirm_zar = true,
                        }),
                        session: session));
                    JsonElement body = await BodyAsync(response);
                    review = body;
                    return (response, body, response.StatusCode == HttpStatusCode.Created && Version(body) == 1);
                }));

                Guid rowId = Rows(review)[0].GetProperty("row_id").GetGuid();
                JsonElement patched = default;
                patch.Add(await MeasureCallAsync("import.small.patch", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePatch(
                        $"{ReviewsPath}/{review.GetProperty("summary").GetProperty("id").GetGuid()}",
                        Json(new { expected_review_version = Version(review), decisions = new[] { new { row_id = rowId, skip = true } } }),
                        session,
                        ip));
                    JsonElement body = await BodyAsync(response);
                    patched = body;
                    return (response, body, response.StatusCode == HttpStatusCode.OK && Version(body) == 2);
                }));

                JsonElement refreshed = default;
                refresh.Add(await MeasureCallAsync("import.small.refresh", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePost(
                        $"{ReviewsPath}/{review.GetProperty("summary").GetProperty("id").GetGuid()}/refresh",
                        Json(new { expected_review_version = Version(patched) }),
                        session,
                        ip));
                    JsonElement body = await BodyAsync(response);
                    refreshed = body;
                    return (response, body, response.StatusCode == HttpStatusCode.OK && Version(body) == 3);
                }));

                commit.Add(await MeasureCallAsync("import.small.commit", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePost(
                        $"{ReviewsPath}/{review.GetProperty("summary").GetProperty("id").GetGuid()}/commit",
                        Json(CommitBody(refreshed)),
                        session,
                        ip));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.Created && body.GetProperty("replayed").GetBoolean() == false);
                }));

                replay.Add(await MeasureCallAsync("import.small.replay", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePost(
                        $"{ReviewsPath}/{review.GetProperty("summary").GetProperty("id").GetGuid()}/commit",
                        Json(CommitBody(refreshed)),
                        session,
                        ip));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK && body.GetProperty("replayed").GetBoolean());
                }));
            }
        }

        Emit(create);
        Emit(patch);
        Emit(refresh);
        Emit(commit);
        Emit(replay);
        Assert.All(create.Concat(patch).Concat(refresh).Concat(commit).Concat(replay), sample => Assert.True(sample.Correct));
    }

    [Fact]
    public async Task BaselineDenseDuplicateReviewRefresh()
    {
        using var counter = new NpgsqlActivityCounter();
        var refresh = new List<PerformanceSample>();
        for (var round = 1; round <= WriteRounds; round++)
        {
            var (session, _) = await NewCustomerAsync();
            Guid accountId = await CreateAccountAsync(session);
            JsonElement review = await CreateReviewAsync(session, accountId, BuildDenseGenericCsv(400), "generic_csv");
            Guid reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
            int version = Version(review);
            for (var index = 1; index <= WriteSamplesPerRound; index++)
            {
                int expected = version;
                refresh.Add(await MeasureCallAsync("import.dense.refresh", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePost(
                        $"{ReviewsPath}/{reviewId}/refresh",
                        Json(new { expected_review_version = expected }),
                        session,
                        SampleIp(round, index)));
                    JsonElement body = await BodyAsync(response);
                    version = Version(body);
                    return (response, body, response.StatusCode == HttpStatusCode.OK);
                }));
            }
        }
        Emit(refresh);
        Assert.All(refresh, sample => Assert.True(sample.Correct));
    }

    [Fact]
    public async Task BaselineSingleEditAndConfirmation()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        Guid otherCategory = await CreateCategoryAsync(session, "Perf second category");
        Guid statementId = await SeedStatementAsync(customerId, accountId);
        Guid transactionId = await SeedTransactionAsync(
            customerId,
            accountId,
            statementId,
            expenseId,
            new DateOnly(2025, 1, 5),
            "Single edit performance row",
            -12.34m,
            categoryConfirmed: false);

        using var counter = new NpgsqlActivityCounter();
        var edit = new List<PerformanceSample>();
        var confirm = new List<PerformanceSample>();
        for (var round = 1; round <= WriteRounds; round++)
        {
            for (var index = 1; index <= WriteSamplesPerRound; index++)
            {
                await ExecuteSqlAsync(
                    $"UPDATE transactions SET category_id = '{expenseId}', category_confirmed = FALSE, version = 1 WHERE id = '{transactionId}'");
                edit.Add(await MeasureCallAsync("transactions.edit", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePatch(
                        $"/v1/finance/transactions/{transactionId}",
                        Json(new { expected_version = 1, changes = new { category_id = otherCategory } }),
                        session,
                        SampleIp(round, index)));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK && body.GetProperty("changed_count").GetInt32() == 1);
                }));

                await ExecuteSqlAsync(
                    $"UPDATE transactions SET category_id = '{expenseId}', category_confirmed = FALSE, version = 1 WHERE id = '{transactionId}'");
                confirm.Add(await MeasureCallAsync("transactions.confirm", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePatch(
                        $"/v1/finance/transactions/{transactionId}",
                        Json(new { expected_version = 1, changes = new { confirm_category = true } }),
                        session,
                        SampleIp(round, index)));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK && body.GetProperty("changed_count").GetInt32() == 1);
                }));
            }
        }
        Emit(edit);
        Emit(confirm);
        Assert.All(edit.Concat(confirm), sample => Assert.True(sample.Correct));
    }

    private async Task<PerformanceSample> MeasureCallAsync(string scenario,
                                                           int round,
                                                           int index,
                                                           NpgsqlActivityCounter counter,
                                                           Func<Task<(HttpResponseMessage Response, JsonElement? Body, bool Correct)>> call)
    {
        counter.Reset();
        Stopwatch watch = Stopwatch.StartNew();
        (HttpResponseMessage response, JsonElement? body, bool correct) = await call();
        watch.Stop();
        ActivityCounts counts = counter.Counts();
        response.Dispose();
        return new PerformanceSample(scenario, round, index, watch.Elapsed.TotalMilliseconds, counts, correct);
    }

    private void Emit(IReadOnlyList<PerformanceSample> samples)
    {
        foreach (IGrouping<string, PerformanceSample> scenario in samples.GroupBy(sample => sample.Scenario))
        {
            _output.WriteLine(PerformanceSummary.Format(scenario.Key, scenario.ToArray()));
            foreach (IGrouping<int, PerformanceSample> round in scenario.GroupBy(sample => sample.Round))
            {
                _output.WriteLine(PerformanceSummary.Format($"{scenario.Key}.round{round.Key}", round.ToArray()));
            }
        }
    }

    [Fact]
    public async Task BaselineMediumImportCommitAndReplay()
    {
        using var counter = new NpgsqlActivityCounter();
        var create = new List<PerformanceSample>();
        var commit = new List<PerformanceSample>();
        var replay = new List<PerformanceSample>();
        var customers = new List<(string Session, Guid CustomerId, Guid AccountId)>();
        for (var customer = 0; customer < 4; customer++)
        {
            var (session, customerId) = await NewCustomerAsync();
            Guid accountId = await CreateAccountAsync(session, $"Perf medium {customer}");
            customers.Add((session, customerId, accountId));
        }

        for (var round = 1; round <= WriteRounds; round++)
        {
            for (var index = 1; index <= WriteSamplesPerRound; index++)
            {
                (string session, Guid customerId, Guid accountId) = customers[(round * WriteSamplesPerRound + index) % customers.Count];
                byte[] csv = BuildGenericCsv(MediumImportRows, round * 100 + index);
                JsonElement review = default;
                create.Add(await MeasureCallAsync("import.medium.create", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(MultipartPost(
                        ReviewsPath,
                        csv,
                        Json(new
                        {
                            financial_account_id = accountId,
                            source_kind = "generic_csv",
                            parser_config = _genericImportConfig,
                            start_date = "2025-01-01",
                            end_date = "2025-01-31",
                            currency = "ZAR",
                            confirm_zar = true,
                        }),
                        session: session));
                    JsonElement body = await BodyAsync(response);
                    review = body;
                    if (response.StatusCode != HttpStatusCode.Created)
                    {
                        _output.WriteLine($"MEDIUM CREATE FAILED status={(int)response.StatusCode} body={body}");
                    }
                    return (response, body, response.StatusCode == HttpStatusCode.Created && body.GetProperty("summary").GetProperty("imported_count").GetInt32() == MediumImportRows);
                }));

                Guid reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
                string ip = SampleIp(round + 3, index);
                commit.Add(await MeasureCallAsync("import.medium.commit", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePost(
                        $"{ReviewsPath}/{reviewId}/commit",
                        Json(CommitBody(review)),
                        session,
                        ip));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.Created && body.GetProperty("statement").GetProperty("imported_count").GetInt32() == MediumImportRows);
                }));

                replay.Add(await MeasureCallAsync("import.medium.replay", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePost(
                        $"{ReviewsPath}/{reviewId}/commit",
                        Json(CommitBody(review)),
                        session,
                        ip));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK && body.GetProperty("replayed").GetBoolean());
                }));
            }
        }

        Emit(create);
        Emit(commit);
        Emit(replay);
        Assert.All(create.Concat(commit).Concat(replay), sample => Assert.True(sample.Correct));
    }

    [Fact]
    public async Task BaselineLeanFiftyThousandMemberBulk()
    {
        using var counter = new NpgsqlActivityCounter();
        var prepare = new List<PerformanceSample>();
        var apply = new List<PerformanceSample>();
        var replay = new List<PerformanceSample>();
        var customers = new List<(string Session, Guid CustomerId, Guid AccountId)>();
        for (var customer = 0; customer < 3; customer++)
        {
            var (session, customerId) = await NewCustomerAsync();
            Guid accountId = await CreateAccountAsync(session, $"Perf bulk {customer}");
            Guid statementId = await SeedStatementAsync(customerId, accountId, label: $"Perf bulk {customer}");
            var (_, expenseId) = await DefaultCategoriesAsync(session);
            await SeedPerformanceRowsAsync(customerId, accountId, statementId, expenseId, LeanBulkRows, 1);
            customers.Add((session, customerId, accountId));
        }

        for (var round = 1; round <= WriteRounds; round++)
        {
            for (var index = 1; index <= WriteSamplesPerRound; index++)
            {
                (string session, Guid customerId, Guid accountId) = customers[index % customers.Count];
                Guid operationId = Guid.NewGuid();
                JsonElement operation = default;
                prepare.Add(await MeasureCallAsync("bulk.50k.prepare", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePost(
                        "/v1/finance/transactions/operations/prepare",
                        Json(new
                        {
                            operation_id = operationId,
                            selection = new { mode = "all_matching", filters = new { visibility = "all" } },
                            changes = new { is_hidden_by_user = true },
                        }),
                        session,
                        SampleIp(round + 6, index)));
                    JsonElement body = await BodyAsync(response);
                    operation = body.GetProperty("operation");
                    return (response, body, response.StatusCode == HttpStatusCode.Created && operation.GetProperty("selected_count").GetInt32() == LeanBulkRows);
                }));

                apply.Add(await MeasureCallAsync("bulk.50k.apply", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePost(
                        $"/v1/finance/transactions/operations/{operationId}/apply",
                        Json(new
                        {
                            confirmation_hash = operation.GetProperty("confirmation_hash").GetString(),
                            expected_selected_count = operation.GetProperty("selected_count").GetInt32(),
                            confirm = true,
                        }),
                        session,
                        SampleIp(round + 6, index)));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK && body.GetProperty("changed_count").GetInt32() == LeanBulkRows);
                }));

                replay.Add(await MeasureCallAsync("bulk.50k.replay", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(FinancePost(
                        $"/v1/finance/transactions/operations/{operationId}/apply",
                        Json(new
                        {
                            confirmation_hash = operation.GetProperty("confirmation_hash").GetString(),
                            expected_selected_count = operation.GetProperty("selected_count").GetInt32(),
                            confirm = true,
                        }),
                        session,
                        SampleIp(round + 6, index)));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK && body.GetProperty("replayed").GetBoolean());
                }));

                await ExecuteSqlParamsAsync(
                    "UPDATE transactions SET is_hidden_by_user = FALSE, version = 1 WHERE customer_id = @customer_id",
                    ("customer_id", customerId));
            }
        }

        Emit(prepare);
        Emit(apply);
        Emit(replay);
        Assert.All(prepare.Concat(apply).Concat(replay), sample => Assert.True(sample.Correct));
    }

    [Fact]
    public async Task BaselineCleanupServices()
    {
        var (session, customerId) = await NewCustomerAsync();
        Guid accountId = await CreateAccountAsync(session);
        using var counter = new NpgsqlActivityCounter();
        var importCleanup = new List<PerformanceSample>();
        var transactionCleanup = new List<PerformanceSample>();
        StatementImportStore importStore = Factory.Services.GetRequiredService<StatementImportStore>();
        TransactionStore transactionStore = Factory.Services.GetRequiredService<TransactionStore>();

        for (var round = 1; round <= WriteRounds; round++)
        {
            for (var index = 1; index <= WriteSamplesPerRound; index++)
            {
                await ExecuteSqlParamsAsync(
                    """
                    INSERT INTO import_reviews (
                        id, customer_id, financial_account_id, source_kind, parser_config,
                        expires_at, account_import_version, duplicate_policy_version,
                        statement_fingerprint, summary_hash, total_rows, imported_count,
                        suppressed_count, skipped_count, possible_duplicate_count,
                        exact_override_count, payload_bytes, created_at, updated_at)
                    SELECT gen_random_uuid(), @customer_id, @account_id, 'generic_csv',
                           '{}'::jsonb, NOW() - INTERVAL '1 hour', 0, 1,
                           'cleanup-' || series, 'hash', 0, 0, 0, 0, 0, 0, 0, NOW(), NOW()
                    FROM generate_series(1, 100) AS series
                    """,
                    ("customer_id", customerId),
                    ("account_id", accountId));
                await ExecuteSqlParamsAsync(
                    """
                    INSERT INTO transaction_operations (
                        customer_id, operation_id, status, selection_mode, request_digest,
                        filter_digest, confirmation_hash, action, target_stamps,
                        selected_count, changed_count, unchanged_count,
                        captured_at, expires_at, created_at, updated_at)
                    SELECT @customer_id, gen_random_uuid(), 'Prepared', 'all_matching',
                           'cleanup-digest', 'cleanup-filter', 'cleanup-hash',
                           '{"category_id":null,"add_tag_ids":[],"remove_tag_ids":[],"is_hidden_by_user":true}'::jsonb,
                           '{}'::jsonb, 0, 0, 0, NOW() - INTERVAL '1 hour',
                           NOW() - INTERVAL '1 minute', NOW(), NOW()
                    FROM generate_series(1, 100)
                    """,
                    ("customer_id", customerId));

                importCleanup.Add(await MeasureCallAsync("cleanup.import", round, index, counter, async () =>
                {
                    await importStore.CleanupExpiredAsync();
                    return (new HttpResponseMessage(HttpStatusCode.OK), null, true);
                }));
                transactionCleanup.Add(await MeasureCallAsync("cleanup.transactions", round, index, counter, async () =>
                {
                    await transactionStore.CleanupExpiredAsync();
                    return (new HttpResponseMessage(HttpStatusCode.OK), null, true);
                }));
            }
        }

        Emit(importCleanup);
        Emit(transactionCleanup);
        Assert.All(importCleanup.Concat(transactionCleanup), sample => Assert.True(sample.Correct));
    }

    [Fact]
    public async Task BaselineLoginAuthentication()
    {
        var users = new List<(string Email, string Password)>();
        for (var index = 0; index < 4; index++)
        {
            string email = UniqueEmail();
            await RegisterAsync(email);
            var token = Factory.Mailer.LastToken("verification", email)!;
            await VerifyAsync(token);
            users.Add((email, TestEnvironment.GoodPassword));
        }

        using var counter = new NpgsqlActivityCounter();
        var login = new List<PerformanceSample>();
        for (var round = 1; round <= WriteRounds; round++)
        {
            for (var index = 1; index <= WriteSamplesPerRound; index++)
            {
                (string email, string password) = users[(round * WriteSamplesPerRound + index) % users.Count];
                login.Add(await MeasureCallAsync("auth.login", round, index, counter, async () =>
                {
                    HttpResponseMessage response = await SendAsync(Post("/v1/login", Json(new { email, password })));
                    JsonElement body = await BodyAsync(response);
                    return (response, body, response.StatusCode == HttpStatusCode.OK && body.GetProperty("token").GetString()?.Length > 0);
                }));
            }
        }
        Emit(login);
        Assert.All(login, sample => Assert.True(sample.Correct));
    }

    private async Task<Guid> CreateCategoryAsync(string session, string name)
    {
        var response = await SendAsync(Post(
            "/v1/finance/categories",
            Json(new { display_name = name, kind = "Expense" }),
            session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CreateReviewAsync(string session, Guid accountId, byte[] csv, string sourceKind)
    {
        var response = await SendAsync(MultipartPost(
            ReviewsPath,
            csv,
            Json(new
            {
                financial_account_id = accountId,
                source_kind = sourceKind,
                parser_config = _genericImportConfig,
                start_date = "2025-01-01",
                end_date = "2025-01-31",
                currency = "ZAR",
                confirm_zar = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await BodyAsync(response);
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
                category_id, source_type, source_page, source_row_number,
                identity_key, occurrence_index, is_hidden_by_user, version,
                created_at, updated_at)
            SELECT
                gen_random_uuid(), @customer_id, @account_id, @statement_id,
                DATE '2024-01-01' + (series % 300),
                'Bulk row ' || series,
                'bulk row ' || series,
                (-(1 + (series % 100)))::numeric(18, 2),
                @category_id, 'fnb_csv', NULL, series,
                'perf-' || series, 1, FALSE, 1, NOW(), NOW()
            FROM generate_series(@first_row, @first_row + @count - 1) AS series
            """,
            ("customer_id", customerId),
            ("account_id", accountId),
            ("statement_id", statementId),
            ("category_id", categoryId),
            ("first_row", firstRow),
            ("count", count));
    }

    private static byte[] BuildGenericCsv(int rows, int seed = 0)
    {
        StringBuilder builder = new("Date,Description,Amount\n");
        for (var index = 0; index < rows; index++)
        {
            DateOnly date = new(2025, 1, 1);
            date = date.AddDays(index % 28);
            builder.Append(date.ToString("yyyy-MM-dd"))
                .Append(",Perf ")
                .Append(seed)
                .Append(" merchant ")
                .Append(index.ToString("D5"))
                .Append(",-")
                .Append((seed * 100_000 + index + 1).ToString())
                .Append(".00\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildDenseGenericCsv(int rows)
    {
        StringBuilder builder = new("Date,Description,Amount\n");
        for (var index = 0; index < rows; index += 2)
        {
            string line = "2025-01-15,Dense merchant," + (-(10 + index / 2)).ToString() + ".00\n";
            builder.Append(line).Append(line);
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static int Version(JsonElement review) =>
        (review.TryGetProperty("summary", out JsonElement summary) ? summary : review)
        .GetProperty("version").GetInt32();

    private static string Hash(JsonElement review) =>
        (review.TryGetProperty("summary", out JsonElement summary) ? summary : review)
        .GetProperty("summary_hash").GetString()!;

    private static JsonElement[] Rows(JsonElement review) =>
        review.GetProperty("page").GetProperty("rows").EnumerateArray().ToArray();

    private static object CommitBody(JsonElement review) => new
    {
        expected_review_version = Version(review),
        confirmed_summary_hash = Hash(review),
    };

    private static string SampleIp(int round, int index) => $"10.{round}.0.{index}";

    private static HttpRequestMessage FinanceGet(string path, string session, string ip)
    {
        HttpRequestMessage request = Get(path, session: session);
        request.Headers.Add("X-Test-Remote-Ip", ip);
        return request;
    }

    private static HttpRequestMessage FinancePost(string path, string body, string session, string ip)
    {
        HttpRequestMessage request = Post(path, body, session: session);
        request.Headers.Add("X-Test-Remote-Ip", ip);
        return request;
    }

    private static HttpRequestMessage FinancePatch(string path, string body, string session, string ip)
    {
        HttpRequestMessage request = Patch(path, body, session: session);
        request.Headers.Add("X-Test-Remote-Ip", ip);
        return request;
    }

    private static string Truncate(string? value) =>
        value is null ? "<null>" : value.Length <= 80 ? value : value[..77] + "...";
}
