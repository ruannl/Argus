using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using argus.Server.Imports;
using Npgsql;
using NpgsqlTypes;

namespace argus.Server.Data;

public sealed record ImportAccountRecord(
    Guid Id,
    string DisplayName,
    string Currency,
    bool IsArchived,
    long ImportVersion);

public sealed record ImportNearCandidateRecord(
    string CandidateId,
    string Source,
    string? ImportId,
    string TransactionDate,
    string Amount,
    string Description,
    double Similarity,
    string[] Reasons);

public sealed record ImportReviewRowRecord(
    Guid RowId,
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
    Guid[] SelectedTagIds);

public sealed record ImportReviewSummaryRecord(
    Guid Id,
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
    bool StatementOverride,
    int StatementOverrideCount,
    long PayloadBytes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ImportReviewPage(
    IReadOnlyList<ImportReviewRowRecord> Rows,
    int Total,
    int Page,
    int PageSize);

public sealed record ImportReviewDetail(
    ImportReviewSummaryRecord Summary,
    ImportReviewPage Page);

public sealed record ImportOpenReviewSummary(
    Guid Id,
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

public sealed record ImportStatementSummary(
    Guid Id,
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

public sealed record ImportStatementRowRecord(
    Guid Id,
    int SourceRowNumber,
    int? SourcePage,
    DateOnly TransactionDate,
    string Description,
    long AmountCents,
    bool IsHiddenByUser,
    bool EffectiveHidden,
    IReadOnlyList<ImportTagRecord> Tags);

public sealed record ImportStatementDetail(
    ImportStatementSummary Summary,
    IReadOnlyList<ImportStatementRowRecord> Rows,
    int Page,
    int PageSize,
    long ExpectedTransactionCount);

public sealed record ImportHistoryPage(
    IReadOnlyList<ImportStatementSummary> Items,
    string? NextCursor);

public sealed record ImportDeleteResult(bool Deleted, long DeletedTransactionCount);

public sealed record ImportCommitResult(ImportStatementSummary Summary, bool Replayed);

public sealed record ImportAbandonResult(bool Committed, ImportCommitResult? Commit);

public sealed record ImportDecision(
    Guid RowId,
    bool? Skip,
    bool? Override,
    string? WithinFileState,
    string? WithinFileGroup,
    string? WithinFileAction,
    Guid[]? SelectedTagIds);

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
    private static readonly JsonSerializerOptions StorageJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Db _db;
    private readonly FinanceStore _financeStore;
    private readonly IImportFaultInjector _faults;

    public StatementImportStore(
        Db db,
        FinanceStore financeStore,
        IImportFaultInjector faults)
    {
        _db = db;
        _financeStore = financeStore;
        _faults = faults;
    }

    public async Task<ImportAccountRecord?> GetAccountAsync(
        Guid customerId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenAsync(cancellationToken);
        return await GetAccountAsync(connection, null, customerId, accountId, false, cancellationToken);
    }

    public async Task<ImportReviewDetail> CreateReviewAsync(
        Guid customerId,
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

        var estimatedPayloadBytes = EstimatePayload(parsed, config);
        var reviewId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var expiresAt = now.AddHours(ImportLimits.ReviewLifetimeHours);

        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await LockQuotaAsync(connection, transaction, customerId, cancellationToken);
        var usedPayloadBytes = await EnforceQuotaAsync(
            connection, transaction, customerId, estimatedPayloadBytes, cancellationToken);

        var account = await GetAccountAsync(
            connection, transaction, customerId, accountId, true, cancellationToken);
        if (account is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        if (account.IsArchived)
        {
            throw new ImportStoreException(ImportErrorCodes.AccountArchived);
        }
        if (!string.Equals(account.Currency, "ZAR", StringComparison.Ordinal))
        {
            throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
        }

        var snapshot = await ComputeDuplicateSnapshotAsync(
            connection, transaction, customerId, accountId, parsed.Rows, null, cancellationToken);
        var rows = snapshot.Rows;
        var payloadBytes = EstimatePayload(parsed, config, rows);
        if (usedPayloadBytes + payloadBytes > ImportLimits.MaxOpenReviewPayloadBytes)
        {
            throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
        }
        var counts = CountRows(rows);
        var summaryHash = ComputeSummaryHash(reviewId, 1, rows, counts, statementOverride: false);
        var statementFingerprint = StatementIdentity.ComputeStatementFingerprint(
            parsed.Rows, startDate, endDate);

        await InsertReviewAsync(
            connection,
            transaction,
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
            false,
            0,
            payloadBytes,
            now,
            now);
        var page = new ImportReviewPage(rows.Take(ImportLimits.MaxPageSize).ToArray(),
            rows.Count, 1, ImportLimits.MaxPageSize);
        return new ImportReviewDetail(summary, page);
    }

    public async Task<IReadOnlyList<ImportOpenReviewSummary>> ListOpenReviewsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, financial_account_id, source_kind, start_date, end_date, version,
                   expires_at, total_rows, imported_count, suppressed_count, skipped_count, created_at
            FROM import_reviews
            WHERE customer_id = @customer_id AND status = 'Prepared' AND expires_at > NOW()
            ORDER BY created_at DESC, id DESC
            LIMIT 50
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        var items = new List<ImportOpenReviewSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ImportOpenReviewSummary(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                reader.GetInt32(5),
                reader.GetFieldValue<DateTime>(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetFieldValue<DateTime>(11)));
        }
        return items;
    }

    public async Task<ImportReviewDetail> GetReviewAsync(
        Guid customerId,
        Guid reviewId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, ImportLimits.MaxPageSize);

        await using var connection = await _db.OpenAsync(cancellationToken);
        var summary = await ReadReviewHeaderAsync(connection, null, customerId, reviewId, false, cancellationToken);
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

        var rows = await ReadReviewRowsAsync(connection, null, reviewId, page, pageSize, cancellationToken);
        var total = await CountReviewRowsAsync(connection, null, reviewId, cancellationToken);
        return new ImportReviewDetail(summary, new ImportReviewPage(rows, total, page, pageSize));
    }

    public async Task<ImportReviewSummaryRecord> PatchReviewAsync(
        Guid customerId,
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

        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var accountId = await ReviewAccountIdAsync(connection, transaction, customerId, reviewId, cancellationToken);
        if (accountId is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        await LockQuotaAsync(connection, transaction, customerId, cancellationToken);
        await LockAccountAsync(connection, transaction, customerId, accountId.Value, cancellationToken);
        var summary = await ReadReviewHeaderAsync(
            connection, transaction, customerId, reviewId, true, cancellationToken);
        if (summary is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        EnsureEditable(summary, expectedVersion);

        var rows = (await ReadReviewRowsAsync(
            connection, transaction, reviewId, 1, int.MaxValue, cancellationToken)).ToList();
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
                    decision.WithinFileAction is not ("keep_all" or "skip_all"))
                {
                    throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
                }
                var state = decision.WithinFileAction == "keep_all" ? "keep" : "skip";
                var skip = decision.WithinFileAction == "skip_all";
                var groupRows = byId.Values
                    .Where(row => row.WithinFileGroup == decision.WithinFileGroup)
                    .ToList();
                if (groupRows.Count == 0)
                {
                    throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
                }
                await SetWithinFileGroupAsync(
                    connection, transaction, reviewId, decision.WithinFileGroup, state, skip, cancellationToken);
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

            var skipValue = decision.Skip ?? row.Skip;
            var overrideValue = decision.Override ?? row.Override;
            if (skipValue && overrideValue)
            {
                throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
            }
            if (overrideValue && row.DuplicateState is not ("suppressed" or "suppressed_within_file"))
            {
                throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
            }

            var withinFileState = row.WithinFileState;
            if (decision.WithinFileState is not null)
            {
                if (row.WithinFileGroup is null ||
                    decision.WithinFileState is not ("keep" or "skip"))
                {
                    throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
                }
                withinFileState = decision.WithinFileState;
                skipValue = decision.WithinFileState == "skip";
            }

            var selectedTagIds = decision.SelectedTagIds is null
                ? row.SelectedTagIds
                : decision.SelectedTagIds.Distinct().ToArray();
            if (selectedTagIds.Length > 100)
            {
                throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
            }
            var newlySelected = selectedTagIds.Except(row.SelectedTagIds).ToArray();
            if (newlySelected.Length > 0 &&
                !await TagsAvailableAsync(
                    connection, transaction, customerId, newlySelected, cancellationToken))
            {
                throw new ImportStoreException(ImportErrorCodes.TagUnavailable);
            }

            await UpdateRowDecisionsAsync(
                connection,
                transaction,
                row.RowId,
                skipValue,
                overrideValue,
                withinFileState,
                selectedTagIds,
                cancellationToken);

            byId[row.RowId] = row with
            {
                Skip = skipValue,
                Override = overrideValue,
                WithinFileState = withinFileState,
                SelectedTagIds = selectedTagIds,
            };
        }

        var decidedRows = byId.Values.OrderBy(row => row.SourceRowNumber).ThenBy(row => row.RowId).ToArray();
        var decisionMap = decidedRows.ToDictionary(
            row => row.RowId, row => new RowDecision(row.Skip, row.Override));
        var snapshot = await ComputeDuplicateSnapshotAsync(
            connection,
            transaction,
            customerId,
            accountId.Value,
            decidedRows.Select(ToParsedRow).ToArray(),
            decisionMap,
            cancellationToken);
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
                };
            }
            else
            {
                var withinState = fresh.WithinFileGroup is null
                    ? "none"
                    : oldRow.WithinFileGroup == fresh.WithinFileGroup &&
                      oldRow.WithinFileState is "keep" or "skip"
                        ? oldRow.WithinFileState
                        : fresh.WithinFileState;
                merged = fresh with
                {
                    Skip = false,
                    Override = false,
                    SelectedTagIds = oldRow.SelectedTagIds,
                    WithinFileState = withinState,
                };
            }
            updatedRows.Add(merged);
            await UpdateDuplicateSnapshotAsync(
                connection, transaction, merged, cancellationToken);
        }

        var overrideFlag = statementOverride ?? summary.StatementOverride;
        var oldRowsJson = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(rows, StorageJson));
        var newRowsJson = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(updatedRows, StorageJson));
        var payloadBytes = Math.Max(0, summary.PayloadBytes + newRowsJson - oldRowsJson);
        var (openReviews, usedPayload) = await QuotaUsageExcludingAsync(
            connection, transaction, customerId, reviewId, cancellationToken);
        if (openReviews >= ImportLimits.MaxOpenReviewsPerCustomer ||
            usedPayload + payloadBytes > ImportLimits.MaxOpenReviewPayloadBytes)
        {
            throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
        }
        var counts = CountRows(updatedRows);
        var version = summary.Version + 1;
        var hash = ComputeSummaryHash(reviewId, version, updatedRows, counts, overrideFlag);
        await UpdateReviewSummaryAsync(
            connection, transaction, reviewId, version, counts, hash, overrideFlag, payloadBytes, cancellationToken);
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
            StatementOverride = overrideFlag,
            StatementOverrideCount = overrideFlag ? 1 : 0,
            PayloadBytes = payloadBytes,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    public async Task<ImportReviewSummaryRecord> RefreshReviewAsync(
        Guid customerId,
        Guid reviewId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var accountId = await ReviewAccountIdAsync(connection, transaction, customerId, reviewId, cancellationToken);
        if (accountId is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        await LockQuotaAsync(connection, transaction, customerId, cancellationToken);
        var account = await LockAccountAsync(
            connection, transaction, customerId, accountId.Value, cancellationToken);
        var summary = await ReadReviewHeaderAsync(
            connection, transaction, customerId, reviewId, true, cancellationToken);
        if (summary is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        EnsureEditable(summary, expectedVersion);

        var existingRows = await ReadReviewRowsAsync(
            connection, transaction, reviewId, 1, int.MaxValue, cancellationToken);
        var decisionMap = existingRows.ToDictionary(
            row => row.RowId, row => new RowDecision(row.Skip, row.Override));
        var parsedRows = existingRows.Select(ToParsedRow).ToArray();
        var snapshot = await ComputeDuplicateSnapshotAsync(
            connection, transaction, customerId, accountId.Value, parsedRows, decisionMap, cancellationToken);
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
                };
            }
            else if (oldRow.Override)
            {
                var stillSuppressed = fresh.DuplicateState is "suppressed" or "suppressed_within_file";
                refreshedRow = stillSuppressed
                    ? oldRow
                    : fresh with { Skip = false, Override = false, SelectedTagIds = oldRow.SelectedTagIds };
            }
            else
            {
                var withinState = fresh.WithinFileGroup is null
                    ? "none"
                    : oldRow.WithinFileGroup == fresh.WithinFileGroup &&
                      oldRow.WithinFileState is "keep" or "skip"
                        ? oldRow.WithinFileState
                        : fresh.WithinFileState;
                refreshedRow = fresh with
                {
                    Skip = false,
                    Override = false,
                    SelectedTagIds = oldRow.SelectedTagIds,
                    WithinFileState = withinState,
                };
            }
            refreshed.Add(refreshedRow);
            await UpdateDuplicateSnapshotAsync(
                connection, transaction, refreshedRow, cancellationToken);
        }

        var oldRowsJson = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(existingRows, StorageJson));
        var newRowsJson = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(refreshed, StorageJson));
        var payloadBytes = Math.Max(0, summary.PayloadBytes + newRowsJson - oldRowsJson);
        var (openReviews, usedPayload) = await QuotaUsageExcludingAsync(
            connection, transaction, customerId, reviewId, cancellationToken);
        if (openReviews >= ImportLimits.MaxOpenReviewsPerCustomer ||
            usedPayload + payloadBytes > ImportLimits.MaxOpenReviewPayloadBytes)
        {
            throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
        }
        var counts = CountRows(refreshed);
        var version = summary.Version + 1;
        var hash = ComputeSummaryHash(reviewId, version, refreshed, counts, summary.StatementOverride);
        await UpdateReviewSummaryAsync(
            connection, transaction, reviewId, version, counts, hash, summary.StatementOverride, payloadBytes, cancellationToken);
        await UpdateReviewAccountVersionAsync(
            connection, transaction, reviewId, account.ImportVersion, cancellationToken);
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
            AccountImportVersion = account.ImportVersion,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    public async Task<ImportAbandonResult> AbandonReviewAsync(
        Guid customerId,
        Guid reviewId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // A committed import purges the review header, so resolve a terminal
        // operation from its receipt before looking for the review.
        var receiptAccountId = await ReceiptAccountIdAsync(
            connection, transaction, customerId, reviewId, cancellationToken);
        if (receiptAccountId is not null)
        {
            await LockAccountAsync(
                connection, transaction, customerId, receiptAccountId.Value, cancellationToken);
            var terminal = await ReadReceiptAsync(
                connection, transaction, customerId, reviewId, true, cancellationToken);
            if (terminal is not null)
            {
                if (terminal.State == "Deleted")
                {
                    throw new ImportStoreException(ImportErrorCodes.ImportDeleted);
                }
                var statement = await ReadStatementSummaryAsync(
                    connection, transaction, customerId, terminal.StatementImportId!.Value, cancellationToken);
                if (statement is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return new ImportAbandonResult(true, new ImportCommitResult(statement, true));
                }
            }
        }

        var accountId = await ReviewAccountIdAsync(connection, transaction, customerId, reviewId, cancellationToken);
        if (accountId is null)
        {
            var lateReceiptAccount = await ReceiptAccountIdAsync(
                connection, transaction, customerId, reviewId, cancellationToken);
            if (lateReceiptAccount is not null)
            {
                await LockAccountAsync(
                    connection, transaction, customerId, lateReceiptAccount.Value, cancellationToken);
                var lateReceipt = await ReadReceiptAsync(
                    connection, transaction, customerId, reviewId, true, cancellationToken);
                if (lateReceipt is not null)
                {
                    if (lateReceipt.State == "Deleted")
                    {
                        throw new ImportStoreException(ImportErrorCodes.ImportDeleted);
                    }
                    var lateStatement = await ReadStatementSummaryAsync(
                        connection, transaction, customerId,
                        lateReceipt.StatementImportId!.Value, cancellationToken);
                    if (lateStatement is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        return new ImportAbandonResult(
                            true, new ImportCommitResult(lateStatement, true));
                    }
                }
            }
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        await LockAccountAsync(connection, transaction, customerId, accountId.Value, cancellationToken);

        var racedReceipt = await ReadReceiptAsync(
            connection, transaction, customerId, reviewId, false, cancellationToken);
        if (racedReceipt is not null)
        {
            if (racedReceipt.State == "Deleted")
            {
                throw new ImportStoreException(ImportErrorCodes.ImportDeleted);
            }
            var statement = await ReadStatementSummaryAsync(
                connection, transaction, customerId, racedReceipt.StatementImportId!.Value, cancellationToken);
            if (statement is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new ImportAbandonResult(true, new ImportCommitResult(statement, true));
            }
        }

        var summary = await ReadReviewHeaderAsync(
            connection, transaction, customerId, reviewId, true, cancellationToken);
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

        await using (var deleteRows = connection.CreateCommand())
        {
            deleteRows.Transaction = transaction;
            deleteRows.CommandText = "DELETE FROM import_review_rows WHERE review_id = @review_id";
            deleteRows.Parameters.AddWithValue("review_id", reviewId);
            await deleteRows.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
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
                WHERE customer_id = @customer_id AND id = @id
                """;
            update.Parameters.AddWithValue("customer_id", customerId);
            update.Parameters.AddWithValue("id", reviewId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ImportAbandonResult(false, null);
    }

    // --- Duplicate snapshot ------------------------------------------------

    private async Task<DuplicateSnapshot> ComputeDuplicateSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid accountId,
        IReadOnlyList<ParsedStatementRow> parsedRows,
        IReadOnlyDictionary<Guid, RowDecision>? decisions,
        CancellationToken cancellationToken)
    {
        var incoming = parsedRows
            .Select((row, index) => ToIncoming(row, index))
            .Where(row => row is not null)
            .Select(row => row!)
            .ToList();

        var identityKeys = incoming.Select(row => row.IdentityKey).Distinct(StringComparer.Ordinal).ToArray();
        var namespaces = incoming.Where(row => row.Namespace is not null)
            .Select(row => row.Namespace!).Distinct(StringComparer.Ordinal).ToArray();
        var ids = incoming.Where(row => row.Value is not null)
            .Select(row => row.Value!).Distinct(StringComparer.Ordinal).ToArray();
        var amounts = incoming.Select(row => row.Cents / 100m).Distinct().ToArray();
        var minDate = incoming.Count == 0 ? DateOnly.MinValue : incoming.Min(row => row.Date).AddDays(-3);
        var maxDate = incoming.Count == 0 ? DateOnly.MinValue : incoming.Max(row => row.Date).AddDays(3);

        var existing = new Dictionary<Guid, ExistingRow>();
        if (identityKeys.Length > 0)
        {
            foreach (var row in await QueryExistingAsync(
                         connection, transaction, customerId, accountId,
                         "t.identity_key = ANY(@keys)",
                         command => command.Parameters.AddWithValue(
                             "keys", NpgsqlDbType.Array | NpgsqlDbType.Text, identityKeys),
                         cancellationToken))
            {
                existing[row.Id] = row;
            }
        }
        if (namespaces.Length > 0 && ids.Length > 0)
        {
            foreach (var row in await QueryExistingAsync(
                         connection, transaction, customerId, accountId,
                         "t.source_id_namespace = ANY(@namespaces) AND t.source_id_value = ANY(@values)",
                         command =>
                         {
                             command.Parameters.AddWithValue(
                                 "namespaces", NpgsqlDbType.Array | NpgsqlDbType.Text, namespaces);
                             command.Parameters.AddWithValue(
                                 "values", NpgsqlDbType.Array | NpgsqlDbType.Text, ids);
                         },
                         cancellationToken))
            {
                existing[row.Id] = row;
            }
        }
        if (amounts.Length > 0)
        {
            foreach (var row in await QueryExistingAsync(
                         connection, transaction, customerId, accountId,
                         "t.amount = ANY(@amounts) AND t.transaction_date BETWEEN @min_date AND @max_date",
                         command =>
                         {
                             command.Parameters.AddWithValue(
                                 "amounts", NpgsqlDbType.Array | NpgsqlDbType.Numeric, amounts);
                             command.Parameters.AddWithValue("min_date", minDate);
                             command.Parameters.AddWithValue("max_date", maxDate);
                         },
                         cancellationToken))
            {
                existing[row.Id] = row;
            }
        }

        var existingList = existing.Values.ToList();
        foreach (var group in existingList.GroupBy(row => row.IdentityKey, StringComparer.Ordinal))
        {
            var first = group.First();
            if (group.Any(row => row.Date != first.Date || row.Cents != first.Cents ||
                                 row.NormalizedDescription != first.NormalizedDescription))
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
        void CheckCanonical(string key, DateOnly date, long cents, string description)
        {
            if (canonicalByKey.TryGetValue(key, out var first))
            {
                if (first.Date != date || first.Cents != cents ||
                    !string.Equals(first.Description, description, StringComparison.Ordinal))
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
        var existingByIdentity = existingList
            .GroupBy(row => row.IdentityKey)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var existingByStable = existingList
            .Where(row => row.Namespace is not null && row.Value is not null)
            .GroupBy(row => (row.Namespace!, row.Value!))
            .ToDictionary(group => group.Key, group => group.ToList());
        var existingByAmount = existingList
            .GroupBy(row => row.Cents)
            .ToDictionary(group => group.Key, group => group.ToList());

        var snapshots = parsedRows
            .Select(row => new MutableSnapshot(row))
            .ToArray();
        var comparisons = 0;

        foreach (var group in incoming.GroupBy(row => row.IdentityKey, StringComparer.Ordinal))
        {
            var incomingGroup = group.OrderBy(row => row.SourceRowNumber).ThenBy(row => row.RowId).ToList();
            var firstIncoming = incomingGroup[0];
            if (incomingGroup.Any(row => row.Date != firstIncoming.Date ||
                                         row.Cents != firstIncoming.Cents ||
                                         row.NormalizedDescription != firstIncoming.NormalizedDescription))
            {
                throw new ImportStoreException(ImportErrorCodes.IdentityConflict);
            }
            var groupDecisions = incomingGroup
                .Select(row => decisions?.GetValueOrDefault(row.RowId) ?? new RowDecision(false, false))
                .ToArray();
            var nonSkipped = incomingGroup
                .Where((_, index) => !groupDecisions[index].Skip)
                .ToList();
            var matchable = incomingGroup
                .Where((_, index) => !groupDecisions[index].Skip && !groupDecisions[index].Override)
                .ToList();
            var existingGroup = existingByIdentity.GetValueOrDefault(group.Key) ?? [];
            var withinFile = nonSkipped.Count > 1;
            var pairs = MatchOccurrences(matchable, existingGroup, cancellationToken);
            foreach (var pair in pairs)
            {
                if (pair.Incoming.Date != pair.Existing.Date ||
                    pair.Incoming.Cents != pair.Existing.Cents ||
                    pair.Incoming.NormalizedDescription != pair.Existing.NormalizedDescription)
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
                    snapshot.NearCandidates.Add(new Candidate(
                        match.Id.ToString(),
                        "existing",
                        match.StatementImportId.ToString(),
                        match.Date,
                        match.Cents,
                        match.Description,
                        0,
                        ["stable_id_changed"]));
                }
            }
        }

        // Near-duplicate scan: exact amount and ±3 inclusive days, excluding the
        // exact identity. The comparison cap is a safe processing bound, never a
        // silent policy truncation.
        foreach (var incomingRow in incoming)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (existingByAmount.TryGetValue(incomingRow.Cents, out var sameAmount))
            {
                foreach (var candidate in sameAmount)
                {
                    if (Math.Abs(candidate.Date.DayNumber - incomingRow.Date.DayNumber) > 3 ||
                        candidate.IdentityKey == incomingRow.IdentityKey)
                    {
                        continue;
                    }
                    if (++comparisons > 250_000)
                    {
                        throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
                    }
                    var similarity = Jaccard(incomingRow.NormalizedDescription, candidate.NormalizedDescription);
                    if (similarity >= 0.5)
                    {
                        snapshots[incomingRow.Index].NearCandidates.Add(new Candidate(
                            candidate.Id.ToString(),
                            "existing",
                            candidate.StatementImportId.ToString(),
                            candidate.Date,
                            candidate.Cents,
                            candidate.Description,
                            similarity,
                            ["near_match"]));
                    }
                }
            }
        }

        var incomingByAmount = incoming
            .GroupBy(row => row.Cents)
            .ToDictionary(group => group.Key, group => group.ToList());
        foreach (var first in incoming)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!incomingByAmount.TryGetValue(first.Cents, out var sameAmount))
            {
                continue;
            }
            foreach (var second in sameAmount)
            {
                if (second.Index <= first.Index ||
                    first.IdentityKey == second.IdentityKey ||
                    Math.Abs(first.Date.DayNumber - second.Date.DayNumber) > 3)
                {
                    continue;
                }
                if (++comparisons > 250_000)
                {
                    throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
                }
                var similarity = Jaccard(first.NormalizedDescription, second.NormalizedDescription);
                if (similarity < 0.5)
                {
                    continue;
                }
                snapshots[first.Index].NearCandidates.Add(new Candidate(
                    second.RowId.ToString(), "review", null, second.Date, second.Cents,
                    second.Description, similarity, ["near_match"]));
                snapshots[second.Index].NearCandidates.Add(new Candidate(
                    first.RowId.ToString(), "review", null, first.Date, first.Cents,
                    first.Description, similarity, ["near_match"]));
            }
        }

        foreach (var snapshot in snapshots)
        {
            if (snapshot.NearCandidates.Count > 0)
            {
                snapshot.Reasons.Add("near_match");
            }
        }

        var activeTags = await LoadActiveTagsAsync(
            connection, transaction, customerId, cancellationToken);
        foreach (var snapshot in snapshots)
        {
            foreach (var tagId in SuggestTags(snapshot.Row.NormalizedDescription, activeTags))
            {
                snapshot.SuggestedTagIds.Add(tagId);
            }
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
                []));
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

    private static List<(IncomingRow Incoming, ExistingRow Existing)> MatchOccurrences(
        IReadOnlyList<IncomingRow> incoming,
        IReadOnlyList<ExistingRow> existing,
        CancellationToken cancellationToken)
    {
        if (incoming.Count == 0 || existing.Count == 0)
        {
            return [];
        }
        if (incoming.All(row => row.Namespace is null) &&
            existing.All(row => row.Namespace is null))
        {
            var count = Math.Min(incoming.Count, existing.Count);
            return Enumerable.Range(0, count)
                .Select(index => (incoming[index], existing[index]))
                .ToList();
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
            var adjacency = remainingIncoming
                .Select(row => remainingExisting
                    .Select((candidate, index) => (candidate, index))
                    .Where(pair => eligible(row, pair.candidate))
                    .Select(pair => pair.index)
                    .ToList())
                .ToList();
            var product = adjacency.Sum(list => list.Count);
            if (product > 5_000_000)
            {
                throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
            }
            var matchExisting = Enumerable.Repeat(-1, remainingExisting.Count).ToArray();
            bool TryMatch(int incomingIndex, bool[] seen)
            {
                foreach (var existingIndex in adjacency[incomingIndex])
                {
                    if (seen[existingIndex])
                    {
                        continue;
                    }
                    seen[existingIndex] = true;
                    if (matchExisting[existingIndex] == -1 ||
                        TryMatch(matchExisting[existingIndex], seen))
                    {
                        matchExisting[existingIndex] = incomingIndex;
                        return true;
                    }
                }
                return false;
            }

            for (var index = 0; index < remainingIncoming.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = TryMatch(index, new bool[remainingExisting.Count]);
            }

            var matchedIncoming = new HashSet<int>();
            for (var existingIndex = 0; existingIndex < matchExisting.Length; existingIndex++)
            {
                var incomingIndex = matchExisting[existingIndex];
                if (incomingIndex < 0)
                {
                    continue;
                }
                matched.Add((remainingIncoming[incomingIndex], remainingExisting[existingIndex]));
                matchedIncoming.Add(incomingIndex);
            }
            remainingIncoming = remainingIncoming
                .Where((_, index) => !matchedIncoming.Contains(index))
                .ToList();
            var matchedExisting = matchExisting
                .Select((incomingIndex, index) => (incomingIndex, index))
                .Where(pair => pair.incomingIndex >= 0)
                .Select(pair => pair.index)
                .ToHashSet();
            remainingExisting = remainingExisting
                .Where((_, index) => !matchedExisting.Contains(index))
                .ToList();
        }

        RunStage((row, candidate) =>
            row.Namespace is not null &&
            candidate.Namespace == row.Namespace &&
            candidate.Value == row.Value);
        RunStage((row, candidate) =>
            row.Namespace is not null &&
            (candidate.Namespace is null ||
             candidate.Namespace != row.Namespace ||
             candidate.Value == row.Value));
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
        var intersection = leftTokens.Count(token => rightTokens.Contains(token));
        var union = leftTokens.Count + rightTokens.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    // --- Persistence helpers ------------------------------------------------

    private static async Task<ImportAccountRecord?> GetAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid customerId,
        Guid accountId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT id, display_name, currency, is_archived, import_version
            FROM financial_accounts
            WHERE customer_id = @customer_id AND id = @account_id
            {(forUpdate ? "FOR UPDATE" : string.Empty)}
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("account_id", accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ImportAccountRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetInt64(4))
            : null;
    }

    private static async Task<ImportAccountRecord> LockAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(connection, transaction, customerId, accountId, true, cancellationToken);
        return account ?? throw new ImportStoreException(ImportErrorCodes.NotFound);
    }

    private static async Task LockQuotaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO import_quota_locks (customer_id) VALUES (@customer_id) ON CONFLICT DO NOTHING";
            insert.Parameters.AddWithValue("customer_id", customerId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT 1 FROM import_quota_locks WHERE customer_id = @customer_id FOR UPDATE";
        command.Parameters.AddWithValue("customer_id", customerId);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<long> EnforceQuotaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        long payloadBytes,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(payload_bytes), 0)
            FROM import_reviews
            WHERE customer_id = @customer_id AND status = 'Prepared' AND expires_at > NOW()
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var open = reader.GetInt64(0);
        var used = reader.GetInt64(1);
        if (open >= ImportLimits.MaxOpenReviewsPerCustomer ||
            used + payloadBytes > ImportLimits.MaxOpenReviewPayloadBytes)
        {
            throw new ImportStoreException(ImportErrorCodes.ProcessingLimit);
        }
        return used;
    }

    private static async Task<Guid?> ReviewAccountIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT financial_account_id FROM import_reviews WHERE customer_id = @customer_id AND id = @id";
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", reviewId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
    }

    private static async Task<ImportReviewSummaryRecord?> ReadReviewHeaderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid customerId,
        Guid reviewId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT id, financial_account_id, source_kind, start_date, end_date, version, status,
                   expires_at, account_import_version, statement_fingerprint, summary_hash, total_rows, imported_count,
                   suppressed_count, skipped_count,
                   (SELECT COUNT(*) FROM import_review_rows r
                    WHERE r.review_id = import_reviews.id
                      AND jsonb_array_length(r.validation_codes) > 0 AND r.skip = FALSE) AS invalid_unskipped_count,
                   (SELECT COUNT(*) FROM import_review_rows r
                    WHERE r.review_id = import_reviews.id AND r.within_file_state = 'pending') AS within_file_pending_count,
                   possible_duplicate_count, exact_override_count,
                   statement_override, statement_override_count, payload_bytes, created_at, updated_at
            FROM import_reviews
            WHERE customer_id = @customer_id AND id = @id
            {(forUpdate ? "FOR UPDATE" : string.Empty)}
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", reviewId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new ImportReviewSummaryRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.GetFieldValue<DateTime>(7),
            reader.GetInt64(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.GetInt32(13),
            reader.GetInt32(14),
            reader.GetInt32(15),
            reader.GetInt32(16),
            reader.GetInt32(17),
            reader.GetInt32(18),
            reader.GetBoolean(19),
            reader.GetInt32(20),
            reader.GetInt64(21),
            reader.GetFieldValue<DateTime>(22),
            reader.GetFieldValue<DateTime>(23));
    }

    private static async Task<IReadOnlyList<ImportReviewRowRecord>> ReadReviewRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid reviewId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT row_id, source_line, source_page, source_row_number, raw_values::text,
                   transaction_date, description, normalized_description, amount_cents,
                   validation_codes::text, warnings::text, source_id_namespace, source_id_value,
                   duplicate_state, duplicate_reasons::text, matching_transaction_ids::text,
                   matching_import_ids::text, near_candidates::text, near_candidate_total,
                   within_file_group, within_file_state, skip, override, suggested_tag_ids::text,
                   selected_tag_ids::text
            FROM import_review_rows
            WHERE review_id = @review_id
            ORDER BY source_row_number, row_id
            LIMIT @limit OFFSET @offset
            """;
        command.Parameters.AddWithValue("review_id", reviewId);
        command.Parameters.AddWithValue("limit", pageSize == int.MaxValue ? 100_000 : pageSize);
        command.Parameters.AddWithValue("offset", pageSize == int.MaxValue ? 0 : (page - 1) * pageSize);
        var rows = new List<ImportReviewRowRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ImportReviewRowRecord(
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetInt32(3),
                JsonSerializer.Deserialize<Dictionary<string, string?>>(reader.GetString(4), StorageJson)
                    ?? new Dictionary<string, string?>(),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                JsonSerializer.Deserialize<string[]>(reader.GetString(9), StorageJson) ?? [],
                JsonSerializer.Deserialize<string[]>(reader.GetString(10), StorageJson) ?? [],
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetString(13),
                JsonSerializer.Deserialize<string[]>(reader.GetString(14), StorageJson) ?? [],
                JsonSerializer.Deserialize<Guid[]>(reader.GetString(15), StorageJson) ?? [],
                JsonSerializer.Deserialize<Guid[]>(reader.GetString(16), StorageJson) ?? [],
                JsonSerializer.Deserialize<ImportNearCandidateRecord[]>(reader.GetString(17), StorageJson) ?? [],
                reader.GetInt32(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.GetString(20),
                reader.GetBoolean(21),
                reader.GetBoolean(22),
                JsonSerializer.Deserialize<Guid[]>(reader.GetString(23), StorageJson) ?? [],
                JsonSerializer.Deserialize<Guid[]>(reader.GetString(24), StorageJson) ?? []));
        }
        return rows;
    }

    private static async Task<int> CountReviewRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM import_review_rows WHERE review_id = @review_id";
        command.Parameters.AddWithValue("review_id", reviewId);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task InsertReviewAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
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
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO import_reviews (
                    id, customer_id, financial_account_id, source_kind, parser_config, currency,
                    start_date, end_date, version, status, expires_at, account_import_version,
                    duplicate_policy_version, statement_fingerprint, summary_hash, total_rows, imported_count,
                    suppressed_count, skipped_count, possible_duplicate_count, exact_override_count,
                    statement_override, statement_override_count, payload_bytes, created_at, updated_at
                )
                VALUES (
                    @id, @customer_id, @financial_account_id, @source_kind, @parser_config, 'ZAR',
                    @start_date, @end_date, 1, 'Prepared', @expires_at, @account_import_version,
                    @duplicate_policy_version, @statement_fingerprint, @summary_hash, @total_rows, @imported_count,
                    @suppressed_count, @skipped_count, @possible_duplicate_count, 0,
                    FALSE, 0, @payload_bytes, @now, @now
                )
                """;
            insert.Parameters.AddWithValue("id", reviewId);
            insert.Parameters.AddWithValue("customer_id", customerId);
            insert.Parameters.AddWithValue("financial_account_id", accountId);
            insert.Parameters.AddWithValue("source_kind", sourceKind);
            insert.Parameters.AddWithValue("parser_config", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(config, StorageJson));
            insert.Parameters.AddWithValue("start_date", startDate);
            insert.Parameters.AddWithValue("end_date", endDate);
            insert.Parameters.AddWithValue("expires_at", expiresAt);
            insert.Parameters.AddWithValue("account_import_version", accountImportVersion);
            insert.Parameters.AddWithValue("duplicate_policy_version", ImportLimits.DuplicatePolicyVersion);
            insert.Parameters.AddWithValue("statement_fingerprint", statementFingerprint);
            insert.Parameters.AddWithValue("summary_hash", summaryHash);
            insert.Parameters.AddWithValue("total_rows", counts.TotalRows);
            insert.Parameters.AddWithValue("imported_count", counts.Imported);
            insert.Parameters.AddWithValue("suppressed_count", counts.Suppressed);
            insert.Parameters.AddWithValue("skipped_count", counts.Skipped);
            insert.Parameters.AddWithValue("possible_duplicate_count", counts.PossibleDuplicates);
            insert.Parameters.AddWithValue("payload_bytes", payloadBytes);
            insert.Parameters.AddWithValue("now", now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY import_review_rows (" +
            "row_id, review_id, customer_id, source_line, source_page, source_row_number, raw_values, " +
            "transaction_date, description, normalized_description, amount_cents, source_id_namespace, " +
            "source_id_value, validation_codes, warnings, duplicate_state, duplicate_reasons, " +
            "matching_transaction_ids, matching_import_ids, near_candidates, near_candidate_total, " +
            "within_file_group, within_file_state, skip, override, suggested_tag_ids, selected_tag_ids, " +
            "created_at, updated_at" +
            ") FROM STDIN (FORMAT BINARY)",
            cancellationToken);
        try
        {
            foreach (var row in rows)
            {
                await copy.StartRowAsync(cancellationToken);
                copy.Write(row.RowId, NpgsqlDbType.Uuid);
                copy.Write(reviewId, NpgsqlDbType.Uuid);
                copy.Write(customerId, NpgsqlDbType.Uuid);
                copy.Write(row.SourceLine, NpgsqlDbType.Integer);
                if (row.SourcePage is null)
                {
                    copy.WriteNull();
                }
                else
                {
                    copy.Write(row.SourcePage.Value, NpgsqlDbType.Integer);
                }
                copy.Write(row.SourceRowNumber, NpgsqlDbType.Integer);
                copy.Write(JsonSerializer.Serialize(row.RawValues, StorageJson), NpgsqlDbType.Jsonb);
                if (row.TransactionDate is null)
                {
                    copy.WriteNull();
                }
                else
                {
                    copy.Write(row.TransactionDate.Value, NpgsqlDbType.Date);
                }
                copy.Write(row.Description, NpgsqlDbType.Text);
                copy.Write(row.NormalizedDescription, NpgsqlDbType.Text);
                if (row.AmountCents is null)
                {
                    copy.WriteNull();
                }
                else
                {
                    copy.Write(row.AmountCents.Value, NpgsqlDbType.Bigint);
                }
                if (row.SourceIdNamespace is null)
                {
                    copy.WriteNull();
                }
                else
                {
                    copy.Write(row.SourceIdNamespace, NpgsqlDbType.Text);
                }
                if (row.SourceIdValue is null)
                {
                    copy.WriteNull();
                }
                else
                {
                    copy.Write(row.SourceIdValue, NpgsqlDbType.Text);
                }
                copy.Write(JsonSerializer.Serialize(row.ValidationCodes, StorageJson), NpgsqlDbType.Jsonb);
                copy.Write(JsonSerializer.Serialize(row.Warnings, StorageJson), NpgsqlDbType.Jsonb);
                copy.Write(row.DuplicateState, NpgsqlDbType.Text);
                copy.Write(JsonSerializer.Serialize(row.DuplicateReasons, StorageJson), NpgsqlDbType.Jsonb);
                copy.Write(JsonSerializer.Serialize(row.MatchingTransactionIds, StorageJson), NpgsqlDbType.Jsonb);
                copy.Write(JsonSerializer.Serialize(row.MatchingImportIds, StorageJson), NpgsqlDbType.Jsonb);
                copy.Write(JsonSerializer.Serialize(row.NearCandidates, StorageJson), NpgsqlDbType.Jsonb);
                copy.Write(row.NearCandidateTotal, NpgsqlDbType.Integer);
                if (row.WithinFileGroup is null)
                {
                    copy.WriteNull();
                }
                else
                {
                    copy.Write(row.WithinFileGroup, NpgsqlDbType.Text);
                }
                copy.Write(row.WithinFileState, NpgsqlDbType.Text);
                copy.Write(row.Skip, NpgsqlDbType.Boolean);
                copy.Write(row.Override, NpgsqlDbType.Boolean);
                copy.Write(JsonSerializer.Serialize(row.SuggestedTagIds, StorageJson), NpgsqlDbType.Jsonb);
                copy.Write(JsonSerializer.Serialize(row.SelectedTagIds, StorageJson), NpgsqlDbType.Jsonb);
                copy.Write(now, NpgsqlDbType.TimestampTz);
                copy.Write(now, NpgsqlDbType.TimestampTz);
            }
            await copy.CompleteAsync(cancellationToken);
        }
        catch
        {
            await copy.DisposeAsync();
            throw;
        }
    }

    private static async Task SetWithinFileGroupAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reviewId,
        string group,
        string state,
        bool skip,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE import_review_rows
            SET within_file_state = @state, skip = @skip, override = CASE WHEN @skip THEN FALSE ELSE override END,
                updated_at = NOW()
            WHERE review_id = @review_id AND within_file_group = @group
            """;
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("skip", skip);
        command.Parameters.AddWithValue("review_id", reviewId);
        command.Parameters.AddWithValue("group", group);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateRowDecisionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid rowId,
        bool skip,
        bool overrideValue,
        string withinFileState,
        Guid[] selectedTagIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE import_review_rows
            SET skip = @skip,
                override = @override,
                within_file_state = @within_file_state,
                selected_tag_ids = @selected_tag_ids,
                updated_at = NOW()
            WHERE row_id = @row_id
            """;
        command.Parameters.AddWithValue("skip", skip);
        command.Parameters.AddWithValue("override", overrideValue);
        command.Parameters.AddWithValue("within_file_state", withinFileState);
        command.Parameters.AddWithValue("selected_tag_ids", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(selectedTagIds, StorageJson));
        command.Parameters.AddWithValue("row_id", rowId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateDuplicateSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ImportReviewRowRecord row,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE import_review_rows
            SET duplicate_state = @duplicate_state,
                duplicate_reasons = @duplicate_reasons,
                matching_transaction_ids = @matching_transaction_ids,
                matching_import_ids = @matching_import_ids,
                near_candidates = @near_candidates,
                near_candidate_total = @near_candidate_total,
                within_file_group = @within_file_group,
                within_file_state = @within_file_state,
                override = @override,
                suggested_tag_ids = @suggested_tag_ids,
                updated_at = NOW()
            WHERE row_id = @row_id
            """;
        command.Parameters.AddWithValue("duplicate_state", row.DuplicateState);
        command.Parameters.AddWithValue("duplicate_reasons", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(row.DuplicateReasons, StorageJson));
        command.Parameters.AddWithValue("matching_transaction_ids", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(row.MatchingTransactionIds, StorageJson));
        command.Parameters.AddWithValue("matching_import_ids", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(row.MatchingImportIds, StorageJson));
        command.Parameters.AddWithValue("near_candidates", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(row.NearCandidates, StorageJson));
        command.Parameters.AddWithValue("near_candidate_total", row.NearCandidateTotal);
        command.Parameters.AddWithValue("within_file_group",
            NpgsqlDbType.Text, (object?)row.WithinFileGroup ?? DBNull.Value);
        command.Parameters.AddWithValue("within_file_state", row.WithinFileState);
        command.Parameters.AddWithValue("override", row.Override);
        command.Parameters.AddWithValue(
            "suggested_tag_ids", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(row.SuggestedTagIds, StorageJson));
        command.Parameters.AddWithValue("row_id", row.RowId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(long OpenReviews, long UsedPayload)> QuotaUsageExcludingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(payload_bytes), 0)
            FROM import_reviews
            WHERE customer_id = @customer_id
              AND status = 'Prepared'
              AND expires_at > NOW()
              AND id <> @review_id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("review_id", reviewId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static async Task UpdateReviewSummaryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reviewId,
        int version,
        RowCounts counts,
        string summaryHash,
        bool statementOverride,
        long payloadBytes,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE import_reviews
            SET version = @version,
                summary_hash = @summary_hash,
                total_rows = @total_rows,
                imported_count = @imported_count,
                suppressed_count = @suppressed_count,
                skipped_count = @skipped_count,
                possible_duplicate_count = @possible_duplicate_count,
                exact_override_count = @exact_override_count,
                statement_override = @statement_override,
                statement_override_count = @statement_override_count,
                payload_bytes = @payload_bytes,
                updated_at = NOW()
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("summary_hash", summaryHash);
        command.Parameters.AddWithValue("total_rows", counts.TotalRows);
        command.Parameters.AddWithValue("imported_count", counts.Imported);
        command.Parameters.AddWithValue("suppressed_count", counts.Suppressed);
        command.Parameters.AddWithValue("skipped_count", counts.Skipped);
        command.Parameters.AddWithValue("possible_duplicate_count", counts.PossibleDuplicates);
        command.Parameters.AddWithValue("exact_override_count", counts.ExactOverrides);
        command.Parameters.AddWithValue("statement_override", statementOverride);
        command.Parameters.AddWithValue("statement_override_count", statementOverride ? 1 : 0);
        command.Parameters.AddWithValue("payload_bytes", payloadBytes);
        command.Parameters.AddWithValue("id", reviewId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateReviewAccountVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reviewId,
        long accountImportVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE import_reviews SET account_import_version = @version WHERE id = @id";
        command.Parameters.AddWithValue("version", accountImportVersion);
        command.Parameters.AddWithValue("id", reviewId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<ExistingRow>> QueryExistingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid accountId,
        string predicate,
        Action<NpgsqlCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT t.id, t.identity_key, t.occurrence_index, t.transaction_date,
                   (t.amount * 100)::bigint AS amount_cents, t.normalized_description,
                   t.description, t.source_id_namespace, t.source_id_value, t.statement_import_id
            FROM transactions t
            WHERE t.customer_id = @customer_id AND t.financial_account_id = @account_id
              AND {predicate}
            LIMIT 20001
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("account_id", accountId);
        configure(command);
        var rows = new List<ExistingRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ExistingRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetInt64(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetGuid(9)));
        }
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
        var imported = 0;
        var suppressed = 0;
        var possibleDuplicates = 0;
        var exactOverrides = 0;
        foreach (var row in rows)
        {
            var isSuppressed = row.DuplicateState is "suppressed" or "suppressed_within_file";
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
        var total = rows.Count;
        var skipped = total - imported - suppressed;
        return new RowCounts(total, imported, suppressed, skipped, possibleDuplicates, exactOverrides);
    }

    private static string ComputeSummaryHash(
        Guid reviewId,
        int version,
        IReadOnlyList<ImportReviewRowRecord> rows,
        RowCounts counts,
        bool statementOverride)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
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
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static long EstimatePayload(
        ParsedStatement parsed,
        ParserConfig config,
        IReadOnlyList<ImportReviewRowRecord>? rows = null)
    {
        long bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(config, StorageJson));
        foreach (var row in parsed.Rows)
        {
            bytes += Encoding.UTF8.GetByteCount(row.Description);
            bytes += Encoding.UTF8.GetByteCount(row.NormalizedDescription);
            bytes += JsonSerializer.Serialize(row.RawValues, StorageJson).Length;
            bytes += JsonSerializer.Serialize(row.ValidationCodes, StorageJson).Length;
            bytes += JsonSerializer.Serialize(row.Warnings, StorageJson).Length;
        }
        if (rows is not null)
        {
            bytes += Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(rows, StorageJson));
        }
        return bytes;
    }

    // --- Commit and terminal lifecycle -------------------------------------

    public async Task<ImportCommitResult> CommitReviewAsync(
        Guid customerId,
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
        var requestDigest = Digest($"commit-v1|{expectedVersion}|{confirmedSummaryHash}");

        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // A receipt is the truth after a lost response, including after the
        // review payload has been purged. Resolve the account from the receipt
        // before looking for the review.
        var receiptAccountId = await ReceiptAccountIdAsync(
            connection, transaction, customerId, reviewId, cancellationToken);
        if (receiptAccountId is not null)
        {
            _ = await LockAccountAsync(
                connection, transaction, customerId, receiptAccountId.Value, cancellationToken);
            var replay = await ReplayReceiptAsync(
                connection, transaction, customerId, reviewId, requestDigest, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var accountId = await ReviewAccountIdAsync(
            connection, transaction, customerId, reviewId, cancellationToken);
        if (accountId is null)
        {
            var lateReceiptAccount = await ReceiptAccountIdAsync(
                connection, transaction, customerId, reviewId, cancellationToken);
            if (lateReceiptAccount is not null)
            {
                _ = await LockAccountAsync(
                    connection, transaction, customerId, lateReceiptAccount.Value, cancellationToken);
                var replay = await ReplayReceiptAsync(
                    connection, transaction, customerId, reviewId, requestDigest, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        var account = await LockAccountAsync(
            connection, transaction, customerId, accountId.Value, cancellationToken);

        // Re-check under the account lock in case a receipt appeared while this
        // request waited for the account.
        var racedReceipt = await ReadReceiptAsync(
            connection, transaction, customerId, reviewId, false, cancellationToken);
        if (racedReceipt is not null)
        {
            var replay = await ReplayReceiptAsync(
                connection, transaction, customerId, reviewId, requestDigest, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var review = await ReadReviewHeaderAsync(
            connection, transaction, customerId, reviewId, true, cancellationToken);
        if (review is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        if (review.Status == "Expired" || review.ExpiresAt <= DateTime.UtcNow)
        {
            throw new ImportStoreException(ImportErrorCodes.ReviewExpired);
        }
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

        var rows = await ReadReviewRowsAsync(
            connection, transaction, reviewId, 1, int.MaxValue, cancellationToken);
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
        var validRows = rows
            .Where(row => row.TransactionDate is not null && row.AmountCents is not null)
            .ToList();
        if (validRows.Count == 0 || validRows.All(row => row.Skip))
        {
            throw new ImportStoreException(ImportErrorCodes.NoIncludedRows);
        }
        var included = rows
            .Where(row => !row.Skip && (row.DuplicateState is not ("suppressed" or "suppressed_within_file") || row.Override))
            .ToList();
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

        var categoryId = await FindSystemCategoryAsync(
            connection, transaction, customerId, cancellationToken);
        if (categoryId is null)
        {
            throw new ImportStoreException(ImportErrorCodes.InvalidRequest);
        }

        var tagIds = included
            .SelectMany(row => row.SelectedTagIds)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        if (tagIds.Length > 0 && !await TagsAvailableAsync(
                connection, transaction, customerId, tagIds, cancellationToken))
        {
            throw new ImportStoreException(ImportErrorCodes.TagUnavailable);
        }

        var fingerprintMatches = await FingerprintMatchesAsync(
            connection, transaction, customerId, accountId.Value,
            review.StatementFingerprint, cancellationToken);
        if (fingerprintMatches.Count > 0 && !review.StatementOverride)
        {
            throw new StatementRepeatException(fingerprintMatches);
        }
        var fingerprintOccurrence = await NextFingerprintOccurrenceAsync(
            connection, transaction, customerId, accountId.Value,
            review.StatementFingerprint, cancellationToken);

        var statementId = Guid.NewGuid();
        var label = BuildStatementLabel(review);
        var overrideSummary = JsonSerializer.Serialize(new
        {
            exact_row_ids = rows
                .Where(row => row.DuplicateState is "suppressed" or "suppressed_within_file")
                .Where(row => row.Override)
                .Select(row => row.RowId)
                .ToArray(),
            exact_count = counts.ExactOverrides,
            statement_override = review.StatementOverride,
        }, StorageJson);
        var createdAt = DateTime.UtcNow;

        await using (var insertStatement = connection.CreateCommand())
        {
            insertStatement.Transaction = transaction;
            insertStatement.CommandText = """
                INSERT INTO statement_imports (
                    id, customer_id, financial_account_id, source_type, statement_label,
                    statement_fingerprint, start_date, end_date, total_rows, imported_count,
                    suppressed_count, skipped_count, version, fingerprint_occurrence,
                    duplicate_policy_version, possible_duplicate_count, exact_override_count,
                    statement_override_count, override_summary, is_hidden, hidden_at,
                    created_at, updated_at
                )
                VALUES (
                    @id, @customer_id, @financial_account_id, @source_type, @statement_label,
                    @statement_fingerprint, @start_date, @end_date, @total_rows, @imported_count,
                    @suppressed_count, @skipped_count, 1, @fingerprint_occurrence,
                    @duplicate_policy_version, @possible_duplicate_count, @exact_override_count,
                    @statement_override_count, @override_summary, FALSE, NULL,
                    @created_at, @created_at
                )
                """;
            insertStatement.Parameters.AddWithValue("id", statementId);
            insertStatement.Parameters.AddWithValue("customer_id", customerId);
            insertStatement.Parameters.AddWithValue("financial_account_id", accountId.Value);
            insertStatement.Parameters.AddWithValue("source_type", review.SourceKind);
            insertStatement.Parameters.AddWithValue("statement_label", label);
            insertStatement.Parameters.AddWithValue("statement_fingerprint", review.StatementFingerprint);
            insertStatement.Parameters.AddWithValue("start_date", review.StartDate!.Value);
            insertStatement.Parameters.AddWithValue("end_date", review.EndDate!.Value);
            insertStatement.Parameters.AddWithValue("total_rows", counts.TotalRows);
            insertStatement.Parameters.AddWithValue("imported_count", counts.Imported);
            insertStatement.Parameters.AddWithValue("suppressed_count", counts.Suppressed);
            insertStatement.Parameters.AddWithValue("skipped_count", counts.Skipped);
            insertStatement.Parameters.AddWithValue("fingerprint_occurrence", fingerprintOccurrence);
            insertStatement.Parameters.AddWithValue(
                "duplicate_policy_version", ImportLimits.DuplicatePolicyVersion);
            insertStatement.Parameters.AddWithValue(
                "possible_duplicate_count", counts.PossibleDuplicates);
            insertStatement.Parameters.AddWithValue("exact_override_count", counts.ExactOverrides);
            insertStatement.Parameters.AddWithValue(
                "statement_override_count", review.StatementOverride ? 1 : 0);
            insertStatement.Parameters.AddWithValue(
                "override_summary", NpgsqlDbType.Jsonb, overrideSummary);
            insertStatement.Parameters.AddWithValue("created_at", createdAt);
            await insertStatement.ExecuteNonQueryAsync(cancellationToken);
        }
        _faults.OnStep("statement_inserted");

        var identityKeys = included
            .Select(ComputeIdentityKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var maxOccurrences = await MaxOccurrencesAsync(
            connection, transaction, customerId, accountId.Value, identityKeys, cancellationToken);
        var nextOccurrence = maxOccurrences.ToDictionary(
            pair => pair.Key, pair => pair.Value + 1, StringComparer.Ordinal);
        var transactionTags = new List<(Guid TransactionId, Guid[] TagIds)>(included.Count);
        await using (var copy = await connection.BeginBinaryImportAsync(
            "COPY transactions (" +
            "id, customer_id, financial_account_id, statement_import_id, transaction_date, " +
            "description, normalized_description, amount, category_id, source_type, source_page, " +
            "source_row_number, identity_key, occurrence_index, identity_version, source_id_namespace, " +
            "source_id_value, is_hidden_by_user, version, created_at, updated_at" +
            ") FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var row in included)
            {
                var identityKey = ComputeIdentityKey(row);
                var occurrence = nextOccurrence.GetValueOrDefault(identityKey, 1);
                nextOccurrence[identityKey] = occurrence + 1;
                var transactionId = Guid.NewGuid();
                transactionTags.Add((transactionId, row.SelectedTagIds));
                await copy.StartRowAsync(cancellationToken);
                copy.Write(transactionId, NpgsqlDbType.Uuid);
                copy.Write(customerId, NpgsqlDbType.Uuid);
                copy.Write(accountId.Value, NpgsqlDbType.Uuid);
                copy.Write(statementId, NpgsqlDbType.Uuid);
                copy.Write(row.TransactionDate!.Value, NpgsqlDbType.Date);
                copy.Write(row.Description, NpgsqlDbType.Text);
                copy.Write(row.NormalizedDescription, NpgsqlDbType.Text);
                copy.Write(row.AmountCents!.Value / 100m, NpgsqlDbType.Numeric);
                copy.Write(categoryId.Value, NpgsqlDbType.Uuid);
                copy.Write(review.SourceKind, NpgsqlDbType.Text);
                if (row.SourcePage is null)
                {
                    copy.WriteNull();
                }
                else
                {
                    copy.Write(row.SourcePage.Value, NpgsqlDbType.Integer);
                }
                copy.Write(row.SourceRowNumber, NpgsqlDbType.Integer);
                copy.Write(identityKey, NpgsqlDbType.Text);
                copy.Write(occurrence, NpgsqlDbType.Integer);
                copy.Write(ImportLimits.IdentityVersion, NpgsqlDbType.Integer);
                if (row.SourceIdNamespace is null)
                {
                    copy.WriteNull();
                }
                else
                {
                    copy.Write(row.SourceIdNamespace, NpgsqlDbType.Text);
                }
                if (row.SourceIdValue is null)
                {
                    copy.WriteNull();
                }
                else
                {
                    copy.Write(row.SourceIdValue, NpgsqlDbType.Text);
                }
                copy.Write(false, NpgsqlDbType.Boolean);
                copy.Write(1, NpgsqlDbType.Integer);
                copy.Write(createdAt, NpgsqlDbType.TimestampTz);
                copy.Write(createdAt, NpgsqlDbType.TimestampTz);
            }
            await copy.CompleteAsync(cancellationToken);
        }
        _faults.OnStep("transactions_inserted");

        var links = transactionTags
            .SelectMany(pair => pair.TagIds.Select(tagId => (pair.TransactionId, TagId: tagId)))
            .ToArray();
        if (links.Length > 0)
        {
            await using var copy = await connection.BeginBinaryImportAsync(
                "COPY transaction_merchant_tags (customer_id, transaction_id, merchant_tag_id, assigned_at) " +
                "FROM STDIN (FORMAT BINARY)", cancellationToken);
            foreach (var (transactionId, tagId) in links)
            {
                await copy.StartRowAsync(cancellationToken);
                copy.Write(customerId, NpgsqlDbType.Uuid);
                copy.Write(transactionId, NpgsqlDbType.Uuid);
                copy.Write(tagId, NpgsqlDbType.Uuid);
                copy.Write(createdAt, NpgsqlDbType.TimestampTz);
            }
            await copy.CompleteAsync(cancellationToken);
        }
        _faults.OnStep("tag_links_inserted");

        var resultJson = JsonSerializer.Serialize(new
        {
            statement_import_id = statementId,
            total_rows = counts.TotalRows,
            imported_count = counts.Imported,
            suppressed_count = counts.Suppressed,
            skipped_count = counts.Skipped,
            possible_duplicate_count = counts.PossibleDuplicates,
            exact_override_count = counts.ExactOverrides,
            statement_override_count = review.StatementOverride ? 1 : 0,
        }, StorageJson);
        await using (var receipt = connection.CreateCommand())
        {
            receipt.Transaction = transaction;
            receipt.CommandText = """
                INSERT INTO import_commit_receipts (
                    id, customer_id, operation_id, financial_account_id, request_digest,
                    state, statement_import_id, result, created_at, updated_at
                )
                VALUES (
                    @id, @customer_id, @operation_id, @financial_account_id, @request_digest,
                    'Committed', @statement_import_id, @result, @created_at, @created_at
                )
                """;
            receipt.Parameters.AddWithValue("id", Guid.NewGuid());
            receipt.Parameters.AddWithValue("customer_id", customerId);
            receipt.Parameters.AddWithValue("operation_id", reviewId);
            receipt.Parameters.AddWithValue("financial_account_id", accountId.Value);
            receipt.Parameters.AddWithValue("request_digest", requestDigest);
            receipt.Parameters.AddWithValue("statement_import_id", statementId);
            receipt.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb, resultJson);
            receipt.Parameters.AddWithValue("created_at", createdAt);
            await receipt.ExecuteNonQueryAsync(cancellationToken);
        }
        _faults.OnStep("receipt_inserted");

        await using (var bump = connection.CreateCommand())
        {
            bump.Transaction = transaction;
            bump.CommandText =
                "UPDATE financial_accounts SET import_version = import_version + 1, updated_at = NOW() WHERE customer_id = @customer_id AND id = @id";
            bump.Parameters.AddWithValue("customer_id", customerId);
            bump.Parameters.AddWithValue("id", accountId.Value);
            await bump.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var purge = connection.CreateCommand())
        {
            purge.Transaction = transaction;
            purge.CommandText = "DELETE FROM import_review_rows WHERE review_id = @review_id; DELETE FROM import_reviews WHERE customer_id = @customer_id AND id = @id";
            purge.Parameters.AddWithValue("review_id", reviewId);
            purge.Parameters.AddWithValue("customer_id", customerId);
            purge.Parameters.AddWithValue("id", reviewId);
            await purge.ExecuteNonQueryAsync(cancellationToken);
        }

        var summary = await ReadStatementSummaryAsync(
            connection, transaction, customerId, statementId, cancellationToken)
            ?? throw new ImportStoreException(ImportErrorCodes.NotFound);
        _faults.OnStep("before_commit");
        await transaction.CommitAsync(cancellationToken);
        _faults.OnStep("after_commit_before_response");
        return new ImportCommitResult(summary, false);
    }

    private async Task<ImportCommitResult> ReplayReceiptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid reviewId,
        string requestDigest,
        CancellationToken cancellationToken)
    {
        var receipt = await ReadReceiptAsync(
            connection, transaction, customerId, reviewId, true, cancellationToken)
            ?? throw new ImportStoreException(ImportErrorCodes.NotFound);
        if (receipt.State == "Deleted")
        {
            throw new ImportStoreException(ImportErrorCodes.ImportDeleted);
        }
        if (receipt.RequestDigest != requestDigest)
        {
            throw new ImportStoreException(ImportErrorCodes.IdempotencyConflict);
        }
        var summary = await ReadStatementSummaryAsync(
            connection, transaction, customerId, receipt.StatementImportId!.Value, cancellationToken)
            ?? throw new ImportStoreException(ImportErrorCodes.NotFound);
        return new ImportCommitResult(summary, true);
    }

    public async Task<ImportHistoryPage> ListHistoryAsync(
        Guid customerId,
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

        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.financial_account_id, s.source_type, s.statement_label,
                   s.start_date, s.end_date, s.total_rows, s.imported_count, s.suppressed_count,
                   s.skipped_count, s.possible_duplicate_count, s.exact_override_count,
                   s.statement_override_count, s.is_hidden, s.version,
                   COALESCE(h.live_hidden_count, 0) AS live_hidden_count,
                   s.created_at, s.updated_at
            FROM statement_imports s
            LEFT JOIN LATERAL (
                SELECT COUNT(*) FILTER (WHERE s.is_hidden OR t.is_hidden_by_user) AS live_hidden_count
                FROM transactions t
                WHERE t.customer_id = s.customer_id AND t.statement_import_id = s.id
            ) h ON TRUE
            WHERE s.customer_id = @customer_id
              AND (@account_id IS NULL OR s.financial_account_id = @account_id)
              AND (@state = 'all'
                   OR (@state = 'hidden' AND COALESCE(h.live_hidden_count, 0) > 0)
                   OR (@state = 'visible' AND COALESCE(h.live_hidden_count, 0) = 0))
              AND (@cursor_created IS NULL OR (s.created_at, s.id) < (@cursor_created, @cursor_id))
            ORDER BY s.created_at DESC, s.id DESC
            LIMIT @limit_plus
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("account_id", NpgsqlDbType.Uuid,
            (object?)accountId ?? DBNull.Value);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("cursor_created", NpgsqlDbType.TimestampTz,
            (object?)cursorCreated ?? DBNull.Value);
        command.Parameters.AddWithValue("cursor_id", NpgsqlDbType.Uuid,
            (object?)cursorId ?? DBNull.Value);
        command.Parameters.AddWithValue("limit_plus", limit + 1);
        var items = new List<ImportStatementSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapStatementSummary(reader));
        }
        if (items.Count <= limit)
        {
            return new ImportHistoryPage(items, null);
        }
        items.RemoveAt(items.Count - 1);
        return new ImportHistoryPage(
            items, EncodeCursor(items[^1].CreatedAt, items[^1].Id));
    }

    public async Task<ImportStatementDetail> GetStatementAsync(
        Guid customerId,
        Guid statementId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, ImportLimits.MaxPageSize);
        await using var connection = await _db.OpenAsync(cancellationToken);
        var summary = await ReadStatementSummaryAsync(connection, null, customerId, statementId, cancellationToken);
        if (summary is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }

        await using (var count = connection.CreateCommand())
        {
            count.CommandText =
                "SELECT COUNT(*) FROM transactions WHERE customer_id = @customer_id AND statement_import_id = @id";
            count.Parameters.AddWithValue("customer_id", customerId);
            count.Parameters.AddWithValue("id", statementId);
            var expectedTransactionCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
            var rows = await ReadStatementRowsAsync(
                connection, null, customerId, statementId, page, pageSize, cancellationToken);
            return new ImportStatementDetail(summary, rows, page, pageSize, expectedTransactionCount);
        }
    }

    public async Task<ImportStatementSummary> SetStatementVisibilityAsync(
        Guid customerId,
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
        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var accountId = await StatementAccountIdAsync(
            connection, transaction, customerId, statementId, cancellationToken);
        if (accountId is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        await LockAccountAsync(connection, transaction, customerId, accountId.Value, cancellationToken);
        var summary = await LockStatementAsync(
            connection, transaction, customerId, statementId, cancellationToken);
        if (summary is null)
        {
            throw new ImportStoreException(ImportErrorCodes.NotFound);
        }
        if (summary.Version != expectedVersion)
        {
            throw new ImportStoreException(ImportErrorCodes.VersionConflict);
        }
        var count = await StatementTransactionCountAsync(
            connection, transaction, customerId, statementId, cancellationToken);
        if (count != expectedTransactionCount)
        {
            throw new ImportStoreException(ImportErrorCodes.VersionConflict);
        }
        if (summary.IsHidden == hidden)
        {
            await transaction.CommitAsync(cancellationToken);
            return summary;
        }
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE statement_imports
                SET is_hidden = @hidden,
                    hidden_at = CASE WHEN @hidden THEN NOW() ELSE NULL END,
                    version = version + 1,
                    updated_at = NOW()
                WHERE customer_id = @customer_id AND id = @id
                """;
            update.Parameters.AddWithValue("hidden", hidden);
            update.Parameters.AddWithValue("customer_id", customerId);
            update.Parameters.AddWithValue("id", statementId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var touch = connection.CreateCommand())
        {
            touch.Transaction = transaction;
            touch.CommandText = """
                UPDATE transactions
                SET version = version + 1, updated_at = NOW()
                WHERE customer_id = @customer_id AND statement_import_id = @id
                """;
            touch.Parameters.AddWithValue("customer_id", customerId);
            touch.Parameters.AddWithValue("id", statementId);
            await touch.ExecuteNonQueryAsync(cancellationToken);
        }
        var updated = await ReadStatementSummaryAsync(
            connection, transaction, customerId, statementId, cancellationToken)
            ?? throw new ImportStoreException(ImportErrorCodes.NotFound);
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<ImportDeleteResult> DeleteStatementAsync(
        Guid customerId,
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
        var deletionDigest = Digest(
            $"delete-v1|{expectedVersion}|{expectedTransactionCount}|true");

        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var accountId = await StatementAccountIdAsync(
            connection, transaction, customerId, statementId, cancellationToken);
        if (accountId is not null)
        {
            await LockAccountAsync(connection, transaction, customerId, accountId.Value, cancellationToken);
        }

        var statement = await LockStatementAsync(
            connection, transaction, customerId, statementId, cancellationToken);
        if (statement is null)
        {
            var tombstone = await ReadDeletionReceiptAsync(
                connection, transaction, customerId, statementId, cancellationToken);
            if (tombstone is not null &&
                tombstone.DeletionDigest == deletionDigest &&
                tombstone.DeletionExpectedCount == expectedTransactionCount)
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
        var count = await StatementTransactionCountAsync(
            connection, transaction, customerId, statementId, cancellationToken);
        if (count != expectedTransactionCount)
        {
            throw new ImportStoreException(ImportErrorCodes.VersionConflict);
        }

        await using (var deleteTransactions = connection.CreateCommand())
        {
            deleteTransactions.Transaction = transaction;
            deleteTransactions.CommandText =
                "DELETE FROM transactions WHERE customer_id = @customer_id AND statement_import_id = @id";
            deleteTransactions.Parameters.AddWithValue("customer_id", customerId);
            deleteTransactions.Parameters.AddWithValue("id", statementId);
            await deleteTransactions.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var deleteStatement = connection.CreateCommand())
        {
            deleteStatement.Transaction = transaction;
            deleteStatement.CommandText =
                "DELETE FROM statement_imports WHERE customer_id = @customer_id AND id = @id";
            deleteStatement.Parameters.AddWithValue("customer_id", customerId);
            deleteStatement.Parameters.AddWithValue("id", statementId);
            await deleteStatement.ExecuteNonQueryAsync(cancellationToken);
        }

        var receipt = await ReadReceiptByStatementAsync(
            connection, transaction, customerId, statementId, cancellationToken);
        var resultJson = JsonSerializer.Serialize(new
        {
            statement_import_id = statementId,
            deleted = true,
            deleted_transaction_count = count,
        }, StorageJson);
        if (receipt is null)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO import_commit_receipts (
                    id, customer_id, operation_id, financial_account_id, request_digest,
                    state, statement_import_id, result, deletion_digest, deletion_expected_count,
                    created_at, updated_at
                )
                VALUES (
                    @id, @customer_id, @operation_id, @financial_account_id, @request_digest,
                    'Deleted', @statement_import_id, @result, @deletion_digest, @deletion_expected_count,
                    NOW(), NOW()
                )
                """;
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("customer_id", customerId);
            insert.Parameters.AddWithValue("operation_id", statementId);
            insert.Parameters.AddWithValue("financial_account_id", statement.FinancialAccountId);
            insert.Parameters.AddWithValue("request_digest", deletionDigest);
            insert.Parameters.AddWithValue("statement_import_id", statementId);
            insert.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb, resultJson);
            insert.Parameters.AddWithValue("deletion_digest", deletionDigest);
            insert.Parameters.AddWithValue("deletion_expected_count", count);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE import_commit_receipts
                SET state = 'Deleted',
                    result = @result,
                    deletion_digest = @deletion_digest,
                    deletion_expected_count = @deletion_expected_count,
                    updated_at = NOW()
                WHERE id = @id
                """;
            update.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb, resultJson);
            update.Parameters.AddWithValue("deletion_digest", deletionDigest);
            update.Parameters.AddWithValue("deletion_expected_count", count);
            update.Parameters.AddWithValue("id", receipt.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var bump = connection.CreateCommand())
        {
            bump.Transaction = transaction;
            bump.CommandText =
                "UPDATE financial_accounts SET import_version = import_version + 1, updated_at = NOW() WHERE customer_id = @customer_id AND id = @id";
            bump.Parameters.AddWithValue("customer_id", customerId);
            bump.Parameters.AddWithValue("id", statement.FinancialAccountId);
            await bump.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ImportDeleteResult(true, count);
    }

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var expired = new List<Guid>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            // Lock the headers before touching their child rows. Commit and
            // review edits acquire the review header first; cleanup must not
            // invert that order. The lock wait also re-evaluates the expiry
            // predicate, so a concurrent commit that purges the review removes
            // it from this set.
            select.CommandText = """
                SELECT id FROM import_reviews
                WHERE status = 'Prepared' AND expires_at <= NOW()
                ORDER BY expires_at
                LIMIT 1000
                FOR UPDATE
                """;
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                expired.Add(reader.GetGuid(0));
            }
        }

        if (expired.Count > 0)
        {
            await using (var rows = connection.CreateCommand())
            {
                rows.Transaction = transaction;
                rows.CommandText = "DELETE FROM import_review_rows WHERE review_id = ANY(@ids)";
                rows.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, expired.ToArray());
                await rows.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var headers = connection.CreateCommand())
            {
                headers.Transaction = transaction;
                headers.CommandText = """
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
                    WHERE id = ANY(@ids)
                    """;
                headers.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, expired.ToArray());
                await headers.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var prune = connection.CreateCommand())
        {
            prune.Transaction = transaction;
            prune.CommandText = """
                DELETE FROM import_reviews
                WHERE status IN ('Abandoned', 'Expired')
                  AND updated_at < NOW() - INTERVAL '30 days'
                """;
            await prune.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // --- Receipt, statement, and row helpers --------------------------------

    /// <summary>
    /// Resolves whether a commit operation is already owned by the caller
    /// before any category seeding occurs. A prepared review or a retained
    /// receipt both establish ownership; anything else must return the same
    /// 404 as an unknown operation.
    /// </summary>
    private async Task<bool> OwnedOperationExistsAsync(
        Guid customerId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM import_reviews
                WHERE customer_id = @customer_id AND id = @operation_id
                UNION ALL
                SELECT 1 FROM import_commit_receipts
                WHERE customer_id = @customer_id AND operation_id = @operation_id
            )
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("operation_id", operationId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<Guid?> ReceiptAccountIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT financial_account_id FROM import_commit_receipts
            WHERE customer_id = @customer_id AND operation_id = @operation_id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("operation_id", operationId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
    }

    private static async Task<ReceiptRecord?> ReadReceiptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid operationId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT id, customer_id, operation_id, financial_account_id, request_digest, state,
                   statement_import_id, result::text, deletion_digest, deletion_expected_count
            FROM import_commit_receipts
            WHERE customer_id = @customer_id AND operation_id = @operation_id
            {(forUpdate ? "FOR UPDATE" : string.Empty)}
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("operation_id", operationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ReceiptRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9))
            : null;
    }

    private static async Task<ReceiptRecord?> ReadDeletionReceiptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid statementId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, customer_id, operation_id, financial_account_id, request_digest, state,
                   statement_import_id, result::text, deletion_digest, deletion_expected_count
            FROM import_commit_receipts
            WHERE customer_id = @customer_id AND statement_import_id = @statement_import_id
              AND state = 'Deleted'
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("statement_import_id", statementId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ReceiptRecord(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
                reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9))
            : null;
    }

    private static async Task<ReceiptRecord?> ReadReceiptByStatementAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid statementId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, customer_id, operation_id, financial_account_id, request_digest, state,
                   statement_import_id, result::text, deletion_digest, deletion_expected_count
            FROM import_commit_receipts
            WHERE customer_id = @customer_id AND statement_import_id = @statement_import_id
            ORDER BY created_at DESC
            LIMIT 1
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("statement_import_id", statementId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ReceiptRecord(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
                reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9))
            : null;
    }

    private static async Task<Guid?> StatementAccountIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid statementId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT financial_account_id FROM statement_imports WHERE customer_id = @customer_id AND id = @id";
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", statementId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
    }

    private static async Task<ImportStatementSummary?> LockStatementAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid statementId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT s.id, s.financial_account_id, s.source_type, s.statement_label,
                   s.start_date, s.end_date, s.total_rows, s.imported_count, s.suppressed_count,
                   s.skipped_count, s.possible_duplicate_count, s.exact_override_count,
                   s.statement_override_count, s.is_hidden, s.version,
                   COALESCE((
                       SELECT COUNT(*) FROM transactions t
                       WHERE t.customer_id = s.customer_id AND t.statement_import_id = s.id
                         AND (s.is_hidden OR t.is_hidden_by_user)
                   ), 0) AS live_hidden_count,
                   s.created_at, s.updated_at
            FROM statement_imports s
            WHERE s.customer_id = @customer_id AND s.id = @id
            FOR UPDATE
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", statementId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapStatementSummary(reader) : null;
    }

    private static async Task<long> StatementTransactionCountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid statementId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM transactions WHERE customer_id = @customer_id AND statement_import_id = @id";
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", statementId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<ImportStatementSummary?> ReadStatementSummaryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid customerId,
        Guid statementId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT s.id, s.financial_account_id, s.source_type, s.statement_label,
                   s.start_date, s.end_date, s.total_rows, s.imported_count, s.suppressed_count,
                   s.skipped_count, s.possible_duplicate_count, s.exact_override_count,
                   s.statement_override_count, s.is_hidden, s.version,
                   COALESCE((
                       SELECT COUNT(*) FROM transactions t
                       WHERE t.customer_id = s.customer_id AND t.statement_import_id = s.id
                         AND (s.is_hidden OR t.is_hidden_by_user)
                   ), 0) AS live_hidden_count,
                   s.created_at, s.updated_at
            FROM statement_imports s
            WHERE s.customer_id = @customer_id AND s.id = @id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", statementId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapStatementSummary(reader) : null;
    }

    private static ImportStatementSummary MapStatementSummary(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetFieldValue<DateOnly>(4),
        reader.GetFieldValue<DateOnly>(5),
        reader.GetInt32(6),
        reader.GetInt32(7),
        reader.GetInt32(8),
        reader.GetInt32(9),
        reader.GetInt32(10),
        reader.GetInt32(11),
        reader.GetInt32(12),
        reader.GetBoolean(13),
        reader.GetInt32(14),
        reader.GetInt64(15),
        reader.GetFieldValue<DateTime>(16),
        reader.GetFieldValue<DateTime>(17));

    private static async Task<IReadOnlyList<ImportStatementRowRecord>> ReadStatementRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid customerId,
        Guid statementId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT t.id, t.source_row_number, t.source_page, t.transaction_date,
                   t.description, (t.amount * 100)::bigint,
                   t.is_hidden_by_user, (s.is_hidden OR t.is_hidden_by_user) AS effective_hidden
            FROM transactions t
            JOIN statement_imports s
              ON s.customer_id = t.customer_id AND s.id = t.statement_import_id
            WHERE t.customer_id = @customer_id AND t.statement_import_id = @statement_id
            ORDER BY t.source_row_number, t.id
            LIMIT @limit OFFSET @offset
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("statement_id", statementId);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (page - 1) * pageSize);
        var rows = new List<(Guid Id, int SourceRowNumber, int? SourcePage, DateOnly Date, string Description, long Cents, bool Hidden, bool EffectiveHidden)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetGuid(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.GetFieldValue<DateOnly>(3),
                    reader.GetString(4),
                    reader.GetInt64(5),
                    reader.GetBoolean(6),
                    reader.GetBoolean(7)));
            }
        }
        if (rows.Count == 0)
        {
            return [];
        }

        var tags = new Dictionary<Guid, List<ImportTagRecord>>();
        await using (var tagCommand = connection.CreateCommand())
        {
            tagCommand.Transaction = transaction;
            tagCommand.CommandText = """
                SELECT j.transaction_id, m.id, m.display_name
                FROM transaction_merchant_tags j
                JOIN merchant_tags m ON m.customer_id = j.customer_id AND m.id = j.merchant_tag_id
                WHERE j.customer_id = @customer_id AND j.transaction_id = ANY(@ids)
                ORDER BY m.display_name, m.id
                """;
            tagCommand.Parameters.AddWithValue("customer_id", customerId);
            tagCommand.Parameters.AddWithValue(
                "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, rows.Select(row => row.Id).ToArray());
            await using var reader = await tagCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!tags.TryGetValue(reader.GetGuid(0), out var list))
                {
                    list = [];
                    tags[reader.GetGuid(0)] = list;
                }
                list.Add(new ImportTagRecord(reader.GetGuid(1), reader.GetString(2)));
            }
        }

        return rows.Select(row => new ImportStatementRowRecord(
            row.Id,
            row.SourceRowNumber,
            row.SourcePage,
            row.Date,
            row.Description,
            row.Cents,
            row.Hidden,
            row.EffectiveHidden,
            tags.GetValueOrDefault(row.Id) ?? [])).ToArray();
    }

    private static async Task<IReadOnlyList<(Guid Id, string[] Tokens)>> LoadActiveTagsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, normalized_name
            FROM merchant_tags
            WHERE customer_id = @customer_id AND is_archived = FALSE
            ORDER BY normalized_name, id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        var tags = new List<(Guid Id, string[] Tokens)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tags.Add((
                reader.GetGuid(0),
                reader.GetString(1).Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }
        return tags;
    }

    private static IEnumerable<Guid> SuggestTags(
        string normalizedDescription,
        IReadOnlyList<(Guid Id, string[] Tokens)> tags)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescription))
        {
            yield break;
        }
        var descriptionTokens = normalizedDescription.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var (id, tagTokens) in tags)
        {
            if (tagTokens.Length == 0 || tagTokens.Length > descriptionTokens.Length)
            {
                continue;
            }
            var found = false;
            for (var start = 0; start <= descriptionTokens.Length - tagTokens.Length; start++)
            {
                var match = true;
                for (var offset = 0; offset < tagTokens.Length; offset++)
                {
                    if (!string.Equals(
                            descriptionTokens[start + offset], tagTokens[offset],
                            StringComparison.Ordinal))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    found = true;
                    break;
                }
            }
            if (found)
            {
                yield return id;
            }
        }
    }

    private static async Task<Guid?> FindSystemCategoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT id FROM categories WHERE customer_id = @customer_id AND is_system = TRUE";
        command.Parameters.AddWithValue("customer_id", customerId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
    }

    private static async Task<bool> TagsAvailableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid[] tagIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id FROM merchant_tags
            WHERE customer_id = @customer_id AND id = ANY(@ids) AND is_archived = FALSE
            ORDER BY id
            FOR UPDATE
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, tagIds);
        var available = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            available++;
        }
        return available == tagIds.Length;
    }

    private static async Task<IReadOnlyList<Guid>> FingerprintMatchesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid accountId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id FROM statement_imports
            WHERE customer_id = @customer_id AND financial_account_id = @account_id
              AND statement_fingerprint = @fingerprint
            ORDER BY created_at, id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }
        return ids;
    }

    private static async Task<int> NextFingerprintOccurrenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid accountId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(MAX(fingerprint_occurrence), 0) + 1
            FROM statement_imports
            WHERE customer_id = @customer_id AND financial_account_id = @account_id
              AND statement_fingerprint = @fingerprint
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<Dictionary<string, int>> MaxOccurrencesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
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
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT identity_key, MAX(occurrence_index)
            FROM transactions
            WHERE customer_id = @customer_id AND financial_account_id = @account_id
              AND identity_key = ANY(@keys)
            GROUP BY identity_key
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("keys", NpgsqlDbType.Array | NpgsqlDbType.Text, identityKeys);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = reader.GetInt32(1);
        }
        return result;
    }

    private static string ComputeIdentityKey(ImportReviewRowRecord row) =>
        StatementIdentity.ComputeIdentityKey(
            row.TransactionDate!.Value, row.AmountCents!.Value, row.NormalizedDescription);

    private static string BuildStatementLabel(ImportReviewSummaryRecord review) =>
        $"Statement {review.StartDate!.Value:yyyy-MM-dd} to {review.EndDate!.Value:yyyy-MM-dd}";

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string EncodeCursor(DateTime createdAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdAt:O}|{id}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (DateTime? CreatedAt, Guid? Id) DecodeCursor(string cursor)
    {
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var separator = text.IndexOf('|');
            if (separator <= 0 ||
                !DateTime.TryParse(text[..separator], CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var createdAt) ||
                !Guid.TryParse(text[(separator + 1)..], out var id))
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

    private sealed record RowCounts(
        int TotalRows,
        int Imported,
        int Suppressed,
        int Skipped,
        int PossibleDuplicates,
        int ExactOverrides);

    private sealed record DuplicateSnapshot(IReadOnlyList<ImportReviewRowRecord> Rows);

    private sealed record RowDecision(bool Skip, bool Override);

    private sealed record IncomingRow(
        int Index,
        Guid RowId,
        int SourceRowNumber,
        DateOnly Date,
        long Cents,
        string NormalizedDescription,
        string Description,
        string IdentityKey,
        string? Namespace,
        string? Value);

    private sealed record ExistingRow(
        Guid Id,
        string IdentityKey,
        int OccurrenceIndex,
        DateOnly Date,
        long Cents,
        string NormalizedDescription,
        string Description,
        string? Namespace,
        string? Value,
        Guid StatementImportId);

    private sealed record Candidate(
        string CandidateId,
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
        public string? WithinFileGroup { get; set; }
        public string WithinFileState { get; set; } = "none";
    }

    private sealed record ReceiptRecord(
        Guid Id,
        Guid CustomerId,
        Guid OperationId,
        Guid FinancialAccountId,
        string RequestDigest,
        string State,
        Guid? StatementImportId,
        string ResultJson,
        string? DeletionDigest,
        long? DeletionExpectedCount);
}
