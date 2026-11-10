using System.Globalization;
using System.Text;
using System.Text.Json;
using argus.Server.Data;
using argus.Server.Imports;
using argus.Server.Security;
using Npgsql;

namespace argus.Server.Http;

/// <summary>
/// Customer-scoped statement import surface. Every route uses the shared
/// finance guard, bounds its body before buffering, and maps failures to safe
/// fixed error codes. Financial source text never enters a log.
/// </summary>
public sealed class StatementImportApi(FinanceGuard guard,
                                       StatementImportStore store,
                                       PdfParserRunner pdfRunner,
                                       ParserAdmission parserAdmission,
                                       RateLimiter rateLimiter,
                                       ILoggerFactory loggerFactory)
{
    private readonly ILogger _audit = loggerFactory.CreateLogger("audit");
    private readonly ILogger _logger = loggerFactory.CreateLogger("finance");

    public void Map(WebApplication app)
    {
        app.MapPost("/v1/finance/imports/inspect", InspectAsync);
        app.MapPost("/v1/finance/imports/reviews", CreateReviewAsync);
        app.MapGet("/v1/finance/imports/reviews", ListReviewsAsync);
        app.MapGet("/v1/finance/imports/reviews/{id:guid}", GetReviewAsync);
        app.MapPatch("/v1/finance/imports/reviews/{id:guid}", PatchReviewAsync);
        app.MapPost("/v1/finance/imports/reviews/{id:guid}/refresh", RefreshReviewAsync);
        app.MapPost("/v1/finance/imports/reviews/{id:guid}/commit", CommitReviewAsync);
        app.MapPost("/v1/finance/imports/reviews/{id:guid}/abandon", AbandonReviewAsync);
        app.MapGet("/v1/finance/imports", ListHistoryAsync);
        app.MapGet("/v1/finance/imports/{id:guid}", GetImportAsync);
        app.MapPost("/v1/finance/imports/{id:guid}/hide", HideAsync);
        app.MapPost("/v1/finance/imports/{id:guid}/restore", RestoreAsync);
        app.MapDelete("/v1/finance/imports/{id:guid}", DeleteAsync);
    }

    private async Task<(Guid? CustomerId, IResult? Failure)> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken) =>
        await guard.AuthenticateAsync(context, cancellationToken);

    private async Task<IResult> GuardedAsync(Func<Task<IResult>> action, string operation)
    {
        try
        {
            return await action();
        }
        catch (StatementRejectedException exception)
        {
            return ParserError(exception.Reason);
        }
        catch (StatementRepeatException exception)
        {
            return Results.Json(new { error = ImportErrorCodes.StatementRepeat, existing_import_ids = exception.ExistingImportIds, }, statusCode: 409);
        }
        catch (ImportStoreException exception)
        {
            return StoreError(exception.Error);
        }
        catch (PostgresException exception)
        {
            _logger.LogError("finance_operation_failed operation={Operation} error_class={ErrorClass} sql_state={SqlState}", operation, "postgres", exception.SqlState);
            return Errors.Internal();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "finance_operation_failed operation={Operation} error_class={ErrorClass}", operation, exception.GetType().Name);
            return Errors.Internal();
        }
    }

    // --- Inspect and prepare -----------------------------------------------

    private Task<IResult> InspectAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null) return failure;

            if (!rateLimiter.CheckImportCustomer(customerId!.Value)) return Errors.RateLimited();

            var multipart = await MultipartImportReader.ReadAsync(context.Request, cancellationToken);
            var parsedOptions = JsonBody.ParseBytes<ImportOptionsRequest>(Encoding.UTF8.GetBytes(multipart.OptionsJson));
            if (!parsedOptions.Parsed) return Errors.InvalidRequest();

            var options = parsedOptions.Value!;
            if (options.FinancialAccountId is null || !StatementSourceKinds.TryParse(options.SourceKind, out var sourceKind) || (options.StartDate is null) != (options.EndDate is null))
            {
                return Errors.InvalidRequest();
            }

            DateOnly? start = null;
            DateOnly? end = null;
            if (options.StartDate is not null)
            {
                if (!TryDate(options.StartDate, out var parsedStart) || !TryDate(options.EndDate, out var parsedEnd) || parsedStart > parsedEnd)
                {
                    return Errors.InvalidRequest();
                }

                start = parsedStart;
                end = parsedEnd;
            }

            var account = await store.GetAccountAsync(customerId.Value, options.FinancialAccountId.Value, cancellationToken);
            if (account is null) return Errors.NotFound();

            if (account.IsArchived) return StoreError(ImportErrorCodes.AccountArchived);

            var result = await StatementParser.InspectAsync(multipart.FileBytes, sourceKind, options.ParserConfig, start, end, pdfRunner, parserAdmission, cancellationToken);

            return Results.Json(new
            {
                detected_source_kind = result.SourceType,
                limits = new
                {
                    max_file_bytes = ImportLimits.MaxFileBytes,
                    max_data_rows = ImportLimits.MaxDataRows,
                    max_pdf_pages = ImportLimits.MaxPdfPages,
                    max_description_scalars = ImportLimits.MaxDescriptionScalars,
                },
                proposed_start_date = DateString(result.ProposedStartDate),
                proposed_end_date = DateString(result.ProposedEndDate),
                mapping = result.MappingSuggestions,
                required_confirmations = result.RequiredConfirmations,
                warnings = result.Warnings,
                preview = result.Rows.Take(ImportLimits.MaxPreviewRows).Select(PreviewRowResponse.From),
                preview_truncated = result.Rows.Count > ImportLimits.MaxPreviewRows,
                can_prepare = result.Rows.Count > 0,
            });
        }, "imports_inspect");

    private Task<IResult> CreateReviewAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null) return failure;

            if (!rateLimiter.CheckImportCustomer(customerId!.Value)) return Errors.RateLimited();

            var multipart = await MultipartImportReader.ReadAsync(context.Request, cancellationToken);
            var parsedOptions = JsonBody.ParseBytes<ImportOptionsRequest>(Encoding.UTF8.GetBytes(multipart.OptionsJson));
            if (!parsedOptions.Parsed) return Errors.InvalidRequest();

            var options = parsedOptions.Value!;
            if (options.FinancialAccountId is null ||
                !StatementSourceKinds.TryParse(options.SourceKind, out var sourceKind) ||
                !TryDate(options.StartDate, out var start) ||
                !TryDate(options.EndDate, out var end) ||
                start > end ||
                !string.Equals(options.Currency, "ZAR", StringComparison.Ordinal) ||
                options.ConfirmZar != true)
            {
                return Errors.InvalidRequest();
            }

            var account = await store.GetAccountAsync(customerId.Value, options.FinancialAccountId.Value, cancellationToken);
            if (account is null) return Errors.NotFound();

            if (account.IsArchived) return StoreError(ImportErrorCodes.AccountArchived);

            var config = options.ParserConfig ?? new ParserConfig
            {
                Format = sourceKind == StatementSourceKind.GenericCsv ? "generic" : null,
            };
            var parsed = await StatementParser.PrepareAsync(multipart.FileBytes, sourceKind, config, start, end, pdfRunner, parserAdmission, cancellationToken);
            var detail = await store.CreateReviewAsync(customerId.Value, options.FinancialAccountId.Value, sourceKind, config, start, end, parsed, cancellationToken);

            _audit.LogInformation("audit.import_review_created customer_id={CustomerId} review_id={ReviewId} rows={Rows}", customerId.Value, detail.Summary.Id, detail.Summary.TotalRows);
            return Results.Json(ReviewResponse.From(detail), statusCode: 201);
        }, "imports_review_create");

    private Task<IResult> ListReviewsAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null) return failure;

            var reviews = await store.ListOpenReviewsAsync(customerId!.Value, cancellationToken);
            return Results.Json(new { reviews = reviews.Select(OpenReviewResponse.From).ToArray() });
        }, "imports_reviews_list");

    private Task<IResult> GetReviewAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null) return failure;

            var id = RouteGuid(context);
            if (id is null) return Errors.NotFound();

            int page = QueryInt(context, "page", 1);
            int pageSize = QueryInt(context, "page_size", ImportLimits.MaxPageSize);
            string sortDirection = context.Request.Query["sort_direction"].ToString();
            bool possibleDuplicatesOnly = string.Equals(context.Request.Query["duplicates"].ToString(), "possible", StringComparison.OrdinalIgnoreCase);
            bool untaggedOnly = string.Equals(context.Request.Query["untagged"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
            var detail = await store.GetReviewAsync(customerId!.Value, id.Value, page, pageSize, sortDirection, possibleDuplicatesOnly, untaggedOnly, cancellationToken);
            return Results.Json(ReviewResponse.From(detail));
        }, "imports_review_get");

    private Task<IResult> PatchReviewAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null) return failure;

            var id = RouteGuid(context);
            if (id is null) return Errors.NotFound();

            byte[] body = await ReadJsonBytesAsync(context.Request, cancellationToken);
            var parsed = JsonBody.ParseBytes<ImportReviewPatchRequest>(body);
            if (!parsed.Parsed || parsed.Value!.ExpectedReviewVersion is null || parsed.Value.Decisions is null)
            {
                return Errors.InvalidRequest();
            }

            var decisions = new List<ImportDecision>(parsed.Value.Decisions.Length);
            foreach (var decision in parsed.Value.Decisions)
            {
                if (decision.RowId is null)
                {
                    return Errors.InvalidRequest();
                }

                JsonElement categoryElement = decision.SelectedCategoryId;
                bool hasSelectedCategory = categoryElement.ValueKind != JsonValueKind.Undefined;
                Guid? selectedCategoryId = null;
                if (hasSelectedCategory)
                {
                    if (categoryElement.ValueKind == JsonValueKind.Null)
                    {
                        selectedCategoryId = null;
                    }
                    else if (categoryElement.ValueKind == JsonValueKind.String &&
                             Guid.TryParse(categoryElement.GetString(), out var categoryId) &&
                             categoryId != Guid.Empty)
                    {
                        selectedCategoryId = categoryId;
                    }
                    else
                    {
                        return Errors.InvalidRequest();
                    }
                }

                bool resetCategory = decision.ResetCategoryToSuggestion == true;
                if (resetCategory && hasSelectedCategory)
                {
                    return Errors.InvalidRequest();
                }

                decisions.Add(new ImportDecision(
                    decision.RowId.Value,
                    decision.Skip,
                    decision.Override,
                    decision.WithinFileState,
                    decision.WithinFileGroup,
                    decision.WithinFileAction,
                    decision.SelectedTagIds,
                    selectedCategoryId,
                    hasSelectedCategory,
                    resetCategory));
            }

            var summary = await store.PatchReviewAsync(customerId!.Value, id.Value, parsed.Value.ExpectedReviewVersion.Value, decisions, parsed.Value.StatementOverride, cancellationToken);
            return Results.Json(ReviewSummaryResponse.From(summary));
        }, "imports_review_patch");

    private Task<IResult> RefreshReviewAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null) return failure;

            var id = RouteGuid(context);
            if (id is null) return Errors.NotFound();

            byte[] body = await ReadJsonBytesAsync(context.Request, cancellationToken);
            var parsed = JsonBody.ParseBytes<ReviewVersionRequest>(body);
            if (!parsed.Parsed || parsed.Value!.ExpectedReviewVersion is null)
            {
                return Errors.InvalidRequest();
            }

            var summary = await store.RefreshReviewAsync(customerId!.Value, id.Value, parsed.Value.ExpectedReviewVersion.Value, cancellationToken);
            return Results.Json(ReviewSummaryResponse.From(summary));
        }, "imports_review_refresh");

    private Task<IResult> CommitReviewAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null) return failure;

            var id = RouteGuid(context);
            if (id is null) return Errors.NotFound();

            byte[] body = await ReadJsonBytesAsync(context.Request, cancellationToken);
            var parsed = JsonBody.ParseBytes<ImportCommitRequest>(body);
            if (!parsed.Parsed || parsed.Value!.ExpectedReviewVersion is null || string.IsNullOrWhiteSpace(parsed.Value.ConfirmedSummaryHash))
            {
                return Errors.InvalidRequest();
            }

            var result = await store.CommitReviewAsync(customerId!.Value, id.Value, parsed.Value.ExpectedReviewVersion.Value, parsed.Value.ConfirmedSummaryHash, cancellationToken);
            _audit.LogInformation("audit.import_committed customer_id={CustomerId} statement_id={StatementId} rows={Rows} replayed={Replayed}", customerId.Value, result.Summary.Id, result.Summary.ImportedCount, result.Replayed);
            return Results.Json(CommitResponse.From(result), statusCode: result.Replayed ? 200 : 201);
        }, "imports_review_commit");

    private Task<IResult> AbandonReviewAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null) return failure;

            var id = RouteGuid(context);
            if (id is null) return Errors.NotFound();

            byte[] body = await ReadJsonBytesAsync(context.Request, cancellationToken);
            var parsed = JsonBody.ParseBytes<ReviewVersionRequest>(body);
            if (!parsed.Parsed || parsed.Value!.ExpectedReviewVersion is null)
            {
                return Errors.InvalidRequest();
            }

            var result = await store.AbandonReviewAsync(customerId!.Value, id.Value, parsed.Value.ExpectedReviewVersion.Value, cancellationToken);
            return Results.Json(new { committed = result.Committed, statement = result.Commit is null ? null : StatementSummaryResponse.From(result.Commit.Summary), replayed = result.Commit?.Replayed ?? false, });
        }, "imports_review_abandon");

    // --- History, detail, and statement lifecycle --------------------------

    private Task<IResult> ListHistoryAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null) return failure;

            var accountId = QueryGuid(context, "account_id");
            string state = context.Request.Query["state"].ToString();
            string cursor = context.Request.Query["cursor"].ToString();
            int limit = QueryInt(context, "limit", 50);
            var page = await store.ListHistoryAsync(customerId!.Value, accountId, string.IsNullOrWhiteSpace(state) ? null : state, string.IsNullOrWhiteSpace(cursor) ? null : cursor, limit, cancellationToken);
            return Results.Json(new { items = page.Items.Select(StatementSummaryResponse.From).ToArray(), next_cursor = page.NextCursor, });
        }, "imports_history_list");

    private Task<IResult> GetImportAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null) return failure;

            var id = RouteGuid(context);
            if (id is null) return Errors.NotFound();

            int page = QueryInt(context, "page", 1);
            int pageSize = QueryInt(context, "page_size", ImportLimits.MaxPageSize);
            var detail = await store.GetStatementAsync(customerId!.Value, id.Value, page, pageSize, cancellationToken);
            return Results.Json(new
            {
                statement = StatementSummaryResponse.From(detail.Summary),
                rows = detail.Rows.Select(StatementRowResponse.From).ToArray(),
                page = detail.Page,
                page_size = detail.PageSize,
                expected_transaction_count = detail.ExpectedTransactionCount,
            });
        }, "imports_statement_get");

    private Task<IResult> HideAsync(HttpContext context, CancellationToken cancellationToken) =>
        SetVisibilityAsync(context, cancellationToken, hidden: true);

    private Task<IResult> RestoreAsync(HttpContext context, CancellationToken cancellationToken) =>
        SetVisibilityAsync(context, cancellationToken, hidden: false);

    private Task<IResult> SetVisibilityAsync(HttpContext context, CancellationToken cancellationToken, bool hidden) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null) return failure;

            var id = RouteGuid(context);
            if (id is null) return Errors.NotFound();

            byte[] body = await ReadJsonBytesAsync(context.Request, cancellationToken);
            var parsed = JsonBody.ParseBytes<StatementMutationRequest>(body);
            if (!parsed.Parsed || parsed.Value!.ExpectedVersion is null || parsed.Value.ExpectedTransactionCount is null || parsed.Value.Confirm != true)
            {
                return Errors.InvalidRequest();
            }

            var summary = await store.SetStatementVisibilityAsync(customerId!.Value, id.Value, hidden, parsed.Value.ExpectedVersion.Value, parsed.Value.ExpectedTransactionCount.Value, true, cancellationToken);
            return Results.Json(StatementSummaryResponse.From(summary));
        }, hidden ? "imports_statement_hide" : "imports_statement_restore");

    private Task<IResult> DeleteAsync(HttpContext context, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var (customerId, failure) = await AuthenticateAsync(context, cancellationToken);
            if (failure is not null) return failure;

            var id = RouteGuid(context);
            if (id is null) return Errors.NotFound();

            var request = context.Request;
            byte[] body = await ReadJsonBytesAsync(request, cancellationToken);
            var parsed = JsonBody.ParseBytes<StatementMutationRequest>(body);
            if (!parsed.Parsed || parsed.Value!.ExpectedVersion is null || parsed.Value.ExpectedTransactionCount is null || parsed.Value.Confirm != true)
            {
                return Errors.InvalidRequest();
            }

            var result = await store.DeleteStatementAsync(customerId!.Value, id.Value, parsed.Value.ExpectedVersion.Value, parsed.Value.ExpectedTransactionCount.Value, true, cancellationToken);
            _audit.LogInformation("audit.import_deleted customer_id={CustomerId} statement_id={StatementId} deleted={Deleted}", customerId.Value, id.Value, result.DeletedTransactionCount);
            return Results.Json(new { deleted = result.Deleted, deleted_transaction_count = result.DeletedTransactionCount, });
        }, "imports_statement_delete");

    // --- Helpers -----------------------------------------------------------

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);

    private static Guid? RouteGuid(HttpContext context) =>
        context.Request.RouteValues.TryGetValue("id", out object? value) &&
        Guid.TryParse(value?.ToString(), out var id)
            ? id
            : null;

    private static int QueryInt(HttpContext context, string name, int fallback) =>
        int.TryParse(context.Request.Query[name].ToString(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    private static Guid? QueryGuid(HttpContext context, string name) =>
        Guid.TryParse(context.Request.Query[name].ToString(), out var value) ? value : null;

    private static async Task<byte[]> ReadJsonBytesAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength is > ImportLimits.MaxJsonBodyBytes)
        {
            throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
        }
        using var output = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > ImportLimits.MaxJsonBodyBytes)
            {
                throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
            }
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static string? DateString(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IResult ParserError(string reason) => Results.Json(
        new { error = reason },
        statusCode: reason switch
        {
            ParserReasons.FileTooLarge => 413,
            ParserReasons.UnsupportedFormat => 415,
            ParserReasons.ImportBusy => 429,
            _ => 400,
        });

    private static IResult StoreError(string error) => Results.Json(
        new { error },
        statusCode: error switch
        {
            ImportErrorCodes.NotFound => 404,
            ImportErrorCodes.ReviewExpired => 410,
            ImportErrorCodes.ImportDeleted => 410,
            ImportErrorCodes.ReviewStale => 409,
            ImportErrorCodes.VersionConflict => 409,
            ImportErrorCodes.StatementRepeat => 409,
            ImportErrorCodes.IdempotencyConflict => 409,
            ImportErrorCodes.AccountArchived => 409,
            ImportErrorCodes.IdentityConflict => 409,
            ImportErrorCodes.TagUnavailable => 409,
            ImportErrorCodes.CategoryUnavailable => 409,
            _ => 400,
        });
}

