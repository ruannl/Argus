using System.Net;
using System.Text;
using System.Text.Json;
using argus.Server.Imports;

namespace argus.Server.Tests;

public sealed class StatementImportConcurrencyTests(KitFactory factory) : ApiTestBase(factory)
{
    private const string ImportsPath = "/v1/finance/imports";
    private const string ReviewsPath = "/v1/finance/imports/reviews";

    private async Task<string> NewSessionAsync()
    {
        var session = await RegisterVerifyLoginAsync(UniqueEmail());
        return session;
    }

    private async Task<Guid> CreateAccountAsync(string session)
    {
        var response = await SendAsync(Post(
            "/v1/finance/accounts", Json(new { display_name = "Cheque" }), session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> PrepareAsync(string session,
                                                 Guid accountId,
                                                 byte[] file,
                                                 DateOnly start,
                                                 DateOnly end)
    {
        var options = Json(new
        {
            financial_account_id = accountId,
            source_kind = "fnb",
            start_date = start.ToString("yyyy-MM-dd"),
            end_date = end.ToString("yyyy-MM-dd"),
            currency = "ZAR",
            confirm_zar = true,
        });
        var response = await SendAsync(MultipartPost(ReviewsPath, file, options, session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await BodyAsync(response);
    }

    private static int Version(JsonElement review) =>
        (review.TryGetProperty("summary", out var summary) ? summary : review)
        .GetProperty("version").GetInt32();

    private static string Hash(JsonElement review) =>
        (review.TryGetProperty("summary", out var summary) ? summary : review)
        .GetProperty("summary_hash").GetString()!;

    private static object CommitBody(JsonElement review) => new
    {
        expected_review_version = Version(review),
        confirmed_summary_hash = Hash(review),
    };

    [Fact]
    public async Task ConcurrentCommitsOfOneReviewReturnOneResult()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: false).Bytes;
        var review = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var body = Json(CommitBody(review));

        var first = SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", body, session: session));
        var second = SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", body, session: session));
        var responses = await Task.WhenAll(first, second);
        var bodies = await Task.WhenAll(responses.Select(BodyAsync));

        var ids = bodies.Select(body => body.GetProperty("statement").GetProperty("id").GetGuid()).Distinct().ToArray();
        Assert.Single(ids);
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3L, await CountRowsAsync("transactions"));
        Assert.Equal(1L, await CountRowsAsync("statement_imports"));
        Assert.Equal(1L, await CountRowsAsync("import_commit_receipts"));
    }

    [Fact]
    public async Task StaleReviewRequiresRefreshBeforeCommit()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: false).Bytes;
        var first = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var second = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));

        var firstCommit = await SendAsync(Post(
            $"{ReviewsPath}/{first.GetProperty("summary").GetProperty("id").GetGuid()}/commit",
            Json(CommitBody(first)), session: session));
        Assert.Equal(HttpStatusCode.Created, firstCommit.StatusCode);

        var stale = await SendAsync(Post(
            $"{ReviewsPath}/{second.GetProperty("summary").GetProperty("id").GetGuid()}/commit",
            Json(CommitBody(second)), session: session));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("review_stale", await ErrorKindAsync(stale));

        var secondId = second.GetProperty("summary").GetProperty("id").GetGuid();
        var refresh = await SendAsync(Post(
            $"{ReviewsPath}/{secondId}/refresh",
            Json(new { expected_review_version = Version(second) }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshed = await BodyAsync(refresh);
        Assert.Equal(0, refreshed.GetProperty("imported_count").GetInt32());
        Assert.Equal(3, refreshed.GetProperty("suppressed_count").GetInt32());

        var patch = await SendAsync(Patch(
            $"{ReviewsPath}/{secondId}",
            Json(new
            {
                expected_review_version = Version(refreshed),
                decisions = Array.Empty<object>(),
                statement_override = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await BodyAsync(patch);
        var commit = await SendAsync(Post(
            $"{ReviewsPath}/{secondId}/commit", Json(CommitBody(patched)), session: session));
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
        Assert.Equal(3L, await CountRowsAsync("transactions"));
        Assert.Equal(2L, await CountRowsAsync("statement_imports"));
    }

    private static JsonElement[] Rows(JsonElement review) =>
        review.GetProperty("page").GetProperty("rows").EnumerateArray().ToArray();

    [Fact]
    public async Task ForeignTagSelectionsAreRejectedBeforeMutation()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var rowId = Rows(review)[0].GetProperty("row_id").GetGuid();

        var response = await SendAsync(Patch(
            $"{ReviewsPath}/{reviewId}",
            Json(new
            {
                expected_review_version = Version(review),
                decisions = new[]
                {
                    new
                    {
                        row_id = rowId,
                        selected_tag_ids = new[] { Guid.NewGuid() },
                    },
                },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("tag_unavailable", await ErrorKindAsync(response));

        var reread = await BodyAsync(await SendAsync(Get($"{ReviewsPath}/{reviewId}", session: session)));
        Assert.Equal(Version(review), reread.GetProperty("summary").GetProperty("version").GetInt32());
        Assert.Empty(Rows(reread)[0].GetProperty("selected_tag_ids").EnumerateArray());
    }

    [Fact]
    public async Task MoreThanOneHundredTagSelectionsAreRejectedWithoutMutation()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var rowId = Rows(review)[0].GetProperty("row_id").GetGuid();

        var response = await SendAsync(Patch(
            $"{ReviewsPath}/{reviewId}",
            Json(new
            {
                expected_review_version = Version(review),
                decisions = new[]
                {
                    new
                    {
                        row_id = rowId,
                        selected_tag_ids = Enumerable.Range(0, 101)
                            .Select(_ => Guid.NewGuid()).ToArray(),
                    },
                },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await ErrorKindAsync(response));

        var reread = await BodyAsync(await SendAsync(Get($"{ReviewsPath}/{reviewId}", session: session)));
        Assert.Equal(Version(review), reread.GetProperty("summary").GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task QuotaGrowthOnPatchIsRejectedWithoutMutation()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var first = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var second = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var secondId = second.GetProperty("summary").GetProperty("id").GetGuid();
        await ExecuteSqlAsync(
            $"UPDATE import_reviews SET payload_bytes = {ImportLimits.MaxOpenReviewPayloadBytes} WHERE id = '{secondId}'");
        var firstId = first.GetProperty("summary").GetProperty("id").GetGuid();

        var patch = await SendAsync(Patch(
            $"{ReviewsPath}/{firstId}",
            Json(new
            {
                expected_review_version = Version(first),
                decisions = Array.Empty<object>(),
            }),
            session: session));

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
        Assert.Equal("processing_limit", await ErrorKindAsync(patch));
        var reread = await BodyAsync(await SendAsync(Get($"{ReviewsPath}/{firstId}", session: session)));
        Assert.Equal(Version(first), reread.GetProperty("summary").GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task PatchPayloadGrowthIsCountedAndRejectedAtTheAggregateBoundary()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var first = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var second = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var firstId = first.GetProperty("summary").GetProperty("id").GetGuid();
        var secondId = second.GetProperty("summary").GetProperty("id").GetGuid();
        var firstRow = Rows(first)[0].GetProperty("row_id").GetGuid();
        var tagOne = await CreateTagAsync(session, "Woolworths");
        var tagTwo = await CreateTagAsync(session, "Groceries");

        var before = await ScalarAsync(
            $"SELECT payload_bytes FROM import_reviews WHERE id = '{firstId}'");
        var grown = await BodyAsync(await SendAsync(Patch(
            $"{ReviewsPath}/{firstId}",
            Json(new
            {
                expected_review_version = Version(first),
                decisions = new[]
                {
                    new { row_id = firstRow, selected_tag_ids = new[] { tagOne } },
                },
            }),
            session: session)));
        var after = await ScalarAsync(
            $"SELECT payload_bytes FROM import_reviews WHERE id = '{firstId}'");
        Assert.True(after > before, $"payload bytes did not grow: {before} -> {after}");

        // Put the aggregate exactly at the limit, then prove a same-state PATCH
        // is accepted and a growing PATCH is rejected without changing the
        // selected IDs, version, or payload accounting.
        await ExecuteSqlAsync(
            $"UPDATE import_reviews SET payload_bytes = {ImportLimits.MaxOpenReviewPayloadBytes - after} " +
            $"WHERE id = '{secondId}'");
        var atBoundary = await BodyAsync(await SendAsync(Patch(
            $"{ReviewsPath}/{firstId}",
            Json(new
            {
                expected_review_version = Version(grown),
                decisions = new[]
                {
                    new { row_id = firstRow, selected_tag_ids = new[] { tagOne } },
                },
            }),
            session: session)));
        var boundaryPayload = await ScalarAsync(
            $"SELECT payload_bytes FROM import_reviews WHERE id = '{firstId}'");
        await ExecuteSqlAsync(
            $"UPDATE import_reviews SET payload_bytes = {ImportLimits.MaxOpenReviewPayloadBytes - boundaryPayload} " +
            $"WHERE id = '{secondId}'");

        var rejected = await SendAsync(Patch(
            $"{ReviewsPath}/{firstId}",
            Json(new
            {
                expected_review_version = Version(atBoundary),
                decisions = new[]
                {
                    new
                    {
                        row_id = firstRow,
                        selected_tag_ids = new[] { tagOne, tagTwo },
                    },
                },
            }),
            session: session));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("processing_limit", await ErrorKindAsync(rejected));
        Assert.Equal(
            Version(atBoundary),
            await ScalarAsync($"SELECT version FROM import_reviews WHERE id = '{firstId}'"));
        Assert.Equal(
            boundaryPayload,
            await ScalarAsync($"SELECT payload_bytes FROM import_reviews WHERE id = '{firstId}'"));
        var reread = await BodyAsync(await SendAsync(Get(
            $"{ReviewsPath}/{firstId}", session: session)));
        var selected = Rows(reread)[0].GetProperty("selected_tag_ids")
            .EnumerateArray().Select(value => value.GetGuid()).ToArray();
        Assert.Equal(new[] { tagOne }, selected);
    }

    [Fact]
    public async Task SharedIdentityStableAndAnonymousRowsPermuteWithoutDuplicates()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var config = new
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
            stable_id_column = 3,
            source_namespace = "bank",
        };
        var first = await PrepareGenericAsync(
            session, accountId,
            Encoding.UTF8.GetBytes(SharedIdentityCsv(rows: 40, reverse: false)),
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), config);
        var firstKept = await KeepAllWithinFileAsync(session, first);
        await CommitAsync(session, firstKept);
        Assert.Equal(40L, await CountRowsAsync("transactions"));

        var second = await PrepareGenericAsync(
            session, accountId,
            Encoding.UTF8.GetBytes(SharedIdentityCsv(rows: 40, reverse: true)),
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), config);
        var secondKept = await KeepAllWithinFileAsync(session, second);

        Assert.Equal(0, secondKept.GetProperty("imported_count").GetInt32());
        Assert.Equal(40, secondKept.GetProperty("suppressed_count").GetInt32());

        var reviewId = secondKept.GetProperty("id").GetGuid();
        var refresh = await SendAsync(Post(
            $"{ReviewsPath}/{reviewId}/refresh",
            Json(new { expected_review_version = Version(secondKept) }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshed = await BodyAsync(refresh);
        Assert.Equal(0, refreshed.GetProperty("imported_count").GetInt32());
        Assert.Equal(40, refreshed.GetProperty("suppressed_count").GetInt32());
    }

    private async Task<JsonElement> KeepAllWithinFileAsync(string session,
                                                           JsonElement review)
    {
        var rows = Rows(review);
        var response = await SendAsync(Patch(
            $"{ReviewsPath}/{review.GetProperty("summary").GetProperty("id").GetGuid()}",
            Json(new
            {
                expected_review_version = Version(review),
                decisions = new[]
                {
                    new
                    {
                        row_id = rows[0].GetProperty("row_id").GetGuid(),
                        within_file_group = rows[0].GetProperty("within_file_group").GetString(),
                        within_file_action = "keep_all",
                    },
                },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    [Fact]
    public async Task NearMatchHistoryIsBoundedAndDisclosed()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var config = new
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
        var existing = new StringBuilder("Date,Description,Amount\n");
        for (var index = 0; index < 60; index++)
        {
            existing.Append("2025-01-10,Coffee shop ")
                .Append(index.ToString("D2"))
                .Append(",-10.00\n");
        }
        var first = await PrepareGenericAsync(
            session, accountId, Encoding.UTF8.GetBytes(existing.ToString()),
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), config);
        await CommitAsync(session, first);

        var incoming = await PrepareGenericAsync(
            session, accountId,
            Encoding.UTF8.GetBytes("Date,Description,Amount\n2025-01-10,Coffee shop new,-10.00\n"),
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), config);
        var row = Rows(incoming)[0];

        Assert.Equal(60, row.GetProperty("near_candidate_total").GetInt32());
        Assert.Equal(5, row.GetProperty("near_candidates").GetArrayLength());
    }

    private async Task<Guid> CreateTagAsync(string session,
                                            string name)
    {
        var response = await SendAsync(Post(
            "/v1/finance/merchant-tags", Json(new { display_name = name }), session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> PrepareGenericAsync(string session,
                                                        Guid accountId,
                                                        byte[] file,
                                                        DateOnly start,
                                                        DateOnly end,
                                                        object config)
    {
        var options = Json(new
        {
            financial_account_id = accountId,
            source_kind = "generic_csv",
            parser_config = config,
            start_date = start.ToString("yyyy-MM-dd"),
            end_date = end.ToString("yyyy-MM-dd"),
            currency = "ZAR",
            confirm_zar = true,
        });
        var response = await SendAsync(MultipartPost(ReviewsPath, file, options, session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await BodyAsync(response);
    }

    private async Task CommitAsync(string session,
                                   JsonElement review)
    {
        var summary = review.TryGetProperty("summary", out var nested) ? nested : review;
        var response = await SendAsync(Post(
            $"{ReviewsPath}/{summary.GetProperty("id").GetGuid()}/commit",
            Json(CommitBody(review)), session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static string SharedIdentityCsv(int rows,
                                            bool reverse)
    {
        var builder = new StringBuilder("Date,Description,Amount,ID\n");
        var ids = Enumerable.Range(0, rows)
            .Select(index => index % 2 == 0 ? $"id-{index:D4}" : string.Empty)
            .ToList();
        if (reverse)
        {
            ids.Reverse();
        }
        foreach (var id in ids)
        {
            builder.Append("2025-01-02,Coffee shop,-10.00,").Append(id).Append('\n');
        }
        return builder.ToString();
    }

    [Fact]
    public async Task CommitWaitingOnSelectedTagMustRejectReviewThatExpiresDuringWait()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var tagId = await CreateTagAsync(session, "Expiry lock tag");
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var rowId = Rows(review)[0].GetProperty("row_id").GetGuid();
        var patch = await SendAsync(Patch(
            $"{ReviewsPath}/{reviewId}",
            Json(new
            {
                expected_review_version = Version(review),
                decisions = new[] { new { row_id = rowId, selected_tag_ids = new[] { tagId } } },
            }), session: session));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await BodyAsync(patch);

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var tag = blocker.CreateCommand())
        {
            tag.Transaction = transaction;
            tag.CommandText = "SELECT id FROM merchant_tags WHERE id = @id FOR UPDATE";
            tag.Parameters.AddWithValue("id", tagId);
            Assert.NotNull(await tag.ExecuteScalarAsync());
        }
        int blockerPid;
        await using (var pid = blocker.CreateCommand())
        {
            pid.Transaction = transaction;
            pid.CommandText = "SELECT pg_backend_pid()";
            blockerPid = (int)(await pid.ExecuteScalarAsync())!;
        }

        var deadline = await ImportExpiryTestHelpers.SetDeadlineAsync(Factory, reviewId);
        var headerBefore = await StringScalarAsync($"SELECT to_jsonb(r)::text FROM import_reviews r WHERE id = '{reviewId}'");
        var rowsBefore = await StringScalarAsync($"""
            SELECT jsonb_agg(to_jsonb(r) ORDER BY source_row_number)::text
            FROM import_review_rows r WHERE review_id = '{reviewId}'
            """);
        var versionBefore = await ScalarAsync($"SELECT import_version FROM financial_accounts WHERE id = '{accountId}'");
        Factory.LogCapture.Clear();
        await using var observer = await db.OpenAsync();
        var commit = SendAsync(Post(
            $"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(patched)), session: session));
        HttpResponseMessage? result = null;
        try
        {
            await ImportExpiryTestHelpers.WaitForTagWaitAsync(observer, blockerPid);
            Assert.False(commit.IsCompleted);
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
            await transaction.RollbackAsync();
            result = await commit.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
        Assert.Equal("review_expired", await ErrorKindAsync(result));
        Assert.Equal(0L, await CountRowsAsync("statement_imports"));
        Assert.Equal(0L, await CountRowsAsync("transactions"));
        Assert.Equal(0L, await CountRowsAsync("transaction_merchant_tags"));
        Assert.Equal(0L, await CountRowsAsync("import_commit_receipts"));
        Assert.Equal(versionBefore, await ScalarAsync($"SELECT import_version FROM financial_accounts WHERE id = '{accountId}'"));
        Assert.Equal(headerBefore, await StringScalarAsync($"SELECT to_jsonb(r)::text FROM import_reviews r WHERE id = '{reviewId}'"));
        Assert.Equal(rowsBefore, await StringScalarAsync($"""
            SELECT jsonb_agg(to_jsonb(r) ORDER BY source_row_number)::text
            FROM import_review_rows r WHERE review_id = '{reviewId}'
            """));
        Assert.False(Factory.LogCapture.Contains("audit.import_committed"));
    }

    [Fact]
    public async Task DeletedTagRejectsCommitWithoutPartialWrites()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var tagResponse = await SendAsync(Post(
            "/v1/finance/merchant-tags", Json(new { display_name = "Woolworths" }), session: session));
        Assert.Equal(HttpStatusCode.Created, tagResponse.StatusCode);
        var tagId = (await BodyAsync(tagResponse)).GetProperty("id").GetGuid();

        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var rowId = review.GetProperty("page").GetProperty("rows")[0].GetProperty("row_id").GetGuid();
        var patch = await SendAsync(Patch(
            $"{ReviewsPath}/{reviewId}",
            Json(new
            {
                expected_review_version = Version(review),
                decisions = new[]
                {
                    new { row_id = rowId, selected_tag_ids = new[] { tagId } },
                },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await BodyAsync(patch);

        var deleteTag = await SendAsync(Delete($"/v1/finance/merchant-tags/{tagId}", session: session));
        Assert.Equal(HttpStatusCode.OK, deleteTag.StatusCode);

        var commit = await SendAsync(Post(
            $"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(patched)), session: session));
        Assert.Equal(HttpStatusCode.Conflict, commit.StatusCode);
        Assert.Equal("tag_unavailable", await ErrorKindAsync(commit));
        Assert.Equal(0L, await CountRowsAsync("statement_imports"));
        Assert.Equal(0L, await CountRowsAsync("transactions"));
        Assert.Equal(1L, await CountRowsAsync("import_reviews"));
    }

    [Fact]
    public async Task ArchivedAccountRejectsCommitAndRetrySucceedsAfterRestore()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var body = Json(CommitBody(review));

        var archive = await SendAsync(Post(
            $"/v1/finance/accounts/{accountId}/archive", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var blocked = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", body, session: session));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("account_archived", await ErrorKindAsync(blocked));
        Assert.Equal(0L, await CountRowsAsync("statement_imports"));

        var restore = await SendAsync(Post(
            $"/v1/finance/accounts/{accountId}/restore", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        var committed = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", body, session: session));
        Assert.Equal(HttpStatusCode.Created, committed.StatusCode);
        Assert.Equal(2L, await CountRowsAsync("transactions"));
    }

    [Fact]
    public async Task AbandonedAndExpiredReviewsCannotCommit()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var abandoned = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var abandonedId = abandoned.GetProperty("summary").GetProperty("id").GetGuid();
        var abandon = await SendAsync(Post(
            $"{ReviewsPath}/{abandonedId}/abandon",
            Json(new { expected_review_version = Version(abandoned) }), session: session));
        Assert.Equal(HttpStatusCode.OK, abandon.StatusCode);
        var abandonedCommit = await SendAsync(Post(
            $"{ReviewsPath}/{abandonedId}/commit", Json(CommitBody(abandoned)), session: session));
        Assert.Equal(HttpStatusCode.NotFound, abandonedCommit.StatusCode);

        var expired = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var expiredId = expired.GetProperty("summary").GetProperty("id").GetGuid();
        await ExecuteSqlAsync(
            $"UPDATE import_reviews SET expires_at = NOW() - INTERVAL '1 minute' WHERE id = '{expiredId}'");
        var expiredCommit = await SendAsync(Post(
            $"{ReviewsPath}/{expiredId}/commit", Json(CommitBody(expired)), session: session));
        Assert.Equal(HttpStatusCode.Gone, expiredCommit.StatusCode);
        Assert.Equal("review_expired", await ErrorKindAsync(expiredCommit));
    }

    [Fact]
    public async Task AbandonAfterCommitReturnsTheCommittedReceipt()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var commit = await SendAsync(Post(
            $"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session));
        var statementId = (await BodyAsync(commit)).GetProperty("statement").GetProperty("id").GetGuid();

        var abandon = await SendAsync(Post(
            $"{ReviewsPath}/{reviewId}/abandon",
            Json(new { expected_review_version = Version(review) }),
            session: session));

        Assert.Equal(HttpStatusCode.OK, abandon.StatusCode);
        var body = await BodyAsync(abandon);
        Assert.True(body.GetProperty("committed").GetBoolean());
        Assert.Equal(statementId, body.GetProperty("statement").GetProperty("id").GetGuid());
        Assert.Equal(1L, await CountRowsAsync("statement_imports"));
    }

    [Fact]
    public async Task DifferentReviewsForOneAccountSerializeToOneCommit()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: false).Bytes;
        var first = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var second = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));

        var responses = await Task.WhenAll(
            SendAsync(Post(
                $"{ReviewsPath}/{first.GetProperty("summary").GetProperty("id").GetGuid()}/commit",
                Json(CommitBody(first)), session: session)),
            SendAsync(Post(
                $"{ReviewsPath}/{second.GetProperty("summary").GetProperty("id").GetGuid()}/commit",
                Json(CommitBody(second)), session: session)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        var stale = Assert.Single(
            responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("review_stale", await ErrorKindAsync(stale));
        Assert.Equal(1L, await CountRowsAsync("statement_imports"));
        Assert.Equal(3L, await CountRowsAsync("transactions"));
        Assert.Equal(1L, await CountRowsAsync("import_commit_receipts"));
    }

    [Fact]
    public async Task ConcurrentAbandonAndCommitLeaveOneTerminalOutcome()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(
            SendAsync(Post(
                $"{ReviewsPath}/{reviewId}/abandon",
                Json(new { expected_review_version = Version(review) }), session: session)),
            SendAsync(Post(
                $"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session)));

        var commit = Assert.Single(
            responses,
            response => response.RequestMessage!.RequestUri!.AbsolutePath.EndsWith("/commit"));
        var abandon = Assert.Single(
            responses,
            response => response.RequestMessage!.RequestUri!.AbsolutePath.EndsWith("/abandon"));

        Assert.Equal(HttpStatusCode.OK, abandon.StatusCode);
        if (commit.StatusCode == HttpStatusCode.Created)
        {
            var abandonBody = await BodyAsync(abandon);
            Assert.True(abandonBody.GetProperty("committed").GetBoolean());
            Assert.Equal(1L, await CountRowsAsync("statement_imports"));
            Assert.Equal(2L, await CountRowsAsync("transactions"));
        }
        else
        {
            Assert.Equal(HttpStatusCode.NotFound, commit.StatusCode);
            Assert.Equal(0L, await CountRowsAsync("statement_imports"));
            Assert.Equal(0L, await CountRowsAsync("transactions"));
        }
    }

    [Fact]
    public async Task ConcurrentTagDeleteAndCommitHaveOneConsistentOutcome()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var tagResponse = await SendAsync(Post(
            "/v1/finance/merchant-tags", Json(new { display_name = "Woolworths" }), session: session));
        var tagId = (await BodyAsync(tagResponse)).GetProperty("id").GetGuid();
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var rowId = Rows(review)[0].GetProperty("row_id").GetGuid();
        var patched = await BodyAsync(await SendAsync(Patch(
            $"{ReviewsPath}/{reviewId}",
            Json(new
            {
                expected_review_version = Version(review),
                decisions = new[]
                {
                    new { row_id = rowId, selected_tag_ids = new[] { tagId } },
                },
            }),
            session: session)));

        var responses = await Task.WhenAll(
            SendAsync(Delete($"/v1/finance/merchant-tags/{tagId}", session: session)),
            SendAsync(Post(
                $"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(patched)), session: session)));

        var commit = Assert.Single(
            responses,
            response => response.RequestMessage!.RequestUri!.AbsolutePath.EndsWith("/commit"));
        var deleteTag = Assert.Single(
            responses,
            response => response.RequestMessage!.Method == HttpMethod.Delete);

        Assert.Equal(HttpStatusCode.OK, deleteTag.StatusCode);
        if (commit.StatusCode == HttpStatusCode.Created)
        {
            Assert.Equal(1L, await CountRowsAsync("statement_imports"));
            Assert.Equal(2L, await CountRowsAsync("transactions"));
            Assert.Equal(0L, await CountRowsAsync("merchant_tags"));
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, commit.StatusCode);
            Assert.Equal("tag_unavailable", await ErrorKindAsync(commit));
            Assert.Equal(0L, await CountRowsAsync("statement_imports"));
            Assert.Equal(0L, await CountRowsAsync("transactions"));
        }
    }

    [Fact]
    public async Task ConcurrentAccountArchiveAndCommitHaveOneConsistentOutcome()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(
            SendAsync(Post(
                $"/v1/finance/accounts/{accountId}/archive", string.Empty, session: session)),
            SendAsync(Post(
                $"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session)));

        var commit = Assert.Single(
            responses,
            response => response.RequestMessage!.RequestUri!.AbsolutePath.EndsWith("/commit"));
        var archive = Assert.Single(
            responses,
            response => response.RequestMessage!.RequestUri!.AbsolutePath.EndsWith("/archive"));

        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        if (commit.StatusCode == HttpStatusCode.Created)
        {
            Assert.Equal(1L, await CountRowsAsync("statement_imports"));
            Assert.Equal(2L, await CountRowsAsync("transactions"));
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, commit.StatusCode);
            Assert.Equal(0L, await CountRowsAsync("statement_imports"));
            Assert.Equal(0L, await CountRowsAsync("transactions"));
        }
    }

    [Fact]
    public async Task ConcurrentDeleteAndCommitReplayNeverResurrectsData()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var committed = await BodyAsync(await SendAsync(Post(
            $"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session)));
        var statement = committed.GetProperty("statement");
        var statementId = statement.GetProperty("id").GetGuid();
        var detail = await BodyAsync(await SendAsync(Get($"{ImportsPath}/{statementId}", session: session)));

        var deleteBody = Json(new
        {
            expected_version = statement.GetProperty("version").GetInt32(),
            expected_transaction_count = detail.GetProperty("expected_transaction_count").GetInt64(),
            confirm = true,
        });
        var responses = await Task.WhenAll(
            SendAsync(DeleteJson($"{ImportsPath}/{statementId}", deleteBody, session: session)),
            SendAsync(Post(
                $"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session)));

        var replay = Assert.Single(
            responses,
            response => response.RequestMessage!.RequestUri!.AbsolutePath.EndsWith("/commit"));
        var delete = Assert.Single(
            responses,
            response => response.RequestMessage!.Method == HttpMethod.Delete);

        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Contains(
            replay.StatusCode,
            new[] { HttpStatusCode.OK, HttpStatusCode.Gone });
        Assert.Equal(0L, await CountRowsAsync("statement_imports"));
        Assert.Equal(0L, await CountRowsAsync("transactions"));

        var retry = await SendAsync(Post(
            $"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session));
        Assert.Equal(HttpStatusCode.Gone, retry.StatusCode);
        Assert.Equal("import_deleted", await ErrorKindAsync(retry));
    }

    [Fact]
    public async Task SixthOpenReviewIsRejectedByQuota()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(1, chequeAccount: false).Bytes;
        for (var index = 0; index < ImportLimits.MaxOpenReviewsPerCustomer; index++)
        {
            _ = await PrepareAsync(
                session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        }

        var response = await SendAsync(MultipartPost(
            ReviewsPath,
            file,
            Json(new
            {
                financial_account_id = accountId,
                source_kind = "fnb",
                start_date = "2024-01-01",
                end_date = "2024-01-31",
                currency = "ZAR",
                confirm_zar = true,
            }),
            session: session));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("processing_limit", await ErrorKindAsync(response));
        Assert.Equal(
            (long)ImportLimits.MaxOpenReviewsPerCustomer,
            await CountRowsAsync("import_reviews"));
    }

    [Fact]
    public async Task ReviewCreationPayloadQuotaIsEnforcedBeforeInsert()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(1, chequeAccount: false).Bytes;
        var first = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var firstId = first.GetProperty("summary").GetProperty("id").GetGuid();
        await ExecuteSqlAsync(
            $"UPDATE import_reviews SET payload_bytes = {ImportLimits.MaxOpenReviewPayloadBytes} WHERE id = '{firstId}'");

        var response = await SendAsync(MultipartPost(
            ReviewsPath,
            file,
            Json(new
            {
                financial_account_id = accountId,
                source_kind = "fnb",
                start_date = "2024-01-01",
                end_date = "2024-01-31",
                currency = "ZAR",
                confirm_zar = true,
            }),
            session: session));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("processing_limit", await ErrorKindAsync(response));
        Assert.Equal(1L, await CountRowsAsync("import_reviews"));
    }

    [Fact]
    public async Task DenseIdenticalRowsMatchWithoutCombinatorialGrowth()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var csv = DenseFnbCsv(rows: 200);
        var first = await PrepareAsync(
            session, accountId, Encoding.UTF8.GetBytes(csv),
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var firstRows = Rows(first);
        Assert.All(firstRows, row =>
            Assert.Equal("pending", row.GetProperty("within_file_state").GetString()));
        var firstId = first.GetProperty("summary").GetProperty("id").GetGuid();
        var kept = await BodyAsync(await SendAsync(Patch(
            $"{ReviewsPath}/{firstId}",
            Json(new
            {
                expected_review_version = Version(first),
                decisions = new[]
                {
                    new
                    {
                        row_id = firstRows[0].GetProperty("row_id").GetGuid(),
                        within_file_group = firstRows[0].GetProperty("within_file_group").GetString(),
                        within_file_action = "keep_all",
                    },
                },
            }),
            session: session)));
        var firstCommit = await SendAsync(Post(
            $"{ReviewsPath}/{firstId}/commit", Json(CommitBody(kept)), session: session));
        Assert.Equal(HttpStatusCode.Created, firstCommit.StatusCode);
        Assert.Equal(200L, await CountRowsAsync("transactions"));

        var second = await PrepareAsync(
            session, accountId, Encoding.UTF8.GetBytes(csv),
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var secondRows = Rows(second);
        var secondId = second.GetProperty("summary").GetProperty("id").GetGuid();
        var secondKept = await BodyAsync(await SendAsync(Patch(
            $"{ReviewsPath}/{secondId}",
            Json(new
            {
                expected_review_version = Version(second),
                decisions = new[]
                {
                    new
                    {
                        row_id = secondRows[0].GetProperty("row_id").GetGuid(),
                        within_file_group = secondRows[0].GetProperty("within_file_group").GetString(),
                        within_file_action = "keep_all",
                    },
                },
            }),
            session: session)));

        Assert.Equal(0, secondKept.GetProperty("imported_count").GetInt32());
        Assert.Equal(200, secondKept.GetProperty("suppressed_count").GetInt32());

        var refresh = await SendAsync(Post(
            $"{ReviewsPath}/{secondId}/refresh",
            Json(new { expected_review_version = Version(secondKept) }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshed = await BodyAsync(refresh);
        Assert.Equal(0, refreshed.GetProperty("imported_count").GetInt32());
        Assert.Equal(200, refreshed.GetProperty("suppressed_count").GetInt32());
    }

    private static string DenseFnbCsv(int rows)
    {
        var builder = new StringBuilder(
            "Synthetic FNB export\nAccount\nSynthetic\n0000\nDo not use\nSynthetic preamble\n" +
            "Date,Amount,Balance,Description\n");
        for (var index = 0; index < rows; index++)
        {
            builder.Append("2024/01/02,-10.00,0.00,Identical coffee\n");
        }
        return builder.ToString();
    }

    [Fact]
    public async Task DeleteThenOldCommitRetryReturnsGone()
    {
        var session = await NewSessionAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(
            session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var commit = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session));
        var statementId = (await BodyAsync(commit)).GetProperty("statement").GetProperty("id").GetGuid();
        var detail = await BodyAsync(await SendAsync(Get($"{ImportsPath}/{statementId}", session: session)));
        var summary = detail.GetProperty("statement");
        var delete = await SendAsync(DeleteJson(
            $"{ImportsPath}/{statementId}",
            Json(new
            {
                expected_version = summary.GetProperty("version").GetInt32(),
                expected_transaction_count = detail.GetProperty("expected_transaction_count").GetInt64(),
                confirm = true,
            }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var retry = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session));
        Assert.Equal(HttpStatusCode.Gone, retry.StatusCode);
        Assert.Equal("import_deleted", await ErrorKindAsync(retry));
    }
}
