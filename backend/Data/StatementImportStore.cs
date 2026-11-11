using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using argus.Server.Data.Entities;
using argus.Server.Finance;
using argus.Server.Imports;
using Microsoft.EntityFrameworkCore;

namespace argus.Server.Data;

public sealed record ImportAccountRecord(Guid Id,
                                         string DisplayName,
                                         string Currency,
                                         bool IsArchived,
                                         long ImportVersion);

public sealed record ImportNearCandidateRecord(string CandidateId,
                                               string Source,
                                               string? ImportId,
                                               string TransactionDate,
                                               string Amount,
                                               string Description,
                                               double Similarity,
                                               string[] Reasons);

public sealed record ImportReviewRowRecord(Guid RowId,
                                           int SourceLine,
                                           int? SourcePage,
                                           int SourceRowNumber,
                                           Dictionary<string, string?> RawValues,
                                           DateOnly? TransactionDate,
                                           string Description,
                                           string NormalizedDescription,
                                           long? AmountCents,
                                           string[] ValidationCodes,
                                           string[] Warnings,
                                           string? SourceIdNamespace,
                                           string? SourceIdValue,
                                           string DuplicateState,
                                           string[] DuplicateReasons,
                                           Guid[] MatchingTransactionIds,
                                           Guid[] MatchingImportIds,
                                           ImportNearCandidateRecord[] NearCandidates,
                                           int NearCandidateTotal,
                                           string? WithinFileGroup,
                                           string WithinFileState,
                                           bool Skip,
                                           bool Override,
                                           Guid[] SuggestedTagIds,
                                           Guid[] SelectedTagIds,
                                           Guid? SuggestedCategoryId,
                                           Guid? SelectedCategoryId,
                                           bool CategoryDecided);

public sealed record ImportReviewSummaryRecord(Guid Id,
                                               Guid FinancialAccountId,
                                               string SourceKind,
                                               DateOnly? StartDate,
                                               DateOnly? EndDate,
                                               int Version,
                                               string Status,
                                               DateTime ExpiresAt,
                                               long AccountImportVersion,
                                               string StatementFingerprint,
                                               string SummaryHash,
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
                                               long PayloadBytes,
                                               DateTime CreatedAt,
                                               DateTime UpdatedAt);

public sealed record ImportReviewPage(IReadOnlyList<ImportReviewRowRecord> Rows,
                                      int Total,
                                      int Page,
                                      int PageSize);

public sealed record ImportReviewDetail(ImportReviewSummaryRecord Summary, ImportReviewPage Page);

public sealed record ImportOpenReviewSummary(Guid Id,
                                             Guid FinancialAccountId,
                                             string SourceKind,
                                             DateOnly? StartDate,
                                             DateOnly? EndDate,
                                             int Version,
                                             DateTime ExpiresAt,
                                             int TotalRows,
                                             int ImportedCount,
                                             int SuppressedCount,
                                             int SkippedCount,
                                             DateTime CreatedAt);

public sealed record ImportStatementSummary(Guid Id,
                                            Guid FinancialAccountId,
                                            string SourceType,
                                            string StatementLabel,
                                            DateOnly StartDate,
                                            DateOnly EndDate,
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
                                            DateTime CreatedAt,
                                            DateTime UpdatedAt);

public sealed record ImportTagRecord(Guid Id, string DisplayName);

public sealed record ImportStatementRowRecord(Guid Id,
                                              int SourceRowNumber,
                                              int? SourcePage,
                                              DateOnly TransactionDate,
                                              string Description,
                                              long AmountCents,
                                              bool IsHiddenByUser,
                                              bool EffectiveHidden,
                                              IReadOnlyList<ImportTagRecord> Tags);

public sealed record ImportStatementDetail(ImportStatementSummary Summary,
                                           IReadOnlyList<ImportStatementRowRecord> Rows,
                                           int Page,
                                           int PageSize,
                                           long ExpectedTransactionCount);

public sealed record ImportHistoryPage(IReadOnlyList<ImportStatementSummary> Items, string? NextCursor);

public sealed record ImportDeleteResult(bool Deleted, long DeletedTransactionCount);

public sealed record ImportCommitResult(ImportStatementSummary Summary, bool Replayed);

public sealed record ImportAbandonResult(bool Committed, ImportCommitResult? Commit);

public sealed record ImportDecision(Guid RowId,
                                    bool? Skip,
                                    bool? Override,
                                    string? WithinFileState,
                                    string? WithinFileGroup,
                                    string? WithinFileAction,
                                    Guid[]? SelectedTagIds,
                                    Guid? SelectedCategoryId,
                                    bool HasSelectedCategory,
                                    bool ResetCategoryToSuggestion);

public sealed class ImportStoreException(string error) : Exception(error)
{
    public string Error { get; } = error;
}

public sealed class StatementRepeatException(IReadOnlyList<Guid> existingImportIds)
    : Exception(ImportErrorCodes.StatementRepeat)
{
    public IReadOnlyList<Guid> ExistingImportIds { get; } = existingImportIds;
}

public static class ImportErrorCodes
{
    public const string NotFound = "not_found";
    public const string InvalidRequest = "invalid_request";
    public const string ProcessingLimit = "processing_limit";
    public const string ReviewExpired = "review_expired";
    public const string ReviewStale = "review_stale";
    public const string VersionConflict = "version_conflict";
    public const string StatementRepeat = "statement_repeat";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string AccountArchived = "account_archived";
    public const string IdentityConflict = "identity_conflict";
    public const string TagUnavailable = "tag_unavailable";
    public const string CategoryUnavailable = "category_unavailable";
    public const string NoIncludedRows = "no_included_rows";
    public const string InvalidRows = "invalid_rows";
    public const string DuplicateDecisionRequired = "duplicate_decision_required";
    public const string ImportDeleted = "import_deleted";
}

/// <summary>
/// Durable, customer-scoped statement import authority: reviews, duplicate
/// snapshots, receipts, history, and the statement lifecycle. Every query is
/// scoped by <c>customer_id</c>; financial source text never enters a log.
/// </summary>
public sealed class StatementImportStore
{
    private static readonly JsonSerializerOptions _storageJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const int ReviewRowInsertBatchSize = 2_000;
    private const int TransactionInsertBatchSize = 2_000;
    private const int TagLinkInsertBatchSize = 5_000;

    private const string StatementSummarySql = """
        SELECT s.id AS "Id",
               s.financial_account_id AS "FinancialAccountId",
               s.source_type AS "SourceType",
               s.statement_label AS "StatementLabel",
               s.start_date AS "StartDate",
               s.end_date AS "EndDate",
               s.total_rows AS "TotalRows",
               s.imported_count AS "ImportedCount",
               s.suppressed_count AS "SuppressedCount",
               s.skipped_count AS "SkippedCount",
               s.possible_duplicate_count AS "PossibleDuplicateCount",
               s.exact_override_count AS "ExactOverrideCount",
               s.statement_override_count AS "StatementOverrideCount",
               s.is_hidden AS "IsHidden",
               s.version AS "Version",
               COALESCE((
                   SELECT COUNT(*) FROM transactions t
                   WHERE t.customer_id = s.customer_id AND t.statement_import_id = s.id
                     AND (s.is_hidden OR t.is_hidden_by_user)
               ), 0) AS "LiveHiddenCount",
               s.created_at AS "CreatedAt",
               s.updated_at AS "UpdatedAt"
        FROM statement_imports s
        WHERE s.customer_id = {0} AND s.id = {1}
        """;

    private const string HistorySql = """
        SELECT s.id AS "Id",
               s.financial_account_id AS "FinancialAccountId",
               s.source_type AS "SourceType",
               s.statement_label AS "StatementLabel",
               s.start_date AS "StartDate",
               s.end_date AS "EndDate",
               s.total_rows AS "TotalRows",
               s.imported_count AS "ImportedCount",
               s.suppressed_count AS "SuppressedCount",
               s.skipped_count AS "SkippedCount",
               s.possible_duplicate_count AS "PossibleDuplicateCount",
               s.exact_override_count AS "ExactOverrideCount",
               s.statement_override_count AS "StatementOverrideCount",
               s.is_hidden AS "IsHidden",
               s.version AS "Version",
               COALESCE(h.live_hidden_count, 0) AS "LiveHiddenCount",
               s.created_at AS "CreatedAt",
               s.updated_at AS "UpdatedAt"
        FROM statement_imports s
        LEFT JOIN LATERAL (
            SELECT COUNT(*) FILTER (WHERE s.is_hidden OR t.is_hidden_by_user) AS live_hidden_count
            FROM transactions t
            WHERE t.customer_id = s.customer_id AND t.statement_import_id = s.id
        ) h ON TRUE
        WHERE s.customer_id = {0}
          AND (CAST({1} AS uuid) IS NULL OR s.financial_account_id = CAST({1} AS uuid))
          AND ({2} = 'all'
               OR ({2} = 'hidden' AND COALESCE(h.live_hidden_count, 0) > 0)
               OR ({2} = 'visible' AND COALESCE(h.live_hidden_count, 0) = 0))
          AND (CAST({3} AS timestamptz) IS NULL OR (s.created_at, s.id) < (CAST({3} AS timestamptz), CAST({4} AS uuid)))
        ORDER BY s.created_at DESC, s.id DESC
        LIMIT {5}
        """;

    private readonly FinanceStore _financeStore;
    private readonly IImportFaultInjector _faults;
    private readonly IDbContextFactory<ArgusDbContext> _contextFactory;

    public StatementImportStore(FinanceStore financeStore,
                                IImportFaultInjector faults,
                                IDbContextFactory<ArgusDbContext> contextFactory)
    {
        _financeStore = financeStore;
        _faults = faults;
        _contextFactory = contextFactory;
    }

    public async Task<ImportAccountRecord?> GetAccountAsync(Guid customerId, Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await GetAccountAsync(context, customerId, accountId, cancellationToken);
    }