public sealed record ImportOptionsRequest(Guid? FinancialAccountId,
                                          string? SourceKind,
                                          ParserConfig? ParserConfig,
                                          string? StartDate,
                                          string? EndDate,
                                          string? Currency,
                                          bool? ConfirmZar,
                                          bool? StatementOverride);

public sealed record ImportReviewPatchRequest(int? ExpectedReviewVersion, ReviewDecisionRequest[]? Decisions, bool? StatementOverride);

public sealed record ReviewDecisionRequest(Guid? RowId,
                                           bool? Skip,
                                           bool? Override,
                                           string? WithinFileState,
                                           string? WithinFileGroup,
                                           string? WithinFileAction,
                                           Guid[]? SelectedTagIds,
                                           JsonElement SelectedCategoryId,
                                           bool? ResetCategoryToSuggestion);

public sealed record ReviewVersionRequest(int? ExpectedReviewVersion);

public sealed record ImportCommitRequest(int? ExpectedReviewVersion, string? ConfirmedSummaryHash);

public sealed record StatementMutationRequest(int? ExpectedVersion, long? ExpectedTransactionCount, bool? Confirm);

public sealed record PreviewRowResponse(Guid RowId,
                                        int SourceLine,
                                        int? SourcePage,
                                        int SourceRowNumber,
                                        Dictionary<string, string?> RawValues,
                                        string? TransactionDate,
                                        string Description,
                                        string? Amount,
                                        string[] ValidationCodes,
                                        string[] Warnings)
{
    public static PreviewRowResponse From(ParsedStatementRow row) => new(
        row.RowId,
        row.SourceLine,
        row.SourcePage,
        row.SourceRowNumber,
        new Dictionary<string, string?>(row.RawValues, StringComparer.Ordinal),
        row.TransactionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        row.Description,
        row.AmountCents is null ? null : StatementText.FormatCents(row.AmountCents.Value),
        row.ValidationCodes.ToArray(),
        row.Warnings.ToArray());
}

