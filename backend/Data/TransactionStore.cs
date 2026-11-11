using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using argus.Server.Data.Entities;
using argus.Server.Finance;
using argus.Server.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace argus.Server.Data;

/// <summary>
/// Owned transaction reads and atomic metadata mutations. Every statement is
/// scoped by the authenticated customer id; foreign and unknown identifiers
/// are indistinguishable.
/// </summary>
public sealed class TransactionStore(IDbContextFactory<ArgusDbContext> contextFactory, ITransactionFaultInjector faults)
{
    private static readonly JsonSerializerOptions _storedJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const int ManifestInsertBatchSize = 5_000;

    private readonly IDbContextFactory<ArgusDbContext> _contextFactory = contextFactory;
    private readonly ITransactionFaultInjector _faults = faults;

    // --- Queries -----------------------------------------------------------

    public async Task<TransactionPageResult> QueryAsync(Guid customerId, TransactionQueryPlan plan, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await SetReadOnlyAsync(context, cancellationToken);
        await ValidateFilterOwnershipAsync(context, customerId, plan.Filter, cancellationToken);

        (long visible, long hidden) = await TransactionQuery.CountsAsync(context, customerId, plan.Filter, cancellationToken);
        long inventory = await context.Transactions
            .LongCountAsync(item => item.CustomerId == customerId, cancellationToken);

        long offset = (long)(plan.Page - 1) * plan.PageSize;
        long matchingTotal = visible + hidden;
        long filteredTotal = plan.Filter.Visibility switch
        {
            TransactionVisibility.Visible => visible,
            TransactionVisibility.Hidden => hidden,
            _ => matchingTotal,
        };

        // Compare the 64-bit offset with the filtered total before EF's
        // 32-bit Skip. A page beyond the customer's inventory is empty; it
        // must never wrap to another page or fail on a negative value.
        List<RowData> rows = offset >= filteredTotal
            ? []
            : await ReadRowsAsync(context, customerId, plan.Filter, plan.SortBy, plan.SortDirection, plan.PageSize, offset, includeVisibility: true, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new TransactionPageResult(rows.Select(row => row.Response).ToArray(), inventory, visible, hidden, matchingTotal, filteredTotal, plan.Page, plan.PageSize, plan.SortBy, plan.SortDirection);
    }

    public async Task<TransactionDetailResult> GetAsync(Guid customerId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await SetReadOnlyAsync(context, cancellationToken);

        var rows = await ReadRowsByIdsAsync(context, customerId, [transactionId], cancellationToken);
        if (rows.Count == 0)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        var row = rows[0];
        var activeTags = await LoadActiveTagsAsync(context, customerId, cancellationToken);
        string? merchantKey = MerchantKey.FromDescription(row.NormalizedDescription);
        SuggestionData suggestionData = await SuggestionLoader.LoadAsync(
            context,
            customerId,
            merchantKey is null ? [] : [merchantKey],
            cancellationToken);
        SuggestionResult suggestion = MerchantSuggestions.Build(
            row.NormalizedDescription,
            activeTags.Select(tag => (tag.Id, tag.Tokens)).ToArray(),
            suggestionData);
        var assigned = row.TagIds.ToHashSet();
        var matches = suggestion.TagIds.Where(id => !assigned.Contains(id)).Take(TransactionLimits.MaxSuggestions + 1).ToArray();
        bool hasMore = matches.Length > TransactionLimits.MaxSuggestions;
        var suggested = matches.Take(TransactionLimits.MaxSuggestions).Select(id => activeTags.First(tag => tag.Id == id)).Select(tag => new TransactionTagRef(tag.Id, tag.DisplayName, false)).ToArray();
        TransactionCategoryRef? suggestedCategory = suggestion.CategoryId is Guid categoryId
            ? await ReadCategoryRefAsync(context, customerId, categoryId, cancellationToken)
            : null;

        await transaction.CommitAsync(cancellationToken);

        return new TransactionDetailResult(row.Response, suggested, suggestedCategory, hasMore, row.SourcePage, row.SourceRowNumber);
    }

    // --- Single metadata edits ---------------------------------------------

    public async Task<TransactionMutationResult> PatchAsync(Guid customerId,
                                                            Guid transactionId,
                                                            int expectedVersion,
                                                            TransactionAction action,
                                                            CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginWorkspaceAsync(context, IsolationLevel.ReadCommitted, cancellationToken);

        var accountId = await TransactionAccountIdAsync(context, customerId, transactionId, cancellationToken);
        if (accountId is null)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        if (!await LockAccountRowAsync(context, customerId, accountId.Value, cancellationToken))
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        await ValidateActionTargetsAsync(context, customerId, action, lockForShare: true, cancellationToken);

        if (!await LockTransactionRowAsync(context, customerId, transactionId, cancellationToken))
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        var member = await ReadMemberByIdAsync(context, customerId, transactionId, cancellationToken) ?? throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        if (member.Version != expectedVersion)
        {
            throw new TransactionStoreException(TransactionErrorCodes.VersionConflict);
        }

        await EnsureRestoreAllowedAsync(context, customerId, [member], action, cancellationToken);
        ValidateTagLimit(member, action);
        bool changed = IsChanged(member, action);

        if (changed)
        {
            await UpdateMembersAsync(context, customerId, [member.Id], action, cancellationToken);
            _faults.OnStep(TransactionFaultPoints.AfterTransactionUpdate);
            if (action.AddTagIds.Length > 0 || action.RemoveTagIds.Length > 0)
            {
                await MutateTagLinksAsync(context, customerId, [member.Id], action, cancellationToken);
                _faults.OnStep(TransactionFaultPoints.AfterTagLinkMutation);
            }
        }

        var rows = await ReadRowsByIdsAsync(context, customerId, [transactionId], cancellationToken);
        if (rows.Count == 0)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }
        await transaction.CommitAsync(cancellationToken);

        return new TransactionMutationResult(rows[0].Response, 1, changed ? 1 : 0, changed ? 0 : 1);
    }

    // --- Bulk preparation --------------------------------------------------

    public async Task<TransactionOperationPrepareResponse> PrepareAsync(Guid customerId,
                                                                        Guid operationId,
                                                                        TransactionSelectionPlan selection,
                                                                        TransactionAction action,
                                                                        CancellationToken cancellationToken = default)
    {
        string requestDigest = RequestDigest(selection, action);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await PrepareOnceAsync(customerId, operationId, selection, action, requestDigest, cancellationToken);
            }
            catch (Exception exception) when (
                PersistenceErrors.FindPostgresException(exception) is { } postgres &&
                postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
            {
                if (attempt == 2)
                {
                    throw new TransactionStoreException(TransactionErrorCodes.OperationBusy);
                }
            }
            catch (Exception exception) when (
                PersistenceErrors.FindPostgresException(exception) is { } postgres &&
                postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
                string.Equals(postgres.ConstraintName, "transaction_operations_pkey", StringComparison.Ordinal))
            {
                var raced = await ReadExistingOperationAsync(customerId, operationId, requestDigest, cancellationToken);
                if (raced is not null)
                {
                    return raced;
                }

                throw new TransactionStoreException(TransactionErrorCodes.IdempotencyConflict);
            }
        }