    public async Task<ImportReviewDetail> CreateReviewAsync(Guid customerId,
                                                            Guid accountId,
                                                            StatementSourceKind sourceKind,
                                                            ParserConfig config,
                                                            DateOnly startDate,
                                                            DateOnly endDate,
                                                            ParsedStatement parsed,
                                                            CancellationToken cancellationToken = default)
    {
        if (parsed.Rows.Count == 0)
        {
            throw new ImportStoreException(ImportErrorCodes.NoIncludedRows);
        }
        if (parsed.Rows.Count > ImportLimits.MaxDataRows)
        {
            throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
        }

        // Seeding happens before the quota/account lock order so no later step
        // enters that order while holding a customer-row lock.
        await _financeStore.EnsureCategoriesSeededAsync(customerId, cancellationToken);

        long estimatedPayloadBytes = EstimatePayload(parsed, config);
        var reviewId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var expiresAt = now.AddHours(ImportLimits.ReviewLifetimeHours);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await LockQuotaAsync(context, customerId, cancellationToken);
        long usedPayloadBytes = await EnforceQuotaAsync(context, customerId, estimatedPayloadBytes, cancellationToken);

        var account = await LockAccountAsync(context, customerId, accountId, cancellationToken);
        if (account.IsArchived)
        {
            throw new ImportStoreException(ImportErrorCodes.AccountArchived);
        }
        if (!string.Equals(account.Currency, "ZAR", StringComparison.Ordinal))
        {
            throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
        }

        var snapshot = await ComputeDuplicateSnapshotAsync(context, customerId, accountId, parsed.Rows, null, cancellationToken);
        var rows = snapshot.Rows.Select(row => row with
        {
            SelectedCategoryId = row.SuggestedCategoryId,
            CategoryDecided = false,
        }).ToArray();
        long payloadBytes = EstimatePayload(parsed, config, rows);
        if (usedPayloadBytes + payloadBytes > ImportLimits.MaxOpenReviewPayloadBytes)
        {
            throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
        }
        var counts = CountRows(rows);
        string summaryHash = ComputeSummaryHash(reviewId, 1, rows, counts, statementOverride: false);
        string statementFingerprint = StatementIdentity.ComputeStatementFingerprint(parsed.Rows, startDate, endDate);

        await InsertReviewAsync(
            context,
            reviewId,
            customerId,
            accountId,
            parsed.SourceType,
            config,
            startDate,
            endDate,
            now,
            expiresAt,
            account.ImportVersion,
            statementFingerprint,
            summaryHash,
            counts,
            payloadBytes,
            rows,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var summary = new ImportReviewSummaryRecord(
            reviewId,
            accountId,
            parsed.SourceType,
            startDate,
            endDate,
            1,
            "Prepared",
            expiresAt,
            account.ImportVersion,
            statementFingerprint,
            summaryHash,
            counts.TotalRows,
            counts.Imported,
            counts.Suppressed,
            counts.Skipped,
            rows.Count(row => row.ValidationCodes.Length > 0 && !row.Skip),
            rows.Count(row => row.WithinFileState == "pending"),
            counts.PossibleDuplicates,
            0,
            CountCategorised(rows),
            false,
            0,
            payloadBytes,
            now,
            now);
        var page = new ImportReviewPage(rows.Take(ImportLimits.MaxPageSize).ToArray(), rows.Length, 1, ImportLimits.MaxPageSize);

        return new ImportReviewDetail(summary, page);
    }

    public async Task<IReadOnlyList<ImportOpenReviewSummary>> ListOpenReviewsAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        List<ImportReview> reviews = await context.ImportReviews
            .FromSqlInterpolated($"""
                SELECT * FROM import_reviews
                WHERE customer_id = {customerId} AND status = 'Prepared' AND expires_at > NOW()
                ORDER BY created_at DESC, id DESC
                LIMIT 50
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return reviews
            .Select(review => new ImportOpenReviewSummary(
                review.Id,
                review.FinancialAccountId,
                review.SourceKind,
                review.StartDate,
                review.EndDate,
                review.Version,
                review.ExpiresAt,
                review.TotalRows,
                review.ImportedCount,
                review.SuppressedCount,
                review.SkippedCount,
                review.CreatedAt))
            .ToList();
    }

    public async Task<ImportReviewDetail> GetReviewAsync(Guid customerId,
                                                         Guid reviewId,
                                                         int page,
                                                         int pageSize,
                                                         string sortDirection = "asc",
                                                         bool possibleDuplicatesOnly = false,
                                                         bool untaggedOnly = false,
                                                         CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, ImportLimits.MaxPageSize);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var summary = await ReadReviewHeaderAsync(context, customerId, reviewId, false, cancellationToken);
        if (summary is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        if (summary.Status == "Expired" || summary.ExpiresAt <= DateTime.UtcNow)
        {
            throw new ImportStoreException(ImportErrorCodes.ReviewExpired);
        }
        if (summary.Status != "Prepared")
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }

        bool descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        var rows = await ReadReviewRowsAsync(context, reviewId, page, pageSize, descending, possibleDuplicatesOnly, untaggedOnly, cancellationToken);
        int total = await CountReviewRowsAsync(context, reviewId, possibleDuplicatesOnly, untaggedOnly, cancellationToken);

        return new ImportReviewDetail(summary, new ImportReviewPage(rows, total, page, pageSize));
    }

    public async Task<ImportReviewSummaryRecord> PatchReviewAsync(Guid customerId,
                                                                  Guid reviewId,
                                                                  int expectedVersion,
                                                                  IReadOnlyList<ImportDecision> decisions,
                                                                  bool? statementOverride,
                                                                  CancellationToken cancellationToken = default)
    {
        if (decisions.Count > ImportLimits.MaxReviewDecisionsPerRequest)
        {
            throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
        }
        if (decisions.Select(decision => decision.RowId).Distinct().Count() != decisions.Count)
        {
            throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var accountId = await ReviewAccountIdAsync(context, customerId, reviewId, cancellationToken);
        if (accountId is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        await LockQuotaAsync(context, customerId, cancellationToken);
        await LockAccountAsync(context, customerId, accountId.Value, cancellationToken);
        var summary = await ReadReviewHeaderAsync(context, customerId, reviewId, true, cancellationToken);
        if (summary is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        EnsureEditable(summary, expectedVersion);

        var rows = (await ReadReviewRowsAsync(context, reviewId, 1, int.MaxValue, descending: false, possibleDuplicatesOnly: false, untaggedOnly: false, cancellationToken)).ToList();
        var byId = rows.ToDictionary(row => row.RowId);
        foreach (var decision in decisions)
        {
            if (!byId.TryGetValue(decision.RowId, out var row))
            {
                throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
            }

            if (decision.WithinFileAction is not null)
            {
                if (decision.WithinFileGroup is null ||
                    decision.WithinFileAction is not ("keep_all" or "skip_all") ||
                    decision.HasSelectedCategory ||
                    decision.ResetCategoryToSuggestion)
                {
                    throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
                }
                string state = decision.WithinFileAction == "keep_all" ? "keep" : "skip";
                bool skip = decision.WithinFileAction == "skip_all";
                var groupRows = byId.Values.Where(row => row.WithinFileGroup == decision.WithinFileGroup).ToList();
                if (groupRows.Count == 0)
                {
                    throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
                }
                await SetWithinFileGroupAsync(context, reviewId, decision.WithinFileGroup, state, skip, cancellationToken);
                foreach (var groupRow in groupRows)
                {
                    byId[groupRow.RowId] = groupRow with
                    {
                        WithinFileState = state,
                        Skip = skip,
                        Override = skip ? false : groupRow.Override,
                    };
                }
                continue;
            }

            bool skipValue = decision.Skip ?? row.Skip;
            bool overrideValue = decision.Override ?? row.Override;
            if (skipValue && overrideValue)
            {
                throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
            }
            if (overrideValue && row.DuplicateState is not ("suppressed" or "suppressed_within_file"))
            {
                throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
            }

            string withinFileState = row.WithinFileState;
            if (decision.WithinFileState is not null)
            {
                if (row.WithinFileGroup is null || decision.WithinFileState is not ("keep" or "skip"))
                {
                    throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
                }
                withinFileState = decision.WithinFileState;
                skipValue = decision.WithinFileState == "skip";
            }

            var selectedTagIds = decision.SelectedTagIds is null ? row.SelectedTagIds : decision.SelectedTagIds.Distinct().ToArray();
            if (selectedTagIds.Length > 100)
            {
                throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
            }
            var newlySelected = selectedTagIds.Except(row.SelectedTagIds).ToArray();
            if (newlySelected.Length > 0 && !await TagsAvailableAsync(context, customerId, newlySelected, cancellationToken))
            {
                throw new ImportStoreException(ImportErrorCodes.TagUnavailable);
            }

            Guid? selectedCategoryId = row.SelectedCategoryId;
            bool categoryDecided = row.CategoryDecided;
            if (decision.ResetCategoryToSuggestion)
            {
                if (decision.HasSelectedCategory)
                {
                    throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
                }
                selectedCategoryId = row.SuggestedCategoryId;
                categoryDecided = false;
            }
            else if (decision.HasSelectedCategory)
            {
                if (decision.SelectedCategoryId is Guid categoryId &&
                    !await CategorySelectableAsync(context, customerId, categoryId, cancellationToken))
                {
                    throw new ImportStoreException(ImportErrorCodes.CategoryUnavailable);
                }
                selectedCategoryId = decision.SelectedCategoryId;
                categoryDecided = true;
            }

            await UpdateRowDecisionsAsync(context, row.RowId, skipValue, overrideValue, withinFileState, selectedTagIds, selectedCategoryId, categoryDecided, cancellationToken);

            byId[row.RowId] = row with
            {
                Skip = skipValue,
                Override = overrideValue,
                WithinFileState = withinFileState,
                SelectedTagIds = selectedTagIds,
                SelectedCategoryId = selectedCategoryId,
                CategoryDecided = categoryDecided,
            };
        }

        var decidedRows = byId.Values.OrderBy(row => row.SourceRowNumber).ThenBy(row => row.RowId).ToArray();
        var decisionMap = decidedRows.ToDictionary(row => row.RowId, row => new RowDecision(row.Skip, row.Override));
        var snapshot = await ComputeDuplicateSnapshotAsync(context, customerId, accountId.Value, decidedRows.Select(ToParsedRow).ToArray(), decisionMap, cancellationToken);
        var freshById = snapshot.Rows.ToDictionary(row => row.RowId);
        var updatedRows = new List<ImportReviewRowRecord>(decidedRows.Length);
        foreach (var oldRow in decidedRows)
        {
            var fresh = freshById[oldRow.RowId];
            ImportReviewRowRecord merged;
            if (oldRow.Skip)
            {
                merged = oldRow with
                {
                    WithinFileState = oldRow.WithinFileGroup is null ? "none" : "skip",
                    SuggestedCategoryId = fresh.SuggestedCategoryId,
                };
            }
            else if (oldRow.Override)
            {
                // An overridden row is deliberately excluded from occurrence
                // matching; retain its classification and let it take a fresh slot.
                merged = oldRow with
                {
                    WithinFileState = fresh.WithinFileGroup is null
                        ? "none"
                        : fresh.WithinFileState == "pending" ? "pending" : oldRow.WithinFileState,
                    SuggestedCategoryId = fresh.SuggestedCategoryId,
                };
            }
            else
            {
                string withinState = fresh.WithinFileGroup is null ? "none" : oldRow.WithinFileGroup == fresh.WithinFileGroup && oldRow.WithinFileState is "keep" or "skip" ? oldRow.WithinFileState : fresh.WithinFileState;
                merged = fresh with
                {
                    Skip = false,
                    Override = false,
                    SelectedTagIds = oldRow.SelectedTagIds,
                    SuggestedCategoryId = fresh.SuggestedCategoryId,
                    SelectedCategoryId = oldRow.SelectedCategoryId,
                    CategoryDecided = oldRow.CategoryDecided,
                    WithinFileState = withinState,
                };
            }
            updatedRows.Add(merged);
            await UpdateDuplicateSnapshotAsync(context, merged, cancellationToken);
        }

        bool overrideFlag = statementOverride ?? summary.StatementOverride;
        int oldRowsJson = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(rows, _storageJson));
        int newRowsJson = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(updatedRows, _storageJson));
        long payloadBytes = Math.Max(0, summary.PayloadBytes + newRowsJson - oldRowsJson);
        var (openReviews, usedPayload) = await QuotaUsageExcludingAsync(context, customerId, reviewId, cancellationToken);
        if (openReviews >= ImportLimits.MaxOpenReviewsPerCustomer || usedPayload + payloadBytes > ImportLimits.MaxOpenReviewPayloadBytes)
        {
            throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
        }
        var counts = CountRows(updatedRows);
        int version = summary.Version + 1;
        string hash = ComputeSummaryHash(reviewId, version, updatedRows, counts, overrideFlag);
        await UpdateReviewSummaryAsync(context, reviewId, version, counts, hash, overrideFlag, payloadBytes, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return summary with
        {
            Version = version,
            SummaryHash = hash,
            TotalRows = counts.TotalRows,
            ImportedCount = counts.Imported,
            SuppressedCount = counts.Suppressed,
            SkippedCount = counts.Skipped,
            InvalidUnskippedCount = updatedRows.Count(row => row.ValidationCodes.Length > 0 && !row.Skip),
            WithinFilePendingCount = updatedRows.Count(row => row.WithinFileState == "pending"),
            PossibleDuplicateCount = counts.PossibleDuplicates,
            ExactOverrideCount = counts.ExactOverrides,
            CategorisedCount = CountCategorised(updatedRows),
            StatementOverride = overrideFlag,
            StatementOverrideCount = overrideFlag ? 1 : 0,
            PayloadBytes = payloadBytes,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    public async Task<ImportReviewSummaryRecord> RefreshReviewAsync(Guid customerId,
                                                                    Guid reviewId,
                                                                    int expectedVersion,
                                                                    CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var accountId = await ReviewAccountIdAsync(context, customerId, reviewId, cancellationToken);
        if (accountId is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        await LockQuotaAsync(context, customerId, cancellationToken);
        var account = await LockAccountAsync(context, customerId, accountId.Value, cancellationToken);
        var summary = await ReadReviewHeaderAsync(context, customerId, reviewId, true, cancellationToken);
        if (summary is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        EnsureEditable(summary, expectedVersion);

        var existingRows = await ReadReviewRowsAsync(context, reviewId, 1, int.MaxValue, descending: false, possibleDuplicatesOnly: false, untaggedOnly: false, cancellationToken);
        var decisionMap = existingRows.ToDictionary(row => row.RowId, row => new RowDecision(row.Skip, row.Override));
        var parsedRows = existingRows.Select(ToParsedRow).ToArray();
        var snapshot = await ComputeDuplicateSnapshotAsync(context, customerId, accountId.Value, parsedRows, decisionMap, cancellationToken);
        var freshById = snapshot.Rows.ToDictionary(row => row.RowId);
        var refreshed = new List<ImportReviewRowRecord>(existingRows.Count);
        foreach (var oldRow in existingRows)
        {
            var fresh = freshById[oldRow.RowId];
            ImportReviewRowRecord refreshedRow;
            if (oldRow.Skip)
            {
                refreshedRow = oldRow with
                {
                    WithinFileState = oldRow.WithinFileGroup is null ? "none" : "skip",
                    SuggestedCategoryId = fresh.SuggestedCategoryId,
                    SelectedCategoryId = oldRow.CategoryDecided ? oldRow.SelectedCategoryId : fresh.SuggestedCategoryId,
                };
            }
            else if (oldRow.Override)
            {
                bool stillSuppressed = fresh.DuplicateState is "suppressed" or "suppressed_within_file";
                refreshedRow = stillSuppressed
                    ? oldRow with
                    {
                        SuggestedCategoryId = fresh.SuggestedCategoryId,
                        SelectedCategoryId = oldRow.CategoryDecided ? oldRow.SelectedCategoryId : fresh.SuggestedCategoryId,
                    }
                    : fresh with
                    {
                        Skip = false,
                        Override = false,
                        SelectedTagIds = oldRow.SelectedTagIds,
                        SuggestedCategoryId = fresh.SuggestedCategoryId,
                        SelectedCategoryId = oldRow.CategoryDecided ? oldRow.SelectedCategoryId : fresh.SuggestedCategoryId,
                        CategoryDecided = oldRow.CategoryDecided,
                    };
            }
            else
            {
                string withinState = fresh.WithinFileGroup is null ? "none" : oldRow.WithinFileGroup == fresh.WithinFileGroup && oldRow.WithinFileState is "keep" or "skip" ? oldRow.WithinFileState : fresh.WithinFileState;
                refreshedRow = fresh with
                {
                    Skip = false,
                    Override = false,
                    SelectedTagIds = oldRow.SelectedTagIds,
                    SuggestedCategoryId = fresh.SuggestedCategoryId,
                    SelectedCategoryId = oldRow.CategoryDecided ? oldRow.SelectedCategoryId : fresh.SuggestedCategoryId,
                    CategoryDecided = oldRow.CategoryDecided,
                    WithinFileState = withinState,
                };
            }
            refreshed.Add(refreshedRow);
            await UpdateDuplicateSnapshotAsync(context, refreshedRow, cancellationToken);
        }

        int oldRowsJson = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(existingRows, _storageJson));
        int newRowsJson = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(refreshed, _storageJson));
        long payloadBytes = Math.Max(0, summary.PayloadBytes + newRowsJson - oldRowsJson);
        var (openReviews, usedPayload) = await QuotaUsageExcludingAsync(context, customerId, reviewId, cancellationToken);
        if (openReviews >= ImportLimits.MaxOpenReviewsPerCustomer || usedPayload + payloadBytes > ImportLimits.MaxOpenReviewPayloadBytes)
        {
            throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
        }
        var counts = CountRows(refreshed);
        int version = summary.Version + 1;
        string hash = ComputeSummaryHash(reviewId, version, refreshed, counts, summary.StatementOverride);
        await UpdateReviewSummaryAsync(context, reviewId, version, counts, hash, summary.StatementOverride, payloadBytes, cancellationToken);
        await UpdateReviewAccountVersionAsync(context, reviewId, account.ImportVersion, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return summary with
        {
            Version = version,
            SummaryHash = hash,
            TotalRows = counts.TotalRows,
            ImportedCount = counts.Imported,
            SuppressedCount = counts.Suppressed,
            SkippedCount = counts.Skipped,
            InvalidUnskippedCount = refreshed.Count(row => row.ValidationCodes.Length > 0 && !row.Skip),
            WithinFilePendingCount = refreshed.Count(row => row.WithinFileState == "pending"),
            PossibleDuplicateCount = counts.PossibleDuplicates,
            ExactOverrideCount = counts.ExactOverrides,
            CategorisedCount = CountCategorised(refreshed),
            AccountImportVersion = account.ImportVersion,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    public async Task<ImportAbandonResult> AbandonReviewAsync(Guid customerId,
                                                              Guid reviewId,
                                                              int expectedVersion,
                                                              CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // A committed import purges the review header, so resolve a terminal
        // operation from its receipt before looking for the review.
        var receiptAccountId = await ReceiptAccountIdAsync(context, customerId, reviewId, cancellationToken);
        if (receiptAccountId is not null)
        {
            await LockAccountAsync(context, customerId, receiptAccountId.Value, cancellationToken);
            var terminal = await ReadReceiptAsync(context, customerId, reviewId, true, cancellationToken);
            if (terminal is not null)
            {
                if (terminal.State == "Deleted")
                {
                    throw new ImportStoreException(ImportErrorCodes.ImportDeleted);
                }
                var statement = await ReadStatementSummaryAsync(context, customerId, terminal.StatementImportId!.Value, cancellationToken);
                if (statement is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return new ImportAbandonResult(true, new ImportCommitResult(statement, true));
                }
            }
        }

        var accountId = await ReviewAccountIdAsync(context, customerId, reviewId, cancellationToken);
        if (accountId is null)
        {
            var lateReceiptAccount = await ReceiptAccountIdAsync(context, customerId, reviewId, cancellationToken);
            if (lateReceiptAccount is not null)
            {
                await LockAccountAsync(context, customerId, lateReceiptAccount.Value, cancellationToken);
                var lateReceipt = await ReadReceiptAsync(context, customerId, reviewId, true, cancellationToken);
                if (lateReceipt is not null)
                {
                    if (lateReceipt.State == "Deleted")
                    {
                        throw new ImportStoreException(ImportErrorCodes.ImportDeleted);
                    }
                    var lateStatement = await ReadStatementSummaryAsync(context, customerId, lateReceipt.StatementImportId!.Value, cancellationToken);
                    if (lateStatement is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        return new ImportAbandonResult(true, new ImportCommitResult(lateStatement, true));
                    }
                }
            }
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        await LockAccountAsync(context, customerId, accountId.Value, cancellationToken);

        var racedReceipt = await ReadReceiptAsync(context, customerId, reviewId, false, cancellationToken);
        if (racedReceipt is not null)
        {
            if (racedReceipt.State == "Deleted")
            {
                throw new ImportStoreException(ImportErrorCodes.ImportDeleted);
            }
            var statement = await ReadStatementSummaryAsync(context, customerId, racedReceipt.StatementImportId!.Value, cancellationToken);
            if (statement is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new ImportAbandonResult(true, new ImportCommitResult(statement, true));
            }
        }

        var summary = await ReadReviewHeaderAsync(context, customerId, reviewId, true, cancellationToken);
        if (summary is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        if (summary.Status == "Expired")
        {
            throw new ImportStoreException(ImportErrorCodes.ReviewExpired);
        }
        if (summary.Status == "Abandoned")
        {
            await transaction.CommitAsync(cancellationToken);
            return new ImportAbandonResult(false, null);
        }
        EnsureEditable(summary, expectedVersion);

        await context.ImportReviewRows
            .Where(row => row.ReviewId == reviewId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE import_reviews
            SET status = 'Abandoned',
                parser_config = '{}'::jsonb,
                start_date = NULL,
                end_date = NULL,
                statement_fingerprint = '',
                summary_hash = '',
                total_rows = 0,
                imported_count = 0,
                suppressed_count = 0,
                skipped_count = 0,
                possible_duplicate_count = 0,
                exact_override_count = 0,
                statement_override = FALSE,
                statement_override_count = 0,
                payload_bytes = 0,
                version = version + 1,
                updated_at = NOW()
            WHERE customer_id = {{customerId}} AND id = {{reviewId}}
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ImportAbandonResult(false, null);
    }

    // --- Duplicate snapshot ------------------------------------------------

    private async Task<DuplicateSnapshot> ComputeDuplicateSnapshotAsync(ArgusDbContext context,
                                                                        Guid customerId,
                                                                        Guid accountId,
                                                                        IReadOnlyList<ParsedStatementRow> parsedRows,
                                                                        IReadOnlyDictionary<Guid, RowDecision>? decisions,
                                                                        CancellationToken cancellationToken)
    {
        var incoming = parsedRows.Select((row, index) => ToIncoming(row, index)).Where(row => row is not null).Select(row => row!).ToList();

        string[] identityKeys = incoming.Select(row => row.IdentityKey).Distinct(StringComparer.Ordinal).ToArray();
        string[] namespaces = incoming.Where(row => row.Namespace is not null).Select(row => row.Namespace!).Distinct(StringComparer.Ordinal).ToArray();
        string[] ids = incoming.Where(row => row.Value is not null).Select(row => row.Value!).Distinct(StringComparer.Ordinal).ToArray();
        decimal[] amounts = incoming.Select(row => row.Cents / 100m).Distinct().ToArray();
        DateOnly[] dates = incoming.Select(row => row.Date).Distinct().ToArray();

        var existing = new Dictionary<Guid, ExistingRow>();
        if (identityKeys.Length > 0)
        {
            foreach (var row in await QueryExistingAsync(
                         context, customerId, accountId,
                         query => query.Where(transaction => identityKeys.Contains(transaction.IdentityKey)),
                         cancellationToken))
            {
                existing[row.Id] = row;
            }
        }
        if (namespaces.Length > 0 && ids.Length > 0)
        {
            foreach (var row in await QueryExistingAsync(
                         context, customerId, accountId,
                         query => query.Where(transaction =>
                             namespaces.Contains(transaction.SourceIdNamespace!) &&
                             ids.Contains(transaction.SourceIdValue!)),
                         cancellationToken))
            {
                existing[row.Id] = row;
            }
        }
        if (amounts.Length > 0 && dates.Length > 0)
        {
            foreach (var row in await QueryExistingAsync(
                         context, customerId, accountId,
                         query => query.Where(transaction =>
                             amounts.Contains(transaction.Amount) &&
                             dates.Contains(transaction.TransactionDate)),
                         cancellationToken))
            {
                existing[row.Id] = row;
            }
        }

        var existingList = existing.Values.ToList();
        foreach (var group in existingList.GroupBy(row => row.IdentityKey, StringComparer.Ordinal))
        {
            var first = group.First();
            if (group.Any(row => row.Date != first.Date || row.Cents != first.Cents || row.NormalizedDescription != first.NormalizedDescription))
            {
                throw new ImportStoreException(ImportErrorCodes.IdentityConflict);
            }
        }

        // A digest must resolve to one canonical component set across the whole
        // comparison group, not only for pairs the discriminator matcher accepts.
        // Compare existing and incoming rows before stable-ID filtering so a
        // conflicting (or corrupted) digest fails closed instead of being
        // treated as a new occurrence.
        var canonicalByKey = new Dictionary<string, (DateOnly Date, long Cents, string Description)>(
            StringComparer.Ordinal);
        void CheckCanonical(string key,
                            DateOnly date,
                            long cents,
                            string description)
        {
            if (canonicalByKey.TryGetValue(key, out var first))
            {
                if (first.Date != date || first.Cents != cents || !string.Equals(first.Description, description, StringComparison.Ordinal))
                {
                    throw new ImportStoreException(ImportErrorCodes.IdentityConflict);
                }
                return;
            }
            canonicalByKey[key] = (date, cents, description);
        }
        foreach (var row in existingList)
        {
            CheckCanonical(row.IdentityKey, row.Date, row.Cents, row.NormalizedDescription);
        }
        foreach (var row in incoming)
        {
            CheckCanonical(row.IdentityKey, row.Date, row.Cents, row.NormalizedDescription);
        }
        var existingByIdentity = existingList.GroupBy(row => row.IdentityKey).ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var existingByStable = existingList.Where(row => row.Namespace is not null && row.Value is not null).GroupBy(row => (row.Namespace!, row.Value!)).ToDictionary(group => group.Key, group => group.ToList());
        var existingByAmount = existingList.GroupBy(row => row.Cents).ToDictionary(group => group.Key, group => group.ToList());

        var snapshots = parsedRows.Select(row => new MutableSnapshot(row)).ToArray();
        int comparisons = 0;

        foreach (var group in incoming.GroupBy(row => row.IdentityKey, StringComparer.Ordinal))
        {
            var incomingGroup = group.OrderBy(row => row.SourceRowNumber).ThenBy(row => row.RowId).ToList();
            var firstIncoming = incomingGroup[0];
            if (incomingGroup.Any(row => row.Date != firstIncoming.Date || row.Cents != firstIncoming.Cents || row.NormalizedDescription != firstIncoming.NormalizedDescription))
            {
                throw new ImportStoreException(ImportErrorCodes.IdentityConflict);
            }
            var groupDecisions = incomingGroup.Select(row => decisions?.GetValueOrDefault(row.RowId) ?? new RowDecision(false, false)).ToArray();
            var nonSkipped = incomingGroup.Where((_, index) => !groupDecisions[index].Skip).ToList();
            var matchable = incomingGroup.Where((_, index) => !groupDecisions[index].Skip && !groupDecisions[index].Override).ToList();
            var existingGroup = existingByIdentity.GetValueOrDefault(group.Key) ?? [];
            bool withinFile = nonSkipped.Count > 1;
            var pairs = MatchOccurrences(matchable, existingGroup, cancellationToken);
            foreach (var pair in pairs)
            {
                if (pair.Incoming.Date != pair.Existing.Date || pair.Incoming.Cents != pair.Existing.Cents || pair.Incoming.NormalizedDescription != pair.Existing.NormalizedDescription)
                {
                    throw new ImportStoreException(ImportErrorCodes.IdentityConflict);
                }
                var snapshot = snapshots[pair.Incoming.Index];
                snapshot.DuplicateState = withinFile ? "suppressed_within_file" : "suppressed";
                snapshot.Reasons.Add("existing_occurrence");
                snapshot.MatchingTransactionIds.Add(pair.Existing.Id);
                snapshot.MatchingImportIds.Add(pair.Existing.StatementImportId);
            }
            if (withinFile)
            {
                foreach (var incomingRow in nonSkipped)
                {
                    var snapshot = snapshots[incomingRow.Index];
                    snapshot.Reasons.Add("within_file");
                    snapshot.WithinFileGroup = group.Key;
                    snapshot.WithinFileState = "pending";
                    if (snapshot.DuplicateState == "none")
                    {
                        snapshot.DuplicateState = "within_file";
                    }
                }
            }

            // A stable ID that appears with changed fallback fields is a review
            // warning, not an automatic rewrite or suppression.
            foreach (var incomingRow in incomingGroup)
            {
                if (incomingRow.Namespace is null || incomingRow.Value is null)
                {
                    continue;
                }

                if (!existingByStable.TryGetValue((incomingRow.Namespace, incomingRow.Value), out var stableMatches))
                {
                    continue;
                }

                foreach (var match in stableMatches)
                {
                    if (match.IdentityKey == incomingRow.IdentityKey)
                    {
                        continue;
                    }

                    var snapshot = snapshots[incomingRow.Index];
                    snapshot.Reasons.Add("stable_id_changed");
                    snapshot.NearCandidates.Add(new Candidate(match.Id.ToString(), "existing", match.StatementImportId.ToString(), match.Date, match.Cents, match.Description, 0, ["stable_id_changed"]));
                }
            }
        }

        // Near-duplicate scan: exact transaction date and amount, excluding the
        // exact identity. A date that differs suggests a separate transaction,
        // even at the same merchant and amount. The comparison cap is a safe
        // processing bound, never a silent policy truncation.
        foreach (var incomingRow in incoming)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (existingByAmount.TryGetValue(incomingRow.Cents, out var sameAmount))
            {
                foreach (var candidate in sameAmount)
                {
                    if (candidate.Date != incomingRow.Date || candidate.IdentityKey == incomingRow.IdentityKey)
                    {
                        continue;
                    }

                    if (++comparisons > 250_000)
                    {
                        throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
                    }
                    double similarity = Jaccard(incomingRow.NormalizedDescription, candidate.NormalizedDescription);
                    if (similarity >= 0.5)
                    {
                        snapshots[incomingRow.Index].NearCandidates.Add(new Candidate(candidate.Id.ToString(), "existing", candidate.StatementImportId.ToString(), candidate.Date, candidate.Cents, candidate.Description, similarity, ["near_match"]));
                    }
                }
            }
        }

        var incomingByAmount = incoming.GroupBy(row => row.Cents).ToDictionary(group => group.Key, group => group.ToList());
        foreach (var first in incoming)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!incomingByAmount.TryGetValue(first.Cents, out var sameAmount))
            {
                continue;
            }

            foreach (var second in sameAmount)
            {
                if (second.Index <= first.Index || first.IdentityKey == second.IdentityKey || first.Date != second.Date)
                {
                    continue;
                }

                if (++comparisons > 250_000)
                {
                    throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
                }
                double similarity = Jaccard(first.NormalizedDescription, second.NormalizedDescription);
                if (similarity < 0.5)
                {
                    continue;
                }

                snapshots[first.Index].NearCandidates.Add(new Candidate(second.RowId.ToString(), "review", null, second.Date, second.Cents, second.Description, similarity, ["near_match"]));
                snapshots[second.Index].NearCandidates.Add(new Candidate(first.RowId.ToString(), "review", null, first.Date, first.Cents, first.Description, similarity, ["near_match"]));
            }
        }

        foreach (var snapshot in snapshots)
        {
            if (snapshot.NearCandidates.Count > 0)
            {
                snapshot.Reasons.Add("near_match");
            }
        }

        var activeTags = await LoadActiveTagsAsync(context, customerId, cancellationToken);
        string[] merchantKeys = snapshots
            .Select(snapshot => MerchantKey.FromDescription(snapshot.Row.NormalizedDescription))
            .Where(key => key is not null)
            .Select(key => key!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        SuggestionData suggestions = await SuggestionLoader.LoadAsync(
            context, customerId, merchantKeys, cancellationToken);
        foreach (var snapshot in snapshots)
        {
            SuggestionResult result = MerchantSuggestions.Build(
                snapshot.Row.NormalizedDescription, activeTags, suggestions);
            foreach (var tagId in result.TagIds.Take(MerchantSuggestions.MaxSuggestions))
            {
                snapshot.SuggestedTagIds.Add(tagId);
            }
            snapshot.SuggestedCategoryId = result.CategoryId;
        }

        var rows = new List<ImportReviewRowRecord>(parsedRows.Count);
        foreach (var snapshot in snapshots)
        {
            var candidates = snapshot.NearCandidates
                .OrderBy(candidate => Math.Abs(candidate.Date.DayNumber - snapshot.Row.TransactionDate!.Value.DayNumber))
                .ThenByDescending(candidate => candidate.Similarity)
                .ThenBy(candidate => candidate.Source, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.ImportId, StringComparer.Ordinal)
                .ToArray();
            var visible = candidates.Take(5).Select(candidate => new ImportNearCandidateRecord(
                candidate.CandidateId,
                candidate.Source,
                candidate.ImportId,
                candidate.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StatementText.FormatCents(candidate.Cents),
                candidate.Description,
                Math.Round(candidate.Similarity, 3),
                candidate.Reasons)).ToArray();
            rows.Add(new ImportReviewRowRecord(
                snapshot.Row.RowId,
                snapshot.Row.SourceLine,
                snapshot.Row.SourcePage,
                snapshot.Row.SourceRowNumber,
                new Dictionary<string, string?>(snapshot.Row.RawValues, StringComparer.Ordinal),
                snapshot.Row.TransactionDate,
                snapshot.Row.Description,
                snapshot.Row.NormalizedDescription,
                snapshot.Row.AmountCents,
                snapshot.Row.ValidationCodes.ToArray(),
                snapshot.Row.Warnings.ToArray(),
                snapshot.Row.SourceIdNamespace,
                snapshot.Row.SourceIdValue,
                snapshot.DuplicateState,
                snapshot.Reasons.Distinct(StringComparer.Ordinal).ToArray(),
                snapshot.MatchingTransactionIds.Distinct().ToArray(),
                snapshot.MatchingImportIds.Distinct().ToArray(),
                visible,
                candidates.Length,
                snapshot.WithinFileGroup,
                snapshot.WithinFileState,
                false,
                false,
                snapshot.SuggestedTagIds.Distinct().ToArray(),
                [],
                snapshot.SuggestedCategoryId,
                snapshot.SuggestedCategoryId,
                false));
        }

        return new DuplicateSnapshot(rows);
    }

    private static IncomingRow? ToIncoming(ParsedStatementRow row, int index)
    {
        if (row.TransactionDate is null || row.AmountCents is null)
        {
            return null;
        }

        return new IncomingRow(
            index,
            row.RowId,
            row.SourceRowNumber,
            row.TransactionDate.Value,
            row.AmountCents.Value,
            row.NormalizedDescription,
            row.Description,
            StatementIdentity.ComputeIdentityKey(
                row.TransactionDate.Value, row.AmountCents.Value, row.NormalizedDescription),
            row.SourceIdNamespace,
            row.SourceIdValue);
    }

    private static ParsedStatementRow ToParsedRow(ImportReviewRowRecord row) => new(
        row.RowId,
        row.SourceLine,
        row.SourcePage,
        row.SourceRowNumber,
        row.RawValues,
        row.TransactionDate,
        row.Description,
        row.NormalizedDescription,
        row.AmountCents,
        row.ValidationCodes,
        row.Warnings,
        row.SourceIdNamespace,
        row.SourceIdValue);

    private static List<(IncomingRow Incoming, ExistingRow Existing)> MatchOccurrences(IReadOnlyList<IncomingRow> incoming, IReadOnlyList<ExistingRow> existing, CancellationToken cancellationToken)
    {
        if (incoming.Count == 0 || existing.Count == 0)
        {
            return [];
        }

        if (incoming.All(row => row.Namespace is null) && existing.All(row => row.Namespace is null))
        {
            int count = Math.Min(incoming.Count, existing.Count);
            return Enumerable.Range(0, count).Select(index => (incoming[index], existing[index])).ToList();
        }

        var remainingIncoming = incoming.ToList();
        var remainingExisting = existing.ToList();
        var matched = new List<(IncomingRow, ExistingRow)>();

        void RunStage(
            Func<IncomingRow, ExistingRow, bool> eligible)
        {
            if ((long)remainingIncoming.Count * remainingExisting.Count > 5_000_000)
            {
                throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
            }
            var adjacency = remainingIncoming.Select(row => remainingExisting.Select((candidate, index) => (candidate, index)).Where(pair => eligible(row, pair.candidate)).Select(pair => pair.index).ToList()).ToList();
            int product = adjacency.Sum(list => list.Count);
            if (product > 5_000_000)
            {
                throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
            }
            int[] matchExisting = Enumerable.Repeat(-1, remainingExisting.Count).ToArray();
            bool TryMatch(int incomingIndex, bool[] seen)
            {
                foreach (int existingIndex in adjacency[incomingIndex])
                {
                    if (seen[existingIndex])
                    {
                        continue;
                    }

                    seen[existingIndex] = true;
                    if (matchExisting[existingIndex] == -1 || TryMatch(matchExisting[existingIndex], seen))
                    {
                        matchExisting[existingIndex] = incomingIndex;
                        return true;
                    }
                }

                return false;
            }

            for (int index = 0; index < remainingIncoming.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = TryMatch(index, new bool[remainingExisting.Count]);
            }

            var matchedIncoming = new HashSet<int>();
            for (int existingIndex = 0; existingIndex < matchExisting.Length; existingIndex++)
            {
                int incomingIndex = matchExisting[existingIndex];
                if (incomingIndex < 0)
                {
                    continue;
                }

                matched.Add((remainingIncoming[incomingIndex], remainingExisting[existingIndex]));
                matchedIncoming.Add(incomingIndex);
            }
            remainingIncoming = remainingIncoming.Where((_, index) => !matchedIncoming.Contains(index)).ToList();
            var matchedExisting = matchExisting.Select((incomingIndex, index) => (incomingIndex, index)).Where(pair => pair.incomingIndex >= 0).Select(pair => pair.index).ToHashSet();
            remainingExisting = remainingExisting.Where((_, index) => !matchedExisting.Contains(index)).ToList();
        }

        RunStage((row, candidate) => row.Namespace is not null && candidate.Namespace == row.Namespace && candidate.Value == row.Value);
        RunStage((row, candidate) => row.Namespace is not null && (candidate.Namespace is null || candidate.Namespace != row.Namespace || candidate.Value == row.Value));
        RunStage((row, _) => row.Namespace is null);

        return matched;
    }

    private static double Jaccard(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0)
        {
            return 1;
        }

        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        int intersection = leftTokens.Count(token => rightTokens.Contains(token));
        int union = leftTokens.Count + rightTokens.Count - intersection;

        return union == 0 ? 0 : (double)intersection / union;
    }

    // --- Persistence helpers ------------------------------------------------

    private static async Task<ImportAccountRecord?> GetAccountAsync(ArgusDbContext context,
                                                                    Guid customerId,
                                                                    Guid accountId,
                                                                    CancellationToken cancellationToken)
    {
        return await context.FinancialAccounts
            .AsNoTracking()
            .Where(account => account.CustomerId == customerId && account.Id == accountId)
            .Select(account => new ImportAccountRecord(
                account.Id,
                account.DisplayName,
                account.Currency,
                account.IsArchived,
                account.ImportVersion))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<ImportAccountRecord> LockAccountAsync(ArgusDbContext context,
                                                                    Guid customerId,
                                                                    Guid accountId,
                                                                    CancellationToken cancellationToken)
    {
        List<AccountLockRow> rows = await context.Database.SqlQueryRaw<AccountLockRow>("""
            SELECT id AS "Id", display_name AS "DisplayName", currency AS "Currency",
                   is_archived AS "IsArchived", import_version AS "ImportVersion"
            FROM financial_accounts
            WHERE customer_id = {0} AND id = {1}
            FOR UPDATE
            """, customerId, accountId).ToListAsync(cancellationToken);
        AccountLockRow? row = rows.SingleOrDefault();

        return row is null
            ? throw new ImportStoreException(ImportErrorCodes.NotFound)
            : new ImportAccountRecord(row.Id, row.DisplayName, row.Currency, row.IsArchived, row.ImportVersion);
    }

    private static async Task LockQuotaAsync(ArgusDbContext context,
                                             Guid customerId,
                                             CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO import_quota_locks (customer_id) VALUES ({customerId}) ON CONFLICT DO NOTHING
            """, cancellationToken);
        await context.Database.SqlQuery<int>($"""
            SELECT 1 AS "Value" FROM import_quota_locks WHERE customer_id = {customerId} FOR UPDATE
            """).ToListAsync(cancellationToken);
    }