public sealed record ReviewResponse(ReviewSummaryResponse Summary, ReviewPageResponse Page)
{
    public static ReviewResponse From(ImportReviewDetail detail) => new(
        ReviewSummaryResponse.From(detail.Summary),
        ReviewPageResponse.From(detail.Page));
}

public sealed record ReviewSummaryResponse(Guid Id,
                                           Guid FinancialAccountId,
                                           string SourceKind,
                                           string? StartDate,
                                           string? EndDate,
                                           int Version,
                                           string Status,
                                           string ExpiresAt,
                                           int TotalRows,
                                           int ImportedCount,
                                           int SuppressedCount,
                                           int SkippedCount,
                                           int InvalidUnskippedCount,
                                           int WithinFilePendingCount,
                                           int PossibleDuplicateCount,
                                           int ExactOverrideCount,
                                           int CategorisedCount,
                                           bool StatementOverride,
                                           int StatementOverrideCount,
                                           string SummaryHash)
{
    public static ReviewSummaryResponse From(ImportReviewSummaryRecord record) => new(
        record.Id,
        record.FinancialAccountId,
        record.SourceKind,
        record.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        record.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        record.Version,
        record.Status,
        Timestamps.Format(record.ExpiresAt),
        record.TotalRows,
        record.ImportedCount,
        record.SuppressedCount,
        record.SkippedCount,
        record.InvalidUnskippedCount,
        record.WithinFilePendingCount,
        record.PossibleDuplicateCount,
        record.ExactOverrideCount,
        record.CategorisedCount,
        record.StatementOverride,
        record.StatementOverrideCount,
        record.SummaryHash);
}

