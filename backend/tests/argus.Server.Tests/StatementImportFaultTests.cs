using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using argus.Server.Imports;

namespace argus.Server.Tests;

public sealed class StatementImportFaultTests
{
    [Theory]
    [InlineData("statement_inserted")]
    [InlineData("transactions_inserted")]
    [InlineData("tag_links_inserted")]
    [InlineData("receipt_inserted")]
    [InlineData("before_commit")]
    public async Task FaultBeforeCommitLeavesNoDurableEffectsAndRetrySucceeds(string step)
    {
        var injector = new FaultInjector();
        using var factory = new KitFactory(injector);
        using var client = factory.CreateClient();
        await ResetAsync(factory);
        var session = await NewSessionAsync(factory, client);
        var accountId = await CreateAccountAsync(client, session);
        var file = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: false).Bytes;
        var review = await PrepareAsync(client, session, accountId, file);
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        if (step == "tag_links_inserted")
        {
            // Reach the seam with a real selected tag so its rollback is
            // proved, not only the absence of a link that was never requested.
            var tagResponse = await client.SendAsync(Request(
                HttpMethod.Post, "/v1/finance/merchant-tags",
                JsonSerializer.Serialize(new { display_name = "Woolworths" }), session));
            Assert.Equal(HttpStatusCode.Created, tagResponse.StatusCode);
            var tagId = (await BodyAsync(tagResponse)).GetProperty("id").GetGuid();
            var rowId = review.GetProperty("page").GetProperty("rows")[0]
                .GetProperty("row_id").GetGuid();
            var patch = await client.SendAsync(Request(
                HttpMethod.Patch,
                $"/v1/finance/imports/reviews/{reviewId}",
                JsonSerializer.Serialize(new
                {
                    expected_review_version = review.GetProperty("summary")
                        .GetProperty("version").GetInt32(),
                    decisions = new[]
                    {
                        new { row_id = rowId, selected_tag_ids = new[] { tagId } },
                    },
                }),
                session));
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
            review = await BodyAsync(patch);
        }

        injector.FailOn = step;
        var failed = await CommitAsync(client, session, reviewId, review);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(0L, await CountAsync(factory, "statement_imports"));
        Assert.Equal(0L, await CountAsync(factory, "transactions"));
        Assert.Equal(0L, await CountAsync(factory, "import_commit_receipts"));
        Assert.Equal(0L, await CountAsync(factory, "transaction_merchant_tags"));
        Assert.Equal(1L, await CountAsync(factory, "import_reviews"));