    private static async Task<long> EnforceQuotaAsync(ArgusDbContext context,
                                                      Guid customerId,
                                                      long payloadBytes,
                                                      CancellationToken cancellationToken)
    {
        List<QuotaUsageRow> usage = await context.Database.SqlQuery<QuotaUsageRow>($"""
            SELECT COUNT(*) AS "OpenReviews", COALESCE(SUM(payload_bytes), 0) AS "UsedPayload"
            FROM import_reviews
            WHERE customer_id = {customerId} AND status = 'Prepared' AND expires_at > NOW()
            """).ToListAsync(cancellationToken);
        QuotaUsageRow row = usage.Single();
        if (row.OpenReviews >= ImportLimits.MaxOpenReviewsPerCustomer || row.UsedPayload + payloadBytes > ImportLimits.MaxOpenReviewPayloadBytes)
        {
            throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
        }

        return row.UsedPayload;
    }

    private static async Task<Guid?> ReviewAccountIdAsync(ArgusDbContext context,
                                                          Guid customerId,
                                                          Guid reviewId,
                                                          CancellationToken cancellationToken)
    {
        return await context.ImportReviews
            .Where(review => review.CustomerId == customerId && review.Id == reviewId)
            .Select(review => (Guid?)review.FinancialAccountId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<ImportReviewSummaryRecord?> ReadReviewHeaderAsync(ArgusDbContext context,
                                                                                Guid customerId,
                                                                                Guid reviewId,
                                                                                bool forUpdate,
                                                                                CancellationToken cancellationToken)
    {
        const string headerSql = """
            SELECT id AS "Id",
                   financial_account_id AS "FinancialAccountId",
                   source_kind AS "SourceKind",
                   start_date AS "StartDate",
                   end_date AS "EndDate",
                   version AS "Version",
                   status AS "Status",
                   expires_at AS "ExpiresAt",
                   account_import_version AS "AccountImportVersion",
                   statement_fingerprint AS "StatementFingerprint",
                   summary_hash AS "SummaryHash",
                   total_rows AS "TotalRows",
                   imported_count AS "ImportedCount",
                   suppressed_count AS "SuppressedCount",
                   skipped_count AS "SkippedCount",
                   (SELECT COUNT(*) FROM import_review_rows r
                    WHERE r.review_id = import_reviews.id
                      AND jsonb_array_length(r.validation_codes) > 0 AND r.skip = FALSE) AS "InvalidUnskippedCount",
                   (SELECT COUNT(*) FROM import_review_rows r
                    WHERE r.review_id = import_reviews.id AND r.within_file_state = 'pending') AS "WithinFilePendingCount",
                   possible_duplicate_count AS "PossibleDuplicateCount",
                   exact_override_count AS "ExactOverrideCount",
                   (SELECT COUNT(*) FROM import_review_rows r
                    WHERE r.review_id = import_reviews.id
                      AND r.skip = FALSE
                      AND r.transaction_date IS NOT NULL AND r.amount_cents IS NOT NULL
                      AND (r.duplicate_state NOT IN ('suppressed', 'suppressed_within_file') OR r.override)
                      AND r.selected_category_id IS NOT NULL) AS "CategorisedCount",
                   statement_override AS "StatementOverride",
                   statement_override_count AS "StatementOverrideCount",
                   payload_bytes AS "PayloadBytes",
                   created_at AS "CreatedAt",
                   updated_at AS "UpdatedAt"
            FROM import_reviews
            WHERE customer_id = {0} AND id = {1}
            """;
        string sql = forUpdate ? string.Concat(headerSql, "\nFOR UPDATE") : headerSql;
        List<ReviewHeaderRow> rows = await context.Database
            .SqlQueryRaw<ReviewHeaderRow>(sql, customerId, reviewId)
            .ToListAsync(cancellationToken);
        ReviewHeaderRow? row = rows.SingleOrDefault();

        return row is null
            ? null
            : new ImportReviewSummaryRecord(
                row.Id,
                row.FinancialAccountId,
                row.SourceKind,
                row.StartDate,
                row.EndDate,
                row.Version,
                row.Status,
                row.ExpiresAt,
                row.AccountImportVersion,
                row.StatementFingerprint,
                row.SummaryHash,
                row.TotalRows,
                row.ImportedCount,
                row.SuppressedCount,
                row.SkippedCount,
                row.InvalidUnskippedCount,
                row.WithinFilePendingCount,
                row.PossibleDuplicateCount,
                row.ExactOverrideCount,
                row.CategorisedCount,
                row.StatementOverride,
                row.StatementOverrideCount,
                row.PayloadBytes,
                row.CreatedAt,
                row.UpdatedAt);
    }

    private static async Task<IReadOnlyList<ImportReviewRowRecord>> ReadReviewRowsAsync(ArgusDbContext context,
                                                                                        Guid reviewId,
                                                                                        int page,
                                                                                        int pageSize,
                                                                                        bool descending,
                                                                                        bool possibleDuplicatesOnly,
                                                                                        bool untaggedOnly,
                                                                                        CancellationToken cancellationToken)
    {
        string orderBy = descending
            ? "transaction_date DESC NULLS LAST, source_row_number DESC, row_id DESC"
            : "transaction_date ASC NULLS LAST, source_row_number ASC, row_id ASC";
        const string rowSql = """
            SELECT row_id AS "RowId",
                   source_line AS "SourceLine",
                   source_page AS "SourcePage",
                   source_row_number AS "SourceRowNumber",
                   raw_values::text AS "RawValues",
                   transaction_date AS "TransactionDate",
                   description AS "Description",
                   normalized_description AS "NormalizedDescription",
                   amount_cents AS "AmountCents",
                   validation_codes::text AS "ValidationCodes",
                   warnings::text AS "Warnings",
                   source_id_namespace AS "SourceIdNamespace",
                   source_id_value AS "SourceIdValue",
                   duplicate_state AS "DuplicateState",
                   duplicate_reasons::text AS "DuplicateReasons",
                   matching_transaction_ids::text AS "MatchingTransactionIds",
                   matching_import_ids::text AS "MatchingImportIds",
                   near_candidates::text AS "NearCandidates",
                   near_candidate_total AS "NearCandidateTotal",
                   within_file_group AS "WithinFileGroup",
                   within_file_state AS "WithinFileState",
                   skip AS "Skip",
                   override AS "Override",
                   suggested_tag_ids::text AS "SuggestedTagIds",
                   selected_tag_ids::text AS "SelectedTagIds",
                   suggested_category_id AS "SuggestedCategoryId",
                   selected_category_id AS "SelectedCategoryId",
                   category_decided AS "CategoryDecided"
            FROM import_review_rows
            WHERE review_id = {0}
              AND ({1} = FALSE OR (
                    skip = FALSE
                    AND (near_candidate_total > 0 OR duplicate_reasons @> '["stable_id_changed"]'::jsonb)
                  ))
              AND ({2} = FALSE OR jsonb_array_length(selected_tag_ids) = 0)
            ORDER BY
            """;
        string sql = string.Concat(rowSql, "\n", orderBy, "\nLIMIT {3} OFFSET {4}");
        List<ReviewRowText> rows = await context.Database.SqlQueryRaw<ReviewRowText>(
            sql,
            reviewId,
            possibleDuplicatesOnly,
            untaggedOnly,
            pageSize == int.MaxValue ? 100_000 : pageSize,
            pageSize == int.MaxValue ? 0 : (page - 1) * pageSize).ToListAsync(cancellationToken);

        return rows.Select(MapReviewRow).ToList();
    }

    private static ImportReviewRowRecord MapReviewRow(ReviewRowText row) => new(
        row.RowId,
        row.SourceLine,
        row.SourcePage,
        row.SourceRowNumber,
        JsonSerializer.Deserialize<Dictionary<string, string?>>(row.RawValues, _storageJson)
            ?? new Dictionary<string, string?>(),
        row.TransactionDate,
        row.Description,
        row.NormalizedDescription,
        row.AmountCents,
        JsonSerializer.Deserialize<string[]>(row.ValidationCodes, _storageJson) ?? [],
        JsonSerializer.Deserialize<string[]>(row.Warnings, _storageJson) ?? [],
        row.SourceIdNamespace,
        row.SourceIdValue,
        row.DuplicateState,
        JsonSerializer.Deserialize<string[]>(row.DuplicateReasons, _storageJson) ?? [],
        JsonSerializer.Deserialize<Guid[]>(row.MatchingTransactionIds, _storageJson) ?? [],
        JsonSerializer.Deserialize<Guid[]>(row.MatchingImportIds, _storageJson) ?? [],
        JsonSerializer.Deserialize<ImportNearCandidateRecord[]>(row.NearCandidates, _storageJson) ?? [],
        row.NearCandidateTotal,
        row.WithinFileGroup,
        row.WithinFileState,
        row.Skip,
        row.Override,
        JsonSerializer.Deserialize<Guid[]>(row.SuggestedTagIds, _storageJson) ?? [],
        JsonSerializer.Deserialize<Guid[]>(row.SelectedTagIds, _storageJson) ?? [],
        row.SuggestedCategoryId,
        row.SelectedCategoryId,
        row.CategoryDecided);

    private static async Task<int> CountReviewRowsAsync(ArgusDbContext context,
                                                        Guid reviewId,
                                                        bool possibleDuplicatesOnly,
                                                        bool untaggedOnly,
                                                        CancellationToken cancellationToken)
    {
        long count = await context.Database.SqlQueryRaw<long>("""
            SELECT COUNT(*) AS "Value"
            FROM import_review_rows
            WHERE review_id = {0}
              AND ({1} = FALSE OR (
                    skip = FALSE
                    AND (near_candidate_total > 0 OR duplicate_reasons @> '["stable_id_changed"]'::jsonb)
                  ))
              AND ({2} = FALSE OR jsonb_array_length(selected_tag_ids) = 0)
            """, reviewId, possibleDuplicatesOnly, untaggedOnly).SingleAsync(cancellationToken);

        return (int)count;
    }

    private static async Task InsertReviewAsync(ArgusDbContext context,
                                                Guid reviewId,
                                                Guid customerId,
                                                Guid accountId,
                                                string sourceKind,
                                                ParserConfig config,
                                                DateOnly startDate,
                                                DateOnly endDate,
                                                DateTime now,
                                                DateTime expiresAt,
                                                long accountImportVersion,
                                                string statementFingerprint,
                                                string summaryHash,
                                                RowCounts counts,
                                                long payloadBytes,
                                                IReadOnlyList<ImportReviewRowRecord> rows,
                                                CancellationToken cancellationToken)
    {
        context.ImportReviews.Add(new ImportReview
        {
            Id = reviewId,
            CustomerId = customerId,
            FinancialAccountId = accountId,
            SourceKind = sourceKind,
            ParserConfig = ToJsonDocument(config),
            Currency = "ZAR",
            StartDate = startDate,
            EndDate = endDate,
            Version = 1,
            Status = "Prepared",
            ExpiresAt = expiresAt,
            AccountImportVersion = accountImportVersion,
            DuplicatePolicyVersion = ImportLimits.DuplicatePolicyVersion,
            StatementFingerprint = statementFingerprint,
            SummaryHash = summaryHash,
            TotalRows = counts.TotalRows,
            ImportedCount = counts.Imported,
            SuppressedCount = counts.Suppressed,
            SkippedCount = counts.Skipped,
            PossibleDuplicateCount = counts.PossibleDuplicates,
            ExactOverrideCount = 0,
            StatementOverride = false,
            StatementOverrideCount = 0,
            PayloadBytes = payloadBytes,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await context.SaveChangesAsync(cancellationToken);

        // Bounded EF-owned set inserts: each batch is one parameterized
        // unnest statement, so a ten-thousand-row review never builds one
        // unbounded command and never needs a driver COPY.
        foreach (ImportReviewRowRecord[] batch in rows.Chunk(ReviewRowInsertBatchSize))
        {
            await InsertReviewRowBatchAsync(context, batch, reviewId, customerId, now, cancellationToken);
        }
    }

    private static async Task InsertReviewRowBatchAsync(ArgusDbContext context,
                                                        IReadOnlyList<ImportReviewRowRecord> batch,
                                                        Guid reviewId,
                                                        Guid customerId,
                                                        DateTime now,
                                                        CancellationToken cancellationToken)
    {
        int count = batch.Count;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO import_review_rows (
                row_id, review_id, customer_id, source_line, source_page, source_row_number, raw_values,
                transaction_date, description, normalized_description, amount_cents, source_id_namespace,
                source_id_value, validation_codes, warnings, duplicate_state, duplicate_reasons,
                matching_transaction_ids, matching_import_ids, near_candidates, near_candidate_total,
                within_file_group, within_file_state, skip, override, suggested_tag_ids, selected_tag_ids,
                suggested_category_id, selected_category_id, category_decided, created_at, updated_at)
            SELECT t.row_id, t.review_id, t.customer_id, t.source_line, t.source_page, t.source_row_number,
                   CAST(t.raw_values AS jsonb), t.transaction_date, t.description, t.normalized_description,
                   t.amount_cents, t.source_id_namespace, t.source_id_value,
                   CAST(t.validation_codes AS jsonb), CAST(t.warnings AS jsonb), t.duplicate_state,
                   CAST(t.duplicate_reasons AS jsonb), CAST(t.matching_transaction_ids AS jsonb),
                   CAST(t.matching_import_ids AS jsonb), CAST(t.near_candidates AS jsonb),
                   t.near_candidate_total, t.within_file_group, t.within_file_state, t.skip, t.override,
                   CAST(t.suggested_tag_ids AS jsonb), CAST(t.selected_tag_ids AS jsonb),
                   t.suggested_category_id, t.selected_category_id, t.category_decided, t.created_at, t.updated_at
            FROM unnest(
                CAST({batch.Select(row => row.RowId).ToArray()} AS uuid[]),
                CAST({Enumerable.Repeat(reviewId, count).ToArray()} AS uuid[]),
                CAST({Enumerable.Repeat(customerId, count).ToArray()} AS uuid[]),
                CAST({batch.Select(row => row.SourceLine).ToArray()} AS integer[]),
                CAST({batch.Select(row => row.SourcePage).ToArray()} AS integer[]),
                CAST({batch.Select(row => row.SourceRowNumber).ToArray()} AS integer[]),
                CAST({batch.Select(row => JsonSerializer.Serialize(row.RawValues, _storageJson)).ToArray()} AS text[]),
                CAST({batch.Select(row => row.TransactionDate).ToArray()} AS date[]),
                CAST({batch.Select(row => row.Description).ToArray()} AS text[]),
                CAST({batch.Select(row => row.NormalizedDescription).ToArray()} AS text[]),
                CAST({batch.Select(row => row.AmountCents).ToArray()} AS bigint[]),
                CAST({batch.Select(row => row.SourceIdNamespace).ToArray()} AS text[]),
                CAST({batch.Select(row => row.SourceIdValue).ToArray()} AS text[]),
                CAST({batch.Select(row => JsonSerializer.Serialize(row.ValidationCodes, _storageJson)).ToArray()} AS text[]),
                CAST({batch.Select(row => JsonSerializer.Serialize(row.Warnings, _storageJson)).ToArray()} AS text[]),
                CAST({batch.Select(row => row.DuplicateState).ToArray()} AS text[]),
                CAST({batch.Select(row => JsonSerializer.Serialize(row.DuplicateReasons, _storageJson)).ToArray()} AS text[]),
                CAST({batch.Select(row => JsonSerializer.Serialize(row.MatchingTransactionIds, _storageJson)).ToArray()} AS text[]),
                CAST({batch.Select(row => JsonSerializer.Serialize(row.MatchingImportIds, _storageJson)).ToArray()} AS text[]),
                CAST({batch.Select(row => JsonSerializer.Serialize(row.NearCandidates, _storageJson)).ToArray()} AS text[]),
                CAST({batch.Select(row => row.NearCandidateTotal).ToArray()} AS integer[]),
                CAST({batch.Select(row => row.WithinFileGroup).ToArray()} AS text[]),
                CAST({batch.Select(row => row.WithinFileState).ToArray()} AS text[]),
                CAST({batch.Select(row => row.Skip).ToArray()} AS boolean[]),
                CAST({batch.Select(row => row.Override).ToArray()} AS boolean[]),
                CAST({batch.Select(row => JsonSerializer.Serialize(row.SuggestedTagIds, _storageJson)).ToArray()} AS text[]),
                CAST({batch.Select(row => JsonSerializer.Serialize(row.SelectedTagIds, _storageJson)).ToArray()} AS text[]),
                CAST({batch.Select(row => row.SuggestedCategoryId).ToArray()} AS uuid[]),
                CAST({batch.Select(row => row.SelectedCategoryId).ToArray()} AS uuid[]),
                CAST({batch.Select(row => row.CategoryDecided).ToArray()} AS boolean[]),
                CAST({Enumerable.Repeat(now, count).ToArray()} AS timestamptz[]),
                CAST({Enumerable.Repeat(now, count).ToArray()} AS timestamptz[])
            ) AS t(
                row_id, review_id, customer_id, source_line, source_page, source_row_number, raw_values,
                transaction_date, description, normalized_description, amount_cents, source_id_namespace,
                source_id_value, validation_codes, warnings, duplicate_state, duplicate_reasons,
                matching_transaction_ids, matching_import_ids, near_candidates, near_candidate_total,
                within_file_group, within_file_state, skip, override, suggested_tag_ids, selected_tag_ids,
                suggested_category_id, selected_category_id, category_decided, created_at, updated_at)
            """, cancellationToken);
    }

    private static ImportReviewRow MapReviewRowEntity(ImportReviewRowRecord row, Guid reviewId, Guid customerId, DateTime now) => new()
    {
        RowId = row.RowId,
        ReviewId = reviewId,
        CustomerId = customerId,
        SourceLine = row.SourceLine,
        SourcePage = row.SourcePage,
        SourceRowNumber = row.SourceRowNumber,
        RawValues = ToJsonDocument(row.RawValues),
        TransactionDate = row.TransactionDate,
        Description = row.Description,
        NormalizedDescription = row.NormalizedDescription,
        AmountCents = row.AmountCents,
        SourceIdNamespace = row.SourceIdNamespace,
        SourceIdValue = row.SourceIdValue,
        ValidationCodes = ToJsonDocument(row.ValidationCodes),
        Warnings = ToJsonDocument(row.Warnings),
        DuplicateState = row.DuplicateState,
        DuplicateReasons = ToJsonDocument(row.DuplicateReasons),
        MatchingTransactionIds = ToJsonDocument(row.MatchingTransactionIds),
        MatchingImportIds = ToJsonDocument(row.MatchingImportIds),
        NearCandidates = ToJsonDocument(row.NearCandidates),
        NearCandidateTotal = row.NearCandidateTotal,
        WithinFileGroup = row.WithinFileGroup,
        WithinFileState = row.WithinFileState,
        Skip = row.Skip,
        Override = row.Override,
        SuggestedTagIds = ToJsonDocument(row.SuggestedTagIds),
        SelectedTagIds = ToJsonDocument(row.SelectedTagIds),
        SuggestedCategoryId = row.SuggestedCategoryId,
        SelectedCategoryId = row.SelectedCategoryId,
        CategoryDecided = row.CategoryDecided,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static async Task InsertTransactionBatchAsync(ArgusDbContext context,
                                                           IReadOnlyList<ImportReviewRowRecord> batch,
                                                           Dictionary<string, int> nextOccurrence,
                                                           List<(Guid TransactionId, Guid[] TagIds)> transactionTags,
                                                           Guid customerId,
                                                           Guid accountId,
                                                           Guid statementId,
                                                           string sourceKind,
                                                           Guid categoryId,
                                                           DateTime createdAt,
                                                           CancellationToken cancellationToken)
    {
        int count = batch.Count;
        var ids = new Guid[count];
        var dates = new DateOnly[count];
        var descriptions = new string[count];
        var normalizedDescriptions = new string[count];
        var amounts = new decimal[count];
        var categoryIds = new Guid[count];
        var categoryConfirmed = new bool[count];
        var sourcePages = new int?[count];
        var sourceRowNumbers = new int[count];
        var identityKeys = new string[count];
        var occurrences = new int[count];
        var identityVersions = new int[count];
        var sourceIdNamespaces = new string?[count];
        var sourceIdValues = new string?[count];
        for (int index = 0; index < count; index++)
        {
            ImportReviewRowRecord row = batch[index];
            string identityKey = ComputeIdentityKey(row);
            int occurrence = nextOccurrence.GetValueOrDefault(identityKey, 1);
            nextOccurrence[identityKey] = occurrence + 1;
            Guid transactionId = Guid.NewGuid();
            transactionTags.Add((transactionId, row.SelectedTagIds));
            ids[index] = transactionId;
            dates[index] = row.TransactionDate!.Value;
            descriptions[index] = row.Description;
            normalizedDescriptions[index] = row.NormalizedDescription;
            amounts[index] = row.AmountCents!.Value / 100m;
            categoryIds[index] = row.SelectedCategoryId ?? categoryId;
            categoryConfirmed[index] = row.CategoryDecided;
            sourcePages[index] = row.SourcePage;
            sourceRowNumbers[index] = row.SourceRowNumber;
            identityKeys[index] = identityKey;
            occurrences[index] = occurrence;
            identityVersions[index] = ImportLimits.IdentityVersion;
            sourceIdNamespaces[index] = row.SourceIdNamespace;
            sourceIdValues[index] = row.SourceIdValue;
        }

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (
                id, customer_id, financial_account_id, statement_import_id, transaction_date,
                description, normalized_description, amount, category_id, category_confirmed, source_type,
                source_page, source_row_number, identity_key, occurrence_index, identity_version,
                source_id_namespace, source_id_value, is_hidden_by_user, version, created_at, updated_at)
            SELECT t.id, t.customer_id, t.financial_account_id, t.statement_import_id, t.transaction_date,
                   t.description, t.normalized_description, t.amount, t.category_id, t.category_confirmed, t.source_type,
                   t.source_page, t.source_row_number, t.identity_key, t.occurrence_index, t.identity_version,
                   t.source_id_namespace, t.source_id_value, t.is_hidden_by_user, t.version, t.created_at, t.updated_at
            FROM unnest(
                CAST({ids} AS uuid[]),
                CAST({Enumerable.Repeat(customerId, count).ToArray()} AS uuid[]),
                CAST({Enumerable.Repeat(accountId, count).ToArray()} AS uuid[]),
                CAST({Enumerable.Repeat(statementId, count).ToArray()} AS uuid[]),
                CAST({dates} AS date[]),
                CAST({descriptions} AS text[]),
                CAST({normalizedDescriptions} AS text[]),
                CAST({amounts} AS numeric[]),
                CAST({categoryIds} AS uuid[]),
                CAST({categoryConfirmed} AS boolean[]),
                CAST({Enumerable.Repeat(sourceKind, count).ToArray()} AS text[]),
                CAST({sourcePages} AS integer[]),
                CAST({sourceRowNumbers} AS integer[]),
                CAST({identityKeys} AS text[]),
                CAST({occurrences} AS integer[]),
                CAST({identityVersions} AS integer[]),
                CAST({sourceIdNamespaces} AS text[]),
                CAST({sourceIdValues} AS text[]),
                CAST({Enumerable.Repeat(false, count).ToArray()} AS boolean[]),
                CAST({Enumerable.Repeat(1, count).ToArray()} AS integer[]),
                CAST({Enumerable.Repeat(createdAt, count).ToArray()} AS timestamptz[]),
                CAST({Enumerable.Repeat(createdAt, count).ToArray()} AS timestamptz[])
            ) AS t(
                id, customer_id, financial_account_id, statement_import_id, transaction_date,
                description, normalized_description, amount, category_id, category_confirmed, source_type,
                source_page, source_row_number, identity_key, occurrence_index, identity_version,
                source_id_namespace, source_id_value, is_hidden_by_user, version, created_at, updated_at)
            """, cancellationToken);
    }

    private static async Task InsertTagLinkBatchAsync(ArgusDbContext context,
                                                      IReadOnlyList<(Guid TransactionId, Guid TagId)> batch,
                                                      Guid customerId,
                                                      DateTime assignedAt,
                                                      CancellationToken cancellationToken)
    {
        int count = batch.Count;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transaction_merchant_tags (customer_id, transaction_id, merchant_tag_id, assigned_at)
            SELECT t.customer_id, t.transaction_id, t.merchant_tag_id, t.assigned_at
            FROM unnest(
                CAST({Enumerable.Repeat(customerId, count).ToArray()} AS uuid[]),
                CAST({batch.Select(link => link.TransactionId).ToArray()} AS uuid[]),
                CAST({batch.Select(link => link.TagId).ToArray()} AS uuid[]),
                CAST({Enumerable.Repeat(assignedAt, count).ToArray()} AS timestamptz[])
            ) AS t(customer_id, transaction_id, merchant_tag_id, assigned_at)
            """, cancellationToken);
    }

    private static JsonDocument ToJsonDocument<T>(T value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value, _storageJson));

    private static async Task SetWithinFileGroupAsync(ArgusDbContext context,
                                                      Guid reviewId,
                                                      string group,
                                                      string state,
                                                      bool skip,
                                                      CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_review_rows
            SET within_file_state = {state}, skip = {skip}, override = CASE WHEN {skip} THEN FALSE ELSE override END,
                updated_at = NOW()
            WHERE review_id = {reviewId} AND within_file_group = {group}
            """, cancellationToken);
    }

    private static async Task UpdateRowDecisionsAsync(ArgusDbContext context,
                                                      Guid rowId,
                                                      bool skip,
                                                      bool overrideValue,
                                                      string withinFileState,
                                                      Guid[] selectedTagIds,
                                                      Guid? selectedCategoryId,
                                                      bool categoryDecided,
                                                      CancellationToken cancellationToken)
    {
        string selectedTagsJson = JsonSerializer.Serialize(selectedTagIds, _storageJson);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_review_rows
            SET skip = {skip},
                override = {overrideValue},
                within_file_state = {withinFileState},
                selected_tag_ids = CAST({selectedTagsJson} AS jsonb),
                selected_category_id = {selectedCategoryId},
                category_decided = {categoryDecided},
                updated_at = NOW()
            WHERE row_id = {rowId}
            """, cancellationToken);
    }

    private static async Task UpdateDuplicateSnapshotAsync(ArgusDbContext context,
                                                           ImportReviewRowRecord row,
                                                           CancellationToken cancellationToken)
    {
        string duplicateReasons = JsonSerializer.Serialize(row.DuplicateReasons, _storageJson);
        string matchingTransactionIds = JsonSerializer.Serialize(row.MatchingTransactionIds, _storageJson);
        string matchingImportIds = JsonSerializer.Serialize(row.MatchingImportIds, _storageJson);
        string nearCandidates = JsonSerializer.Serialize(row.NearCandidates, _storageJson);
        string suggestedTagIds = JsonSerializer.Serialize(row.SuggestedTagIds, _storageJson);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_review_rows
            SET duplicate_state = {row.DuplicateState},
                duplicate_reasons = CAST({duplicateReasons} AS jsonb),
                matching_transaction_ids = CAST({matchingTransactionIds} AS jsonb),
                matching_import_ids = CAST({matchingImportIds} AS jsonb),
                near_candidates = CAST({nearCandidates} AS jsonb),
                near_candidate_total = {row.NearCandidateTotal},
                within_file_group = {row.WithinFileGroup},
                within_file_state = {row.WithinFileState},
                override = {row.Override},
                suggested_tag_ids = CAST({suggestedTagIds} AS jsonb),
                suggested_category_id = {row.SuggestedCategoryId},
                selected_category_id = {row.SelectedCategoryId},
                category_decided = {row.CategoryDecided},
                updated_at = NOW()
            WHERE row_id = {row.RowId}
            """, cancellationToken);
    }

    private static async Task<(long OpenReviews, long UsedPayload)> QuotaUsageExcludingAsync(ArgusDbContext context,
                                                                                             Guid customerId,
                                                                                             Guid reviewId,
                                                                                             CancellationToken cancellationToken)
    {
        List<QuotaUsageRow> usage = await context.Database.SqlQuery<QuotaUsageRow>($"""
            SELECT COUNT(*) AS "OpenReviews", COALESCE(SUM(payload_bytes), 0) AS "UsedPayload"
            FROM import_reviews
            WHERE customer_id = {customerId}
              AND status = 'Prepared'
              AND expires_at > NOW()
              AND id <> {reviewId}
            """).ToListAsync(cancellationToken);
        QuotaUsageRow row = usage.Single();

        return (row.OpenReviews, row.UsedPayload);
    }

    private static async Task UpdateReviewSummaryAsync(ArgusDbContext context,
                                                       Guid reviewId,
                                                       int version,
                                                       RowCounts counts,
                                                       string summaryHash,
                                                       bool statementOverride,
                                                       long payloadBytes,
                                                       CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_reviews
            SET version = {version},
                summary_hash = {summaryHash},
                total_rows = {counts.TotalRows},
                imported_count = {counts.Imported},
                suppressed_count = {counts.Suppressed},
                skipped_count = {counts.Skipped},
                possible_duplicate_count = {counts.PossibleDuplicates},
                exact_override_count = {counts.ExactOverrides},
                statement_override = {statementOverride},
                statement_override_count = {(statementOverride ? 1 : 0)},
                payload_bytes = {payloadBytes},
                updated_at = NOW()
            WHERE id = {reviewId}
            """, cancellationToken);
    }

    private static async Task UpdateReviewAccountVersionAsync(ArgusDbContext context,
                                                              Guid reviewId,
                                                              long accountImportVersion,
                                                              CancellationToken cancellationToken)
    {
        await context.ImportReviews
            .Where(review => review.Id == reviewId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(review => review.AccountImportVersion, accountImportVersion),
                cancellationToken);
    }

    private static async Task<IReadOnlyList<ExistingRow>> QueryExistingAsync(ArgusDbContext context,
                                                                             Guid customerId,
                                                                             Guid accountId,
                                                                             Func<IQueryable<Transaction>, IQueryable<Transaction>> filter,
                                                                             CancellationToken cancellationToken)
    {
        IQueryable<Transaction> query = context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.CustomerId == customerId && transaction.FinancialAccountId == accountId);
        List<ExistingRow> rows = await filter(query)
            .Take(20_001)
            .Select(transaction => new ExistingRow(
                transaction.Id,
                transaction.IdentityKey,
                transaction.OccurrenceIndex,
                transaction.TransactionDate,
                (long)(transaction.Amount * 100m),
                transaction.NormalizedDescription,
                transaction.Description,
                transaction.SourceIdNamespace,
                transaction.SourceIdValue,
                transaction.StatementImportId))
            .ToListAsync(cancellationToken);
        if (rows.Count > 20_000)
        {
            throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
        }

        return rows;
    }

    private static void EnsureEditable(ImportReviewSummaryRecord summary, int expectedVersion)
    {
        if (summary.Status == "Expired" || summary.ExpiresAt <= DateTime.UtcNow)
        {
            throw new ImportStoreException(ImportErrorCodes.ReviewExpired);
        }
        if (summary.Status != "Prepared")
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        if (summary.Version != expectedVersion)
        {
            throw new ImportStoreException(ImportErrorCodes.VersionConflict);
        }
    }

    private static RowCounts CountRows(IReadOnlyList<ImportReviewRowRecord> rows)
    {
        int imported = 0;
        int suppressed = 0;
        int possibleDuplicates = 0;
        int exactOverrides = 0;
        foreach (var row in rows)
        {
            bool isSuppressed = row.DuplicateState is "suppressed" or "suppressed_within_file";
            if (row.Skip)
            {
                continue;
            }

            if (isSuppressed && !row.Override)
            {
                suppressed++;
                continue;
            }
            if (row.TransactionDate is not null && row.AmountCents is not null)
            {
                imported++;
            }
            if (isSuppressed && row.Override)
            {
                exactOverrides++;
            }
            if (row.NearCandidateTotal > 0 || row.DuplicateReasons.Contains("stable_id_changed"))
            {
                possibleDuplicates++;
            }
        }
        int total = rows.Count;
        int skipped = total - imported - suppressed;

        return new RowCounts(total, imported, suppressed, skipped, possibleDuplicates, exactOverrides);
    }

    private static int CountCategorised(IReadOnlyList<ImportReviewRowRecord> rows) =>
        rows.Count(row =>
            !row.Skip &&
            row.TransactionDate is not null &&
            row.AmountCents is not null &&
            (row.DuplicateState is not ("suppressed" or "suppressed_within_file") || row.Override) &&
            row.SelectedCategoryId is not null);

    private static string ComputeSummaryHash(Guid reviewId,
                                             int version,
                                             IReadOnlyList<ImportReviewRowRecord> rows,
                                             RowCounts counts,
                                             bool statementOverride)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        Add("summary-v1");
        Add(reviewId.ToString("N"));
        Add(version.ToString(CultureInfo.InvariantCulture));
        Add(statementOverride ? "1" : "0");
        Add(counts.TotalRows.ToString(CultureInfo.InvariantCulture));
        Add(counts.Imported.ToString(CultureInfo.InvariantCulture));
        Add(counts.Suppressed.ToString(CultureInfo.InvariantCulture));
        Add(counts.Skipped.ToString(CultureInfo.InvariantCulture));
        Add(counts.PossibleDuplicates.ToString(CultureInfo.InvariantCulture));
        Add(counts.ExactOverrides.ToString(CultureInfo.InvariantCulture));
        foreach (var row in rows.OrderBy(row => row.RowId))
        {
            Add(row.RowId.ToString("N"));
            Add(row.Skip ? "1" : "0");
            Add(row.Override ? "1" : "0");
            Add(row.WithinFileState);
            Add(row.DuplicateState);
            Add(string.Join(',', row.DuplicateReasons.Order(StringComparer.Ordinal)));
            Add(string.Join(',', row.SelectedTagIds.Order()));
            Add(row.SelectedCategoryId?.ToString("N") ?? string.Empty);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static long EstimatePayload(ParsedStatement parsed, ParserConfig config, IReadOnlyList<ImportReviewRowRecord>? rows = null)
    {
        long bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(config, _storageJson));
        foreach (var row in parsed.Rows)
        {
            bytes += Encoding.UTF8.GetByteCount(row.Description);
            bytes += Encoding.UTF8.GetByteCount(row.NormalizedDescription);
            bytes += JsonSerializer.Serialize(row.RawValues, _storageJson).Length;
            bytes += JsonSerializer.Serialize(row.ValidationCodes, _storageJson).Length;
            bytes += JsonSerializer.Serialize(row.Warnings, _storageJson).Length;
        }
        if (rows is not null)
        {
            bytes += Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(rows, _storageJson));
        }

        return bytes;
    }

    // --- Commit and terminal lifecycle -------------------------------------

    public async Task<ImportCommitResult> CommitReviewAsync(Guid customerId,
                                                            Guid reviewId,
                                                            int expectedVersion,
                                                            string confirmedSummaryHash,
                                                            CancellationToken cancellationToken = default)
    {
        // A denied commit must not seed finance data. Prove the operation
        // belongs to this customer before category initialization; the normal
        // lock-order rechecks below still establish commit authority.
        if (!await OwnedOperationExistsAsync(customerId, reviewId, cancellationToken))
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }

        await _financeStore.EnsureCategoriesSeededAsync(customerId, cancellationToken);
        string requestDigest = Digest($"commit-v1|{expectedVersion}|{confirmedSummaryHash}");

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // A receipt is the truth after a lost response, including after the
        // review payload has been purged. Resolve the account from the receipt
        // before looking for the review.
        var receiptAccountId = await ReceiptAccountIdAsync(context, customerId, reviewId, cancellationToken);
        if (receiptAccountId is not null)
        {
            _ = await LockAccountAsync(context, customerId, receiptAccountId.Value, cancellationToken);
            var replay = await ReplayReceiptAsync(context, customerId, reviewId, requestDigest, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var accountId = await ReviewAccountIdAsync(context, customerId, reviewId, cancellationToken);
        if (accountId is null)
        {
            var lateReceiptAccount = await ReceiptAccountIdAsync(context, customerId, reviewId, cancellationToken);
            if (lateReceiptAccount is not null)
            {
                _ = await LockAccountAsync(context, customerId, lateReceiptAccount.Value, cancellationToken);
                var replay = await ReplayReceiptAsync(context, customerId, reviewId, requestDigest, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        var account = await LockAccountAsync(context, customerId, accountId.Value, cancellationToken);

        // Re-check under the account lock in case a receipt appeared while this
        // request waited for the account.
        var racedReceipt = await ReadReceiptAsync(
            context, customerId, reviewId, false, cancellationToken);
        if (racedReceipt is not null)
        {
            var replay = await ReplayReceiptAsync(context, customerId, reviewId, requestDigest, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var review = await ReadReviewHeaderAsync(context, customerId, reviewId, true, cancellationToken);
        if (review is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        if (review.Status == "Expired")
        {
            throw new ImportStoreException(ImportErrorCodes.ReviewExpired);
        }
        await ThrowIfCommitExpiredAsync(context, review.ExpiresAt, cancellationToken);
        if (review.Status != "Prepared")
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        if (review.Version != expectedVersion || review.SummaryHash != confirmedSummaryHash)
        {
            throw new ImportStoreException(ImportErrorCodes.VersionConflict);
        }
        if (review.AccountImportVersion != account.ImportVersion)
        {
            throw new ImportStoreException(ImportErrorCodes.ReviewStale);
        }
        if (account.IsArchived)
        {
            throw new ImportStoreException(ImportErrorCodes.AccountArchived);
        }
        if (!string.Equals(account.Currency, "ZAR", StringComparison.Ordinal))
        {
            throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
        }

        var rows = await ReadReviewRowsAsync(context, reviewId, 1, int.MaxValue, descending: false, possibleDuplicatesOnly: false, untaggedOnly: false, cancellationToken);
        if (rows.Any(row => row.ValidationCodes.Length > 0 && !row.Skip))
        {
            throw new ImportStoreException(ImportErrorCodes.InvalidRows);
        }
        if (rows.Any(row => row.WithinFileState == "pending"))
        {
            throw new ImportStoreException(ImportErrorCodes.DuplicateDecisionRequired);
        }
        if (rows.Any(row => row.Skip && row.Override))
        {
            throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
        }
        var validRows = rows.Where(row => row.TransactionDate is not null && row.AmountCents is not null).ToList();
        if (validRows.Count == 0 || validRows.All(row => row.Skip))
        {
            throw new ImportStoreException(ImportErrorCodes.NoIncludedRows);
        }
        var included = rows.Where(row => !row.Skip && (row.DuplicateState is not ("suppressed" or "suppressed_within_file") || row.Override)).ToList();
        if (included.Any(row => row.TransactionDate is null || row.AmountCents is null))
        {
            throw new ImportStoreException(ImportErrorCodes.InvalidRows);
        }

        var counts = CountRows(rows);
        if (counts.TotalRows != review.TotalRows ||
            counts.Imported != review.ImportedCount ||
            counts.Suppressed != review.SuppressedCount ||
            counts.Skipped != review.SkippedCount ||
            counts.PossibleDuplicates != review.PossibleDuplicateCount ||
            counts.ExactOverrides != review.ExactOverrideCount)
        {
            throw new ImportStoreException(ImportErrorCodes.VersionConflict);
        }

        var categoryId = await FindSystemCategoryAsync(context, customerId, cancellationToken);
        if (categoryId is null)
        {
            throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
        }

        var tagIds = included.SelectMany(row => row.SelectedTagIds).Distinct().OrderBy(id => id).ToArray();
        if (tagIds.Length > 0 && !await TagsAvailableAsync(context, customerId, tagIds, cancellationToken))
        {
            throw new ImportStoreException(ImportErrorCodes.TagUnavailable);
        }
        _faults.OnStep("tags_validated");

        var selectedCategoryIds = included
            .Where(row => row.SelectedCategoryId is not null)
            .Select(row => row.SelectedCategoryId!.Value)
            .Distinct()
            .Order()
            .ToArray();
        if (selectedCategoryIds.Length > 0 &&
            !await CategoriesSelectableAsync(context, customerId, selectedCategoryIds, cancellationToken))
        {
            throw new ImportStoreException(ImportErrorCodes.CategoryUnavailable);
        }

        await ThrowIfCommitExpiredAsync(context, review.ExpiresAt, cancellationToken);

        var fingerprintMatches = await FingerprintMatchesAsync(context, customerId, accountId.Value, review.StatementFingerprint, cancellationToken);
        if (fingerprintMatches.Count > 0 && !review.StatementOverride)
        {
            throw new StatementRepeatException(fingerprintMatches);
        }
        int fingerprintOccurrence = await NextFingerprintOccurrenceAsync(context, customerId, accountId.Value, review.StatementFingerprint, cancellationToken);

        var statementId = Guid.NewGuid();
        string label = BuildStatementLabel(review);
        string overrideSummary = JsonSerializer.Serialize(new
        {
            exact_row_ids = rows
                .Where(row => row.DuplicateState is "suppressed" or "suppressed_within_file")
                .Where(row => row.Override)
                .Select(row => row.RowId)
                .ToArray(),
            exact_count = counts.ExactOverrides,
            statement_override = review.StatementOverride,
        }, _storageJson);
        var createdAt = DateTime.UtcNow;

        context.StatementImports.Add(new StatementImport
        {
            Id = statementId,
            CustomerId = customerId,
            FinancialAccountId = accountId.Value,
            SourceType = review.SourceKind,
            StatementLabel = label,
            StatementFingerprint = review.StatementFingerprint,
            StartDate = review.StartDate!.Value,
            EndDate = review.EndDate!.Value,
            TotalRows = counts.TotalRows,
            ImportedCount = counts.Imported,
            SuppressedCount = counts.Suppressed,
            SkippedCount = counts.Skipped,
            Version = 1,
            FingerprintOccurrence = fingerprintOccurrence,
            DuplicatePolicyVersion = ImportLimits.DuplicatePolicyVersion,
            PossibleDuplicateCount = counts.PossibleDuplicates,
            ExactOverrideCount = counts.ExactOverrides,
            StatementOverrideCount = review.StatementOverride ? 1 : 0,
            OverrideSummary = JsonDocument.Parse(overrideSummary),
            IsHidden = false,
            HiddenAt = null,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });
        await context.SaveChangesAsync(cancellationToken);
        _faults.OnStep("statement_inserted");

        string[] identityKeys = included.Select(ComputeIdentityKey).Distinct(StringComparer.Ordinal).ToArray();
        var maxOccurrences = await MaxOccurrencesAsync(context, customerId, accountId.Value, identityKeys, cancellationToken);
        var nextOccurrence = maxOccurrences.ToDictionary(pair => pair.Key, pair => pair.Value + 1, StringComparer.Ordinal);
        var transactionTags = new List<(Guid TransactionId, Guid[] TagIds)>(included.Count);
        foreach (ImportReviewRowRecord[] batch in included.Chunk(TransactionInsertBatchSize))
        {
            await InsertTransactionBatchAsync(
                context, batch, nextOccurrence, transactionTags, customerId, accountId.Value, statementId, review.SourceKind, categoryId.Value, createdAt, cancellationToken);
        }
        _faults.OnStep("transactions_inserted");

        var links = transactionTags.SelectMany(pair => pair.TagIds.Select(tagId => (pair.TransactionId, TagId: tagId))).ToArray();
        foreach (var batch in links.Chunk(TagLinkInsertBatchSize))
        {
            await InsertTagLinkBatchAsync(context, batch, customerId, createdAt, cancellationToken);
        }
        _faults.OnStep("tag_links_inserted");

        string resultJson = JsonSerializer.Serialize(new
        {
            statement_import_id = statementId,
            total_rows = counts.TotalRows,
            imported_count = counts.Imported,
            suppressed_count = counts.Suppressed,
            skipped_count = counts.Skipped,
            possible_duplicate_count = counts.PossibleDuplicates,
            exact_override_count = counts.ExactOverrides,
            statement_override_count = review.StatementOverride ? 1 : 0,
        }, _storageJson);
        context.ImportCommitReceipts.Add(new ImportCommitReceipt
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            OperationId = reviewId,
            FinancialAccountId = accountId.Value,
            RequestDigest = requestDigest,
            State = "Committed",
            StatementImportId = statementId,
            Result = JsonDocument.Parse(resultJson),
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });
        await context.SaveChangesAsync(cancellationToken);
        _faults.OnStep("receipt_inserted");

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE financial_accounts
            SET import_version = import_version + 1, updated_at = NOW()
            WHERE customer_id = {customerId} AND id = {accountId.Value}
            """, cancellationToken);

        await context.ImportReviewRows
            .Where(row => row.ReviewId == reviewId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.ImportReviews
            .Where(review => review.CustomerId == customerId && review.Id == reviewId)
            .ExecuteDeleteAsync(cancellationToken);

        var summary = await ReadStatementSummaryAsync(context, customerId, statementId, cancellationToken) ?? throw new ImportStoreException(ImportErrorCodes.NotFound);
        _faults.OnStep("before_commit");
        await ThrowIfCommitExpiredAsync(context, review.ExpiresAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _faults.OnStep("after_commit_before_response");

        return new ImportCommitResult(summary, false);
    }

    private static async Task ThrowIfCommitExpiredAsync(ArgusDbContext context,
                                                        DateTime expiresAt,
                                                        CancellationToken cancellationToken)
    {
        bool expired = await context.Database
            .SqlQuery<bool>($$"""
                SELECT clock_timestamp() >= {{expiresAt}} AS "Value"
                """)
            .SingleAsync(cancellationToken);
        if (expired)
        {
            throw new ImportStoreException(ImportErrorCodes.ReviewExpired);
        }
    }

    private static async Task<ImportCommitResult> ReplayReceiptAsync(ArgusDbContext context,
                                                                     Guid customerId,
                                                                     Guid reviewId,
                                                                     string requestDigest,
                                                                     CancellationToken cancellationToken)
    {
        var receipt = await ReadReceiptAsync(context, customerId, reviewId, true, cancellationToken) ?? throw new ImportStoreException(ImportErrorCodes.NotFound);
        if (receipt.State == "Deleted")
        {
            throw new ImportStoreException(ImportErrorCodes.ImportDeleted);
        }
        if (receipt.RequestDigest != requestDigest)
        {
            throw new ImportStoreException(ImportErrorCodes.IdempotencyConflict);
        }
        var summary = await ReadStatementSummaryAsync(context, customerId, receipt.StatementImportId!.Value, cancellationToken) ?? throw new ImportStoreException(ImportErrorCodes.NotFound);

        return new ImportCommitResult(summary, true);
    }

    public async Task<ImportHistoryPage> ListHistoryAsync(Guid customerId,
                                                          Guid? accountId,
                                                          string? state,
                                                          string? cursor,
                                                          int limit,
                                                          CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        state = state is "visible" or "hidden" ? state : "all";
        DateTime? cursorCreated = null;
        Guid? cursorId = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            (cursorCreated, cursorId) = DecodeCursor(cursor);
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        List<StatementSummaryRow> rows = await context.Database
            .SqlQueryRaw<StatementSummaryRow>(
                HistorySql,
                customerId,
                (object)(accountId ?? (object)DBNull.Value),
                state,
                (object)(cursorCreated ?? (object)DBNull.Value),
                (object)(cursorId ?? (object)DBNull.Value),
                limit + 1)
            .ToListAsync(cancellationToken);
        var items = rows.Select(ToStatementSummary).ToList();
        if (items.Count <= limit)
        {
            return new ImportHistoryPage(items, null);
        }

        items.RemoveAt(items.Count - 1);

        return new ImportHistoryPage(items, EncodeCursor(items[^1].CreatedAt, items[^1].Id));
    }

    public async Task<ImportStatementDetail> GetStatementAsync(Guid customerId,
                                                               Guid statementId,
                                                               int page,
                                                               int pageSize,
                                                               CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, ImportLimits.MaxPageSize);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var summary = await ReadStatementSummaryAsync(context, customerId, statementId, cancellationToken);
        if (summary is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }

        long expectedTransactionCount = await context.Transactions
            .LongCountAsync(
                transaction => transaction.CustomerId == customerId && transaction.StatementImportId == statementId,
                cancellationToken);
        var rows = await ReadStatementRowsAsync(context, customerId, statementId, page, pageSize, cancellationToken);

        return new ImportStatementDetail(summary, rows, page, pageSize, expectedTransactionCount);
    }

    public async Task<ImportStatementSummary> SetStatementVisibilityAsync(Guid customerId,
                                                                          Guid statementId,
                                                                          bool hidden,
                                                                          int expectedVersion,
                                                                          long expectedTransactionCount,
                                                                          bool confirm,
                                                                          CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
        }
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var accountId = await StatementAccountIdAsync(context, customerId, statementId, cancellationToken);
        if (accountId is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        await LockAccountAsync(context, customerId, accountId.Value, cancellationToken);
        var summary = await LockStatementAsync(context, customerId, statementId, cancellationToken);
        if (summary is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        if (summary.Version != expectedVersion)
        {
            throw new ImportStoreException(ImportErrorCodes.VersionConflict);
        }
        long count = await StatementTransactionCountAsync(context, customerId, statementId, cancellationToken);
        if (count != expectedTransactionCount)
        {
            throw new ImportStoreException(ImportErrorCodes.VersionConflict);
        }
        if (summary.IsHidden == hidden)
        {
            await transaction.CommitAsync(cancellationToken);
            return summary;
        }
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE statement_imports
            SET is_hidden = {hidden},
                hidden_at = CASE WHEN {hidden} THEN NOW() ELSE NULL END,
                version = version + 1,
                updated_at = NOW()
            WHERE customer_id = {customerId} AND id = {statementId}
            """, cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE transactions
            SET version = version + 1, updated_at = NOW()
            WHERE customer_id = {customerId} AND statement_import_id = {statementId}
            """, cancellationToken);
        var updated = await ReadStatementSummaryAsync(context, customerId, statementId, cancellationToken) ?? throw new ImportStoreException(ImportErrorCodes.NotFound);
        await transaction.CommitAsync(cancellationToken);

        return updated;
    }

    public async Task<ImportDeleteResult> DeleteStatementAsync(Guid customerId,
                                                               Guid statementId,
                                                               int expectedVersion,
                                                               long expectedTransactionCount,
                                                               bool confirm,
                                                               CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
        }
        string deletionDigest = Digest($"delete-v1|{expectedVersion}|{expectedTransactionCount}|true");

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var accountId = await StatementAccountIdAsync(context, customerId, statementId, cancellationToken);
        if (accountId is not null)
        {
            await LockAccountAsync(context, customerId, accountId.Value, cancellationToken);
        }

        var statement = await LockStatementAsync(context, customerId, statementId, cancellationToken);
        if (statement is null)
        {
            var tombstone = await ReadDeletionReceiptAsync(context, customerId, statementId, cancellationToken);
            if (tombstone is not null && tombstone.DeletionDigest == deletionDigest && tombstone.DeletionExpectedCount == expectedTransactionCount)
            {
                await transaction.CommitAsync(cancellationToken);
                return new ImportDeleteResult(true, expectedTransactionCount);
            }
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        if (statement.Version != expectedVersion)
        {
            throw new ImportStoreException(ImportErrorCodes.VersionConflict);
        }
        long count = await StatementTransactionCountAsync(context, customerId, statementId, cancellationToken);
        if (count != expectedTransactionCount)
        {
            throw new ImportStoreException(ImportErrorCodes.VersionConflict);
        }

        await context.Transactions
            .Where(transaction => transaction.CustomerId == customerId && transaction.StatementImportId == statementId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.StatementImports
            .Where(statement => statement.CustomerId == customerId && statement.Id == statementId)
            .ExecuteDeleteAsync(cancellationToken);

        var receipt = await ReadReceiptByStatementAsync(context, customerId, statementId, cancellationToken);
        string resultJson = JsonSerializer.Serialize(new { statement_import_id = statementId, deleted = true, deleted_transaction_count = count, }, _storageJson);
        if (receipt is null)
        {
            // The legacy fallback predates commit receipts and must keep the
            // database clock, exactly as the pre-conversion INSERT did.
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO import_commit_receipts (
                    id, customer_id, operation_id, financial_account_id, request_digest,
                    state, statement_import_id, result, deletion_digest, deletion_expected_count,
                    created_at, updated_at)
                VALUES (
                    {Guid.NewGuid()}, {customerId}, {statementId}, {statement.FinancialAccountId}, {deletionDigest},
                    'Deleted', {statementId}, CAST({resultJson} AS jsonb), {deletionDigest}, {count},
                    NOW(), NOW())
                """, cancellationToken);
        }
        else
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE import_commit_receipts
                SET state = 'Deleted',
                    result = CAST({resultJson} AS jsonb),
                    deletion_digest = {deletionDigest},
                    deletion_expected_count = {count},
                    updated_at = NOW()
                WHERE id = {receipt.Id}
                """, cancellationToken);
        }
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE financial_accounts
            SET import_version = import_version + 1, updated_at = NOW()
            WHERE customer_id = {customerId} AND id = {statement.FinancialAccountId}
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ImportDeleteResult(true, count);
    }

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Lock the headers before touching their child rows. Commit and
        // review edits acquire the review header first; cleanup must not
        // invert that order. The lock wait also re-evaluates the expiry
        // predicate, so a concurrent commit that purges the review removes
        // it from this set.
        List<Guid> expired = await context.Database.SqlQuery<Guid>($"""
            SELECT id FROM import_reviews
            WHERE status = 'Prepared' AND expires_at <= NOW()
            ORDER BY expires_at
            LIMIT 1000
            FOR UPDATE
            """).ToListAsync(cancellationToken);

        if (expired.Count > 0)
        {
            await context.ImportReviewRows
                .Where(row => expired.Contains(row.ReviewId))
                .ExecuteDeleteAsync(cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE import_reviews
                SET status = 'Expired',
                    parser_config = '{}'::jsonb,
                    start_date = NULL,
                    end_date = NULL,
                    statement_fingerprint = '',
                    summary_hash = '',
                    total_rows = 0,
                    imported_count = 0,
                    suppressed_count = 0,
                    skipped_count = 0,
                    possible_duplicate_count = 0,
                    exact_override_count = 0,
                    statement_override = FALSE,
                    statement_override_count = 0,
                    payload_bytes = 0,
                    version = version + 1,
                    updated_at = NOW()
                WHERE id = ANY({{expired.ToArray()}})
                """, cancellationToken);
        }

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM import_reviews
            WHERE status IN ('Abandoned', 'Expired')
              AND updated_at < NOW() - INTERVAL '30 days'
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    // --- Receipt, statement, and row helpers --------------------------------

    /// <summary>
    /// Resolves whether a commit operation is already owned by the caller
    /// before any category seeding occurs. A prepared review or a retained
    /// receipt both establish ownership; anything else must return the same
    /// 404 as an unknown operation.
    /// </summary>
    private async Task<bool> OwnedOperationExistsAsync(Guid customerId, Guid operationId, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        bool reviewExists = await context.ImportReviews
            .AnyAsync(review => review.CustomerId == customerId && review.Id == operationId, cancellationToken);
        if (reviewExists)
        {
            return true;
        }

        return await context.ImportCommitReceipts
            .AnyAsync(receipt => receipt.CustomerId == customerId && receipt.OperationId == operationId, cancellationToken);
    }

    private static async Task<Guid?> ReceiptAccountIdAsync(ArgusDbContext context,
                                                           Guid customerId,
                                                           Guid operationId,
                                                           CancellationToken cancellationToken)
    {
        return await context.ImportCommitReceipts
            .Where(receipt => receipt.CustomerId == customerId && receipt.OperationId == operationId)
            .Select(receipt => (Guid?)receipt.FinancialAccountId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<ReceiptRecord?> ReadReceiptAsync(ArgusDbContext context,
                                                               Guid customerId,
                                                               Guid operationId,
                                                               bool forUpdate,
                                                               CancellationToken cancellationToken)
    {
        const string receiptSql = """
            SELECT id AS "Id", customer_id AS "CustomerId", operation_id AS "OperationId",
                   financial_account_id AS "FinancialAccountId", request_digest AS "RequestDigest",
                   state AS "State", statement_import_id AS "StatementImportId", result::text AS "ResultJson",
                   deletion_digest AS "DeletionDigest", deletion_expected_count AS "DeletionExpectedCount"
            FROM import_commit_receipts
            WHERE customer_id = {0} AND operation_id = {1}
            """;
        string sql = forUpdate ? string.Concat(receiptSql, "\nFOR UPDATE") : receiptSql;
        List<ReceiptRow> rows = await context.Database
            .SqlQueryRaw<ReceiptRow>(sql, customerId, operationId)
            .ToListAsync(cancellationToken);

        return rows.SingleOrDefault() is { } row ? ToReceiptRecord(row) : null;
    }

    private static async Task<ReceiptRecord?> ReadDeletionReceiptAsync(ArgusDbContext context,
                                                                       Guid customerId,
                                                                       Guid statementId,
                                                                       CancellationToken cancellationToken)
    {
        List<ReceiptRow> rows = await context.Database.SqlQueryRaw<ReceiptRow>("""
            SELECT id AS "Id", customer_id AS "CustomerId", operation_id AS "OperationId",
                   financial_account_id AS "FinancialAccountId", request_digest AS "RequestDigest",
                   state AS "State", statement_import_id AS "StatementImportId", result::text AS "ResultJson",
                   deletion_digest AS "DeletionDigest", deletion_expected_count AS "DeletionExpectedCount"
            FROM import_commit_receipts
            WHERE customer_id = {0} AND statement_import_id = {1}
              AND state = 'Deleted'
            """, customerId, statementId).ToListAsync(cancellationToken);

        return rows.SingleOrDefault() is { } row ? ToReceiptRecord(row) : null;
    }

    private static async Task<ReceiptRecord?> ReadReceiptByStatementAsync(ArgusDbContext context,
                                                                          Guid customerId,
                                                                          Guid statementId,
                                                                          CancellationToken cancellationToken)
    {
        List<ReceiptRow> rows = await context.Database.SqlQueryRaw<ReceiptRow>("""
            SELECT id AS "Id", customer_id AS "CustomerId", operation_id AS "OperationId",
                   financial_account_id AS "FinancialAccountId", request_digest AS "RequestDigest",
                   state AS "State", statement_import_id AS "StatementImportId", result::text AS "ResultJson",
                   deletion_digest AS "DeletionDigest", deletion_expected_count AS "DeletionExpectedCount"
            FROM import_commit_receipts
            WHERE customer_id = {0} AND statement_import_id = {1}
            ORDER BY created_at DESC
            LIMIT 1
            """, customerId, statementId).ToListAsync(cancellationToken);

        return rows.SingleOrDefault() is { } row ? ToReceiptRecord(row) : null;
    }

    private static ReceiptRecord ToReceiptRecord(ReceiptRow row) => new(
        row.Id,
        row.CustomerId,
        row.OperationId,
        row.FinancialAccountId,
        row.RequestDigest,
        row.State,
        row.StatementImportId,
        row.ResultJson,
        row.DeletionDigest,
        row.DeletionExpectedCount);

    private static async Task<Guid?> StatementAccountIdAsync(ArgusDbContext context,
                                                             Guid customerId,
                                                             Guid statementId,
                                                             CancellationToken cancellationToken)
    {
        return await context.StatementImports
            .Where(statement => statement.CustomerId == customerId && statement.Id == statementId)
            .Select(statement => (Guid?)statement.FinancialAccountId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<ImportStatementSummary?> LockStatementAsync(ArgusDbContext context,
                                                                          Guid customerId,
                                                                          Guid statementId,
                                                                          CancellationToken cancellationToken)
    {
        List<StatementSummaryRow> rows = await context.Database
            .SqlQueryRaw<StatementSummaryRow>(StatementSummarySql + "\nFOR UPDATE", customerId, statementId)
            .ToListAsync(cancellationToken);

        return rows.SingleOrDefault() is { } row ? ToStatementSummary(row) : null;
    }

    private static async Task<long> StatementTransactionCountAsync(ArgusDbContext context,
                                                                   Guid customerId,
                                                                   Guid statementId,
                                                                   CancellationToken cancellationToken)
    {
        return await context.Transactions
            .LongCountAsync(
                transaction => transaction.CustomerId == customerId && transaction.StatementImportId == statementId,
                cancellationToken);
    }

    private static async Task<ImportStatementSummary?> ReadStatementSummaryAsync(ArgusDbContext context,
                                                                                 Guid customerId,
                                                                                 Guid statementId,
                                                                                 CancellationToken cancellationToken)
    {
        List<StatementSummaryRow> rows = await context.Database
            .SqlQueryRaw<StatementSummaryRow>(StatementSummarySql, customerId, statementId)
            .ToListAsync(cancellationToken);

        return rows.SingleOrDefault() is { } row ? ToStatementSummary(row) : null;
    }

    private static ImportStatementSummary ToStatementSummary(StatementSummaryRow row) => new(
        row.Id,
        row.FinancialAccountId,
        row.SourceType,
        row.StatementLabel,
        row.StartDate,
        row.EndDate,
        row.TotalRows,
        row.ImportedCount,
        row.SuppressedCount,
        row.SkippedCount,
        row.PossibleDuplicateCount,
        row.ExactOverrideCount,
        row.StatementOverrideCount,
        row.IsHidden,
        row.Version,
        row.LiveHiddenCount,
        row.CreatedAt,
        row.UpdatedAt);

    private static async Task<IReadOnlyList<ImportStatementRowRecord>> ReadStatementRowsAsync(ArgusDbContext context,
                                                                                              Guid customerId,
                                                                                              Guid statementId,
                                                                                              int page,
                                                                                              int pageSize,
                                                                                              CancellationToken cancellationToken)
    {
        var rows = await (
            from transaction in context.Transactions.AsNoTracking()
            join statement in context.StatementImports
                on new { transaction.CustomerId, Id = transaction.StatementImportId } equals new { statement.CustomerId, Id = statement.Id }
            where transaction.CustomerId == customerId && transaction.StatementImportId == statementId
            orderby transaction.SourceRowNumber, transaction.Id
            select new
            {
                transaction.Id,
                transaction.SourceRowNumber,
                transaction.SourcePage,
                transaction.TransactionDate,
                transaction.Description,
                transaction.Amount,
                transaction.IsHiddenByUser,
                StatementHidden = statement.IsHidden,
            })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        Guid[] ids = rows.Select(row => row.Id).ToArray();
        var tagRows = await (
            from link in context.TransactionMerchantTags.AsNoTracking()
            join tag in context.MerchantTags
                on new { link.CustomerId, Id = link.MerchantTagId } equals new { tag.CustomerId, Id = tag.Id }
            where link.CustomerId == customerId && ids.Contains(link.TransactionId)
            orderby tag.DisplayName, tag.Id
            select new { link.TransactionId, tag.Id, tag.DisplayName })
            .ToListAsync(cancellationToken);
        var tags = new Dictionary<Guid, List<ImportTagRecord>>();
        foreach (var tagRow in tagRows)
        {
            if (!tags.TryGetValue(tagRow.TransactionId, out List<ImportTagRecord>? list))
            {
                list = [];
                tags[tagRow.TransactionId] = list;
            }
            list.Add(new ImportTagRecord(tagRow.Id, tagRow.DisplayName));
        }

        return rows
            .Select(row => new ImportStatementRowRecord(
                row.Id,
                row.SourceRowNumber,
                row.SourcePage,
                row.TransactionDate,
                row.Description,
                (long)(row.Amount * 100m),
                row.IsHiddenByUser,
                row.StatementHidden || row.IsHiddenByUser,
                tags.GetValueOrDefault(row.Id) ?? []))
            .ToArray();
    }

    private static async Task<IReadOnlyList<(Guid Id, string[] Tokens)>> LoadActiveTagsAsync(ArgusDbContext context,
                                                                                             Guid customerId,
                                                                                             CancellationToken cancellationToken)
    {
        var rows = await context.MerchantTags
            .AsNoTracking()
            .Where(tag => tag.CustomerId == customerId && !tag.IsArchived)
            .OrderBy(tag => tag.NormalizedName)
            .ThenBy(tag => tag.Id)
            .Select(tag => new { tag.Id, tag.NormalizedName })
            .ToListAsync(cancellationToken);

        return rows
            .Select(tag => (tag.Id, tag.NormalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .ToList();
    }

    private static async Task<Guid?> FindSystemCategoryAsync(ArgusDbContext context,
                                                             Guid customerId,
                                                             CancellationToken cancellationToken)
    {
        return await context.Categories
            .Where(category => category.CustomerId == customerId && category.IsSystem)
            .Select(category => (Guid?)category.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<bool> TagsAvailableAsync(ArgusDbContext context,
                                                       Guid customerId,
                                                       Guid[] tagIds,
                                                       CancellationToken cancellationToken)
    {
        List<Guid> available = await context.Database.SqlQuery<Guid>($"""
            SELECT id FROM merchant_tags
            WHERE customer_id = {customerId} AND id = ANY({tagIds}) AND is_archived = FALSE
            ORDER BY id
            FOR UPDATE
            """).ToListAsync(cancellationToken);

        return available.Count == tagIds.Length;
    }

    private static async Task<bool> CategorySelectableAsync(ArgusDbContext context,
                                                            Guid customerId,
                                                            Guid categoryId,
                                                            CancellationToken cancellationToken)
    {
        return await context.Categories
            .AnyAsync(
                category => category.CustomerId == customerId &&
                            category.Id == categoryId &&
                            !category.IsArchived &&
                            !category.IsSystem,
                cancellationToken);
    }

    private static async Task<bool> CategoriesSelectableAsync(ArgusDbContext context,
                                                              Guid customerId,
                                                              Guid[] categoryIds,
                                                              CancellationToken cancellationToken)
    {
        List<Guid> available = await context.Database.SqlQuery<Guid>($"""
            SELECT id FROM categories
            WHERE customer_id = {customerId} AND id = ANY({categoryIds})
              AND is_archived = FALSE AND is_system = FALSE
            ORDER BY id
            FOR SHARE
            """).ToListAsync(cancellationToken);

        return available.Count == categoryIds.Length;
    }

    private static async Task<IReadOnlyList<Guid>> FingerprintMatchesAsync(ArgusDbContext context,
                                                                           Guid customerId,
                                                                           Guid accountId,
                                                                           string fingerprint,
                                                                           CancellationToken cancellationToken)
    {
        return await context.StatementImports
            .AsNoTracking()
            .Where(statement => statement.CustomerId == customerId &&
                                statement.FinancialAccountId == accountId &&
                                statement.StatementFingerprint == fingerprint)
            .OrderBy(statement => statement.CreatedAt)
            .ThenBy(statement => statement.Id)
            .Select(statement => statement.Id)
            .ToListAsync(cancellationToken);
    }

    private static async Task<int> NextFingerprintOccurrenceAsync(ArgusDbContext context,
                                                                  Guid customerId,
                                                                  Guid accountId,
                                                                  string fingerprint,
                                                                  CancellationToken cancellationToken)
    {
        int? max = await context.StatementImports
            .Where(statement => statement.CustomerId == customerId &&
                                statement.FinancialAccountId == accountId &&
                                statement.StatementFingerprint == fingerprint)
            .MaxAsync(statement => (int?)statement.FingerprintOccurrence, cancellationToken);

        return (max ?? 0) + 1;
    }

    private static async Task<Dictionary<string, int>> MaxOccurrencesAsync(ArgusDbContext context,
                                                                           Guid customerId,
                                                                           Guid accountId,
                                                                           string[] identityKeys,
                                                                           CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (identityKeys.Length == 0)
        {
            return result;
        }

        var rows = await context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.CustomerId == customerId &&
                                  transaction.FinancialAccountId == accountId &&
                                  identityKeys.Contains(transaction.IdentityKey))
            .GroupBy(transaction => transaction.IdentityKey)
            .Select(group => new { IdentityKey = group.Key, MaxIndex = group.Max(transaction => transaction.OccurrenceIndex) })
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            result[row.IdentityKey] = row.MaxIndex;
        }

        return result;
    }

    private static string ComputeIdentityKey(ImportReviewRowRecord row) => StatementIdentity.ComputeIdentityKey(row.TransactionDate!.Value, row.AmountCents!.Value, row.NormalizedDescription);

    private static string BuildStatementLabel(ImportReviewSummaryRecord review) => $"Statement {review.StartDate!.Value:yyyy-MM-dd} to {review.EndDate!.Value:yyyy-MM-dd}";

    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string EncodeCursor(DateTime createdAt, Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdAt:O}|{id}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (DateTime? CreatedAt, Guid? Id) DecodeCursor(string cursor)
    {
        try
        {
            string padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            string text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            int separator = text.IndexOf('|');
            if (separator <= 0 || !DateTime.TryParse(text[..separator], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt) || !Guid.TryParse(text[(separator + 1)..], out var id))
            {
                return (null, null);
            }

            return (createdAt, id);
        }
        catch
        {
            return (null, null);
        }
    }

    private sealed record RowCounts(int TotalRows,
                                    int Imported,
                                    int Suppressed,
                                    int Skipped,
                                    int PossibleDuplicates,
                                    int ExactOverrides);

    private sealed record DuplicateSnapshot(IReadOnlyList<ImportReviewRowRecord> Rows);

    private sealed record RowDecision(bool Skip, bool Override);

    private sealed record IncomingRow(int Index,
                                      Guid RowId,
                                      int SourceRowNumber,
                                      DateOnly Date,
                                      long Cents,
                                      string NormalizedDescription,
                                      string Description,
                                      string IdentityKey,
                                      string? Namespace,
                                      string? Value);

    private sealed record ExistingRow(Guid Id,
                                      string IdentityKey,
                                      int OccurrenceIndex,
                                      DateOnly Date,
                                      long Cents,
                                      string NormalizedDescription,
                                      string Description,
                                      string? Namespace,
                                      string? Value,
                                      Guid StatementImportId);

    private sealed record Candidate(string CandidateId,
                                    string Source,
                                    string? ImportId,
                                    DateOnly Date,
                                    long Cents,
                                    string Description,
                                    double Similarity,
                                    string[] Reasons);

    private sealed class MutableSnapshot(ParsedStatementRow row)
    {
        public ParsedStatementRow Row { get; } = row;
        public string DuplicateState { get; set; } = "none";
        public List<string> Reasons { get; } = [];
        public List<Guid> MatchingTransactionIds { get; } = [];
        public List<Guid> MatchingImportIds { get; } = [];
        public List<Candidate> NearCandidates { get; } = [];
        public List<Guid> SuggestedTagIds { get; } = [];
        public Guid? SuggestedCategoryId { get; set; }
        public string? WithinFileGroup { get; set; }
        public string WithinFileState { get; set; } = "none";
    }

    private sealed record ReceiptRecord(Guid Id,
                                        Guid CustomerId,
                                        Guid OperationId,
                                        Guid FinancialAccountId,
                                        string RequestDigest,
                                        string State,
                                        Guid? StatementImportId,
                                        string ResultJson,
                                        string? DeletionDigest,
                                        long? DeletionExpectedCount);

    private sealed class AccountLockRow
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public bool IsArchived { get; set; }
        public long ImportVersion { get; set; }
    }

    private sealed class QuotaUsageRow
    {
        public long OpenReviews { get; set; }
        public long UsedPayload { get; set; }
    }

    private sealed class ReviewHeaderRow
    {
        public Guid Id { get; set; }
        public Guid FinancialAccountId { get; set; }
        public string SourceKind { get; set; } = string.Empty;
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public int Version { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public long AccountImportVersion { get; set; }
        public string StatementFingerprint { get; set; } = string.Empty;
        public string SummaryHash { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int ImportedCount { get; set; }
        public int SuppressedCount { get; set; }
        public int SkippedCount { get; set; }
        public int InvalidUnskippedCount { get; set; }
        public int WithinFilePendingCount { get; set; }
        public int PossibleDuplicateCount { get; set; }
        public int ExactOverrideCount { get; set; }
        public int CategorisedCount { get; set; }
        public bool StatementOverride { get; set; }
        public int StatementOverrideCount { get; set; }
        public long PayloadBytes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class ReviewRowText
    {
        public Guid RowId { get; set; }
        public int SourceLine { get; set; }
        public int? SourcePage { get; set; }
        public int SourceRowNumber { get; set; }
        public string RawValues { get; set; } = "{}";
        public DateOnly? TransactionDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string NormalizedDescription { get; set; } = string.Empty;
        public long? AmountCents { get; set; }
        public string ValidationCodes { get; set; } = "[]";
        public string Warnings { get; set; } = "[]";
        public string? SourceIdNamespace { get; set; }
        public string? SourceIdValue { get; set; }
        public string DuplicateState { get; set; } = string.Empty;
        public string DuplicateReasons { get; set; } = "[]";
        public string MatchingTransactionIds { get; set; } = "[]";
        public string MatchingImportIds { get; set; } = "[]";
        public string NearCandidates { get; set; } = "[]";
        public int NearCandidateTotal { get; set; }
        public string? WithinFileGroup { get; set; }
        public string WithinFileState { get; set; } = string.Empty;
        public bool Skip { get; set; }
        public bool Override { get; set; }
        public string SuggestedTagIds { get; set; } = "[]";
        public string SelectedTagIds { get; set; } = "[]";
        public Guid? SuggestedCategoryId { get; set; }
        public Guid? SelectedCategoryId { get; set; }
        public bool CategoryDecided { get; set; }
    }

    private sealed class ReceiptRow
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public Guid OperationId { get; set; }
        public Guid FinancialAccountId { get; set; }
        public string RequestDigest { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public Guid? StatementImportId { get; set; }
        public string ResultJson { get; set; } = "{}";
        public string? DeletionDigest { get; set; }
        public long? DeletionExpectedCount { get; set; }
    }

    private sealed class StatementSummaryRow
    {
        public Guid Id { get; set; }
        public Guid FinancialAccountId { get; set; }
        public string SourceType { get; set; } = string.Empty;
        public string StatementLabel { get; set; } = string.Empty;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public int TotalRows { get; set; }
        public int ImportedCount { get; set; }
        public int SuppressedCount { get; set; }
        public int SkippedCount { get; set; }
        public int PossibleDuplicateCount { get; set; }
        public int ExactOverrideCount { get; set; }
        public int StatementOverrideCount { get; set; }
        public bool IsHidden { get; set; }
        public int Version { get; set; }
        public long LiveHiddenCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