public sealed record ReviewPageResponse(ReviewRowResponse[] Rows,
                                        int Total,
                                        int Page,
                                        int PageSize)
{
    public static ReviewPageResponse From(ImportReviewPage page) => new(
        page.Rows.Select(ReviewRowResponse.From).ToArray(),
        page.Total,
        page.Page,
        page.PageSize);
}

public sealed record ReviewRowResponse(Guid RowId,
                                       int SourceLine,
                                       int? SourcePage,
                                       int SourceRowNumber,
                                       Dictionary<string, string?> RawValues,
                                       string? TransactionDate,
                                       string Description,
                                       string? Amount,
                                       string[] ValidationCodes,
                                       string[] Warnings,
                                       string DuplicateState,
                                       string[] DuplicateReasons,
                                       Guid[] MatchingImportIds,
                                       NearCandidateResponse[] NearCandidates,
                                       int NearCandidateTotal,
                                       string? WithinFileGroup,
                                       string WithinFileState,
                                       bool Skip,
                                       bool Override,
                                       Guid[] SuggestedTagIds,
                                       Guid[] SelectedTagIds,
                                       Guid? SuggestedCategoryId,
                                       Guid? SelectedCategoryId,
                                       bool CategoryDecided)
{
    public static ReviewRowResponse From(ImportReviewRowRecord row) => new(
        row.RowId,
        row.SourceLine,
        row.SourcePage,
        row.SourceRowNumber,
        new Dictionary<string, string?>(row.RawValues, StringComparer.Ordinal),
        row.TransactionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        row.Description,
        row.AmountCents is null ? null : StatementText.FormatCents(row.AmountCents.Value),
        row.ValidationCodes.ToArray(),
        row.Warnings.ToArray(),
        row.DuplicateState,
        row.DuplicateReasons.ToArray(),
        row.MatchingImportIds.ToArray(),
        row.NearCandidates.Select(NearCandidateResponse.From).ToArray(),
        row.NearCandidateTotal,
        row.WithinFileGroup,
        row.WithinFileState,
        row.Skip,
        row.Override,
        row.SuggestedTagIds.ToArray(),
        row.SelectedTagIds.ToArray(),
        row.SuggestedCategoryId,
        row.SelectedCategoryId,
        row.CategoryDecided);
}