        injector.FailOn = null;
        var retried = await CommitAsync(client, session, reviewId, review);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Equal(1L, await CountAsync(factory, "statement_imports"));
        Assert.Equal(3L, await CountAsync(factory, "transactions"));
        Assert.Equal(1L, await CountAsync(factory, "import_commit_receipts"));
        Assert.Equal(step == "tag_links_inserted" ? 1L : 0L,
            await CountAsync(factory, "transaction_merchant_tags"));
        Assert.Equal(0L, await CountAsync(factory, "import_reviews"));
    }

    [Fact]
    public async Task FaultAfterCommitBeforeResponseReplaysTheSameReceipt()
    {
        var injector = new FaultInjector();
        using var factory = new KitFactory(injector);
        using var client = factory.CreateClient();
        await ResetAsync(factory);
        var session = await NewSessionAsync(factory, client);
        var accountId = await CreateAccountAsync(client, session);
        var file = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: false).Bytes;
        var review = await PrepareAsync(client, session, accountId, file);
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();

        injector.FailOn = "after_commit_before_response";
        var lost = await CommitAsync(client, session, reviewId, review);
        Assert.Equal(HttpStatusCode.InternalServerError, lost.StatusCode);
        Assert.Equal(1L, await CountAsync(factory, "statement_imports"));
        Assert.Equal(3L, await CountAsync(factory, "transactions"));
        Assert.Equal(1L, await CountAsync(factory, "import_commit_receipts"));
        Assert.Equal(0L, await CountAsync(factory, "import_reviews"));

        injector.FailOn = null;
        var replay = await CommitAsync(client, session, reviewId, review);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await BodyAsync(replay);
        Assert.True(replayBody.GetProperty("replayed").GetBoolean());
        Assert.Equal(1L, await CountAsync(factory, "statement_imports"));
        Assert.Equal(3L, await CountAsync(factory, "transactions"));
    }

    [Theory]
    [InlineData("before_commit", "tagged")]
    [InlineData("before_commit", "no_tags")]
    [InlineData("before_commit", "suppressed")]
    [InlineData("tags_validated", "no_tags")]
    [InlineData("tags_validated", "suppressed")]
    public async Task ExpiryAtCommitSeamsRollsBackEveryEffect(string pauseStep, string scenario)
    {
        using var injector = new PausingFaultInjector();
        using var factory = new KitFactory(injector);
        using var client = factory.CreateClient();
        await ResetAsync(factory);
        var session = await NewSessionAsync(factory, client);
        var accountId = await CreateAccountAsync(client, session);
        var file = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: false).Bytes;

        if (scenario == "suppressed")
        {
            var first = await PrepareAsync(client, session, accountId, file);
            var firstId = first.GetProperty("summary").GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.Created, (await CommitAsync(client, session, firstId, first)).StatusCode);
        }

        var review = await PrepareAsync(client, session, accountId, file);
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        if (scenario == "tagged")
        {
            var tag = await client.SendAsync(Request(HttpMethod.Post, "/v1/finance/merchant-tags",
                JsonSerializer.Serialize(new { display_name = "Expiry tag" }), session));
            Assert.Equal(HttpStatusCode.Created, tag.StatusCode);
            var tagId = (await BodyAsync(tag)).GetProperty("id").GetGuid();
            var rowId = review.GetProperty("page").GetProperty("rows")[0].GetProperty("row_id").GetGuid();
            var patch = await client.SendAsync(Request(HttpMethod.Patch,
                $"/v1/finance/imports/reviews/{reviewId}", JsonSerializer.Serialize(new
                {
                    expected_review_version = review.GetProperty("summary").GetProperty("version").GetInt32(),
                    decisions = new[] { new { row_id = rowId, selected_tag_ids = new[] { tagId } } },
                }), session));
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
            review = await BodyAsync(patch);
        }
        else if (scenario == "suppressed")
        {
            Assert.Equal(0, review.GetProperty("summary").GetProperty("imported_count").GetInt32());
            var patch = await client.SendAsync(Request(HttpMethod.Patch,
                $"/v1/finance/imports/reviews/{reviewId}", JsonSerializer.Serialize(new
                {
                    expected_review_version = review.GetProperty("summary").GetProperty("version").GetInt32(),
                    decisions = Array.Empty<object>(),
                    statement_override = true,
                }), session));
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
            review = await BodyAsync(patch);
        }

        var deadline = await ImportExpiryTestHelpers.SetDeadlineAsync(factory, reviewId);
        var before = await ImportSnapshotAsync(factory, accountId);
        factory.LogCapture.Clear();
        injector.PauseAt = pauseStep;
        var commit = Task.Run(() => CommitAsync(client, session, reviewId, review));
        await using var observer = await factory.CreateDb().OpenAsync();
        HttpResponseMessage? result = null;
        try
        {
            await injector.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(commit.IsCompleted);
            if (pauseStep == "before_commit")
            {
                Assert.Contains("statement_inserted", injector.Steps);
                Assert.Contains("transactions_inserted", injector.Steps);
                Assert.Contains("tag_links_inserted", injector.Steps);
                Assert.Contains("receipt_inserted", injector.Steps);
            }
            await using (var stillValid = observer.CreateCommand())
            {
                stillValid.CommandText = "SELECT clock_timestamp() < @deadline";
                stillValid.Parameters.AddWithValue("deadline", deadline);
                Assert.Equal(true, await stillValid.ExecuteScalarAsync());
            }
            await ImportExpiryTestHelpers.WaitForDeadlineAsync(observer, deadline);
        }
        finally
        {
            injector.Release.Set();
            result = await commit.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
        Assert.Equal("review_expired", (await BodyAsync(result)).GetProperty("error").GetString());
        Assert.Equal(before, await ImportSnapshotAsync(factory, accountId));
        Assert.False(factory.LogCapture.Contains("audit.import_committed"));
    }

    [Fact]
    public async Task CommittedReceiptReplaysAfterOriginalDeadlineInNewHost()
    {
        using var factory = new KitFactory();
        using var client = factory.CreateClient();
        await ResetAsync(factory);
        var session = await NewSessionAsync(factory, client);
        var accountId = await CreateAccountAsync(client, session);
        var file = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: false).Bytes;
        var review = await PrepareAsync(client, session, accountId, file);
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var deadline = await ImportExpiryTestHelpers.SetDeadlineAsync(factory, reviewId);

        var committed = await CommitAsync(client, session, reviewId, review);
        Assert.Equal(HttpStatusCode.Created, committed.StatusCode);
        var firstBody = await BodyAsync(committed);
        var firstId = firstBody.GetProperty("statement").GetProperty("id").GetGuid();
        var afterCommit = await ImportSnapshotAsync(factory, accountId);
        await using var observer = await factory.CreateDb().OpenAsync();
        await ImportExpiryTestHelpers.WaitForDeadlineAsync(observer, deadline);

        using var newFactory = new KitFactory();
        using var newClient = newFactory.CreateClient();
        var replay = await CommitAsync(newClient, session, reviewId, review);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await BodyAsync(replay);
        Assert.True(replayBody.GetProperty("replayed").GetBoolean());
        Assert.Equal(firstId, replayBody.GetProperty("statement").GetProperty("id").GetGuid());
        Assert.Equal(firstBody.GetProperty("statement").GetRawText(), replayBody.GetProperty("statement").GetRawText());
        Assert.Equal(afterCommit, await ImportSnapshotAsync(factory, accountId));
    }

    private static async Task<string> ImportSnapshotAsync(KitFactory factory, Guid accountId)
    {
        await using var connection = await factory.CreateDb().OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT jsonb_build_object(
                'account', (SELECT to_jsonb(a) FROM financial_accounts a WHERE id = @account),
                'reviews', (SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY r.id), '[]'::jsonb) FROM import_reviews r),
                'review_rows', (SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY r.row_id), '[]'::jsonb) FROM import_review_rows r),
                'statements', (SELECT COALESCE(jsonb_agg(to_jsonb(s) ORDER BY s.id), '[]'::jsonb) FROM statement_imports s),
                'transactions', (SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY t.id), '[]'::jsonb) FROM transactions t),
                'tags', (SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY t.transaction_id, t.merchant_tag_id), '[]'::jsonb) FROM transaction_merchant_tags t),
                'receipts', (SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY r.id), '[]'::jsonb) FROM import_commit_receipts r)
            )::text
            """;
        command.Parameters.AddWithValue("account", accountId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ResetAsync(KitFactory factory)
    {
        var db = factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "TRUNCATE import_review_rows, import_reviews, import_commit_receipts, import_quota_locks, " +
            "transaction_merchant_tags, transactions, statement_imports, merchant_tags, " +
            "categories, financial_accounts, customers, sessions, email_verification_tokens, " +
            "password_reset_tokens CASCADE";
        await command.ExecuteNonQueryAsync();
        factory.Mailer.Clear();
        factory.LogCapture.Clear();
    }

    private static async Task<string> NewSessionAsync(KitFactory factory, HttpClient client)
    {
        var email = $"fault-{Guid.NewGuid():N}@example.test";
        var password = TestEnvironment.GoodPassword;
        var register = await client.SendAsync(Request(HttpMethod.Post, "/v1/register",
            JsonSerializer.Serialize(new { email, password })));
        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
        var verification = factory.Mailer.LastToken("verification", email);
        Assert.NotNull(verification);
        var verify = await client.SendAsync(Request(HttpMethod.Post, "/v1/verify",
            JsonSerializer.Serialize(new { token = verification })));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var login = await client.SendAsync(Request(HttpMethod.Post, "/v1/login",
            JsonSerializer.Serialize(new { email, password })));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await BodyAsync(login)).GetProperty("token").GetString()!;
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string session)
    {
        var response = await client.SendAsync(Request(HttpMethod.Post, "/v1/finance/accounts",
            JsonSerializer.Serialize(new { display_name = "Cheque" }), session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PrepareAsync(HttpClient client,
                                                        string session,
                                                        Guid accountId,
                                                        byte[] file)
    {
        var options = JsonSerializer.Serialize(new
        {
            financial_account_id = accountId,
            source_kind = "fnb",
            start_date = "2024-01-01",
            end_date = "2024-01-31",
            currency = "ZAR",
            confirm_zar = true,
        });
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(options, Encoding.UTF8, "application/json"), "options");
        content.Add(new ByteArrayContent(file), "file", "statement.bin");
        var request = Request(HttpMethod.Post, "/v1/finance/imports/reviews", null, session);
        request.Content = content;
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await BodyAsync(response);
    }

    private static Task<HttpResponseMessage> CommitAsync(HttpClient client,
                                                         string session,
                                                         Guid reviewId,
                                                         JsonElement review)
    {
        var summary = review.TryGetProperty("summary", out var nested) ? nested : review;
        var body = JsonSerializer.Serialize(new
        {
            expected_review_version = summary.GetProperty("version").GetInt32(),
            confirmed_summary_hash = summary.GetProperty("summary_hash").GetString(),
        });
        return client.SendAsync(Request(
            HttpMethod.Post, $"/v1/finance/imports/reviews/{reviewId}/commit", body, session));
    }

    private static HttpRequestMessage Request(HttpMethod method,
                                              string path,
                                              string? json,
                                              string? session = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(Brand.ApplicationSecretHeader, TestEnvironment.ApiSecret);
        if (session is not null)
        {
            request.Headers.Add(Brand.SessionHeader, session);
        }
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        return request;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<long> CountAsync(KitFactory factory, string table)
    {
        var db = factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class FaultInjector : IImportFaultInjector
    {
        public string? FailOn { get; set; }

        public void OnStep(string step)
        {
            if (step == FailOn)
            {
                throw new InvalidOperationException("test fault");
            }
        }
    }

    private sealed class PausingFaultInjector : IImportFaultInjector, IDisposable
    {
        public string? PauseAt { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public ConcurrentQueue<string> Steps { get; } = new();

        public void OnStep(string step)
        {
            Steps.Enqueue(step);
            if (step == PauseAt)
            {
                Entered.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(20)))
                {
                    throw new TimeoutException("The test did not release the import commit.");
                }
            }
        }

        public void Dispose() => Release.Dispose();
    }
}