        throw new TransactionStoreException(TransactionErrorCodes.OperationBusy);
    }

    private async Task<TransactionOperationPrepareResponse> PrepareOnceAsync(Guid customerId,
                                                                             Guid operationId,
                                                                             TransactionSelectionPlan selection,
                                                                             TransactionAction action,
                                                                             string requestDigest,
                                                                             CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginWorkspaceAsync(context, IsolationLevel.Serializable, cancellationToken);

        await LockCustomerAsync(context, customerId, cancellationToken);

        var existing = await ReadHeaderAsync(context, customerId, operationId, forUpdate: false, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestDigest, requestDigest, StringComparison.Ordinal))
            {
                throw new TransactionStoreException(TransactionErrorCodes.IdempotencyConflict);
            }

            if (existing.Status == "Prepared")
            {
                if (await IsExpiredAsync(context, existing.ExpiresAt, cancellationToken))
                {
                    await ExpireHeaderAsync(context, customerId, operationId, cancellationToken);
                    var expired = await ReadHeaderAsync(context, customerId, operationId, forUpdate: false, cancellationToken) ?? throw new TransactionStoreException(TransactionErrorCodes.NotFound);
                    await transaction.CommitAsync(cancellationToken);
                    return new TransactionOperationPrepareResponse(TerminalSummary(expired), false);
                }

                var replay = await BuildPreparedSummaryAsync(context, existing, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new TransactionOperationPrepareResponse(replay, false);
            }

            await transaction.CommitAsync(cancellationToken);
            return new TransactionOperationPrepareResponse(TerminalSummary(existing), false);
        }

        var targets = await ValidateActionTargetsAsync(context, customerId, action, lockForShare: false, cancellationToken);

        List<MemberRow> members;
        string? filterDigest = null;
        if (selection.Mode == "explicit")
        {
            var ids = selection.ExplicitMembers.Select(member => member.Id).ToArray();
            members = await ReadMembersAsync(context, customerId, ids, cancellationToken);
            if (members.Count != ids.Length)
            {
                throw new TransactionStoreException(TransactionErrorCodes.NotFound);
            }

            var versions = selection.ExplicitMembers.ToDictionary(member => member.Id, member => member.ExpectedVersion);
            if (members.Any(member => member.Version != versions[member.Id]))
            {
                throw new TransactionStoreException(TransactionErrorCodes.SelectionStale);
            }

            members = members.OrderBy(member => member.Id).ToList();
        }
        else
        {
            await ValidateFilterOwnershipAsync(context, customerId, selection.Filters!, cancellationToken);
            filterDigest = Sha256(selection.Filters!.Canonical());
            members = await CaptureMatchingMembersAsync(context, customerId, selection.Filters!, TransactionLimits.MaxAllMatching + 1, cancellationToken);
            if (members.Count > TransactionLimits.MaxAllMatching)
            {
                throw new TransactionStoreException(TransactionErrorCodes.SelectionTooLarge);
            }
        }

        if (members.Count == 0)
        {
            throw new TransactionStoreException(TransactionErrorCodes.EmptySelection);
        }

        await EnsureRestoreAllowedAsync(context, customerId, members, action, cancellationToken);
        foreach (var member in members)
        {
            ValidateTagLimit(member, action);
        }

        int changed = members.Count(member => IsChanged(member, action));
        int unchanged = members.Count - changed;

        await CheckQuotaAsync(context, customerId, members.Count, cancellationToken);

        var (capturedAt, expiresAt) = await ReadOperationClockAsync(context, cancellationToken);
        string membersDigest = MembersDigest(members.Select(member => (member.Id, member.Version)));
        string targetCanonical = TargetsCanonical(targets.Stored);
        string confirmationHash = ConfirmationHash(customerId, operationId, selection.Mode, membersDigest, action, targetCanonical, capturedAt, expiresAt, members.Count, changed, unchanged);

        await InsertHeaderAsync(context, customerId, operationId, selection.Mode, requestDigest, filterDigest, confirmationHash, action, targets.Stored, members.Count, changed, unchanged, capturedAt, expiresAt, cancellationToken);
        await InsertManifestAsync(context, customerId, operationId, members, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var summary = new TransactionOperationSummaryResponse(
            operationId,
            "Prepared",
            selection.Mode,
            FormatTimestamp(capturedAt),
            FormatTimestamp(expiresAt),
            members.Count,
            changed,
            unchanged,
            confirmationHash,
            null,
            null,
            new TransactionOperationActionResponse(
                targets.Category,
                targets.AddTags,
                targets.RemoveTags,
                action.IsHiddenByUser,
                action.ConfirmCategory,
                false));

        return new TransactionOperationPrepareResponse(summary, true);
    }

    private async Task<TransactionOperationPrepareResponse?> ReadExistingOperationAsync(Guid customerId,
                                                                                         Guid operationId,
                                                                                         string requestDigest,
                                                                                         CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginWorkspaceAsync(context, IsolationLevel.ReadCommitted, cancellationToken);
        var header = await ReadHeaderAsync(context, customerId, operationId, forUpdate: true, cancellationToken);
        if (header is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        if (!string.Equals(header.RequestDigest, requestDigest, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (header.Status == "Prepared" && await IsExpiredAsync(context, header.ExpiresAt, cancellationToken))
        {
            await ExpireHeaderAsync(context, customerId, operationId, cancellationToken);
            header = await ReadHeaderAsync(context, customerId, operationId, forUpdate: false, cancellationToken) ?? throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        TransactionOperationSummaryResponse summary;
        if (header.Status == "Prepared")
        {
            summary = await BuildPreparedSummaryAsync(context, header, cancellationToken);
        }
        else
        {
            summary = TerminalSummary(header);
        }
        await transaction.CommitAsync(cancellationToken);

        return new TransactionOperationPrepareResponse(summary, false);
    }

    // --- Status, apply and abandon -----------------------------------------

    public async Task<TransactionOperationSummaryResponse> StatusAsync(Guid customerId, Guid operationId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginWorkspaceAsync(context, IsolationLevel.ReadCommitted, cancellationToken);

        var header = await ReadHeaderAsync(context, customerId, operationId, forUpdate: true, cancellationToken);
        if (header is null)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        if (header.Status == "Prepared" && await IsExpiredAsync(context, header.ExpiresAt, cancellationToken))
        {
            await ExpireHeaderAsync(context, customerId, operationId, cancellationToken);
            header = await ReadHeaderAsync(context, customerId, operationId, forUpdate: true, cancellationToken) ?? throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        var summary = header.Status == "Prepared" ? await BuildPreparedSummaryAsync(context, header, cancellationToken) : TerminalSummary(header);
        await transaction.CommitAsync(cancellationToken);

        return summary;
    }

    public async Task<TransactionOperationApplyResponse> ApplyAsync(Guid customerId,
                                                                    Guid operationId,
                                                                    string confirmationHash,
                                                                    int expectedSelectedCount,
                                                                    CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginWorkspaceAsync(context, IsolationLevel.ReadCommitted, cancellationToken);

        var header = await ReadHeaderAsync(context, customerId, operationId, forUpdate: false, cancellationToken);
        if (header is null)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        if (header.Status == "Applied")
        {
            await transaction.CommitAsync(cancellationToken);
            return AppliedResponse(header, confirmationHash, expectedSelectedCount, replayed: true);
        }
        if (header.Status != "Prepared")
        {
            throw TerminalState(header.Status);
        }
        var manifest = await ReadManifestAsync(context, customerId, operationId, cancellationToken);
        var accounts = manifest.Select(item => item.AccountId).Distinct().Order().ToArray();
        foreach (var accountId in accounts)
        {
            if (!await LockAccountRowAsync(context, customerId, accountId, cancellationToken))
            {
                throw new TransactionStoreException(TransactionErrorCodes.SelectionStale, new Dictionary<string, object?> { ["stale_count"] = 0, ["missing_count"] = manifest.Count, });
            }
        }

        header = await ReadHeaderAsync(context, customerId, operationId, forUpdate: true, cancellationToken);
        if (header is null)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }
        if (header.Status == "Applied")
        {
            await transaction.CommitAsync(cancellationToken);
            return AppliedResponse(header, confirmationHash, expectedSelectedCount, replayed: true);
        }
        if (header.Status != "Prepared")
        {
            throw TerminalState(header.Status);
        }
        if (await IsExpiredAsync(context, header.ExpiresAt, cancellationToken))
        {
            await ExpireHeaderAsync(context, customerId, operationId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new TransactionStoreException(TransactionErrorCodes.OperationExpired);
        }

        if (!string.Equals(header.ConfirmationHash, confirmationHash, StringComparison.Ordinal) || header.SelectedCount != expectedSelectedCount)
        {
            throw new TransactionStoreException(TransactionErrorCodes.IdempotencyConflict);
        }

        var action = DeserializeAction(header.ActionJson);
        var storedTargets = DeserializeTargets(header.TargetJson);
        string expectedHash = ConfirmationHash(
            customerId,
            operationId,
            header.SelectionMode,
            MembersDigest(manifest.Select(item => (item.TransactionId, item.Version))),
            action,
            TargetsCanonical(storedTargets),
            header.CapturedAt,
            header.ExpiresAt,
            header.SelectedCount,
            header.ChangedCount,
            header.UnchangedCount);
        if (!string.Equals(expectedHash, confirmationHash, StringComparison.Ordinal))
        {
            throw new TransactionStoreException(TransactionErrorCodes.IdempotencyConflict);
        }

        await LockAndValidateTargetsAsync(context, customerId, action, storedTargets, cancellationToken);

        var memberIds = manifest.Select(item => item.TransactionId).Order().ToArray();
        await LockTransactionRowsAsync(context, customerId, memberIds, cancellationToken);
        var members = await ReadMembersAsync(context, customerId, memberIds, cancellationToken);
        var byId = members.ToDictionary(member => member.Id);
        int missing = manifest.Count(item => !byId.ContainsKey(item.TransactionId));
        int stale = manifest.Count(item => byId.TryGetValue(item.TransactionId, out var member) && member.Version != item.Version);
        if (missing > 0 || stale > 0 || members.Count != header.SelectedCount)
        {
            throw new TransactionStoreException(TransactionErrorCodes.SelectionStale, new Dictionary<string, object?> { ["stale_count"] = stale, ["missing_count"] = missing, });
        }

        await EnsureRestoreAllowedAsync(context, customerId, members, action, cancellationToken);
        foreach (var member in members)
        {
            ValidateTagLimit(member, action);
        }

        var changedMembers = members.Where(member => IsChanged(member, action)).ToArray();
        if (changedMembers.Length != header.ChangedCount || members.Count - changedMembers.Length != header.UnchangedCount)
        {
            throw new TransactionStoreException(TransactionErrorCodes.SelectionStale, new Dictionary<string, object?> { ["stale_count"] = members.Count, ["missing_count"] = 0, });
        }

        // Account and header checks above precede the classification and
        // transaction locks. A confirmation can expire during either later
        // wait, so use the database wall clock once more before any effects.
        if (await IsExpiredAsync(context, header.ExpiresAt, cancellationToken))
        {
            await ExpireHeaderAsync(context, customerId, operationId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new TransactionStoreException(TransactionErrorCodes.OperationExpired);
        }

        if (changedMembers.Length > 0)
        {
            await UpdateMembersAsync(context, customerId, changedMembers.Select(member => member.Id).ToArray(), action, cancellationToken);
            _faults.OnStep(TransactionFaultPoints.AfterTransactionUpdate);
        }

        if (action.AddTagIds.Length > 0 || action.RemoveTagIds.Length > 0)
        {
            await MutateTagLinksAsync(context, customerId, memberIds, action, cancellationToken);
            _faults.OnStep(TransactionFaultPoints.AfterTagLinkMutation);
        }

        _faults.OnStep(TransactionFaultPoints.BeforeReceiptWrite);
        var appliedAt = await MarkAppliedAsync(context, customerId, operationId, cancellationToken);
        _faults.OnStep(TransactionFaultPoints.AfterReceiptWrite);
        await transaction.CommitAsync(cancellationToken);
        _faults.OnStep(TransactionFaultPoints.AfterCommit);

        return new TransactionOperationApplyResponse(operationId, "Applied", header.SelectedCount, header.ChangedCount, header.UnchangedCount, FormatTimestamp(appliedAt), false);
    }

    public async Task<TransactionOperationApplyResponse> AbandonAsync(Guid customerId, Guid operationId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginWorkspaceAsync(context, IsolationLevel.ReadCommitted, cancellationToken);

        var header = await ReadHeaderAsync(context, customerId, operationId, forUpdate: true, cancellationToken);
        if (header is null)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        if (header.Status == "Applied")
        {
            await transaction.CommitAsync(cancellationToken);
            return new TransactionOperationApplyResponse(operationId, header.Status, header.SelectedCount, header.ChangedCount, header.UnchangedCount, header.AppliedAt is null ? null : FormatTimestamp(header.AppliedAt.Value), false);
        }
        if (header.Status == "Abandoned" || header.Status == "Expired")
        {
            await transaction.CommitAsync(cancellationToken);
            return new TransactionOperationApplyResponse(operationId, header.Status, header.SelectedCount, header.ChangedCount, header.UnchangedCount, null, false);
        }

        var abandonedAt = await ClockAsync(context, cancellationToken);
        await context.TransactionOperationItems
            .Where(item => item.CustomerId == customerId && item.OperationId == operationId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE transaction_operations
            SET status = 'Abandoned',
                terminal_at = {{abandonedAt}},
                action = '{}'::jsonb,
                target_stamps = '{}'::jsonb,
                updated_at = {{abandonedAt}}
            WHERE customer_id = {{customerId}} AND operation_id = {{operationId}}
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new TransactionOperationApplyResponse(operationId, "Abandoned", header.SelectedCount, header.ChangedCount, header.UnchangedCount, null, false);
    }

    // --- Cleanup -----------------------------------------------------------

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        List<ExpiredOperationRow> expiredRows = await context.Database.SqlQueryRaw<ExpiredOperationRow>("""
            SELECT customer_id AS "CustomerId", operation_id AS "OperationId"
            FROM transaction_operations
            WHERE status = 'Prepared' AND expires_at <= clock_timestamp()
            ORDER BY expires_at
            LIMIT 1000
            FOR UPDATE
            """).ToListAsync(cancellationToken);

        if (expiredRows.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        // These arrays are parallel: an operation UUID is only unique within
        // its customer namespace. Never turn the selected pairs into a
        // Cartesian product of independently matched IDs.
        var customerIds = expiredRows.Select(item => item.CustomerId).ToArray();
        var operationIds = expiredRows.Select(item => item.OperationId).ToArray();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM transaction_operation_items i
            USING unnest({customerIds}, {operationIds}) AS expired(customer_id, operation_id)
            WHERE i.customer_id = expired.customer_id
              AND i.operation_id = expired.operation_id
            """, cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE transaction_operations o
            SET status = 'Expired',
                terminal_at = clock_timestamp(),
                action = '{}'::jsonb,
                target_stamps = '{}'::jsonb,
                updated_at = clock_timestamp()
            FROM unnest({{customerIds}}, {{operationIds}}) AS expired(customer_id, operation_id)
            WHERE o.customer_id = expired.customer_id
              AND o.operation_id = expired.operation_id
              AND o.status = 'Prepared'
              AND o.expires_at <= clock_timestamp()
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    // --- Mutation helpers --------------------------------------------------

    private sealed record MemberRow(Guid Id,
                                    int Version,
                                    Guid AccountId,
                                    Guid StatementId,
                                    Guid CategoryId,
                                    bool CategoryConfirmed,
                                    bool HiddenByUser,
                                    bool StatementHidden,
                                    Guid[] TagIds);

    private sealed record ManifestRow(Guid TransactionId,
                                      int Version,
                                      Guid AccountId,
                                      Guid StatementId);

    private sealed record ActionTargets(StoredTargets Stored,
                                        TransactionCategoryRef? Category,
                                        TransactionTagRef[] AddTags,
                                        TransactionTagRef[] RemoveTags);

    private sealed record StoredAction(Guid? CategoryId,
                                       Guid[] AddTagIds,
                                       Guid[] RemoveTagIds,
                                       bool? IsHiddenByUser,
                                       bool? ConfirmCategory);

    private sealed record StoredCategoryStamp(Guid Id,
                                              string DisplayName,
                                              string Kind,
                                              bool IsArchived,
                                              string UpdatedAt);

    private sealed record StoredTagStamp(Guid Id,
                                         string DisplayName,
                                         bool IsArchived,
                                         string UpdatedAt);

    private sealed record StoredTargets(StoredCategoryStamp? Category, StoredTagStamp[] AddTags, StoredTagStamp[] RemoveTags);

    private sealed record OperationHeader(Guid CustomerId,
                                          Guid OperationId,
                                          string Status,
                                          string SelectionMode,
                                          string RequestDigest,
                                          string? FilterDigest,
                                          string? ConfirmationHash,
                                          string ActionJson,
                                          string TargetJson,
                                          int SelectedCount,
                                          int ChangedCount,
                                          int UnchangedCount,
                                          DateTime CapturedAt,
                                          DateTime ExpiresAt,
                                          DateTime? AppliedAt,
                                          DateTime? TerminalAt);

    private sealed class ExpiredOperationRow
    {
        public Guid CustomerId { get; set; }
        public Guid OperationId { get; set; }
    }

    private sealed class OperationClockRow
    {
        public DateTime CapturedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    private sealed class OperationHeaderRow
    {
        public Guid CustomerId { get; set; }
        public Guid OperationId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string SelectionMode { get; set; } = string.Empty;
        public string RequestDigest { get; set; } = string.Empty;
        public string? FilterDigest { get; set; }
        public string? ConfirmationHash { get; set; }
        public string ActionJson { get; set; } = "{}";
        public string TargetJson { get; set; } = "{}";
        public int SelectedCount { get; set; }
        public int ChangedCount { get; set; }
        public int UnchangedCount { get; set; }
        public DateTime CapturedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? AppliedAt { get; set; }
        public DateTime? TerminalAt { get; set; }
    }

    private sealed class CategoryStampRow
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public bool IsSystem { get; set; }
        public bool IsArchived { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class TagStampRow
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsArchived { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private static async Task<IDbContextTransaction> BeginWorkspaceAsync(ArgusDbContext context, IsolationLevel isolation, CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(isolation, cancellationToken);
        string settings = $"SET LOCAL lock_timeout = '{TransactionLimits.LockTimeoutSeconds}s'; SET LOCAL statement_timeout = '{TransactionLimits.StatementTimeoutSeconds}s';";
        await context.Database.ExecuteSqlRawAsync(settings, cancellationToken);

        return transaction;
    }

    private static async Task LockCustomerAsync(ArgusDbContext context, Guid customerId, CancellationToken cancellationToken)
    {
        List<int> found = await context.Database.SqlQuery<int>($"""
            SELECT 1 AS "Value" FROM customers WHERE id = {customerId} FOR UPDATE
            """).ToListAsync(cancellationToken);
        if (found.Count == 0)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }
    }

    private static async Task<Guid?> TransactionAccountIdAsync(ArgusDbContext context,
                                                               Guid customerId,
                                                               Guid transactionId,
                                                               CancellationToken cancellationToken)
    {
        return await context.Transactions
            .Where(item => item.CustomerId == customerId && item.Id == transactionId)
            .Select(item => (Guid?)item.FinancialAccountId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<bool> LockAccountRowAsync(ArgusDbContext context,
                                                        Guid customerId,
                                                        Guid accountId,
                                                        CancellationToken cancellationToken)
    {
        List<int> found = await context.Database.SqlQuery<int>($"""
            SELECT 1 AS "Value" FROM financial_accounts
            WHERE customer_id = {customerId} AND id = {accountId}
            FOR UPDATE
            """).ToListAsync(cancellationToken);

        return found.Count > 0;
    }

    private static async Task<bool> LockTransactionRowAsync(ArgusDbContext context,
                                                            Guid customerId,
                                                            Guid transactionId,
                                                            CancellationToken cancellationToken)
    {
        List<int> found = await context.Database.SqlQuery<int>($"""
            SELECT 1 AS "Value" FROM transactions
            WHERE customer_id = {customerId} AND id = {transactionId}
            FOR UPDATE
            """).ToListAsync(cancellationToken);

        return found.Count > 0;
    }

    private static async Task LockTransactionRowsAsync(ArgusDbContext context,
                                                       Guid customerId,
                                                       Guid[] transactionIds,
                                                       CancellationToken cancellationToken)
    {
        List<Guid> found = await context.Database.SqlQuery<Guid>($"""
            SELECT id FROM transactions
            WHERE customer_id = {customerId} AND id = ANY({transactionIds})
            ORDER BY id
            FOR UPDATE
            """).ToListAsync(cancellationToken);
        if (found.Count != transactionIds.Length)
        {
            throw new TransactionStoreException(TransactionErrorCodes.SelectionStale, new Dictionary<string, object?> { ["stale_count"] = 0, ["missing_count"] = transactionIds.Length - found.Count, });
        }
    }

    private async Task<MemberRow?> ReadMemberByIdAsync(ArgusDbContext context,
                                                       Guid customerId,
                                                       Guid transactionId,
                                                       CancellationToken cancellationToken)
    {
        var members = await ReadMembersAsync(context, customerId, [transactionId], cancellationToken);

        return members.Count == 0 ? null : members[0];
    }

    private async Task<List<MemberRow>> ReadMembersAsync(ArgusDbContext context,
                                                         Guid customerId,
                                                         Guid[] transactionIds,
                                                         CancellationToken cancellationToken)
    {
        var rows = await (
            from transaction in context.Transactions.AsNoTracking()
            join statement in context.StatementImports
                on new { transaction.CustomerId, Id = transaction.StatementImportId } equals new { statement.CustomerId, Id = statement.Id }
            where transaction.CustomerId == customerId && transactionIds.Contains(transaction.Id)
            orderby transaction.Id
            select new
            {
                transaction.Id,
                transaction.Version,
                transaction.FinancialAccountId,
                transaction.StatementImportId,
                transaction.CategoryId,
                transaction.CategoryConfirmed,
                transaction.IsHiddenByUser,
                StatementHidden = statement.IsHidden,
            }).ToListAsync(cancellationToken);

        var tags = await LoadMemberTagIdsAsync(context, customerId, transactionIds, cancellationToken);

        return rows.Select(row => new MemberRow(
            row.Id,
            row.Version,
            row.FinancialAccountId,
            row.StatementImportId,
            row.CategoryId,
            row.CategoryConfirmed,
            row.IsHiddenByUser,
            row.StatementHidden,
            tags.GetValueOrDefault(row.Id) ?? [])).ToList();
    }

    private async Task<List<MemberRow>> CaptureMatchingMembersAsync(ArgusDbContext context,
                                                                    Guid customerId,
                                                                    TransactionFilter filter,
                                                                    int limit,
                                                                    CancellationToken cancellationToken)
    {
        var rows = await (
            from transaction in TransactionQuery.Apply(context, customerId, filter, includeVisibility: true)
            join statement in context.StatementImports
                on new { transaction.CustomerId, Id = transaction.StatementImportId } equals new { statement.CustomerId, Id = statement.Id }
            orderby transaction.Id
            select new
            {
                transaction.Id,
                transaction.Version,
                transaction.FinancialAccountId,
                transaction.StatementImportId,
                transaction.CategoryId,
                transaction.CategoryConfirmed,
                transaction.IsHiddenByUser,
                StatementHidden = statement.IsHidden,
            })
            .Take(limit)
            .ToListAsync(cancellationToken);

        Guid[] ids = rows.Select(row => row.Id).ToArray();
        var tags = await LoadMemberTagIdsAsync(context, customerId, ids, cancellationToken);

        return rows.Select(row => new MemberRow(
            row.Id,
            row.Version,
            row.FinancialAccountId,
            row.StatementImportId,
            row.CategoryId,
            row.CategoryConfirmed,
            row.IsHiddenByUser,
            row.StatementHidden,
            tags.GetValueOrDefault(row.Id) ?? [])).ToList();
    }

    private static async Task<Dictionary<Guid, Guid[]>> LoadMemberTagIdsAsync(ArgusDbContext context,
                                                                              Guid customerId,
                                                                              IReadOnlyCollection<Guid> transactionIds,
                                                                              CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Guid[]>();
        if (transactionIds.Count == 0)
        {
            return result;
        }

        var rows = await context.TransactionMerchantTags
            .AsNoTracking()
            .Where(link => link.CustomerId == customerId && transactionIds.Contains(link.TransactionId))
            .OrderBy(link => link.TransactionId)
            .ThenBy(link => link.MerchantTagId)
            .Select(link => new { link.TransactionId, link.MerchantTagId })
            .ToListAsync(cancellationToken);
        var collected = new Dictionary<Guid, List<Guid>>();
        foreach (var row in rows)
        {
            if (!collected.TryGetValue(row.TransactionId, out List<Guid>? list))
            {
                list = [];
                collected[row.TransactionId] = list;
            }
            list.Add(row.MerchantTagId);
        }

        foreach (var (id, list) in collected)
        {
            result[id] = list.ToArray();
        }

        return result;
    }

    private static async Task<ActionTargets> ValidateActionTargetsAsync(ArgusDbContext context,
                                                                        Guid customerId,
                                                                        TransactionAction action,
                                                                        bool lockForShare,
                                                                        CancellationToken cancellationToken)
    {
        StoredCategoryStamp? storedCategory = null;
        TransactionCategoryRef? categoryRef = null;
        if (action.CategoryId is not null)
        {
            const string categorySql = """
                SELECT id AS "Id", display_name AS "DisplayName", kind AS "Kind",
                       is_system AS "IsSystem", is_archived AS "IsArchived", updated_at AS "UpdatedAt"
                FROM categories
                WHERE customer_id = {0} AND id = {1}
                """;
            string sql = lockForShare ? string.Concat(categorySql, "\nFOR SHARE") : categorySql;
            List<CategoryStampRow> rows = await context.Database
                .SqlQueryRaw<CategoryStampRow>(sql, customerId, action.CategoryId.Value)
                .ToListAsync(cancellationToken);
            CategoryStampRow? row = rows.SingleOrDefault();
            if (row is null)
            {
                throw new TransactionStoreException(TransactionErrorCodes.NotFound);
            }
            if (row.IsArchived)
            {
                throw new TransactionStoreException(TransactionErrorCodes.CategoryUnavailable);
            }
            storedCategory = new StoredCategoryStamp(row.Id, row.DisplayName, row.Kind, row.IsArchived, FormatStamp(row.UpdatedAt));
            categoryRef = new TransactionCategoryRef(storedCategory.Id, storedCategory.DisplayName, storedCategory.Kind, row.IsSystem, storedCategory.IsArchived);
        }

        var ids = action.AddTagIds.Concat(action.RemoveTagIds).Distinct().Order().ToArray();
        var addIds = action.AddTagIds.ToHashSet();
        var tagsById = new Dictionary<Guid, StoredTagStamp>();
        if (ids.Length > 0)
        {
            const string tagSql = """
                SELECT id AS "Id", display_name AS "DisplayName", is_archived AS "IsArchived",
                       updated_at AS "UpdatedAt"
                FROM merchant_tags
                WHERE customer_id = {0} AND id = ANY({1})
                ORDER BY id
                """;
            string sql = lockForShare ? string.Concat(tagSql, "\nFOR SHARE") : tagSql;
            List<TagStampRow> rows = await context.Database
                .SqlQueryRaw<TagStampRow>(sql, customerId, ids)
                .ToListAsync(cancellationToken);
            foreach (TagStampRow row in rows)
            {
                var stamp = new StoredTagStamp(row.Id, row.DisplayName, row.IsArchived, FormatStamp(row.UpdatedAt));
                if (addIds.Contains(stamp.Id) && stamp.IsArchived)
                {
                    throw new TransactionStoreException(TransactionErrorCodes.TagUnavailable);
                }
                tagsById[stamp.Id] = stamp;
            }
        }

        if (tagsById.Count != ids.Length)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        TransactionTagRef[] RefsFor(Guid[] tagIds) => tagIds
            .Select(id => tagsById[id])
            .Select(tag => new TransactionTagRef(tag.Id, tag.DisplayName, tag.IsArchived))
            .ToArray();

        var stored = new StoredTargets(storedCategory, action.AddTagIds.Select(id => tagsById[id]).ToArray(), action.RemoveTagIds.Select(id => tagsById[id]).ToArray());

        return new ActionTargets(stored, categoryRef, RefsFor(action.AddTagIds), RefsFor(action.RemoveTagIds));
    }

    private static async Task LockAndValidateTargetsAsync(ArgusDbContext context,
                                                          Guid customerId,
                                                          TransactionAction action,
                                                          StoredTargets stored,
                                                          CancellationToken cancellationToken)
    {
        if (action.CategoryId is not null)
        {
            List<CategoryStampRow> rows = await context.Database.SqlQueryRaw<CategoryStampRow>("""
                SELECT id AS "Id", display_name AS "DisplayName", kind AS "Kind",
                       is_system AS "IsSystem", is_archived AS "IsArchived", updated_at AS "UpdatedAt"
                FROM categories
                WHERE customer_id = {0} AND id = {1}
                FOR SHARE
                """, customerId, action.CategoryId.Value).ToListAsync(cancellationToken);
            CategoryStampRow? row = rows.SingleOrDefault();
            if (row is null || stored.Category is null)
            {
                throw new TransactionStoreException(TransactionErrorCodes.ClassificationChanged);
            }
            var current = new StoredCategoryStamp(row.Id, row.DisplayName, row.Kind, row.IsArchived, FormatStamp(row.UpdatedAt));
            if (current != stored.Category)
            {
                throw new TransactionStoreException(TransactionErrorCodes.ClassificationChanged);
            }
        }

        var allTagIds = action.AddTagIds.Concat(action.RemoveTagIds).Distinct().Order().ToArray();
        if (allTagIds.Length > 0)
        {
            List<TagStampRow> rows = await context.Database.SqlQueryRaw<TagStampRow>("""
                SELECT id AS "Id", display_name AS "DisplayName", is_archived AS "IsArchived",
                       updated_at AS "UpdatedAt"
                FROM merchant_tags
                WHERE customer_id = {0} AND id = ANY({1})
                ORDER BY id
                FOR SHARE
                """, customerId, allTagIds).ToListAsync(cancellationToken);
            var current = new Dictionary<Guid, StoredTagStamp>();
            foreach (TagStampRow row in rows)
            {
                var stamp = new StoredTagStamp(row.Id, row.DisplayName, row.IsArchived, FormatStamp(row.UpdatedAt));
                current[stamp.Id] = stamp;
            }

            var expected = new Dictionary<Guid, StoredTagStamp>();
            foreach (var tag in stored.AddTags)
            {
                expected[tag.Id] = tag;
            }
            foreach (var tag in stored.RemoveTags)
            {
                expected[tag.Id] = tag;
            }
            if (expected.Count != current.Count || expected.Any(pair => !current.TryGetValue(pair.Key, out var value) || value != pair.Value))
            {
                throw new TransactionStoreException(TransactionErrorCodes.ClassificationChanged);
            }
        }
    }

    private static async Task<(DateTime CapturedAt, DateTime ExpiresAt)> ReadOperationClockAsync(ArgusDbContext context, CancellationToken cancellationToken)
    {
        List<OperationClockRow> rows = await context.Database.SqlQueryRaw<OperationClockRow>("""
            SELECT clock_timestamp() AS "CapturedAt",
                   clock_timestamp() + make_interval(mins => {0}) AS "ExpiresAt"
            """, TransactionLimits.OperationExpiryMinutes).ToListAsync(cancellationToken);
        OperationClockRow row = rows.Single();

        return (row.CapturedAt, row.ExpiresAt);
    }

    private static async Task<DateTime> ClockAsync(ArgusDbContext context, CancellationToken cancellationToken)
    {
        return await context.Database
            .SqlQuery<DateTime>($"""
                SELECT clock_timestamp() AS "Value"
                """)
            .SingleAsync(cancellationToken);
    }

    private static async Task<bool> IsExpiredAsync(ArgusDbContext context,
                                                   DateTime expiresAt,
                                                   CancellationToken cancellationToken)
    {
        return await context.Database
            .SqlQuery<bool>($"""
                SELECT clock_timestamp() >= {expiresAt} AS "Value"
                """)
            .SingleAsync(cancellationToken);
    }

    private static async Task CheckQuotaAsync(ArgusDbContext context,
                                              Guid customerId,
                                              int selectionCount,
                                              CancellationToken cancellationToken)
    {
        long openOperations = await context.Database.SqlQuery<long>($"""
            SELECT COUNT(*) AS "Value"
            FROM transaction_operations
            WHERE customer_id = {customerId}
              AND status = 'Prepared'
              AND expires_at > clock_timestamp()
            """).SingleAsync(cancellationToken);

        long liveEntries = await context.Database.SqlQuery<long>($"""
            SELECT COUNT(*) AS "Value"
            FROM transaction_operation_items i
            JOIN transaction_operations o
              ON o.customer_id = i.customer_id AND o.operation_id = i.operation_id
            WHERE o.customer_id = {customerId}
              AND o.status = 'Prepared'
              AND o.expires_at > clock_timestamp()
            """).SingleAsync(cancellationToken);

        if (openOperations >= TransactionLimits.MaxOpenOperations || liveEntries + selectionCount > TransactionLimits.MaxLiveManifestEntries)
        {
            throw new TransactionStoreException(TransactionErrorCodes.OperationQuota);
        }
    }

    private static async Task InsertHeaderAsync(ArgusDbContext context,
                                                Guid customerId,
                                                Guid operationId,
                                                string selectionMode,
                                                string requestDigest,
                                                string? filterDigest,
                                                string confirmationHash,
                                                TransactionAction action,
                                                StoredTargets targets,
                                                int selectedCount,
                                                int changedCount,
                                                int unchangedCount,
                                                DateTime capturedAt,
                                                DateTime expiresAt,
                                                CancellationToken cancellationToken)
    {
        string actionJson = JsonSerializer.Serialize(new StoredAction(action.CategoryId, action.AddTagIds, action.RemoveTagIds, action.IsHiddenByUser, action.ConfirmCategory), _storedJson);
        string targetsJson = JsonSerializer.Serialize(targets, _storedJson);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transaction_operations (
                customer_id, operation_id, status, selection_mode, request_digest,
                filter_digest, confirmation_hash, action, target_stamps,
                selected_count, changed_count, unchanged_count,
                captured_at, expires_at, created_at, updated_at)
            VALUES (
                {customerId}, {operationId}, 'Prepared', {selectionMode}, {requestDigest},
                {filterDigest}, {confirmationHash}, CAST({actionJson} AS jsonb), CAST({targetsJson} AS jsonb),
                {selectedCount}, {changedCount}, {unchangedCount},
                {capturedAt}, {expiresAt}, NOW(), NOW())
            """, cancellationToken);
    }

    private static async Task InsertManifestAsync(ArgusDbContext context,
                                                  Guid customerId,
                                                  Guid operationId,
                                                  IReadOnlyList<MemberRow> members,
                                                  CancellationToken cancellationToken)
    {
        int ordinal = 0;
        foreach (MemberRow[] batch in members.Chunk(ManifestInsertBatchSize))
        {
            int count = batch.Length;
            int[] ordinals = new int[count];
            for (int index = 0; index < count; index++)
            {
                ordinals[index] = ordinal;
                ordinal++;
            }

            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO transaction_operation_items (
                    customer_id, operation_id, transaction_id, transaction_version,
                    financial_account_id, statement_import_id, ordinal)
                SELECT t.customer_id, t.operation_id, t.transaction_id, t.transaction_version,
                       t.financial_account_id, t.statement_import_id, t.ordinal
                FROM unnest(
                    CAST({Enumerable.Repeat(customerId, count).ToArray()} AS uuid[]),
                    CAST({Enumerable.Repeat(operationId, count).ToArray()} AS uuid[]),
                    CAST({batch.Select(member => member.Id).ToArray()} AS uuid[]),
                    CAST({batch.Select(member => member.Version).ToArray()} AS integer[]),
                    CAST({batch.Select(member => member.AccountId).ToArray()} AS uuid[]),
                    CAST({batch.Select(member => member.StatementId).ToArray()} AS uuid[]),
                    CAST({ordinals} AS integer[])
                ) AS t(customer_id, operation_id, transaction_id, transaction_version,
                       financial_account_id, statement_import_id, ordinal)
                """, cancellationToken);
        }
    }

    private static async Task UpdateMembersAsync(ArgusDbContext context,
                                                 Guid customerId,
                                                 Guid[] transactionIds,
                                                 TransactionAction action,
                                                 CancellationToken cancellationToken)
    {
        bool setCategory = action.CategoryId is not null;
        bool confirmCategory = action.CategoryId is not null || action.ConfirmCategory is true;
        bool setHidden = action.IsHiddenByUser is not null;
        Guid? categoryId = action.CategoryId;
        bool? hidden = action.IsHiddenByUser;
        int affected = await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE transactions
            SET category_id = CASE WHEN {setCategory} THEN {categoryId} ELSE category_id END,
                category_confirmed = CASE WHEN {confirmCategory} THEN TRUE ELSE category_confirmed END,
                is_hidden_by_user = CASE WHEN {setHidden} THEN {hidden} ELSE is_hidden_by_user END,
                version = version + 1,
                updated_at = clock_timestamp()
            WHERE customer_id = {customerId} AND id = ANY({transactionIds})
            """, cancellationToken);
        if (affected != transactionIds.Length)
        {
            throw new TransactionStoreException(TransactionErrorCodes.SelectionStale);
        }
    }

    private static async Task MutateTagLinksAsync(ArgusDbContext context,
                                                  Guid customerId,
                                                  Guid[] transactionIds,
                                                  TransactionAction action,
                                                  CancellationToken cancellationToken)
    {
        if (action.AddTagIds.Length > 0)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO transaction_merchant_tags (customer_id, transaction_id, merchant_tag_id)
                SELECT {customerId}, target.id, add.tag_id
                FROM unnest({transactionIds}) AS target(id)
                CROSS JOIN unnest({action.AddTagIds}) AS add(tag_id)
                ON CONFLICT (transaction_id, merchant_tag_id) DO NOTHING
                """, cancellationToken);
        }

        if (action.RemoveTagIds.Length > 0)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM transaction_merchant_tags j
                USING unnest({transactionIds}) AS target(id)
                WHERE j.customer_id = {customerId}
                  AND j.transaction_id = target.id
                  AND j.merchant_tag_id = ANY({action.RemoveTagIds})
                """, cancellationToken);
        }
    }

    private static bool IsChanged(MemberRow member, TransactionAction action)
    {
        if (action.CategoryId is not null && action.CategoryId.Value != member.CategoryId)
        {
            return true;
        }

        if (action.CategoryId is not null && !member.CategoryConfirmed)
        {
            return true;
        }

        if (action.ConfirmCategory is true && !member.CategoryConfirmed)
        {
            return true;
        }

        if (action.IsHiddenByUser is not null && action.IsHiddenByUser.Value != member.HiddenByUser)
        {
            return true;
        }

        var assigned = member.TagIds.ToHashSet();
        if (action.RemoveTagIds.Any(assigned.Contains))
        {
            return true;
        }

        return action.AddTagIds.Any(tagId => !assigned.Contains(tagId));
    }

    private static void ValidateTagLimit(MemberRow member, TransactionAction action)
    {
        var existing = member.TagIds.ToHashSet();
        var adds = action.AddTagIds.ToHashSet();
        var removes = action.RemoveTagIds.ToHashSet();
        int resulting = existing.Union(adds).Except(removes).Count();
        if (resulting <= TransactionLimits.MaxTagsPerTransaction)
        {
            return;
        }

        if (adds.Count == 0 && resulting <= existing.Count)
        {
            // A no-add edit may reduce a legacy oversized set without first
            // forcing unrelated removals.
            return;
        }

        throw new TransactionStoreException(TransactionErrorCodes.TagLimitExceeded);
    }

    private static async Task EnsureRestoreAllowedAsync(ArgusDbContext context,
                                                        Guid customerId,
                                                        IReadOnlyCollection<MemberRow> members,
                                                        TransactionAction action,
                                                        CancellationToken cancellationToken)
    {
        if (action.IsHiddenByUser != false)
        {
            return;
        }

        var hiddenStatementIds = members.Where(member => member.StatementHidden).Select(member => member.StatementId).Distinct().Take(5).ToArray();
        if (hiddenStatementIds.Length == 0)
        {
            return;
        }

        var rows = await context.StatementImports
            .AsNoTracking()
            .Where(statement => statement.CustomerId == customerId && hiddenStatementIds.Contains(statement.Id))
            .OrderBy(statement => statement.Id)
            .Select(statement => new { statement.Id, statement.StatementLabel })
            .ToListAsync(cancellationToken);
        var links = rows
            .Select(row => new Dictionary<string, object?> { ["id"] = row.Id, ["statement_label"] = row.StatementLabel })
            .ToList();

        throw new TransactionStoreException(TransactionErrorCodes.ParentHidden, new Dictionary<string, object?> { ["statements"] = links });
    }

    private static async Task<List<ManifestRow>> ReadManifestAsync(ArgusDbContext context,
                                                                   Guid customerId,
                                                                   Guid operationId,
                                                                   CancellationToken cancellationToken)
    {
        return await context.TransactionOperationItems
            .AsNoTracking()
            .Where(item => item.CustomerId == customerId && item.OperationId == operationId)
            .OrderBy(item => item.TransactionId)
            .Select(item => new ManifestRow(item.TransactionId, item.TransactionVersion, item.FinancialAccountId, item.StatementImportId))
            .ToListAsync(cancellationToken);
    }

    private static async Task<OperationHeader?> ReadHeaderAsync(ArgusDbContext context,
                                                                Guid customerId,
                                                                Guid operationId,
                                                                bool forUpdate,
                                                                CancellationToken cancellationToken)
    {
        const string headerSql = """
            SELECT customer_id AS "CustomerId", operation_id AS "OperationId",
                   status AS "Status", selection_mode AS "SelectionMode",
                   request_digest AS "RequestDigest", filter_digest AS "FilterDigest",
                   confirmation_hash AS "ConfirmationHash", action::text AS "ActionJson",
                   target_stamps::text AS "TargetJson", selected_count AS "SelectedCount",
                   changed_count AS "ChangedCount", unchanged_count AS "UnchangedCount",
                   captured_at AS "CapturedAt", expires_at AS "ExpiresAt",
                   applied_at AS "AppliedAt", terminal_at AS "TerminalAt"
            FROM transaction_operations
            WHERE customer_id = {0} AND operation_id = {1}
            """;
        string sql = forUpdate ? string.Concat(headerSql, "\nFOR UPDATE") : headerSql;
        List<OperationHeaderRow> rows = await context.Database
            .SqlQueryRaw<OperationHeaderRow>(sql, customerId, operationId)
            .ToListAsync(cancellationToken);
        OperationHeaderRow? row = rows.SingleOrDefault();

        return row is null
            ? null
            : new OperationHeader(
                row.CustomerId,
                row.OperationId,
                row.Status,
                row.SelectionMode,
                row.RequestDigest,
                row.FilterDigest,
                row.ConfirmationHash,
                row.ActionJson,
                row.TargetJson,
                row.SelectedCount,
                row.ChangedCount,
                row.UnchangedCount,
                row.CapturedAt,
                row.ExpiresAt,
                row.AppliedAt,
                row.TerminalAt);
    }

    private async Task<TransactionOperationSummaryResponse> BuildPreparedSummaryAsync(ArgusDbContext context,
                                                                                     OperationHeader header,
                                                                                     CancellationToken cancellationToken)
    {
        var action = DeserializeAction(header.ActionJson);
        var stored = DeserializeTargets(header.TargetJson);
        var resolved = await ResolveActionAsync(context, header.CustomerId, action, stored, cancellationToken);

        return new TransactionOperationSummaryResponse(
            header.OperationId,
            header.Status,
            header.SelectionMode,
            FormatTimestamp(header.CapturedAt),
            FormatTimestamp(header.ExpiresAt),
            header.SelectedCount,
            header.ChangedCount,
            header.UnchangedCount,
            header.ConfirmationHash,
            header.AppliedAt is null ? null : FormatTimestamp(header.AppliedAt.Value),
            header.TerminalAt is null ? null : FormatTimestamp(header.TerminalAt.Value),
            resolved);
    }

    private async Task<TransactionOperationActionResponse> ResolveActionAsync(ArgusDbContext context,
                                                                             Guid customerId,
                                                                             TransactionAction action,
                                                                             StoredTargets stored,
                                                                             CancellationToken cancellationToken)
    {
        bool stale = false;
        TransactionCategoryRef? category = null;
        if (action.CategoryId is not null)
        {
            var row = await context.Categories
                .AsNoTracking()
                .Where(item => item.CustomerId == customerId && item.Id == action.CategoryId.Value)
                .Select(item => new { item.Id, item.DisplayName, item.Kind, item.IsSystem, item.IsArchived, item.UpdatedAt })
                .SingleOrDefaultAsync(cancellationToken);
            if (row is null)
            {
                stale = true;
            }
            else
            {
                var stamp = new StoredCategoryStamp(row.Id, row.DisplayName, row.Kind, row.IsArchived, FormatStamp(row.UpdatedAt));
                category = new TransactionCategoryRef(stamp.Id, stamp.DisplayName, stamp.Kind, row.IsSystem, stamp.IsArchived);
                if (stored.Category != stamp)
                {
                    stale = true;
                }
            }
        }

        var tagIds = action.AddTagIds.Concat(action.RemoveTagIds).Distinct().Order().ToArray();
        var current = new Dictionary<Guid, TransactionTagRef>();
        var currentStamps = new Dictionary<Guid, StoredTagStamp>();
        if (tagIds.Length > 0)
        {
            var rows = await context.MerchantTags
                .AsNoTracking()
                .Where(tag => tag.CustomerId == customerId && tagIds.Contains(tag.Id))
                .Select(tag => new { tag.Id, tag.DisplayName, tag.IsArchived, tag.UpdatedAt })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                var stamp = new StoredTagStamp(row.Id, row.DisplayName, row.IsArchived, FormatStamp(row.UpdatedAt));
                current[stamp.Id] = new TransactionTagRef(stamp.Id, stamp.DisplayName, stamp.IsArchived);
                currentStamps[stamp.Id] = stamp;
            }
        }

        var expectedStamps = stored.AddTags.Concat(stored.RemoveTags).ToDictionary(tag => tag.Id);
        if (expectedStamps.Count != currentStamps.Count || expectedStamps.Any(pair => !currentStamps.TryGetValue(pair.Key, out var stamp) || stamp != pair.Value))
        {
            stale = true;
        }

        TransactionTagRef[] RefsFor(Guid[] ids) => ids
            .Where(current.ContainsKey)
            .Select(id => current[id])
            .ToArray();

        return new TransactionOperationActionResponse(category, RefsFor(action.AddTagIds), RefsFor(action.RemoveTagIds), action.IsHiddenByUser, action.ConfirmCategory, stale);
    }

    private static TransactionOperationSummaryResponse TerminalSummary(OperationHeader header) => new(
        header.OperationId,
        header.Status,
        header.SelectionMode,
        FormatTimestamp(header.CapturedAt),
        FormatTimestamp(header.ExpiresAt),
        header.SelectedCount,
        header.ChangedCount,
        header.UnchangedCount,
        header.ConfirmationHash,
        header.AppliedAt is null ? null : FormatTimestamp(header.AppliedAt.Value),
        header.TerminalAt is null ? null : FormatTimestamp(header.TerminalAt.Value),
        null);

    private static async Task ExpireHeaderAsync(ArgusDbContext context,
                                                Guid customerId,
                                                Guid operationId,
                                                CancellationToken cancellationToken)
    {
        await context.TransactionOperationItems
            .Where(item => item.CustomerId == customerId && item.OperationId == operationId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE transaction_operations
            SET status = 'Expired',
                terminal_at = clock_timestamp(),
                action = '{}'::jsonb,
                target_stamps = '{}'::jsonb,
                updated_at = clock_timestamp()
            WHERE customer_id = {{customerId}} AND operation_id = {{operationId}}
              AND status = 'Prepared'
            """, cancellationToken);
    }

    private static async Task<DateTime> MarkAppliedAsync(ArgusDbContext context,
                                                         Guid customerId,
                                                         Guid operationId,
                                                         CancellationToken cancellationToken)
    {
        await context.TransactionOperationItems
            .Where(item => item.CustomerId == customerId && item.OperationId == operationId)
            .ExecuteDeleteAsync(cancellationToken);

        List<TransactionOperation> updated = await context.TransactionOperations
            .FromSqlInterpolated($$"""
                WITH updated AS (
                    UPDATE transaction_operations
                    SET status = 'Applied',
                        applied_at = clock_timestamp(),
                        terminal_at = clock_timestamp(),
                        action = '{}'::jsonb,
                        target_stamps = '{}'::jsonb,
                        updated_at = clock_timestamp()
                    WHERE customer_id = {{customerId}} AND operation_id = {{operationId}}
                    RETURNING *
                )
                SELECT * FROM updated
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);

        return updated.Single().AppliedAt!.Value;
    }

    private static TransactionStoreException TerminalState(string status) => status switch
    {
        "Abandoned" => new TransactionStoreException(TransactionErrorCodes.OperationAbandoned),
        _ => new TransactionStoreException(TransactionErrorCodes.OperationExpired),
    };

    private static TransactionOperationApplyResponse AppliedResponse(OperationHeader header,
                                                                     string confirmationHash,
                                                                     int expectedSelectedCount,
                                                                     bool replayed)
    {
        if (!string.Equals(header.ConfirmationHash, confirmationHash, StringComparison.Ordinal) || header.SelectedCount != expectedSelectedCount)
        {
            throw new TransactionStoreException(TransactionErrorCodes.IdempotencyConflict);
        }

        return new TransactionOperationApplyResponse(header.OperationId, header.Status, header.SelectedCount, header.ChangedCount, header.UnchangedCount, header.AppliedAt is null ? null : FormatTimestamp(header.AppliedAt.Value), replayed);
    }

    private static TransactionAction DeserializeAction(string json)
    {
        var stored = JsonSerializer.Deserialize<StoredAction>(json, _storedJson) ?? throw new TransactionStoreException(TransactionErrorCodes.InvalidRequest);

        return new TransactionAction(stored.CategoryId, stored.AddTagIds, stored.RemoveTagIds, stored.IsHiddenByUser, stored.ConfirmCategory);
    }

    private static StoredTargets DeserializeTargets(string json) =>
        JsonSerializer.Deserialize<StoredTargets>(json, _storedJson)
        ?? throw new TransactionStoreException(TransactionErrorCodes.InvalidRequest);

    private static string RequestDigest(TransactionSelectionPlan selection, TransactionAction action)
    {
        if (selection.Mode == "explicit")
        {
            string selectionCanonical = string.Join('\n', selection.ExplicitMembers.OrderBy(member => member.Id).Select(member => $"{member.Id:N}:{member.ExpectedVersion}"));
            return Sha256(string.Join('|', "v1", "mode=explicit", $"selection={Sha256(selectionCanonical)}", $"action={action.Canonical()}"));
        }

        return Sha256(string.Join('|', "v1", "mode=all_matching", $"filters={Sha256(selection.Filters!.Canonical())}", $"action={action.Canonical()}"));
    }

    private static string MembersDigest(IEnumerable<(Guid Id, int Version)> members) =>
        Sha256(string.Join(
            '\n',
            members
                .OrderBy(member => member.Id)
                .Select(member => $"{member.Id:N}:{member.Version}")));

    private static string TargetsCanonical(StoredTargets targets)
    {
        var parts = new List<string>();
        if (targets.Category is not null)
        {
            parts.Add(string.Join('|', "c", targets.Category.Id.ToString("N"), Encode(targets.Category.DisplayName), targets.Category.Kind, targets.Category.IsArchived, targets.Category.UpdatedAt));
        }
        foreach (var tag in targets.AddTags.OrderBy(tag => tag.Id))
        {
            parts.Add(string.Join('|', "a", tag.Id.ToString("N"), Encode(tag.DisplayName), tag.IsArchived, tag.UpdatedAt));
        }
        foreach (var tag in targets.RemoveTags.OrderBy(tag => tag.Id))
        {
            parts.Add(string.Join('|', "r", tag.Id.ToString("N"), Encode(tag.DisplayName), tag.IsArchived, tag.UpdatedAt));
        }

        return string.Join('\n', parts);
    }

    private static string ConfirmationHash(Guid customerId,
                                           Guid operationId,
                                           string selectionMode,
                                           string membersDigest,
                                           TransactionAction action,
                                           string targetsCanonical,
                                           DateTime capturedAt,
                                           DateTime expiresAt,
                                           int selectedCount,
                                           int changedCount,
                                           int unchangedCount)
    {
        string payload = string.Join(
            '\n',
            "v1",
            $"owner={customerId:N}",
            $"operation={operationId:N}",
            $"mode={selectionMode}",
            $"members={membersDigest}",
            $"action={action.Canonical()}",
            $"targets={targetsCanonical}",
            $"captured={capturedAt:O}",
            $"expires={expiresAt:O}",
            $"selected={selectedCount}",
            $"changed={changedCount}",
            $"unchanged={unchangedCount}");

        return Sha256(payload);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string FormatStamp(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc)
            .ToString("O", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static bool IsSystemCategory(string kind) =>
        string.Equals(kind, "Uncategorised", StringComparison.Ordinal);

    // --- Read helpers ------------------------------------------------------

    private sealed record RowData(TransactionResponse Response,
                                  string NormalizedDescription,
                                  Guid[] TagIds,
                                  int? SourcePage,
                                  int SourceRowNumber);

    private static async Task SetReadOnlyAsync(ArgusDbContext context, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
    }

    private static async Task ValidateFilterOwnershipAsync(ArgusDbContext context,
                                                           Guid customerId,
                                                           TransactionFilter filter,
                                                           CancellationToken cancellationToken)
    {
        if (filter.FinancialAccountId is not null)
        {
            await RequireOwnedAsync(context, customerId, "financial_accounts", filter.FinancialAccountId.Value, cancellationToken);
        }
        if (filter.StatementImportId is not null)
        {
            await RequireOwnedAsync(context, customerId, "statement_imports", filter.StatementImportId.Value, cancellationToken);
        }
        if (filter.CategoryId is not null)
        {
            await RequireOwnedAsync(context, customerId, "categories", filter.CategoryId.Value, cancellationToken);
        }
        if (filter.MerchantTagId is not null)
        {
            await RequireOwnedAsync(context, customerId, "merchant_tags", filter.MerchantTagId.Value, cancellationToken);
        }
    }

    internal static async Task RequireOwnedAsync(ArgusDbContext context,
                                                 Guid customerId,
                                                 string table,
                                                 Guid id,
                                                 CancellationToken cancellationToken)
    {
        bool owned = table switch
        {
            "financial_accounts" => await context.FinancialAccounts.AnyAsync(item => item.CustomerId == customerId && item.Id == id, cancellationToken),
            "statement_imports" => await context.StatementImports.AnyAsync(item => item.CustomerId == customerId && item.Id == id, cancellationToken),
            "categories" => await context.Categories.AnyAsync(item => item.CustomerId == customerId && item.Id == id, cancellationToken),
            "merchant_tags" => await context.MerchantTags.AnyAsync(item => item.CustomerId == customerId && item.Id == id, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(table), "unsupported owned table"),
        };
        if (!owned)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }
    }

    private async Task<List<RowData>> ReadRowsAsync(ArgusDbContext context,
                                                    Guid customerId,
                                                    TransactionFilter filter,
                                                    string sortBy,
                                                    string sortDirection,
                                                    int limit,
                                                    long offset,
                                                    bool includeVisibility,
                                                    CancellationToken cancellationToken)
    {
        IQueryable<Transaction> ordered = TransactionQuery.Order(
            TransactionQuery.Apply(context, customerId, filter, includeVisibility),
            sortBy,
            sortDirection);
        List<RawRow> rawRows = await (
            from transaction in ordered
            join account in context.FinancialAccounts
                on new { transaction.CustomerId, Id = transaction.FinancialAccountId } equals new { account.CustomerId, Id = account.Id }
            join category in context.Categories
                on new { transaction.CustomerId, Id = transaction.CategoryId } equals new { category.CustomerId, Id = category.Id }
            join statement in context.StatementImports
                on new { transaction.CustomerId, Id = transaction.StatementImportId } equals new { statement.CustomerId, Id = statement.Id }
            select new RawRow(
                transaction.Id,
                transaction.Version,
                transaction.TransactionDate,
                transaction.Description,
                transaction.NormalizedDescription,
                transaction.Amount,
                account.Id,
                account.DisplayName,
                account.IsArchived,
                category.Id,
                category.DisplayName,
                category.Kind,
                category.IsSystem,
                category.IsArchived,
                transaction.CategoryConfirmed,
                statement.Id,
                statement.StatementLabel,
                statement.SourceType,
                statement.IsHidden,
                transaction.IsHiddenByUser,
                transaction.SourcePage,
                transaction.SourceRowNumber))
            .Skip((int)offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var tags = await LoadAssignedTagsAsync(context, customerId, rawRows.Select(row => row.Id).ToArray(), cancellationToken);

        return rawRows.Select(raw => MapRow(raw, tags)).ToList();
    }

    private async Task<List<RowData>> ReadRowsByIdsAsync(ArgusDbContext context,
                                                         Guid customerId,
                                                         Guid[] ids,
                                                         CancellationToken cancellationToken)
    {
        List<RawRow> rawRows = await (
            from transaction in context.Transactions.AsNoTracking()
            join account in context.FinancialAccounts
                on new { transaction.CustomerId, Id = transaction.FinancialAccountId } equals new { account.CustomerId, Id = account.Id }
            join category in context.Categories
                on new { transaction.CustomerId, Id = transaction.CategoryId } equals new { category.CustomerId, Id = category.Id }
            join statement in context.StatementImports
                on new { transaction.CustomerId, Id = transaction.StatementImportId } equals new { statement.CustomerId, Id = statement.Id }
            where transaction.CustomerId == customerId && ids.Contains(transaction.Id)
            orderby transaction.Id
            select new RawRow(
                transaction.Id,
                transaction.Version,
                transaction.TransactionDate,
                transaction.Description,
                transaction.NormalizedDescription,
                transaction.Amount,
                account.Id,
                account.DisplayName,
                account.IsArchived,
                category.Id,
                category.DisplayName,
                category.Kind,
                category.IsSystem,
                category.IsArchived,
                transaction.CategoryConfirmed,
                statement.Id,
                statement.StatementLabel,
                statement.SourceType,
                statement.IsHidden,
                transaction.IsHiddenByUser,
                transaction.SourcePage,
                transaction.SourceRowNumber))
            .ToListAsync(cancellationToken);

        var tags = await LoadAssignedTagsAsync(context, customerId, rawRows.Select(row => row.Id).ToArray(), cancellationToken);

        return rawRows.Select(raw => MapRow(raw, tags)).ToList();
    }

    private sealed record RawRow(Guid Id,
                                 int Version,
                                 DateOnly TransactionDate,
                                 string Description,
                                 string NormalizedDescription,
                                 decimal Amount,
                                 Guid AccountId,
                                 string AccountName,
                                 bool AccountArchived,
                                 Guid CategoryId,
                                 string CategoryName,
                                 string CategoryKind,
                                 bool CategorySystem,
                                 bool CategoryArchived,
                                 bool CategoryConfirmed,
                                 Guid StatementId,
                                 string StatementLabel,
                                 string StatementSourceType,
                                 bool StatementHidden,
                                 bool HiddenByUser,
                                 int? SourcePage,
                                 int SourceRowNumber);

    private static RowData MapRow(RawRow raw, IReadOnlyDictionary<Guid, TransactionTagRef[]> tags)
    {
        string amount = TransactionRequestParser.FormatAmount(raw.Amount);
        string direction = TransactionRequestParser.DirectionOf(raw.Amount);
        bool effectiveHidden = raw.StatementHidden || raw.HiddenByUser;
        string reason = raw.StatementHidden && raw.HiddenByUser ? "both" : raw.StatementHidden ? "statement" : raw.HiddenByUser ? "individual" : "none";

        var response = new TransactionResponse(
            raw.Id,
            raw.Version,
            raw.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            raw.Description,
            amount,
            "ZAR",
            direction,
            new TransactionAccountRef(raw.AccountId, raw.AccountName, raw.AccountArchived),
            new TransactionCategoryRef(
                raw.CategoryId,
                raw.CategoryName,
                raw.CategoryKind,
                raw.CategorySystem,
                raw.CategoryArchived),
            raw.CategoryConfirmed,
            tags.GetValueOrDefault(raw.Id) ?? [],
            new TransactionStatementRef(
                raw.StatementId,
                raw.StatementLabel,
                raw.StatementSourceType,
                raw.StatementHidden),
            raw.HiddenByUser,
            effectiveHidden,
            reason);

        var tagIds = (tags.GetValueOrDefault(raw.Id) ?? []).Select(tag => tag.Id).ToArray();

        return new RowData(response, raw.NormalizedDescription, tagIds, raw.SourcePage, raw.SourceRowNumber);
    }

    private static async Task<Dictionary<Guid, TransactionTagRef[]>> LoadAssignedTagsAsync(ArgusDbContext context,
                                                                                           Guid customerId,
                                                                                           IReadOnlyCollection<Guid> transactionIds,
                                                                                           CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, TransactionTagRef[]>();
        if (transactionIds.Count == 0)
        {
            return result;
        }

        var rows = await (
            from link in context.TransactionMerchantTags.AsNoTracking()
            join tag in context.MerchantTags
                on new { link.CustomerId, Id = link.MerchantTagId } equals new { tag.CustomerId, Id = tag.Id }
            where link.CustomerId == customerId && transactionIds.Contains(link.TransactionId)
            orderby tag.DisplayName, tag.Id
            select new { link.TransactionId, tag.Id, tag.DisplayName, tag.IsArchived })
            .ToListAsync(cancellationToken);
        var collected = new Dictionary<Guid, List<TransactionTagRef>>();
        foreach (var row in rows)
        {
            if (!collected.TryGetValue(row.TransactionId, out List<TransactionTagRef>? list))
            {
                list = [];
                collected[row.TransactionId] = list;
            }
            list.Add(new TransactionTagRef(row.Id, row.DisplayName, row.IsArchived));
        }

        foreach (var (id, list) in collected)
        {
            result[id] = list.ToArray();
        }

        return result;
    }

    private static async Task<TransactionCategoryRef?> ReadCategoryRefAsync(ArgusDbContext context,
                                                                            Guid customerId,
                                                                            Guid categoryId,
                                                                            CancellationToken cancellationToken)
    {
        return await context.Categories
            .AsNoTracking()
            .Where(category => category.CustomerId == customerId && category.Id == categoryId)
            .Select(category => new TransactionCategoryRef(
                category.Id,
                category.DisplayName,
                category.Kind,
                category.IsSystem,
                category.IsArchived))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<(Guid Id, string DisplayName, string[] Tokens)>> LoadActiveTagsAsync(ArgusDbContext context,
                                                                                                                 Guid customerId,
                                                                                                                 CancellationToken cancellationToken)
    {
        var rows = await context.MerchantTags
            .AsNoTracking()
            .Where(tag => tag.CustomerId == customerId && !tag.IsArchived)
            .OrderBy(tag => tag.NormalizedName)
            .ThenBy(tag => tag.Id)
            .Select(tag => new { tag.Id, tag.DisplayName, tag.NormalizedName })
            .ToListAsync(cancellationToken);

        return rows
            .Select(tag => (tag.Id, tag.DisplayName, tag.NormalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .ToList();
    }
}