public sealed record NearCandidateResponse(string CandidateId,
                                           string Source,
                                           string? ImportId,
                                           string TransactionDate,
                                           string Amount,
                                           string Description,
                                           double Similarity,
                                           string[] Reasons)
{
    public static NearCandidateResponse From(ImportNearCandidateRecord record) => new(
        record.CandidateId,
        record.Source,
        record.ImportId,
        record.TransactionDate,
        record.Amount,
        record.Description,
        record.Similarity,
        record.Reasons.ToArray());
}

public sealed record OpenReviewResponse(Guid Id,
                                        Guid FinancialAccountId,
                                        string SourceKind,
                                        string? StartDate,
                                        string? EndDate,
                                        int Version,
                                        string ExpiresAt,
                                        int TotalRows,
                                        int ImportedCount,
                                        int SuppressedCount,
                                        int SkippedCount,
                                        string CreatedAt)
{
    public static OpenReviewResponse From(ImportOpenReviewSummary record) => new(
        record.Id,
        record.FinancialAccountId,
        record.SourceKind,
        record.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        record.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        record.Version,
        Timestamps.Format(record.ExpiresAt),
        record.TotalRows,
        record.ImportedCount,
        record.SuppressedCount,
        record.SkippedCount,
        Timestamps.Format(record.CreatedAt));
}

