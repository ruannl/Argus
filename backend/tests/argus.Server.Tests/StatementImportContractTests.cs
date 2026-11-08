using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using argus.Server.Data;
using argus.Server.Imports;

namespace argus.Server.Tests;

public sealed class StatementImportContractTests(KitFactory factory) : ApiTestBase(factory)
{
    private const string ImportsPath = "/v1/finance/imports";
    private const string ReviewsPath = "/v1/finance/imports/reviews";
    private const string InspectPath = "/v1/finance/imports/inspect";

    private async Task<(string Session, Guid CustomerId)> NewCustomerAsync()
    {
        var email = UniqueEmail();
        var session = await RegisterVerifyLoginAsync(email);
        var me = await SendAsync(Get("/v1/me", session: session));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        return (session, (await BodyAsync(me)).GetProperty("id").GetGuid());
    }

    private async Task<Guid> CreateAccountAsync(string session, string name = "Cheque")
    {
        var response = await SendAsync(Post("/v1/finance/accounts", Json(new { display_name = name }), session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> InspectAsync(string session,
                                                 Guid accountId,
                                                 byte[] file,
                                                 string sourceKind = "fnb")
    {
        var options = Json(new { financial_account_id = accountId, source_kind = sourceKind, });
        var response = await SendAsync(MultipartPost(InspectPath, file, options, session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private async Task<JsonElement> PrepareAsync(string session,
                                                 Guid accountId,
                                                 byte[] file,
                                                 DateOnly start,
                                                 DateOnly end,
                                                 string sourceKind = "fnb",
                                                 object? parserConfig = null)
    {
        var options = Json(new { financial_account_id = accountId, source_kind = sourceKind, parser_config = parserConfig, start_date = start.ToString("yyyy-MM-dd"), end_date = end.ToString("yyyy-MM-dd"), currency = "ZAR", confirm_zar = true, });
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

    private static JsonElement[] Rows(JsonElement review) =>
        review.GetProperty("page").GetProperty("rows").EnumerateArray().ToArray();

    private static object CommitBody(JsonElement review) => new
    {
        expected_review_version = Version(review),
        confirmed_summary_hash = Hash(review),
    };

    [Fact]
    public async Task InspectFnbReturnsPreviewAndPersistsNothing()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(10, chequeAccount: false).Bytes;

        var inspect = await InspectAsync(session, accountId, file);

        Assert.Equal("fnb_csv", inspect.GetProperty("detected_source_kind").GetString());
        Assert.True(inspect.GetProperty("can_prepare").GetBoolean());
        Assert.Equal(10, inspect.GetProperty("preview").GetArrayLength());
        Assert.Equal(0L, await CountRowsAsync("import_reviews"));
        Assert.Equal(0L, await CountRowsAsync("statement_imports"));
        Assert.Equal(0L, await CountRowsAsync("transactions"));
    }

    [Fact]
    public async Task PrepareCommitHistoryAndDetailFlow()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: false).Bytes;

        var review = await PrepareAsync(session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        Assert.Equal(3, review.GetProperty("summary").GetProperty("imported_count").GetInt32());
        Assert.Equal(3, Rows(review).Length);

        var commit = await SendAsync(Post($"{ReviewsPath}/{review.GetProperty("summary").GetProperty("id").GetGuid()}/commit", Json(CommitBody(review)), session: session));
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
        var commitBody = await BodyAsync(commit);
        Assert.False(commitBody.GetProperty("replayed").GetBoolean());
        var statementId = commitBody.GetProperty("statement").GetProperty("id").GetGuid();
        Assert.Equal(3, commitBody.GetProperty("statement").GetProperty("imported_count").GetInt32());

        var history = await SendAsync(Get($"{ImportsPath}?limit=10", session: session));
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var items = (await BodyAsync(history)).GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(statementId, items[0].GetProperty("id").GetGuid());

        var detail = await SendAsync(Get($"{ImportsPath}/{statementId}", session: session));
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailBody = await BodyAsync(detail);
        Assert.Equal(3, detailBody.GetProperty("rows").GetArrayLength());
        Assert.Equal(3, detailBody.GetProperty("expected_transaction_count").GetInt32());
        Assert.Equal(0L, await CountRowsAsync("import_reviews"));
    }

    [Fact]
    public async Task CsvCommitPreservesDisplayWhitespaceAndNormalizesMatching()
    {
        var (session, _) = await NewCustomerAsync();
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
        const string firstDescription = "Cafe  Corner\tnote";
        const string secondDescription = "Multi\nline   note";
        var firstFile = Encoding.UTF8.GetBytes("Date,Description,Amount\n" + $"2025-01-02,\"{firstDescription}\",-10.00\n" + $"2025-01-03,\"{secondDescription}\",-20.00\n");
        var first = await PrepareAsync(session, accountId, firstFile, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), sourceKind: "generic_csv", parserConfig: config);
        Assert.Equal(firstDescription, Rows(first)[0].GetProperty("description").GetString());
        Assert.Equal(secondDescription, Rows(first)[1].GetProperty("description").GetString());

        var commit = await SendAsync(Post($"{ReviewsPath}/{first.GetProperty("summary").GetProperty("id").GetGuid()}/commit", Json(CommitBody(first)), session: session));
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
        var statementId = (await BodyAsync(commit)).GetProperty("statement").GetProperty("id").GetGuid();
        var detailRows = (await BodyAsync(await SendAsync(Get($"{ImportsPath}/{statementId}", session: session)))).GetProperty("rows");
        Assert.Equal(firstDescription, detailRows[0].GetProperty("description").GetString());
        Assert.Equal(secondDescription, detailRows[1].GetProperty("description").GetString());

        var secondFile = Encoding.UTF8.GetBytes("Date,Description,Amount\n" + "2025-01-02,Cafe Corner note,-10.00\n" + "2025-01-03,Multi line note,-20.00\n");
        var second = await PrepareAsync(session, accountId, secondFile, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), sourceKind: "generic_csv", parserConfig: config);
        Assert.Equal(2, second.GetProperty("summary").GetProperty("suppressed_count").GetInt32());
        Assert.Equal("Cafe Corner note", Rows(second)[0].GetProperty("description").GetString());
        Assert.Equal("Multi line note", Rows(second)[1].GetProperty("description").GetString());
    }

    [Fact]
    public async Task NegativeGenericColumnIndexIsRejectedWithoutEffects()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var file = Encoding.UTF8.GetBytes("Date,Description,Amount\n2025-01-02,Shop,-1.00\n");
        var options = Json(new
        {
            financial_account_id = accountId,
            source_kind = "generic_csv",
            parser_config = new
            {
                format = "generic",
                header_line = 1,
                delimiter = ",",
                date_format = "yyyy-MM-dd",
                decimal_separator = ".",
                thousands_separator = "none",
                amount_mode = "signed",
                date_column = -1,
                description_column = 1,
                amount_column = 2,
                direction = "normal",
            },
            start_date = "2025-01-01",
            end_date = "2025-01-31",
            currency = "ZAR",
            confirm_zar = true,
        });

        var response = await SendAsync(MultipartPost(ReviewsPath, file, options, session: session));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("format_confirmation_required", await ErrorKindAsync(response));
        Assert.Equal(0L, await CountRowsAsync("import_reviews"));
        Assert.Equal(0L, await CountRowsAsync("import_review_rows"));
        Assert.Equal(0L, await CountRowsAsync("import_commit_receipts"));
    }

    [Fact]
    public async Task PatchCannotRewritePreparedSourceRowsOrSummaryIdentity()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var summary = review.GetProperty("summary");
        var reviewId = summary.GetProperty("id").GetGuid();
        var versionBefore = summary.GetProperty("version").GetInt32();
        var totalBefore = summary.GetProperty("total_rows").GetInt32();
        var importedBefore = summary.GetProperty("imported_count").GetInt32();
        var suppressedBefore = summary.GetProperty("suppressed_count").GetInt32();
        var skippedBefore = summary.GetProperty("skipped_count").GetInt32();
        var hashBefore = summary.GetProperty("summary_hash").GetString();
        var parserConfigBefore = await StringScalarAsync($"SELECT parser_config::text FROM import_reviews WHERE id = '{reviewId}'");
        var fingerprintBefore = await StringScalarAsync($"SELECT statement_fingerprint FROM import_reviews WHERE id = '{reviewId}'");
        var expiresAtBefore = await StringScalarAsync($"SELECT expires_at::text FROM import_reviews WHERE id = '{reviewId}'");
        var rowsBefore = Rows(review);
        var rowsBeforeRaw = review.GetProperty("page").GetProperty("rows").GetRawText();
        var snapshots = rowsBefore.Select(row => new
        {
            RowId = row.GetProperty("row_id").GetGuid(),
            SourceLine = row.GetProperty("source_line").GetInt32(),
            SourcePage = row.GetProperty("source_page").GetRawText(),
            SourceRowNumber = row.GetProperty("source_row_number").GetInt32(),
            TransactionDate = row.GetProperty("transaction_date").GetString(),
            Description = row.GetProperty("description").GetString(),
            Amount = row.GetProperty("amount").GetString(),
            RawValues = row.GetProperty("raw_values").GetRawText(),
            Validation = row.GetProperty("validation_codes").GetRawText(),
            Warnings = row.GetProperty("warnings").GetRawText(),
            DuplicateState = row.GetProperty("duplicate_state").GetString(),
            WithinFileState = row.GetProperty("within_file_state").GetString(),
            Override = row.GetProperty("override").GetBoolean(),
        }).ToArray();

        var injectedResponse = await SendAsync(Patch(
            $"{ReviewsPath}/{reviewId}",
            Json(new
            {
                expected_review_version = versionBefore,
                decisions = new[]
                {
                    new
                    {
                        row_id = rowsBefore[0].GetProperty("row_id").GetGuid(),
                        skip = true,
                        description = "Injected rewrite",
                        amount = "999.99",
                    },
                },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.BadRequest, injectedResponse.StatusCode);
        var unchanged = await BodyAsync(await SendAsync(Get($"{ReviewsPath}/{reviewId}", session: session)));
        var unchangedSummary = unchanged.GetProperty("summary");
        Assert.Equal(versionBefore, unchangedSummary.GetProperty("version").GetInt32());
        Assert.Equal(hashBefore, unchangedSummary.GetProperty("summary_hash").GetString());
        Assert.Equal(totalBefore, unchangedSummary.GetProperty("total_rows").GetInt32());
        Assert.Equal(importedBefore, unchangedSummary.GetProperty("imported_count").GetInt32());
        Assert.Equal(suppressedBefore, unchangedSummary.GetProperty("suppressed_count").GetInt32());
        Assert.Equal(skippedBefore, unchangedSummary.GetProperty("skipped_count").GetInt32());
        Assert.Equal(summary.GetProperty("possible_duplicate_count").GetInt32(), unchangedSummary.GetProperty("possible_duplicate_count").GetInt32());
        Assert.Equal(summary.GetProperty("exact_override_count").GetInt32(), unchangedSummary.GetProperty("exact_override_count").GetInt32());
        Assert.Equal(summary.GetProperty("within_file_pending_count").GetInt32(), unchangedSummary.GetProperty("within_file_pending_count").GetInt32());
        Assert.Equal(summary.GetProperty("statement_override").GetBoolean(), unchangedSummary.GetProperty("statement_override").GetBoolean());
        Assert.Equal(summary.GetProperty("financial_account_id").GetString(), unchangedSummary.GetProperty("financial_account_id").GetString());
        Assert.Equal(summary.GetProperty("source_kind").GetString(), unchangedSummary.GetProperty("source_kind").GetString());
        Assert.Equal(summary.GetProperty("start_date").GetString(), unchangedSummary.GetProperty("start_date").GetString());
        Assert.Equal(summary.GetProperty("end_date").GetString(), unchangedSummary.GetProperty("end_date").GetString());
        Assert.Equal(summary.GetProperty("status").GetString(), unchangedSummary.GetProperty("status").GetString());
        Assert.Equal(summary.GetProperty("invalid_unskipped_count").GetInt32(), unchangedSummary.GetProperty("invalid_unskipped_count").GetInt32());
        Assert.Equal(parserConfigBefore, await StringScalarAsync($"SELECT parser_config::text FROM import_reviews WHERE id = '{reviewId}'"));
        Assert.Equal(fingerprintBefore, await StringScalarAsync($"SELECT statement_fingerprint FROM import_reviews WHERE id = '{reviewId}'"));
        Assert.Equal(expiresAtBefore, await StringScalarAsync($"SELECT expires_at::text FROM import_reviews WHERE id = '{reviewId}'"));
        Assert.Equal(rowsBeforeRaw, unchanged.GetProperty("page").GetProperty("rows").GetRawText());

        var patchedResponse = await SendAsync(Patch(
            $"{ReviewsPath}/{reviewId}",
            Json(new
            {
                expected_review_version = versionBefore,
                decisions = new[]
                {
                    new
                    {
                        row_id = rowsBefore[0].GetProperty("row_id").GetGuid(),
                        skip = true,
                    },
                },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, patchedResponse.StatusCode);
        var patched = await BodyAsync(patchedResponse);
        Assert.Equal(versionBefore + 1, patched.GetProperty("version").GetInt32());
        Assert.Equal(totalBefore, patched.GetProperty("total_rows").GetInt32());
        Assert.Equal(importedBefore - 1, patched.GetProperty("imported_count").GetInt32());
        Assert.Equal(suppressedBefore, patched.GetProperty("suppressed_count").GetInt32());
        Assert.Equal(skippedBefore + 1, patched.GetProperty("skipped_count").GetInt32());
        Assert.NotEqual(hashBefore, patched.GetProperty("summary_hash").GetString());
        Assert.Equal(parserConfigBefore, await StringScalarAsync($"SELECT parser_config::text FROM import_reviews WHERE id = '{reviewId}'"));
        Assert.Equal(fingerprintBefore, await StringScalarAsync($"SELECT statement_fingerprint FROM import_reviews WHERE id = '{reviewId}'"));
        Assert.Equal(expiresAtBefore, await StringScalarAsync($"SELECT expires_at::text FROM import_reviews WHERE id = '{reviewId}'"));

        var reread = await BodyAsync(await SendAsync(Get($"{ReviewsPath}/{reviewId}", session: session)));
        var rereadSummary = reread.GetProperty("summary");
        Assert.Equal(versionBefore + 1, rereadSummary.GetProperty("version").GetInt32());
        Assert.Equal(summary.GetProperty("financial_account_id").GetString(), rereadSummary.GetProperty("financial_account_id").GetString());
        Assert.Equal(summary.GetProperty("source_kind").GetString(), rereadSummary.GetProperty("source_kind").GetString());
        Assert.Equal(summary.GetProperty("start_date").GetString(), rereadSummary.GetProperty("start_date").GetString());
        Assert.Equal(summary.GetProperty("end_date").GetString(), rereadSummary.GetProperty("end_date").GetString());
        Assert.Equal(totalBefore, rereadSummary.GetProperty("total_rows").GetInt32());
        Assert.Equal(importedBefore - 1, rereadSummary.GetProperty("imported_count").GetInt32());
        Assert.Equal(suppressedBefore, rereadSummary.GetProperty("suppressed_count").GetInt32());
        Assert.Equal(skippedBefore + 1, rereadSummary.GetProperty("skipped_count").GetInt32());
        var rowsAfter = Rows(reread);
        Assert.Equal(snapshots.Length, rowsAfter.Length);
        var skippedRow = Assert.Single(rowsAfter, candidate => candidate.GetProperty("row_id").GetGuid() == snapshots[0].RowId);
        Assert.True(skippedRow.GetProperty("skip").GetBoolean());
        var untouchedRow = Assert.Single(rowsAfter, candidate => candidate.GetProperty("row_id").GetGuid() == snapshots[1].RowId);
        Assert.False(untouchedRow.GetProperty("skip").GetBoolean());
        foreach (var snapshot in snapshots)
        {
            var row = Assert.Single(rowsAfter, candidate => candidate.GetProperty("row_id").GetGuid() == snapshot.RowId);
            Assert.Equal(snapshot.SourceLine, row.GetProperty("source_line").GetInt32());
            Assert.Equal(snapshot.SourcePage, row.GetProperty("source_page").GetRawText());
            Assert.Equal(snapshot.SourceRowNumber, row.GetProperty("source_row_number").GetInt32());
            Assert.Equal(snapshot.TransactionDate, row.GetProperty("transaction_date").GetString());
            Assert.Equal(snapshot.Description, row.GetProperty("description").GetString());
            Assert.Equal(snapshot.Amount, row.GetProperty("amount").GetString());
            Assert.Equal(snapshot.RawValues, row.GetProperty("raw_values").GetRawText());
            Assert.Equal(snapshot.Validation, row.GetProperty("validation_codes").GetRawText());
            Assert.Equal(snapshot.Warnings, row.GetProperty("warnings").GetRawText());
            Assert.Equal(snapshot.DuplicateState, row.GetProperty("duplicate_state").GetString());
            Assert.Equal(snapshot.WithinFileState, row.GetProperty("within_file_state").GetString());
            Assert.Equal(snapshot.Override, row.GetProperty("override").GetBoolean());
        }
    }

    [Fact]
    public async Task PrepareDoesNotMutateSeededFinanceData()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var tagResponse = await SendAsync(Post("/v1/finance/merchant-tags", Json(new { display_name = "SeedTag" }), session: session));
        Assert.Equal(HttpStatusCode.Created, tagResponse.StatusCode);
        var tagId = (await BodyAsync(tagResponse)).GetProperty("id").GetGuid();

        const string preamble = "one\ntwo\nthree\nfour\nfive\nsix\n" + "Date,Amount,Balance,Description\n";
        var seedFile = Encoding.UTF8.GetBytes(preamble + "2025/01/02,-10.00,0.00,Seed merchant\n");
        var seed = await PrepareAsync(session, accountId, seedFile, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));
        var seedReviewId = seed.GetProperty("summary").GetProperty("id").GetGuid();
        var seedRowId = Rows(seed)[0].GetProperty("row_id").GetGuid();
        var tagged = await SendAsync(Patch($"{ReviewsPath}/{seedReviewId}", Json(new { expected_review_version = Version(seed), decisions = new[] { new { row_id = seedRowId, selected_tag_ids = new[] { tagId } }, }, }), session: session));
        Assert.Equal(HttpStatusCode.OK, tagged.StatusCode);
        var seedPatched = await BodyAsync(tagged);
        var seedCommit = await SendAsync(Post($"{ReviewsPath}/{seedReviewId}/commit", Json(CommitBody(seedPatched)), session: session));
        Assert.Equal(HttpStatusCode.Created, seedCommit.StatusCode);
        var statementId = (await BodyAsync(seedCommit)).GetProperty("statement").GetProperty("id").GetGuid();
        var detail = await BodyAsync(await SendAsync(Get($"{ImportsPath}/{statementId}", session: session)));
        var transactionId = detail.GetProperty("rows")[0].GetProperty("id").GetGuid();
        Assert.Contains(tagId, detail.GetProperty("rows")[0].GetProperty("tags").EnumerateArray().Select(tag => tag.GetProperty("id").GetGuid()));

        var categories = await BodyAsync(await SendAsync(Get("/v1/finance/categories", session: session)));
        var uncategorised = categories.GetProperty("categories").EnumerateArray().Single(category => category.GetProperty("display_name").GetString() == "Uncategorised");
        var categoryId = uncategorised.GetProperty("id").GetGuid();
        var categoriesBeforeRaw = categories.GetRawText();
        var tagsBeforeRaw = (await BodyAsync(await SendAsync(Get("/v1/finance/merchant-tags", session: session)))).GetRawText();
        var statementBeforeRaw = (await BodyAsync(await SendAsync(Get($"{ImportsPath}/{statementId}", session: session)))).GetProperty("statement").GetRawText();

        var categoryCountBefore = await CountRowsAsync("categories");
        var tagCountBefore = await CountRowsAsync("merchant_tags");
        var tagLinkCountBefore = await CountRowsAsync("transaction_merchant_tags");
        var importVersionBefore = await ScalarAsync($"SELECT import_version FROM financial_accounts WHERE id = '{accountId}'");
        var transactionCountBefore = await CountRowsAsync("transactions");
        var transactionPredicate =
            $"id = '{transactionId}' AND financial_account_id = '{accountId}' " +
            $"AND category_id = '{categoryId}' AND description = 'Seed merchant' " +
            "AND amount = -10.00 AND transaction_date = DATE '2025-01-02' " +
            "AND is_hidden_by_user = FALSE AND version = 1";
        var tagPredicate = $"id = '{tagId}' AND display_name = 'SeedTag'";
        var categoryPredicate = $"id = '{categoryId}' AND display_name = 'Uncategorised'";
        var tagLinkPredicate = $"transaction_id = '{transactionId}' AND merchant_tag_id = '{tagId}'";
        Assert.Equal(1L, await CountWhereAsync("transactions", transactionPredicate));
        Assert.Equal(1L, await CountWhereAsync("merchant_tags", tagPredicate));
        Assert.Equal(1L, await CountWhereAsync("categories", categoryPredicate));
        Assert.Equal(1L, await CountWhereAsync("transaction_merchant_tags", tagLinkPredicate));

        var secondFile = Encoding.UTF8.GetBytes(preamble + "2025/01/03,-20.00,0.00,Second merchant\n");
        var second = await PrepareAsync(session, accountId, secondFile, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));
        Assert.Equal(1, second.GetProperty("summary").GetProperty("total_rows").GetInt32());

        Assert.Equal(transactionCountBefore, await CountRowsAsync("transactions"));
        Assert.Equal(1L, await CountRowsAsync("statement_imports"));
        Assert.Equal(importVersionBefore, await ScalarAsync($"SELECT import_version FROM financial_accounts WHERE id = '{accountId}'"));
        Assert.Equal(categoriesBeforeRaw, (await BodyAsync(await SendAsync(Get("/v1/finance/categories", session: session)))).GetRawText());
        Assert.Equal(tagsBeforeRaw, (await BodyAsync(await SendAsync(Get("/v1/finance/merchant-tags", session: session)))).GetRawText());
        Assert.Equal(statementBeforeRaw, (await BodyAsync(await SendAsync(Get($"{ImportsPath}/{statementId}", session: session)))).GetProperty("statement").GetRawText());
        Assert.Equal(categoryCountBefore, await CountRowsAsync("categories"));
        Assert.Equal(tagCountBefore, await CountRowsAsync("merchant_tags"));
        Assert.Equal(tagLinkCountBefore, await CountRowsAsync("transaction_merchant_tags"));
        Assert.Equal(1L, await CountWhereAsync("transactions", transactionPredicate));
        Assert.Equal(1L, await CountWhereAsync("merchant_tags", tagPredicate));
        Assert.Equal(1L, await CountWhereAsync("categories", categoryPredicate));
        Assert.Equal(1L, await CountWhereAsync("transaction_merchant_tags", tagLinkPredicate));
        Assert.Equal(1L, await CountWhereAsync("statement_imports", $"id = '{statementId}' AND financial_account_id = '{accountId}'"));
        Assert.Equal(1L, await CountRowsAsync("import_reviews"));
    }

    [Fact]
    public async Task RepeatedCommitReplaysTheSameReceipt()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: false).Bytes;
        var review = await PrepareAsync(session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();

        var first = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstId = (await BodyAsync(first)).GetProperty("statement").GetProperty("id").GetGuid();

        var second = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await BodyAsync(second);
        Assert.True(secondBody.GetProperty("replayed").GetBoolean());
        Assert.Equal(firstId, secondBody.GetProperty("statement").GetProperty("id").GetGuid());
        Assert.Equal(3L, await CountRowsAsync("transactions"));
        Assert.Equal(1L, await CountRowsAsync("statement_imports"));
    }

    [Fact]
    public async Task EntirelySuppressedStatementCommitsWithZeroNewRowsAfterStatementOverride()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: false).Bytes;
        var first = await PrepareAsync(session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var firstCommit = await SendAsync(Post($"{ReviewsPath}/{first.GetProperty("summary").GetProperty("id").GetGuid()}/commit", Json(CommitBody(first)), session: session));
        Assert.Equal(HttpStatusCode.Created, firstCommit.StatusCode);

        var second = await PrepareAsync(session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        Assert.Equal(0, second.GetProperty("summary").GetProperty("imported_count").GetInt32());
        Assert.Equal(3, second.GetProperty("summary").GetProperty("suppressed_count").GetInt32());

        var reviewId = second.GetProperty("summary").GetProperty("id").GetGuid();
        var repeat = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(second)), session: session));
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        Assert.Equal("statement_repeat", await ErrorKindAsync(repeat));

        var patch = await SendAsync(Patch($"{ReviewsPath}/{reviewId}", Json(new { expected_review_version = Version(second), decisions = Array.Empty<object>(), statement_override = true, }), session: session));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await BodyAsync(patch);
        var commit = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(patched)), session: session));
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
        var statement = (await BodyAsync(commit)).GetProperty("statement");
        Assert.Equal(0, statement.GetProperty("imported_count").GetInt32());
        Assert.Equal(3, statement.GetProperty("suppressed_count").GetInt32());
        Assert.Equal(3L, await CountRowsAsync("transactions"));
        Assert.Equal(2L, await CountRowsAsync("statement_imports"));
    }

    [Fact]
    public async Task WorkspaceClassificationAndHiddenLayersDoNotReleaseImportOccurrences()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: false).Bytes;
        var first = await PrepareAsync(session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var firstId = first.GetProperty("summary").GetProperty("id").GetGuid();
        var firstCommit = await SendAsync(Post($"{ReviewsPath}/{firstId}/commit", Json(CommitBody(first)), session: session));
        Assert.Equal(HttpStatusCode.Created, firstCommit.StatusCode);
        var statementId = (await BodyAsync(firstCommit)).GetProperty("statement").GetProperty("id").GetGuid();
        var statementDetail = await SendAsync(Get($"{ImportsPath}/{statementId}", session: session));
        var transactionId = (await BodyAsync(statementDetail)).GetProperty("rows")[0].GetProperty("id").GetGuid();
        var identityBefore = await StringScalarAsync($"SELECT identity_key FROM transactions WHERE customer_id = '{customerId}' AND id = '{transactionId}'");

        var categoriesResponse = await SendAsync(Get("/v1/finance/categories", session: session));
        var categoryId = (await BodyAsync(categoriesResponse)).GetProperty("categories").EnumerateArray().First(category => category.GetProperty("kind").GetString() == "Expense").GetProperty("id").GetGuid();
        var tagResponse = await SendAsync(Post("/v1/finance/merchant-tags", Json(new { display_name = "Synthetic overlap tag" }), session: session));
        Assert.Equal(HttpStatusCode.Created, tagResponse.StatusCode);
        var tagId = (await BodyAsync(tagResponse)).GetProperty("id").GetGuid();
        var edit = await SendAsync(Patch(
            $"/v1/finance/transactions/{transactionId}",
            Json(new
            {
                expected_version = 1,
                changes = new { category_id = categoryId, add_tag_ids = new[] { tagId }, is_hidden_by_user = true },
                confirm = true,
            }), session: session));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        var hideParent = await SendAsync(Post($"{ImportsPath}/{statementId}/hide", Json(new { expected_version = 1, expected_transaction_count = 3, confirm = true }), session: session));
        Assert.Equal(HttpStatusCode.OK, hideParent.StatusCode);

        var second = await PrepareAsync(session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        Assert.Equal(0, second.GetProperty("summary").GetProperty("imported_count").GetInt32());
        Assert.Equal(3, second.GetProperty("summary").GetProperty("suppressed_count").GetInt32());
        var secondId = second.GetProperty("summary").GetProperty("id").GetGuid();
        var overrideResponse = await SendAsync(Patch($"{ReviewsPath}/{secondId}", Json(new { expected_review_version = Version(second), decisions = Array.Empty<object>(), statement_override = true }), session: session));
        Assert.Equal(HttpStatusCode.OK, overrideResponse.StatusCode);
        var overridden = await BodyAsync(overrideResponse);
        var secondCommit = await SendAsync(Post($"{ReviewsPath}/{secondId}/commit", Json(CommitBody(overridden)), session: session));
        Assert.Equal(HttpStatusCode.Created, secondCommit.StatusCode);
        Assert.Equal(0, (await BodyAsync(secondCommit)).GetProperty("statement").GetProperty("imported_count").GetInt32());

        Assert.Equal(3L, await CountRowsAsync("transactions"));
        Assert.Equal(identityBefore, await StringScalarAsync($"SELECT identity_key FROM transactions WHERE customer_id = '{customerId}' AND id = '{transactionId}'"));
        Assert.Equal(1L, await ScalarAsync($"""
            SELECT COUNT(*) FROM transactions
            WHERE customer_id = '{customerId}' AND id = '{transactionId}'
              AND category_id = '{categoryId}' AND is_hidden_by_user = TRUE
            """));
        Assert.Equal(1L, await ScalarAsync($"""
            SELECT COUNT(*) FROM transaction_merchant_tags
            WHERE customer_id = '{customerId}' AND transaction_id = '{transactionId}'
              AND merchant_tag_id = '{tagId}'
            """));
    }

    [Fact]
    public async Task HideRestorePreservesIndividualFlagAndDeleteReleasesIdentities()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var file = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: false).Bytes;
        var review = await PrepareAsync(session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var commit = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session));
        var statementId = (await BodyAsync(commit)).GetProperty("statement").GetProperty("id").GetGuid();

        var transactionId = (await BodyAsync(await SendAsync(Get($"{ImportsPath}/{statementId}", session: session)))).GetProperty("rows")[0].GetProperty("id").GetGuid();
        await ExecuteSqlAsync($"UPDATE transactions SET is_hidden_by_user = TRUE WHERE id = '{transactionId}'");

        var detail = await BodyAsync(await SendAsync(Get($"{ImportsPath}/{statementId}", session: session)));
        var summary = detail.GetProperty("statement");
        var expectedVersion = summary.GetProperty("version").GetInt32();
        var expectedCount = detail.GetProperty("expected_transaction_count").GetInt64();

        var hide = await SendAsync(Post($"{ImportsPath}/{statementId}/hide", Json(new { expected_version = expectedVersion, expected_transaction_count = expectedCount, confirm = true }), session: session));
        Assert.Equal(HttpStatusCode.OK, hide.StatusCode);
        var hidden = await BodyAsync(hide);
        Assert.True(hidden.GetProperty("is_hidden").GetBoolean());
        Assert.Equal(3, hidden.GetProperty("live_hidden_count").GetInt32());

        var restore = await SendAsync(Post($"{ImportsPath}/{statementId}/restore", Json(new { expected_version = hidden.GetProperty("version").GetInt32(), expected_transaction_count = expectedCount, confirm = true, }), session: session));
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        var restored = await BodyAsync(restore);
        Assert.False(restored.GetProperty("is_hidden").GetBoolean());
        Assert.Equal(1, restored.GetProperty("live_hidden_count").GetInt32());

        var hiddenFlag = await SendAsync(Get($"{ImportsPath}/{statementId}", session: session));
        var hiddenFlagBody = await BodyAsync(hiddenFlag);
        Assert.True(hiddenFlagBody.GetProperty("rows")[0].GetProperty("is_hidden_by_user").GetBoolean());

        var deleted = await SendAsync(DeleteJson($"{ImportsPath}/{statementId}", Json(new { expected_version = restored.GetProperty("version").GetInt32(), expected_transaction_count = expectedCount, confirm = true, }), session: session));
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.True((await BodyAsync(deleted)).GetProperty("deleted").GetBoolean());
        Assert.Equal(0L, await CountRowsAsync("statement_imports"));
        Assert.Equal(0L, await CountRowsAsync("transactions"));

        var reimport = await PrepareAsync(session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        Assert.Equal(3, reimport.GetProperty("summary").GetProperty("imported_count").GetInt32());
    }

    [Fact]
    public async Task ForeignReviewAndStatementIdsReturnNotFound()
    {
        var (ownerSession, _) = await NewCustomerAsync();
        var (intruderSession, _) = await NewCustomerAsync();
        var ownerAccount = await CreateAccountAsync(ownerSession);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(ownerSession, ownerAccount, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();

        var probe = await SendAsync(Get($"{ReviewsPath}/{reviewId}", session: intruderSession));
        Assert.Equal(HttpStatusCode.NotFound, probe.StatusCode);
        var foreignInspect = await SendAsync(MultipartPost(InspectPath, file, Json(new { financial_account_id = ownerAccount, source_kind = "fnb" }), session: intruderSession));
        Assert.Equal(HttpStatusCode.NotFound, foreignInspect.StatusCode);
    }

    [Fact]
    public async Task CommitToMissingOrForeignReviewDoesNotSeedCategories()
    {
        var (ownerSession, _) = await NewCustomerAsync();
        var (intruderSession, intruderCustomerId) = await NewCustomerAsync();
        var ownerAccount = await CreateAccountAsync(ownerSession);
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var review = await PrepareAsync(ownerSession, ownerAccount, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var commitBody = Json(CommitBody(review));

        async Task<long> IntruderCategoryCountAsync() => await ScalarAsync(
            $"SELECT COUNT(*) FROM categories WHERE customer_id = '{intruderCustomerId}'");
        async Task<long> IntruderSeededMarkerAsync() => await ScalarAsync(
            "SELECT COUNT(*) FROM customers " +
            $"WHERE id = '{intruderCustomerId}' AND categories_seeded_at IS NOT NULL");

        Assert.Equal(0L, await IntruderCategoryCountAsync());
        Assert.Equal(0L, await IntruderSeededMarkerAsync());

        var missing = await SendAsync(Post($"{ReviewsPath}/{Guid.NewGuid()}/commit", commitBody, session: intruderSession));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("not_found", await ErrorKindAsync(missing));
        Assert.Equal(0L, await IntruderCategoryCountAsync());
        Assert.Equal(0L, await IntruderSeededMarkerAsync());

        var foreign = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", commitBody, session: intruderSession));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("not_found", await ErrorKindAsync(foreign));
        Assert.Equal(0L, await IntruderCategoryCountAsync());
        Assert.Equal(0L, await IntruderSeededMarkerAsync());

        Assert.Equal(0L, await CountRowsAsync("statement_imports"));
        Assert.Equal(0L, await CountRowsAsync("transactions"));
        Assert.Equal(0L, await CountRowsAsync("import_commit_receipts"));
        var reread = await BodyAsync(await SendAsync(Get($"{ReviewsPath}/{reviewId}", session: ownerSession)));
        Assert.Equal(Version(review), reread.GetProperty("summary").GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task SourceCanaryNeverReachesLogs()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var csv = "one\ntwo\nthree\nfour\nfive\nsix\nDate,Amount,Balance,Description\n" + "2024/01/02,-10.00,0.00,CanaryDescription_7731\n" + "2024/01/03,-11.00,0.00,Second row\n";
        var file = System.Text.Encoding.UTF8.GetBytes(csv);
        var review = await PrepareAsync(session, accountId, file, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31), sourceKind: "fnb");
        var commit = await SendAsync(Post($"{ReviewsPath}/{review.GetProperty("summary").GetProperty("id").GetGuid()}/commit", Json(CommitBody(review)), session: session));
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
        Assert.False(Factory.LogCapture.Contains("Canary"));
        Assert.False(Factory.LogCapture.Contains("CanaryDescription_7731"));
    }

    [Fact]
    public async Task ReviewSuggestionsUseOwnedTagNamesAsCompleteTokens()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var tagResponse = await SendAsync(Post("/v1/finance/merchant-tags", Json(new { display_name = "Woolworths" }), session: session));
        Assert.Equal(HttpStatusCode.Created, tagResponse.StatusCode);
        var tagId = (await BodyAsync(tagResponse)).GetProperty("id").GetGuid();

        var csv = "one\ntwo\nthree\nfour\nfive\nsix\n" + "Date,Amount,Balance,Description\n" + "2024/01/02,-10.00,0.00,Woolworths groceries\n" + "2024/01/03,-11.00,0.00,WoolworthsX groceries\n";
        var review = await PrepareAsync(session, accountId, System.Text.Encoding.UTF8.GetBytes(csv), new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        var rows = Rows(review);
        Assert.Contains(tagId.ToString(), rows[0].GetProperty("suggested_tag_ids").EnumerateArray().Select(value => value.GetString()));
        Assert.DoesNotContain(tagId.ToString(), rows[1].GetProperty("suggested_tag_ids").EnumerateArray().Select(value => value.GetString()));
        Assert.Empty(rows[0].GetProperty("selected_tag_ids").EnumerateArray());
    }

    [Fact]
    public async Task NearMatchIsAWarningAndDoesNotSuppress()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var firstCsv = "one\ntwo\nthree\nfour\nfive\nsix\n" + "Date,Amount,Balance,Description\n" + "2025/01/02,-10.00,0.00,Coffee shop\n";
        var first = await PrepareAsync(session, accountId, System.Text.Encoding.UTF8.GetBytes(firstCsv), new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));
        var firstCommit = await SendAsync(Post($"{ReviewsPath}/{first.GetProperty("summary").GetProperty("id").GetGuid()}/commit", Json(CommitBody(first)), session: session));
        Assert.Equal(HttpStatusCode.Created, firstCommit.StatusCode);

        var secondCsv = "one\ntwo\nthree\nfour\nfive\nsix\n" + "Date,Amount,Balance,Description\n" + "2025/01/03,-10.00,0.00,Coffee shop\n";
        var second = await PrepareAsync(session, accountId, System.Text.Encoding.UTF8.GetBytes(secondCsv), new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));
        var row = Rows(second)[0];
        Assert.Equal(1, second.GetProperty("summary").GetProperty("possible_duplicate_count").GetInt32());
        Assert.Equal("none", row.GetProperty("duplicate_state").GetString());
        Assert.NotEmpty(row.GetProperty("near_candidates").EnumerateArray());
    }

    [Fact]
    public async Task WithinFileRepeatsRequireAGroupDecision()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var csv = "one\ntwo\nthree\nfour\nfive\nsix\n" + "Date,Amount,Balance,Description\n" + "2025/01/02,-10.00,0.00,Identical coffee\n" + "2025/01/02,-10.00,0.00,Identical coffee\n";
        var review = await PrepareAsync(session, accountId, System.Text.Encoding.UTF8.GetBytes(csv), new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));
        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        var rows = Rows(review);
        Assert.All(rows, row => Assert.Equal("pending", row.GetProperty("within_file_state").GetString()));

        var blocked = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session));
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal("duplicate_decision_required", await ErrorKindAsync(blocked));

        var patch = await SendAsync(Patch(
            $"{ReviewsPath}/{reviewId}",
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
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await BodyAsync(patch);
        var commit = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(patched)), session: session));
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
        Assert.Equal(2, (await BodyAsync(commit)).GetProperty("statement").GetProperty("imported_count").GetInt32());
        Assert.Equal(2L, await CountRowsAsync("transactions"));
    }

    [Fact]
    public async Task EmptyDescriptionAndLiteralFallbackHaveDifferentIdentities()
    {
        var (session, _) = await NewCustomerAsync();
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

        var empty = await PrepareAsync(session, accountId, System.Text.Encoding.UTF8.GetBytes("Date,Description,Amount\n2025-01-02,,-10.00\n"), new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), sourceKind: "generic_csv", parserConfig: config);
        var emptyCommit = await SendAsync(Post($"{ReviewsPath}/{empty.GetProperty("summary").GetProperty("id").GetGuid()}/commit", Json(CommitBody(empty)), session: session));
        Assert.Equal(HttpStatusCode.Created, emptyCommit.StatusCode);

        var literal = await PrepareAsync(
            session, accountId,
            System.Text.Encoding.UTF8.GetBytes("Date,Description,Amount\n2025-01-02,No description,-10.00\n"),
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31),
            sourceKind: "generic_csv", parserConfig: config);
        Assert.Equal(1, literal.GetProperty("summary").GetProperty("imported_count").GetInt32());
        Assert.Equal(0, literal.GetProperty("summary").GetProperty("suppressed_count").GetInt32());

        var literalCommit = await SendAsync(Post($"{ReviewsPath}/{literal.GetProperty("summary").GetProperty("id").GetGuid()}/commit", Json(CommitBody(literal)), session: session));
        Assert.Equal(HttpStatusCode.Created, literalCommit.StatusCode);

        var caseVariant = await PrepareAsync(
            session, accountId,
            System.Text.Encoding.UTF8.GetBytes("Date,Description,Amount\n2025-01-02,no description,-10.00\n"),
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31),
            sourceKind: "generic_csv", parserConfig: config);
        Assert.Equal(0, caseVariant.GetProperty("summary").GetProperty("imported_count").GetInt32());
        Assert.Equal(1, caseVariant.GetProperty("summary").GetProperty("suppressed_count").GetInt32());
    }

    [Fact]
    public async Task SkippingTheMatchedOccurrenceRepairsPairingBeforeCommit()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var preamble = "one\ntwo\nthree\nfour\nfive\nsix\n" + "Date,Amount,Balance,Description\n";
        var firstFile = System.Text.Encoding.UTF8.GetBytes(preamble + "2025/01/02,-10.00,0.00,Same coffee\n");
        var first = await PrepareAsync(session, accountId, firstFile, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));
        var firstCommit = await SendAsync(Post($"{ReviewsPath}/{first.GetProperty("summary").GetProperty("id").GetGuid()}/commit", Json(CommitBody(first)), session: session));
        Assert.Equal(HttpStatusCode.Created, firstCommit.StatusCode);

        var secondFile = System.Text.Encoding.UTF8.GetBytes(preamble + "2025/01/02,-10.00,0.00,Same coffee\n" + "2025/01/02,-10.00,0.00,Same coffee\n");
        var second = await PrepareAsync(session, accountId, secondFile, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));
        Assert.Equal(1, second.GetProperty("summary").GetProperty("imported_count").GetInt32());
        Assert.Equal(1, second.GetProperty("summary").GetProperty("suppressed_count").GetInt32());
        var rows = Rows(second);
        var reviewId = second.GetProperty("summary").GetProperty("id").GetGuid();

        var skipped = await SendAsync(Patch(
            $"{ReviewsPath}/{reviewId}",
            Json(new
            {
                expected_review_version = Version(second),
                decisions = new[]
                {
                    new
                    {
                        row_id = rows[0].GetProperty("row_id").GetGuid(),
                        skip = true,
                        within_file_state = "skip",
                    },
                },
            }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, skipped.StatusCode);
        var patched = await BodyAsync(skipped);
        Assert.Equal(0, patched.GetProperty("imported_count").GetInt32());
        Assert.Equal(1, patched.GetProperty("suppressed_count").GetInt32());
        Assert.Equal(1, patched.GetProperty("skipped_count").GetInt32());

        var reread = await BodyAsync(await SendAsync(Get($"{ReviewsPath}/{reviewId}", session: session)));
        var rereadRows = Rows(reread);
        Assert.Equal("suppressed", rereadRows[1].GetProperty("duplicate_state").GetString());
        Assert.Equal("none", rereadRows[1].GetProperty("within_file_state").GetString());

        var commit = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(patched)), session: session));
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
        Assert.Equal(0, (await BodyAsync(commit)).GetProperty("statement").GetProperty("imported_count").GetInt32());
        Assert.Equal(1L, await CountRowsAsync("transactions"));
    }

    [Fact]
    public async Task TenThousandRowGenericImportPreparesWithoutCombinatorialWork()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        StringBuilder builder = new StringBuilder("Date,Description,Amount\n");
        for (var index = 0; index < 10_000; index++)
        {
            var date = new DateOnly(2025, 1, 1).AddDays(index % 28);
            builder.Append(date.ToString("yyyy-MM-dd")).Append(",Merchant ").Append(index.ToString("D5")).Append(",-").Append((index + 1).ToString()).Append(".00\n");
        }
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

        var review = await PrepareAsync(session, accountId, Encoding.UTF8.GetBytes(builder.ToString()), new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), sourceKind: "generic_csv", parserConfig: config);

        Assert.Equal(10_000, review.GetProperty("summary").GetProperty("total_rows").GetInt32());
        Assert.Equal(10_000, review.GetProperty("summary").GetProperty("imported_count").GetInt32());
        Assert.Equal(0, review.GetProperty("summary").GetProperty("suppressed_count").GetInt32());

        var reviewId = review.GetProperty("summary").GetProperty("id").GetGuid();
        Stopwatch commitClock = Stopwatch.StartNew();
        var commit = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session));
        commitClock.Stop();
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
        Assert.Equal(10_000, (await BodyAsync(commit)).GetProperty("statement").GetProperty("imported_count").GetInt32());
        Assert.Equal(10_000L, await CountRowsAsync("transactions"));

        Stopwatch replayClock = Stopwatch.StartNew();
        var replay = await SendAsync(Post($"{ReviewsPath}/{reviewId}/commit", Json(CommitBody(review)), session: session));
        replayClock.Stop();
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await BodyAsync(replay)).GetProperty("replayed").GetBoolean());
        Assert.Equal(1L, await CountRowsAsync("statement_imports"));
        Assert.Equal(1L, await CountRowsAsync("import_commit_receipts"));

        Console.WriteLine($"10k import commit {commitClock.ElapsedMilliseconds}ms; " + $"receipt replay {replayClock.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task InconsistentCanonicalDigestAcrossUnpairedRowsFailsClosed()
    {
        var (session, customerId) = await NewCustomerAsync();
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
        var first = await PrepareAsync(session, accountId, Encoding.UTF8.GetBytes("Date,Description,Amount,ID\n2025-01-02,Shop one,-10.00,A\n"), new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), sourceKind: "generic_csv", parserConfig: config);
        var firstId = first.GetProperty("summary").GetProperty("id").GetGuid();
        var commit = await SendAsync(Post($"{ReviewsPath}/{firstId}/commit", Json(CommitBody(first)), session: session));
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);

        // Simulate a corrupted/foreign digest: the existing row's canonical
        // components differ from the digest, and its stable ID (A) conflicts
        // with the incoming row's stable ID (B) so the matcher would not pair
        // them. The whole-group canonical check must still fail closed.
        var incomingKey = StatementIdentity.ComputeIdentityKey(
            new DateOnly(2025, 1, 3), -2000, "shop two");
        await ExecuteSqlAsync($"UPDATE transactions SET identity_key = '{incomingKey}' WHERE customer_id = '{customerId}'");

        var secondCsv = "Date,Description,Amount,ID\n2025-01-03,Shop two,-20.00,B\n";
        var options = Json(new { financial_account_id = accountId, source_kind = "generic_csv", parser_config = config, start_date = "2025-01-01", end_date = "2025-01-31", currency = "ZAR", confirm_zar = true, });
        var response = await SendAsync(MultipartPost(ReviewsPath, Encoding.UTF8.GetBytes(secondCsv), options, session: session));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("identity_conflict", await ErrorKindAsync(response));
    }

    [Fact]
    public async Task CanonicalBackfillSuppressesAReimportOfALegacyRow()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var categories = await BodyAsync(await SendAsync(Get("/v1/finance/categories", session: session)));
        var categoryId = categories.GetProperty("categories").EnumerateArray().First(item => item.GetProperty("display_name").GetString() == "Groceries").GetProperty("id").GetGuid();
        Guid statementId = Guid.NewGuid();
        Guid transactionId = Guid.NewGuid();
        await ExecuteSqlAsync($"""
            INSERT INTO statement_imports (
                id, customer_id, financial_account_id, source_type, statement_label,
                statement_fingerprint, start_date, end_date, created_at, updated_at)
            VALUES (
                '{statementId}', '{customerId}', '{accountId}', 'csv', 'Legacy statement',
                'legacy-fingerprint', DATE '2025-01-01', DATE '2025-01-31', NOW(), NOW());
            INSERT INTO transactions (
                id, customer_id, financial_account_id, statement_import_id, transaction_date,
                description, normalized_description, amount, category_id, source_type,
                source_row_number, identity_key, occurrence_index, created_at, updated_at)
            VALUES (
                '{transactionId}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-02',
                'Legacy coffee', 'legacy coffee', -10.00, '{categoryId}', 'csv',
                1, 'legacy-key', 1, NOW(), NOW());
            """);

        // The startup migration has already recorded its marker; this test
        // simulates a populated legacy upgrade by clearing it for the isolated
        // test database.
        await ExecuteSqlAsync(
            "DELETE FROM data_migrations WHERE name = '0006_canonical_identity_backfill'");
        var db = Factory.CreateDb();
        await using (var connection = await db.OpenAsync())
        {
            await Db.BackfillCanonicalIdentitiesAsync(connection);
        }

        var csv = "one\ntwo\nthree\nfour\nfive\nsix\n" + "Date,Amount,Balance,Description\n" + "2025/01/02,-10.00,0.00,Legacy coffee\n";
        var review = await PrepareAsync(session, accountId, System.Text.Encoding.UTF8.GetBytes(csv), new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));

        Assert.Equal(0, review.GetProperty("summary").GetProperty("imported_count").GetInt32());
        Assert.Equal(1, review.GetProperty("summary").GetProperty("suppressed_count").GetInt32());
    }

    [Fact]
    public async Task ImportRoutesSetNoStore()
    {
        var (session, _) = await NewCustomerAsync();
        var response = await SendAsync(Get("/v1/finance/imports", session: session));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task BackfillHandlesMixedLegacyAndCanonicalKeys()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var categories = await BodyAsync(await SendAsync(Get("/v1/finance/categories", session: session)));
        var categoryId = categories.GetProperty("categories").EnumerateArray().First(item => item.GetProperty("display_name").GetString() == "Groceries").GetProperty("id").GetGuid();
        DateOnly date = new DateOnly(2025, 1, 2);
        const long cents = -1000;
        const string display = "Same canonical row";
        const string normalized = "same canonical row";
        var canonicalKey = StatementIdentity.ComputeIdentityKey(date, cents, normalized);
        var canonicalFingerprint = StatementIdentity.ComputeStatementFingerprint(
            [
                new ParsedStatementRow(
                    Guid.NewGuid(), 1, null, 1,
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["date"] = "2025-01-02",
                        ["description"] = display,
                        ["amount"] = "-10.00",
                    },
                    date, display, normalized, cents, [], [], null, null),
            ],
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));
        Guid firstStatement = Guid.NewGuid();
        Guid secondStatement = Guid.NewGuid();
        Guid firstTransaction = Guid.NewGuid();
        Guid secondTransaction = Guid.NewGuid();
        await ExecuteSqlAsync($"""
            INSERT INTO statement_imports (
                id, customer_id, financial_account_id, source_type, statement_label,
                statement_fingerprint, start_date, end_date, created_at, updated_at)
            VALUES
                ('{firstStatement}', '{customerId}', '{accountId}', 'csv', 'Legacy one',
                 'legacy-fingerprint-one', DATE '2025-01-01', DATE '2025-01-31',
                 NOW() - INTERVAL '2 minutes', NOW() - INTERVAL '2 minutes'),
                ('{secondStatement}', '{customerId}', '{accountId}', 'csv', 'Legacy two',
                 '{canonicalFingerprint}', DATE '2025-01-01', DATE '2025-01-31',
                 NOW() - INTERVAL '1 minute', NOW() - INTERVAL '1 minute');
            INSERT INTO transactions (
                id, customer_id, financial_account_id, statement_import_id, transaction_date,
                description, normalized_description, amount, category_id, source_type,
                source_row_number, identity_key, occurrence_index, created_at, updated_at)
            VALUES
                ('{firstTransaction}', '{customerId}', '{accountId}', '{firstStatement}',
                 DATE '2025-01-02', '{display}', '{normalized}', -10.00, '{categoryId}', 'csv',
                 1, 'legacy-one', 1, NOW() - INTERVAL '2 minutes', NOW() - INTERVAL '2 minutes'),
                ('{secondTransaction}', '{customerId}', '{accountId}', '{secondStatement}',
                 DATE '2025-01-02', '{display}', '{normalized}', -10.00, '{categoryId}', 'csv',
                 1, '{canonicalKey}', 1, NOW() - INTERVAL '1 minute', NOW() - INTERVAL '1 minute');
            """);
        await ExecuteSqlAsync("DELETE FROM data_migrations WHERE name = '0006_canonical_identity_backfill'");
        var db = Factory.CreateDb();
        await using (var connection = await db.OpenAsync())
        {
            await Db.BackfillCanonicalIdentitiesAsync(connection);
        }

        Assert.Equal(2L, await CountWhereAsync("transactions", $"identity_key = '{canonicalKey}'"));
        Assert.Equal(1L, await CountWhereAsync("transactions", $"identity_key = '{canonicalKey}' AND occurrence_index = 1"));
        Assert.Equal(1L, await CountWhereAsync("transactions", $"identity_key = '{canonicalKey}' AND occurrence_index = 2"));
        Assert.Equal(2L, await CountWhereAsync("statement_imports", $"statement_fingerprint = '{canonicalFingerprint}'"));
        Assert.Equal(1L, await CountWhereAsync("statement_imports", $"statement_fingerprint = '{canonicalFingerprint}' AND fingerprint_occurrence = 1"));
        Assert.Equal(1L, await CountWhereAsync("statement_imports", $"statement_fingerprint = '{canonicalFingerprint}' AND fingerprint_occurrence = 2"));
    }

    [Fact]
    public async Task GenericCsvInspectAndCommit()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var csv = "Date,Description,Amount\n" + "2025-01-02,\"Coffee, large\",-12.34\n" + "2025-01-03,Salary,1000.00\n";
        var file = System.Text.Encoding.UTF8.GetBytes(csv);
        var inspect = await InspectAsync(session, accountId, file, "generic_csv");
        Assert.Equal("generic_csv", inspect.GetProperty("detected_source_kind").GetString());
        Assert.True(inspect.GetProperty("mapping").TryGetProperty("header_line", out _));

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
        var review = await PrepareAsync(session, accountId, file, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), sourceKind: "generic_csv", parserConfig: config);
        Assert.Equal(2, review.GetProperty("summary").GetProperty("imported_count").GetInt32());
        var commit = await SendAsync(Post($"{ReviewsPath}/{review.GetProperty("summary").GetProperty("id").GetGuid()}/commit", Json(CommitBody(review)), session: session));
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
    }

    [Fact]
    public async Task PdfRejectedByParserCreatesNoRecordsAndLeaksNoCanary()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var file = BuildPdfWithHeaderlessCanaryPage();
        var inspectOptions = Json(new { financial_account_id = accountId, source_kind = "standard_bank_pdf", });

        var inspect = await SendAsync(MultipartPost(InspectPath, file, inspectOptions, session: session));
        Assert.Equal(HttpStatusCode.BadRequest, inspect.StatusCode);
        Assert.Equal("pdf_import_deferred", await ErrorKindAsync(inspect));
        var inspectText = await inspect.Content.ReadAsStringAsync();
        Assert.DoesNotContain("CanaryOmitted_7731", inspectText, StringComparison.Ordinal);
        Assert.DoesNotContain("BAD-DATE", inspectText, StringComparison.Ordinal);
        Assert.False(Factory.LogCapture.Contains("CanaryOmitted_7731"));

        var prepareOptions = Json(new { financial_account_id = accountId, source_kind = "standard_bank_pdf", start_date = "2025-01-01", end_date = "2025-01-31", currency = "ZAR", confirm_zar = true, });
        var prepare = await SendAsync(MultipartPost(ReviewsPath, file, prepareOptions, session: session));
        Assert.Equal(HttpStatusCode.BadRequest, prepare.StatusCode);
        Assert.Equal("pdf_import_deferred", await ErrorKindAsync(prepare));
        var prepareText = await prepare.Content.ReadAsStringAsync();
        Assert.DoesNotContain("CanaryOmitted_7731", prepareText, StringComparison.Ordinal);
        Assert.False(Factory.LogCapture.Contains("CanaryOmitted_7731"));

        Assert.Equal(0L, await CountRowsAsync("import_reviews"));
        Assert.Equal(0L, await CountRowsAsync("import_review_rows"));
        Assert.Equal(0L, await CountRowsAsync("statement_imports"));
        Assert.Equal(0L, await CountRowsAsync("transactions"));
        Assert.Equal(0L, await CountRowsAsync("import_commit_receipts"));
    }

    [Fact]
    public async Task PdfWithARasterCoverIsRejectedWithoutRecords()
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage([new PdfText(40, 800, "Synthetic Bank"), new PdfText(40, 780, "Synthetic statement")], 1, 50, 50);
        var file = pdf.Build();
        var inspectOptions = Json(new { financial_account_id = accountId, source_kind = "standard_bank_pdf", });

        var inspect = await SendAsync(MultipartPost(InspectPath, file, inspectOptions, session: session));
        Assert.Equal(HttpStatusCode.BadRequest, inspect.StatusCode);
        Assert.Equal("pdf_import_deferred", await ErrorKindAsync(inspect));

        var prepareOptions = Json(new { financial_account_id = accountId, source_kind = "standard_bank_pdf", start_date = "2025-01-01", end_date = "2025-01-31", currency = "ZAR", confirm_zar = true, });
        var prepare = await SendAsync(MultipartPost(ReviewsPath, file, prepareOptions, session: session));
        Assert.Equal(HttpStatusCode.BadRequest, prepare.StatusCode);
        Assert.Equal("pdf_import_deferred", await ErrorKindAsync(prepare));

        Assert.Equal(0L, await CountRowsAsync("import_reviews"));
        Assert.Equal(0L, await CountRowsAsync("import_review_rows"));
        Assert.Equal(0L, await CountRowsAsync("statement_imports"));
        Assert.Equal(0L, await CountRowsAsync("transactions"));
        Assert.Equal(0L, await CountRowsAsync("import_commit_receipts"));
    }

    [Fact]
    public async Task PdfInvalidCandidateIsDeferredBeforeReview()
    {
        await AssertPdfRejectionHasNoEffectsAsync("Invalid merchant", BuildPdfWithRecoverableInvalidRow());
    }

    [Fact]
    public async Task PdfDeferredResponseDoesNotExposeBalances()
    {
        List<PdfText> texts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 770, "Opening balance"),
            new(530, 770, "9,999.99"),
            new(100, 706, "Closing balance"),
            new(530, 706, "9,999.99"),
        };
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);
        await AssertPdfRejectionHasNoEffectsAsync("9,999.99", pdf.Build());
    }

    private static byte[] BuildPdfWithHeaderlessCanaryPage()
    {
        List<PdfText> second = new List<PdfText>
        {
            new(40, 780, "Statement period: 01 Jan 2025 to 31 Jan 2025"),
            new(40, 760, "BAD-DATE"),
            new(100, 760, "CanaryOmitted_7731"),
            new(300, 760, "-20.00"),
        };

        return StatementFixtureBuilder.BuildTwoPagePdf(StatementFixtureBuilder.HandAuthoredSmallPdfTexts(), second);
    }

    private static byte[] BuildPdfWithRecoverableInvalidRow()
    {
        List<PdfText> texts = new List<PdfText>
        {
            new(40, 800, "Date"),
            new(100, 800, "Description"),
            new(300, 800, "Payments"),
            new(400, 800, "Deposits"),
            new(470, 800, "Bank"),
            new(500, 800, "fees"),
            new(530, 800, "Balance"),
            new(40, 760, "01 Feb 2025"),
            new(100, 760, "Valid merchant"),
            new(300, 760, "10.00"),
            new(100, 742, "Invalid merchant"),
            new(300, 742, "20.00"),
        };
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        return pdf.Build();
    }

    [Fact]
    public async Task PdfMetadataPreambleIsRejectedWithoutEffects()
    {
        await AssertPdfRejectionHasNoEffectsAsync("PRIVATE_ACCOUNT_7731", BuildPdfWithMetadataPreamble());
    }

    [Fact]
    public async Task PdfConflictingHeaderBalanceIsRejectedWithoutEffects()
    {
        await AssertPdfRejectionHasNoEffectsAsync("9,999.99", BuildPdfWithConflictingHeaderBalance());
    }

    [Fact]
    public async Task PdfNegativeXRowIsRejectedWithoutEffects()
    {
        await AssertPdfRejectionHasNoEffectsAsync("UNMAPPED_CANARY_4421", BuildPdfWithNegativeXRow());
    }

    [Fact]
    public async Task PdfNegativeXTotalIsRejectedWithoutEffects()
    {
        await AssertPdfRejectionHasNoEffectsAsync("UNMAPPED_CANARY_4421", BuildPdfWithNegativeXTotal());
    }

    [Fact]
    public async Task PdfZeroDayCandidateIsDeferredBeforeReview()
    {
        await AssertPdfRejectionHasNoEffectsAsync("Zero day", BuildPdfWithZeroDayRow());
    }

    [Fact]
    public async Task PdfDisqualifiedTotalIsDeferredBeforeReview()
    {
        await AssertPdfRejectionHasNoEffectsAsync("Total payments", BuildPdfWithTotalBeforeSummary());
    }

    private async Task AssertPdfRejectionHasNoEffectsAsync(string canary, byte[] file)
    {
        var (session, _) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var importVersion = await ScalarAsync($"SELECT import_version FROM financial_accounts WHERE id = '{accountId}'");
        var accountCount = await CountRowsAsync("financial_accounts");

        var inspectOptions = Json(new { financial_account_id = accountId, source_kind = "standard_bank_pdf", });
        var inspect = await SendAsync(MultipartPost(InspectPath, file, inspectOptions, session: session));
        Assert.Equal(HttpStatusCode.BadRequest, inspect.StatusCode);
        var inspectText = await inspect.Content.ReadAsStringAsync();
        Assert.Contains("pdf_import_deferred", inspectText, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, inspectText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"preview\"", inspectText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"summary\"", inspectText, StringComparison.Ordinal);
        Assert.Equal(importVersion, await ScalarAsync($"SELECT import_version FROM financial_accounts WHERE id = '{accountId}'"));
        Assert.Equal(accountCount, await CountRowsAsync("financial_accounts"));
        await AssertNoImportEffectsAsync();
        Assert.False(Factory.LogCapture.Contains(canary));

        var prepareOptions = Json(new { financial_account_id = accountId, source_kind = "standard_bank_pdf", start_date = "2025-02-01", end_date = "2025-02-28", currency = "ZAR", confirm_zar = true, });
        var prepare = await SendAsync(MultipartPost(ReviewsPath, file, prepareOptions, session: session));
        Assert.Equal(HttpStatusCode.BadRequest, prepare.StatusCode);
        var prepareText = await prepare.Content.ReadAsStringAsync();
        Assert.Contains("pdf_import_deferred", prepareText, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, prepareText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"summary\"", prepareText, StringComparison.Ordinal);
        Assert.Equal(importVersion, await ScalarAsync($"SELECT import_version FROM financial_accounts WHERE id = '{accountId}'"));
        Assert.Equal(accountCount, await CountRowsAsync("financial_accounts"));
        await AssertNoImportEffectsAsync();
        Assert.False(Factory.LogCapture.Contains(canary));
    }

    private async Task AssertNoImportEffectsAsync()
    {
        Assert.Equal(0L, await CountRowsAsync("import_reviews"));
        Assert.Equal(0L, await CountRowsAsync("import_review_rows"));
        Assert.Equal(0L, await CountRowsAsync("statement_imports"));
        Assert.Equal(0L, await CountRowsAsync("transactions"));
        Assert.Equal(0L, await CountRowsAsync("import_commit_receipts"));
    }

    private static List<PdfText> PdfHeader() =>
    [
        new(40, 800, "Date"),
        new(100, 800, "Description"),
        new(300, 800, "Payments"),
        new(400, 800, "Deposits"),
        new(470, 800, "Bank"),
        new(500, 800, "fees"),
        new(530, 800, "Balance"),
    ];

    private static List<PdfText> PdfHeaderAt(double y, params double[] xs)
    {
        var labels = new[]
        {
            "Date", "Description", "Payments", "Deposits", "Bank", "fees", "Balance",
        };
        Assert.Equal(labels.Length, xs.Length);

        return labels.Select((label, index) => new PdfText(xs[index], y, label)).ToList();
    }

    private static void AddPdfPaymentRow(List<PdfText> texts,
                                         double y,
                                         string date,
                                         string description,
                                         string payment) =>
        texts.AddRange(
        [
            new PdfText(40, y, date),
            new PdfText(100, y, description),
            new PdfText(300, y, payment),
        ]);

    private static byte[] BuildPdfWithMetadataPreamble()
    {
        List<PdfText> texts = new List<PdfText>
        {
            new(40, 840, "Synthetic Bank"),
            new(40, 820, "Account: PRIVATE_ACCOUNT_7731"),
        };
        texts.AddRange(PdfHeader());
        AddPdfPaymentRow(texts, 760, "01 Feb 2025", "Valid row", "10.00");
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        return pdf.Build();
    }

    private static byte[] BuildPdfWithConflictingHeaderBalance()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        List<PdfText> first = new List<PdfText>(PdfHeader());
        AddPdfPaymentRow(first, 760, "01 Feb 2025", "First row", "10.00");
        pdf.AddPage(first);

        List<PdfText> middle = new List<PdfText>(PdfHeader());
        AddPdfPaymentRow(middle, 760, "02 Feb 2025", "Middle row", "20.00");
        middle.AddRange(PdfHeaderAt(700, 40, 100, 220, 300, 400, 430, 500));
        middle.AddRange([new PdfText(40, 680, "03 Feb 2025"), new PdfText(100, 680, "Balance only"), new PdfText(500, 680, "9,999.99"),]);
        pdf.AddPage(middle);

        List<PdfText> last = new List<PdfText>(PdfHeader());
        AddPdfPaymentRow(last, 760, "04 Feb 2025", "Last row", "40.00");
        pdf.AddPage(last);

        return pdf.Build();
    }

    private static byte[] BuildPdfWithNegativeXRow()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        List<PdfText> first = new List<PdfText>(PdfHeader());
        AddPdfPaymentRow(first, 760, "01 Feb 2025", "First row", "10.00");
        pdf.AddPage(first);

        List<PdfText> last = new List<PdfText>(PdfHeader());
        AddPdfPaymentRow(last, 760, "02 Feb 2025", "Last row", "20.00");
        last.Add(new PdfText(-95, 760, "UNMAPPED_CANARY_4421"));
        pdf.AddPage(last);

        return pdf.Build();
    }

    private static byte[] BuildPdfWithNegativeXTotal()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        List<PdfText> first = new List<PdfText>(PdfHeader());
        AddPdfPaymentRow(first, 760, "01 Feb 2025", "First row", "10.00");
        pdf.AddPage(first);

        List<PdfText> last = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
            new(-95, 706, "UNMAPPED_CANARY_4421"),
        };
        pdf.AddPage(last);

        return pdf.Build();
    }

    private static byte[] BuildPdfWithZeroDayRow()
    {
        List<PdfText> texts = new List<PdfText>(PdfHeader());
        AddPdfPaymentRow(texts, 760, "01 Feb 2025", "Valid row", "10.00");
        AddPdfPaymentRow(texts, 742, "00 Feb 2025", "Zero day", "20.00");
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        return pdf.Build();
    }

    private static byte[] BuildPdfWithTotalBeforeSummary()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        List<PdfText> first = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
        };
        pdf.AddPage(first);
        pdf.AddPage([new PdfText(40, 780, "Summary")]);

        return pdf.Build();
    }

    [Fact]
    public async Task PdfFixtureImportIsDeferredBeforeReview()
    {
        var fixture = StatementFixtureBuilder.BuildStandardBankPdf(4, 49, 66);
        await AssertPdfRejectionHasNoEffectsAsync("Synthetic merchant", fixture.Bytes);
    }
}