public sealed record StatementSummaryResponse(Guid Id,
                                              Guid FinancialAccountId,
                                              string SourceType,
                                              string StatementLabel,
                                              string StartDate,
                                              string EndDate,
                                              int TotalRows,
                                              int ImportedCount,
                                              int SuppressedCount,
                                              int SkippedCount,
                                              int PossibleDuplicateCount,
                                              int ExactOverrideCount,
                                              int StatementOverrideCount,
                                              bool IsHidden,
                                              int Version,
                                              long LiveHiddenCount,
                                              string CreatedAt,
                                              string UpdatedAt)
{
    public static StatementSummaryResponse From(ImportStatementSummary record) => new(
        record.Id,
        record.FinancialAccountId,
        record.SourceType,
        record.StatementLabel,
        record.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        record.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        record.TotalRows,
        record.ImportedCount,
        record.SuppressedCount,
        record.SkippedCount,
        record.PossibleDuplicateCount,
        record.ExactOverrideCount,
        record.StatementOverrideCount,
        record.IsHidden,
        record.Version,
        record.LiveHiddenCount,
        Timestamps.Format(record.CreatedAt),
        Timestamps.Format(record.UpdatedAt));
}

public sealed record StatementRowResponse(Guid Id,
                                          int SourceRowNumber,
                                          int? SourcePage,
                                          string TransactionDate,
                                          string Description,
                                          string Amount,
                                          bool IsHiddenByUser,
                                          bool EffectiveHidden,
                                          TagResponse[] Tags)
{
    public static StatementRowResponse From(ImportStatementRowRecord record) => new(
        record.Id,
        record.SourceRowNumber,
        record.SourcePage,
        record.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        record.Description,
        StatementText.FormatCents(record.AmountCents),
        record.IsHiddenByUser,
        record.EffectiveHidden,
        record.Tags.Select(TagResponse.From).ToArray());
}

public sealed record TagResponse(Guid Id, string DisplayName)
{
    public static TagResponse From(ImportTagRecord record) => new(record.Id, record.DisplayName);
}

public sealed record CommitResponse(bool Replayed, StatementSummaryResponse Statement)
{
    public static CommitResponse From(ImportCommitResult result) => new(
        result.Replayed,
        StatementSummaryResponse.From(result.Summary));
}
