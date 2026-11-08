using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using argus.Server.Finance;
using argus.Server.Transactions;
using Npgsql;
using NpgsqlTypes;

namespace argus.Server.Data;

/// <summary>
/// Owned transaction reads and atomic metadata mutations. Every statement is
/// scoped by the authenticated customer id; foreign and unknown identifiers
/// are indistinguishable.
/// </summary>
public sealed class TransactionStore(Db db,
                                     ITransactionFaultInjector faults)
{
    private static readonly JsonSerializerOptions _storedJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
    // --- Queries -----------------------------------------------------------

    public async Task<TransactionPageResult> QueryAsync(Guid customerId,
                                                        TransactionQueryPlan plan,
                                                        CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        await SetReadOnlyAsync(connection, transaction, cancellationToken);
        await ValidateFilterOwnershipAsync(
            connection, transaction, customerId, plan.Filter, cancellationToken);

        var visible = 0L;
        var hidden = 0L;
        await using (var counts = connection.CreateCommand())
        {
            counts.Transaction = transaction;
            var where = TransactionSql.BuildWhere(plan.Filter, counts, includeVisibility: false);
            counts.CommandText = $"""
                SELECT
                    COUNT(*) FILTER (WHERE NOT (s.is_hidden OR t.is_hidden_by_user)),
                    COUNT(*) FILTER (WHERE (s.is_hidden OR t.is_hidden_by_user))
                FROM transactions t
                {TransactionSql.Joins}
                WHERE {where}
                """;
            counts.Parameters.AddWithValue("customer_id", customerId);
            await using var reader = await counts.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                visible = reader.GetInt64(0);
                hidden = reader.GetInt64(1);
            }
        }

        long inventory;
        await using (var inventoryCommand = connection.CreateCommand())
        {
            inventoryCommand.Transaction = transaction;
            inventoryCommand.CommandText =
                "SELECT COUNT(*) FROM transactions WHERE customer_id = @customer_id";
            inventoryCommand.Parameters.AddWithValue("customer_id", customerId);
            inventory = (long)(await inventoryCommand.ExecuteScalarAsync(cancellationToken))!;
        }

        var offset = (long)(plan.Page - 1) * plan.PageSize;
        var rows = await ReadRowsAsync(
            connection,
            transaction,
            customerId,
            plan.Filter,
            TransactionSql.OrderClause(plan.SortBy, plan.SortDirection),
            plan.PageSize,
            offset,
            includeVisibility: true,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var matchingTotal = visible + hidden;
        var filteredTotal = plan.Filter.Visibility switch
        {
            TransactionVisibility.Visible => visible,
            TransactionVisibility.Hidden => hidden,
            _ => matchingTotal,
        };

        return new TransactionPageResult(
            rows.Select(row => row.Response).ToArray(),
            inventory,
            visible,
            hidden,
            matchingTotal,
            filteredTotal,
            plan.Page,
            plan.PageSize,
            plan.SortBy,
            plan.SortDirection);
    }

    public async Task<TransactionDetailResult> GetAsync(Guid customerId,
                                                        Guid transactionId,
                                                        CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        await SetReadOnlyAsync(connection, transaction, cancellationToken);

        var rows = await ReadRowsByIdsAsync(
            connection,
            transaction,
            customerId,
            [transactionId],
            cancellationToken);
        if (rows.Count == 0)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        var row = rows[0];
        var activeTags = await LoadActiveTagsAsync(
            connection, transaction, customerId, cancellationToken);
        var assigned = row.TagIds.ToHashSet();
        var matches = TagSuggestions
            .Match(
                row.NormalizedDescription,
                activeTags.Select(tag => (tag.Id, tag.Tokens)).ToArray())
            .Where(id => !assigned.Contains(id))
            .Take(TransactionLimits.MaxSuggestions + 1)
            .ToArray();
        var hasMore = matches.Length > TransactionLimits.MaxSuggestions;
        var suggested = matches
            .Take(TransactionLimits.MaxSuggestions)
            .Select(id => activeTags.First(tag => tag.Id == id))
            .Select(tag => new TransactionTagRef(tag.Id, tag.DisplayName, false))
            .ToArray();

        await transaction.CommitAsync(cancellationToken);
        return new TransactionDetailResult(
            row.Response,
            suggested,
            hasMore,
            row.SourcePage,
            row.SourceRowNumber);
    }

    // --- Single metadata edits ---------------------------------------------

    public async Task<TransactionMutationResult> PatchAsync(Guid customerId,
                                                            Guid transactionId,
                                                            int expectedVersion,
                                                            TransactionAction action,
                                                            CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await BeginWorkspaceTransactionAsync(
            connection, cancellationToken);

        var accountId = await TransactionAccountIdAsync(
            connection, transaction, customerId, transactionId, cancellationToken);
        if (accountId is null)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        if (!await LockAccountRowAsync(
                connection, transaction, customerId, accountId.Value, cancellationToken))
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        await ValidateActionTargetsAsync(
            connection, transaction, customerId, action, lockForShare: true, cancellationToken);

        if (!await LockTransactionRowAsync(
                connection, transaction, customerId, transactionId, cancellationToken))
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        var member = await ReadMemberByIdAsync(
            connection, transaction, customerId, transactionId, cancellationToken)
            ?? throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        if (member.Version != expectedVersion)
        {
            throw new TransactionStoreException(TransactionErrorCodes.VersionConflict);
        }

        await EnsureRestoreAllowedAsync(
            connection, transaction, customerId, [member], action, cancellationToken);
        ValidateTagLimit(member, action);
        var changed = IsChanged(member, action);

        if (changed)
        {
            await UpdateMembersAsync(
                connection, transaction, customerId, [member.Id], action, cancellationToken);
            faults.OnStep(TransactionFaultPoints.AfterTransactionUpdate);
            if (action.AddTagIds.Length > 0 || action.RemoveTagIds.Length > 0)
            {
                await MutateTagLinksAsync(
                    connection, transaction, customerId, [member.Id], action, cancellationToken);
                faults.OnStep(TransactionFaultPoints.AfterTagLinkMutation);
            }
        }

        var rows = await ReadRowsByIdsAsync(
            connection, transaction, customerId, [transactionId], cancellationToken);
        if (rows.Count == 0)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }
        await transaction.CommitAsync(cancellationToken);

        return new TransactionMutationResult(
            rows[0].Response, 1, changed ? 1 : 0, changed ? 0 : 1);
    }

    // --- Bulk preparation --------------------------------------------------

    public async Task<TransactionOperationPrepareResponse> PrepareAsync(Guid customerId,
                                                                        Guid operationId,
                                                                        TransactionSelectionPlan selection,
                                                                        TransactionAction action,
                                                                        CancellationToken cancellationToken = default)
    {
        var requestDigest = RequestDigest(selection, action);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await PrepareOnceAsync(
                    customerId, operationId, selection, action, requestDigest, cancellationToken);
            }
            catch (PostgresException exception) when (
                exception.SqlState is PostgresErrorCodes.SerializationFailure or
                    PostgresErrorCodes.DeadlockDetected)
            {
                if (attempt == 2)
                {
                    throw new TransactionStoreException(TransactionErrorCodes.OperationBusy);
                }
            }
            catch (PostgresException exception) when (
                exception.SqlState == PostgresErrorCodes.UniqueViolation &&
                string.Equals(exception.ConstraintName, "transaction_operations_pkey", StringComparison.Ordinal))
            {
                var raced = await ReadExistingOperationAsync(
                    customerId, operationId, requestDigest, cancellationToken);
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
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await BeginWorkspaceTransactionAsync(
            connection, cancellationToken, IsolationLevel.Serializable);

        await LockCustomerAsync(connection, transaction, customerId, cancellationToken);

        var existing = await ReadHeaderAsync(
            connection, transaction, customerId, operationId, forUpdate: false, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestDigest, requestDigest, StringComparison.Ordinal))
            {
                throw new TransactionStoreException(TransactionErrorCodes.IdempotencyConflict);
            }

            if (existing.Status == "Prepared")
            {
                if (await IsExpiredAsync(connection, transaction, existing.ExpiresAt, cancellationToken))
                {
                    await ExpireHeaderAsync(
                        connection, transaction, customerId, operationId, cancellationToken);
                    var expired = await ReadHeaderAsync(
                        connection, transaction, customerId, operationId, forUpdate: false,
                        cancellationToken)
                        ?? throw new TransactionStoreException(TransactionErrorCodes.NotFound);
                    await transaction.CommitAsync(cancellationToken);
                    return new TransactionOperationPrepareResponse(
                        TerminalSummary(expired), false);
                }

                var replay = await BuildPreparedSummaryAsync(
                    connection, transaction, existing, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new TransactionOperationPrepareResponse(replay, false);
            }

            await transaction.CommitAsync(cancellationToken);
            return new TransactionOperationPrepareResponse(TerminalSummary(existing), false);
        }

        var targets = await ValidateActionTargetsAsync(
            connection, transaction, customerId, action, lockForShare: false, cancellationToken);

        List<MemberRow> members;
        string? filterDigest = null;
        if (selection.Mode == "explicit")
        {
            var ids = selection.ExplicitMembers.Select(member => member.Id).ToArray();
            members = await ReadMembersAsync(
                connection, transaction, customerId, ids, cancellationToken);
            if (members.Count != ids.Length)
            {
                throw new TransactionStoreException(TransactionErrorCodes.NotFound);
            }

            var versions = selection.ExplicitMembers.ToDictionary(
                member => member.Id, member => member.ExpectedVersion);
            if (members.Any(member => member.Version != versions[member.Id]))
            {
                throw new TransactionStoreException(TransactionErrorCodes.SelectionStale);
            }

            members = members
                .OrderBy(member => member.Id)
                .ToList();
        }
        else
        {
            await ValidateFilterOwnershipAsync(
                connection, transaction, customerId, selection.Filters!, cancellationToken);
            filterDigest = Sha256(selection.Filters!.Canonical());
            members = await CaptureMatchingMembersAsync(
                connection, transaction, customerId, selection.Filters!,
                TransactionLimits.MaxAllMatching + 1, cancellationToken);
            if (members.Count > TransactionLimits.MaxAllMatching)
            {
                throw new TransactionStoreException(TransactionErrorCodes.SelectionTooLarge);
            }
        }

        if (members.Count == 0)
        {
            throw new TransactionStoreException(TransactionErrorCodes.EmptySelection);
        }

        await EnsureRestoreAllowedAsync(
            connection, transaction, customerId, members, action, cancellationToken);
        foreach (var member in members)
        {
            ValidateTagLimit(member, action);
        }

        var changed = members.Count(member => IsChanged(member, action));
        var unchanged = members.Count - changed;

        await CheckQuotaAsync(connection, transaction, customerId, members.Count, cancellationToken);

        var (capturedAt, expiresAt) = await ReadOperationClockAsync(
            connection, transaction, cancellationToken);
        var membersDigest = MembersDigest(members.Select(member => (member.Id, member.Version)));
        var targetCanonical = TargetsCanonical(targets.Stored);
        var confirmationHash = ConfirmationHash(
            customerId,
            operationId,
            selection.Mode,
            membersDigest,
            action,
            targetCanonical,
            capturedAt,
            expiresAt,
            members.Count,
            changed,
            unchanged);

        await InsertHeaderAsync(
            connection,
            transaction,
            customerId,
            operationId,
            selection.Mode,
            requestDigest,
            filterDigest,
            confirmationHash,
            action,
            targets.Stored,
            members.Count,
            changed,
            unchanged,
            capturedAt,
            expiresAt,
            cancellationToken);
        await InsertManifestAsync(
            connection, transaction, customerId, operationId, members, cancellationToken);
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
                false));
        return new TransactionOperationPrepareResponse(summary, true);
    }

    private async Task<TransactionOperationPrepareResponse?> ReadExistingOperationAsync(Guid customerId,
                                                                                        Guid operationId,
                                                                                        string requestDigest,
                                                                                        CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await BeginWorkspaceTransactionAsync(
            connection, cancellationToken);
        var header = await ReadHeaderAsync(
            connection, transaction, customerId, operationId, forUpdate: true, cancellationToken);
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

        if (header.Status == "Prepared" &&
            await IsExpiredAsync(connection, transaction, header.ExpiresAt, cancellationToken))
        {
            await ExpireHeaderAsync(connection, transaction, customerId, operationId, cancellationToken);
            header = await ReadHeaderAsync(
                connection, transaction, customerId, operationId, forUpdate: false, cancellationToken)
                ?? throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        TransactionOperationSummaryResponse summary;
        if (header.Status == "Prepared")
        {
            summary = await BuildPreparedSummaryAsync(
                connection, transaction, header, cancellationToken);
        }
        else
        {
            summary = TerminalSummary(header);
        }
        await transaction.CommitAsync(cancellationToken);
        return new TransactionOperationPrepareResponse(summary, false);
    }

    // --- Status, apply and abandon -----------------------------------------

    public async Task<TransactionOperationSummaryResponse> StatusAsync(Guid customerId,
                                                                       Guid operationId,
                                                                       CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await BeginWorkspaceTransactionAsync(
            connection, cancellationToken);

        var header = await ReadHeaderAsync(
            connection, transaction, customerId, operationId, forUpdate: true, cancellationToken);
        if (header is null)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        if (header.Status == "Prepared" &&
            await IsExpiredAsync(connection, transaction, header.ExpiresAt, cancellationToken))
        {
            await ExpireHeaderAsync(
                connection, transaction, customerId, operationId, cancellationToken);
            header = await ReadHeaderAsync(
                connection, transaction, customerId, operationId, forUpdate: true,
                cancellationToken)
                ?? throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        var summary = header.Status == "Prepared"
            ? await BuildPreparedSummaryAsync(connection, transaction, header, cancellationToken)
            : TerminalSummary(header);
        await transaction.CommitAsync(cancellationToken);
        return summary;
    }

    public async Task<TransactionOperationApplyResponse> ApplyAsync(Guid customerId,
                                                                    Guid operationId,
                                                                    string confirmationHash,
                                                                    int expectedSelectedCount,
                                                                    CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await BeginWorkspaceTransactionAsync(
            connection, cancellationToken);

        var header = await ReadHeaderAsync(
            connection, transaction, customerId, operationId, forUpdate: false, cancellationToken);
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
        var manifest = await ReadManifestAsync(
            connection, transaction, customerId, operationId, cancellationToken);
        var accounts = manifest
            .Select(item => item.AccountId)
            .Distinct()
            .Order()
            .ToArray();
        foreach (var accountId in accounts)
        {
            if (!await LockAccountRowAsync(
                    connection, transaction, customerId, accountId, cancellationToken))
            {
                throw new TransactionStoreException(
                    TransactionErrorCodes.SelectionStale,
                    new Dictionary<string, object?>
                    {
                        ["stale_count"] = 0,
                        ["missing_count"] = manifest.Count,
                    });
            }
        }

        header = await ReadHeaderAsync(
            connection, transaction, customerId, operationId, forUpdate: true, cancellationToken);
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
        if (await IsExpiredAsync(connection, transaction, header.ExpiresAt, cancellationToken))
        {
            await ExpireHeaderAsync(
                connection, transaction, customerId, operationId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new TransactionStoreException(TransactionErrorCodes.OperationExpired);
        }

        if (!string.Equals(header.ConfirmationHash, confirmationHash, StringComparison.Ordinal) ||
            header.SelectedCount != expectedSelectedCount)
        {
            throw new TransactionStoreException(TransactionErrorCodes.IdempotencyConflict);
        }

        var action = DeserializeAction(header.ActionJson);
        var storedTargets = DeserializeTargets(header.TargetJson);
        var expectedHash = ConfirmationHash(
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

        await LockAndValidateTargetsAsync(
            connection, transaction, customerId, action, storedTargets, cancellationToken);

        var memberIds = manifest
            .Select(item => item.TransactionId)
            .Order()
            .ToArray();
        await LockTransactionRowsAsync(
            connection, transaction, customerId, memberIds, cancellationToken);
        var members = await ReadMembersAsync(
            connection, transaction, customerId, memberIds, cancellationToken);
        var byId = members.ToDictionary(member => member.Id);
        var missing = manifest.Count(item => !byId.ContainsKey(item.TransactionId));
        var stale = manifest.Count(item =>
            byId.TryGetValue(item.TransactionId, out var member) &&
            member.Version != item.Version);
        if (missing > 0 || stale > 0 || members.Count != header.SelectedCount)
        {
            throw new TransactionStoreException(
                TransactionErrorCodes.SelectionStale,
                new Dictionary<string, object?>
                {
                    ["stale_count"] = stale,
                    ["missing_count"] = missing,
                });
        }

        await EnsureRestoreAllowedAsync(
            connection, transaction, customerId, members, action, cancellationToken);
        foreach (var member in members)
        {
            ValidateTagLimit(member, action);
        }

        var changedMembers = members
            .Where(member => IsChanged(member, action))
            .ToArray();
        if (changedMembers.Length != header.ChangedCount ||
            members.Count - changedMembers.Length != header.UnchangedCount)
        {
            throw new TransactionStoreException(
                TransactionErrorCodes.SelectionStale,
                new Dictionary<string, object?>
                {
                    ["stale_count"] = members.Count,
                    ["missing_count"] = 0,
                });
        }

        // Account and header checks above precede the classification and
        // transaction locks. A confirmation can expire during either later
        // wait, so use the database wall clock once more before any effects.
        if (await IsExpiredAsync(connection, transaction, header.ExpiresAt, cancellationToken))
        {
            await ExpireHeaderAsync(
                connection, transaction, customerId, operationId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new TransactionStoreException(TransactionErrorCodes.OperationExpired);
        }

        if (changedMembers.Length > 0)
        {
            await UpdateMembersAsync(
                connection,
                transaction,
                customerId,
                changedMembers.Select(member => member.Id).ToArray(),
                action,
                cancellationToken);
            faults.OnStep(TransactionFaultPoints.AfterTransactionUpdate);
        }

        if (action.AddTagIds.Length > 0 || action.RemoveTagIds.Length > 0)
        {
            await MutateTagLinksAsync(
                connection, transaction, customerId, memberIds, action, cancellationToken);
            faults.OnStep(TransactionFaultPoints.AfterTagLinkMutation);
        }

        faults.OnStep(TransactionFaultPoints.BeforeReceiptWrite);
        var appliedAt = await MarkAppliedAsync(
            connection, transaction, customerId, operationId, cancellationToken);
        faults.OnStep(TransactionFaultPoints.AfterReceiptWrite);
        await transaction.CommitAsync(cancellationToken);
        faults.OnStep(TransactionFaultPoints.AfterCommit);

        return new TransactionOperationApplyResponse(
            operationId,
            "Applied",
            header.SelectedCount,
            header.ChangedCount,
            header.UnchangedCount,
            FormatTimestamp(appliedAt),
            false);
    }

    public async Task<TransactionOperationApplyResponse> AbandonAsync(Guid customerId,
                                                                      Guid operationId,
                                                                      CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await BeginWorkspaceTransactionAsync(
            connection, cancellationToken);

        var header = await ReadHeaderAsync(
            connection, transaction, customerId, operationId, forUpdate: true, cancellationToken);
        if (header is null)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }

        if (header.Status == "Applied")
        {
            await transaction.CommitAsync(cancellationToken);
            return new TransactionOperationApplyResponse(
                operationId,
                header.Status,
                header.SelectedCount,
                header.ChangedCount,
                header.UnchangedCount,
                header.AppliedAt is null ? null : FormatTimestamp(header.AppliedAt.Value),
                false);
        }
        if (header.Status == "Abandoned" || header.Status == "Expired")
        {
            await transaction.CommitAsync(cancellationToken);
            return new TransactionOperationApplyResponse(
                operationId,
                header.Status,
                header.SelectedCount,
                header.ChangedCount,
                header.UnchangedCount,
                null,
                false);
        }

        var abandonedAt = await ClockAsync(connection, transaction, cancellationToken);
        await using (var purgeItems = connection.CreateCommand())
        {
            purgeItems.Transaction = transaction;
            purgeItems.CommandText = """
                DELETE FROM transaction_operation_items
                WHERE customer_id = @customer_id AND operation_id = @operation_id
                """;
            purgeItems.Parameters.AddWithValue("customer_id", customerId);
            purgeItems.Parameters.AddWithValue("operation_id", operationId);
            await purgeItems.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE transaction_operations
                SET status = 'Abandoned',
                    terminal_at = @terminal_at,
                    action = '{}'::jsonb,
                    target_stamps = '{}'::jsonb,
                    updated_at = @terminal_at
                WHERE customer_id = @customer_id AND operation_id = @operation_id
                """;
            update.Parameters.AddWithValue("terminal_at", abandonedAt);
            update.Parameters.AddWithValue("customer_id", customerId);
            update.Parameters.AddWithValue("operation_id", operationId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        return new TransactionOperationApplyResponse(
            operationId,
            "Abandoned",
            header.SelectedCount,
            header.ChangedCount,
            header.UnchangedCount,
            null,
            false);
    }

    // --- Cleanup -----------------------------------------------------------

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var expired = new List<(Guid CustomerId, Guid OperationId)>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT customer_id, operation_id
                FROM transaction_operations
                WHERE status = 'Prepared' AND expires_at <= clock_timestamp()
                ORDER BY expires_at
                LIMIT 1000
                FOR UPDATE
                """;
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                expired.Add((reader.GetGuid(0), reader.GetGuid(1)));
            }
        }

        if (expired.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        // These arrays are parallel: an operation UUID is only unique within
        // its customer namespace. Never turn the selected pairs into a
        // Cartesian product of independently matched IDs.
        var customerIds = expired.Select(item => item.CustomerId).ToArray();
        var operationIds = expired.Select(item => item.OperationId).ToArray();
        await using (var items = connection.CreateCommand())
        {
            items.Transaction = transaction;
            items.CommandText = """
                DELETE FROM transaction_operation_items i
                USING unnest(@customer_ids, @operation_ids) AS expired(customer_id, operation_id)
                WHERE i.customer_id = expired.customer_id
                  AND i.operation_id = expired.operation_id
                """;
            items.Parameters.AddWithValue(
                "customer_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, customerIds);
            items.Parameters.AddWithValue(
                "operation_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, operationIds);
            await items.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var headers = connection.CreateCommand())
        {
            headers.Transaction = transaction;
            headers.CommandText = """
                UPDATE transaction_operations o
                SET status = 'Expired',
                    terminal_at = clock_timestamp(),
                    action = '{}'::jsonb,
                    target_stamps = '{}'::jsonb,
                    updated_at = clock_timestamp()
                FROM unnest(@customer_ids, @operation_ids) AS expired(customer_id, operation_id)
                WHERE o.customer_id = expired.customer_id
                  AND o.operation_id = expired.operation_id
                  AND o.status = 'Prepared'
                  AND o.expires_at <= clock_timestamp()
                """;
            headers.Parameters.AddWithValue(
                "customer_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, customerIds);
            headers.Parameters.AddWithValue(
                "operation_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, operationIds);
            await headers.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // --- Mutation helpers --------------------------------------------------

    private sealed record MemberRow(Guid Id,
                                    int Version,
                                    Guid AccountId,
                                    Guid StatementId,
                                    Guid CategoryId,
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
                                       bool? IsHiddenByUser);

    private sealed record StoredCategoryStamp(Guid Id,
                                              string DisplayName,
                                              string Kind,
                                              bool IsArchived,
                                              string UpdatedAt);

    private sealed record StoredTagStamp(Guid Id,
                                         string DisplayName,
                                         bool IsArchived,
                                         string UpdatedAt);

    private sealed record StoredTargets(StoredCategoryStamp? Category,
                                        StoredTagStamp[] AddTags,
                                        StoredTagStamp[] RemoveTags);

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

    private static async Task<NpgsqlTransaction> BeginWorkspaceTransactionAsync(NpgsqlConnection connection,
                                                                                CancellationToken cancellationToken,
                                                                                IsolationLevel isolation = IsolationLevel.ReadCommitted)
    {
        var transaction = await connection.BeginTransactionAsync(isolation, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SET LOCAL lock_timeout = '{TransactionLimits.LockTimeoutSeconds}s';
            SET LOCAL statement_timeout = '{TransactionLimits.StatementTimeoutSeconds}s';
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return transaction;
    }

    private static async Task LockCustomerAsync(NpgsqlConnection connection,
                                                NpgsqlTransaction transaction,
                                                Guid customerId,
                                                CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM customers WHERE id = @customer_id FOR UPDATE";
        command.Parameters.AddWithValue("customer_id", customerId);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }
    }

    private static async Task<Guid?> TransactionAccountIdAsync(NpgsqlConnection connection,
                                                               NpgsqlTransaction transaction,
                                                               Guid customerId,
                                                               Guid transactionId,
                                                               CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT financial_account_id
            FROM transactions
            WHERE customer_id = @customer_id AND id = @id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", transactionId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
    }

    private static async Task<bool> LockAccountRowAsync(NpgsqlConnection connection,
                                                        NpgsqlTransaction transaction,
                                                        Guid customerId,
                                                        Guid accountId,
                                                        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM financial_accounts
            WHERE customer_id = @customer_id AND id = @id
            FOR UPDATE
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", accountId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> LockTransactionRowAsync(NpgsqlConnection connection,
                                                            NpgsqlTransaction transaction,
                                                            Guid customerId,
                                                            Guid transactionId,
                                                            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM transactions
            WHERE customer_id = @customer_id AND id = @id
            FOR UPDATE
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", transactionId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task LockTransactionRowsAsync(NpgsqlConnection connection,
                                                       NpgsqlTransaction transaction,
                                                       Guid customerId,
                                                       Guid[] transactionIds,
                                                       CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id FROM transactions
            WHERE customer_id = @customer_id AND id = ANY(@ids)
            ORDER BY id
            FOR UPDATE
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue(
            "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, transactionIds);
        var found = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found++;
        }
        if (found != transactionIds.Length)
        {
            throw new TransactionStoreException(
                TransactionErrorCodes.SelectionStale,
                new Dictionary<string, object?>
                {
                    ["stale_count"] = 0,
                    ["missing_count"] = transactionIds.Length - found,
                });
        }
    }

    private async Task<MemberRow?> ReadMemberByIdAsync(NpgsqlConnection connection,
                                                       NpgsqlTransaction transaction,
                                                       Guid customerId,
                                                       Guid transactionId,
                                                       CancellationToken cancellationToken)
    {
        var members = await ReadMembersAsync(
            connection, transaction, customerId, [transactionId], cancellationToken);
        return members.Count == 0 ? null : members[0];
    }

    private async Task<List<MemberRow>> ReadMembersAsync(NpgsqlConnection connection,
                                                         NpgsqlTransaction transaction,
                                                         Guid customerId,
                                                         Guid[] transactionIds,
                                                         CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT t.id, t.version, t.financial_account_id, t.statement_import_id,
                   t.category_id, t.is_hidden_by_user, s.is_hidden
            FROM transactions t
            JOIN statement_imports s
              ON s.customer_id = t.customer_id AND s.id = t.statement_import_id
            WHERE t.customer_id = @customer_id AND t.id = ANY(@ids)
            ORDER BY t.id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue(
            "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, transactionIds);

        var rows = new List<(Guid Id, int Version, Guid AccountId, Guid StatementId,
            Guid CategoryId, bool HiddenByUser, bool StatementHidden)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetGuid(0),
                    reader.GetInt32(1),
                    reader.GetGuid(2),
                    reader.GetGuid(3),
                    reader.GetGuid(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6)));
            }
        }

        var tags = await LoadMemberTagIdsAsync(
            connection, transaction, customerId, transactionIds, cancellationToken);
        return rows.Select(row => new MemberRow(
            row.Id,
            row.Version,
            row.AccountId,
            row.StatementId,
            row.CategoryId,
            row.HiddenByUser,
            row.StatementHidden,
            tags.GetValueOrDefault(row.Id) ?? [])).ToList();
    }

    private async Task<List<MemberRow>> CaptureMatchingMembersAsync(NpgsqlConnection connection,
                                                                    NpgsqlTransaction transaction,
                                                                    Guid customerId,
                                                                    TransactionFilter filter,
                                                                    int limit,
                                                                    CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var where = TransactionSql.BuildWhere(filter, command, includeVisibility: true);
        command.CommandText = $"""
            SELECT t.id, t.version, t.financial_account_id, t.statement_import_id,
                   t.category_id, t.is_hidden_by_user, s.is_hidden
            FROM transactions t
            {TransactionSql.Joins}
            WHERE {where}
            ORDER BY t.id
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("limit", limit);

        var rows = new List<(Guid Id, int Version, Guid AccountId, Guid StatementId,
            Guid CategoryId, bool HiddenByUser, bool StatementHidden)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetGuid(0),
                    reader.GetInt32(1),
                    reader.GetGuid(2),
                    reader.GetGuid(3),
                    reader.GetGuid(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6)));
            }
        }

        var ids = rows.Select(row => row.Id).ToArray();
        var tags = await LoadMemberTagIdsAsync(
            connection, transaction, customerId, ids, cancellationToken);
        return rows.Select(row => new MemberRow(
            row.Id,
            row.Version,
            row.AccountId,
            row.StatementId,
            row.CategoryId,
            row.HiddenByUser,
            row.StatementHidden,
            tags.GetValueOrDefault(row.Id) ?? [])).ToList();
    }

    private static async Task<Dictionary<Guid, Guid[]>> LoadMemberTagIdsAsync(NpgsqlConnection connection,
                                                                              NpgsqlTransaction transaction,
                                                                              Guid customerId,
                                                                              Guid[] transactionIds,
                                                                              CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Guid[]>();
        if (transactionIds.Length == 0)
        {
            return result;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT transaction_id, merchant_tag_id
            FROM transaction_merchant_tags
            WHERE customer_id = @customer_id AND transaction_id = ANY(@ids)
            ORDER BY transaction_id, merchant_tag_id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue(
            "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, transactionIds);

        var collected = new Dictionary<Guid, List<Guid>>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var transactionId = reader.GetGuid(0);
                if (!collected.TryGetValue(transactionId, out var list))
                {
                    list = [];
                    collected[transactionId] = list;
                }
                list.Add(reader.GetGuid(1));
            }
        }

        foreach (var (id, list) in collected)
        {
            result[id] = list.ToArray();
        }
        return result;
    }

    private static async Task<ActionTargets> ValidateActionTargetsAsync(NpgsqlConnection connection,
                                                                        NpgsqlTransaction transaction,
                                                                        Guid customerId,
                                                                        TransactionAction action,
                                                                        bool lockForShare,
                                                                        CancellationToken cancellationToken)
    {
        StoredCategoryStamp? storedCategory = null;
        TransactionCategoryRef? categoryRef = null;
        if (action.CategoryId is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                SELECT id, display_name, kind, is_archived, updated_at
                FROM categories
                WHERE customer_id = @customer_id AND id = @id
                {(lockForShare ? "FOR SHARE" : string.Empty)}
                """;
            command.Parameters.AddWithValue("customer_id", customerId);
            command.Parameters.AddWithValue("id", action.CategoryId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new TransactionStoreException(TransactionErrorCodes.NotFound);
            }
            if (reader.GetBoolean(3))
            {
                throw new TransactionStoreException(TransactionErrorCodes.CategoryUnavailable);
            }
            storedCategory = new StoredCategoryStamp(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                FormatStamp(reader.GetFieldValue<DateTime>(4)));
            categoryRef = new TransactionCategoryRef(
                storedCategory.Id,
                storedCategory.DisplayName,
                storedCategory.Kind,
                IsSystemCategory(storedCategory.Kind),
                storedCategory.IsArchived);
        }

        var ids = action.AddTagIds.Concat(action.RemoveTagIds).Distinct().Order().ToArray();
        var addIds = action.AddTagIds.ToHashSet();
        var tagsById = new Dictionary<Guid, StoredTagStamp>();
        if (ids.Length > 0)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                SELECT id, display_name, is_archived, updated_at
                FROM merchant_tags
                WHERE customer_id = @customer_id AND id = ANY(@ids)
                ORDER BY id
                {(lockForShare ? "FOR SHARE" : string.Empty)}
                """;
            command.Parameters.AddWithValue("customer_id", customerId);
            command.Parameters.AddWithValue(
                "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, ids);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var stamp = new StoredTagStamp(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetBoolean(2),
                    FormatStamp(reader.GetFieldValue<DateTime>(3)));
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

        var stored = new StoredTargets(
            storedCategory,
            action.AddTagIds.Select(id => tagsById[id]).ToArray(),
            action.RemoveTagIds.Select(id => tagsById[id]).ToArray());

        return new ActionTargets(
            stored,
            categoryRef,
            RefsFor(action.AddTagIds),
            RefsFor(action.RemoveTagIds));
    }

    private static async Task LockAndValidateTargetsAsync(NpgsqlConnection connection,
                                                          NpgsqlTransaction transaction,
                                                          Guid customerId,
                                                          TransactionAction action,
                                                          StoredTargets stored,
                                                          CancellationToken cancellationToken)
    {
        if (action.CategoryId is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, display_name, kind, is_archived, updated_at
                FROM categories
                WHERE customer_id = @customer_id AND id = @id
                FOR SHARE
                """;
            command.Parameters.AddWithValue("customer_id", customerId);
            command.Parameters.AddWithValue("id", action.CategoryId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || stored.Category is null)
            {
                throw new TransactionStoreException(TransactionErrorCodes.ClassificationChanged);
            }
            var current = new StoredCategoryStamp(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                FormatStamp(reader.GetFieldValue<DateTime>(4)));
            if (current != stored.Category)
            {
                throw new TransactionStoreException(TransactionErrorCodes.ClassificationChanged);
            }
        }

        var allTagIds = action.AddTagIds
            .Concat(action.RemoveTagIds)
            .Distinct()
            .Order()
            .ToArray();
        if (allTagIds.Length > 0)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, display_name, is_archived, updated_at
                FROM merchant_tags
                WHERE customer_id = @customer_id AND id = ANY(@ids)
                ORDER BY id
                FOR SHARE
                """;
            command.Parameters.AddWithValue("customer_id", customerId);
            command.Parameters.AddWithValue(
                "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, allTagIds);
            var current = new Dictionary<Guid, StoredTagStamp>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var stamp = new StoredTagStamp(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetBoolean(2),
                        FormatStamp(reader.GetFieldValue<DateTime>(3)));
                    current[stamp.Id] = stamp;
                }
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
            if (expected.Count != current.Count ||
                expected.Any(pair => !current.TryGetValue(pair.Key, out var value) || value != pair.Value))
            {
                throw new TransactionStoreException(TransactionErrorCodes.ClassificationChanged);
            }
        }
    }

    private static async Task<(DateTime CapturedAt, DateTime ExpiresAt)> ReadOperationClockAsync(NpgsqlConnection connection,
                                                                                                 NpgsqlTransaction transaction,
                                                                                                 CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT clock_timestamp(),
                   clock_timestamp() + make_interval(mins => @minutes)
            """;
        command.Parameters.AddWithValue("minutes", TransactionLimits.OperationExpiryMinutes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetFieldValue<DateTime>(0), reader.GetFieldValue<DateTime>(1));
    }

    private static async Task<DateTime> ClockAsync(NpgsqlConnection connection,
                                                   NpgsqlTransaction transaction,
                                                   CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT clock_timestamp()";
        return (DateTime)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> IsExpiredAsync(NpgsqlConnection connection,
                                                   NpgsqlTransaction transaction,
                                                   DateTime expiresAt,
                                                   CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT clock_timestamp() >= @expires_at";
        command.Parameters.AddWithValue("expires_at", expiresAt);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task CheckQuotaAsync(NpgsqlConnection connection,
                                              NpgsqlTransaction transaction,
                                              Guid customerId,
                                              int selectionCount,
                                              CancellationToken cancellationToken)
    {
        long openOperations;
        await using (var open = connection.CreateCommand())
        {
            open.Transaction = transaction;
            open.CommandText = """
                SELECT COUNT(*)
                FROM transaction_operations
                WHERE customer_id = @customer_id
                  AND status = 'Prepared'
                  AND expires_at > clock_timestamp()
                """;
            open.Parameters.AddWithValue("customer_id", customerId);
            openOperations = (long)(await open.ExecuteScalarAsync(cancellationToken))!;
        }

        long liveEntries;
        await using (var entries = connection.CreateCommand())
        {
            entries.Transaction = transaction;
            entries.CommandText = """
                SELECT COUNT(*)
                FROM transaction_operation_items i
                JOIN transaction_operations o
                  ON o.customer_id = i.customer_id AND o.operation_id = i.operation_id
                WHERE o.customer_id = @customer_id
                  AND o.status = 'Prepared'
                  AND o.expires_at > clock_timestamp()
                """;
            entries.Parameters.AddWithValue("customer_id", customerId);
            liveEntries = (long)(await entries.ExecuteScalarAsync(cancellationToken))!;
        }

        if (openOperations >= TransactionLimits.MaxOpenOperations ||
            liveEntries + selectionCount > TransactionLimits.MaxLiveManifestEntries)
        {
            throw new TransactionStoreException(TransactionErrorCodes.OperationQuota);
        }
    }

    private static async Task InsertHeaderAsync(NpgsqlConnection connection,
                                                NpgsqlTransaction transaction,
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
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO transaction_operations (
                customer_id, operation_id, status, selection_mode, request_digest,
                filter_digest, confirmation_hash, action, target_stamps,
                selected_count, changed_count, unchanged_count,
                captured_at, expires_at, created_at, updated_at)
            VALUES (
                @customer_id, @operation_id, 'Prepared', @selection_mode, @request_digest,
                @filter_digest, @confirmation_hash, @action, @targets,
                @selected_count, @changed_count, @unchanged_count,
                @captured_at, @expires_at, NOW(), NOW())
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("operation_id", operationId);
        command.Parameters.AddWithValue("selection_mode", selectionMode);
        command.Parameters.AddWithValue("request_digest", requestDigest);
        command.Parameters.AddWithValue(
            "filter_digest", NpgsqlDbType.Text, (object?)filterDigest ?? DBNull.Value);
        command.Parameters.AddWithValue("confirmation_hash", confirmationHash);
        command.Parameters.AddWithValue(
            "action",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(
                new StoredAction(
                    action.CategoryId,
                    action.AddTagIds,
                    action.RemoveTagIds,
                    action.IsHiddenByUser),
                _storedJson));
        command.Parameters.AddWithValue(
            "targets", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(targets, _storedJson));
        command.Parameters.AddWithValue("selected_count", selectedCount);
        command.Parameters.AddWithValue("changed_count", changedCount);
        command.Parameters.AddWithValue("unchanged_count", unchangedCount);
        command.Parameters.AddWithValue("captured_at", capturedAt);
        command.Parameters.AddWithValue("expires_at", expiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertManifestAsync(NpgsqlConnection connection,
                                                  NpgsqlTransaction transaction,
                                                  Guid customerId,
                                                  Guid operationId,
                                                  IReadOnlyList<MemberRow> members,
                                                  CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(
            """
            COPY transaction_operation_items (
                customer_id, operation_id, transaction_id, transaction_version,
                financial_account_id, statement_import_id, ordinal)
            FROM STDIN (FORMAT BINARY)
            """,
            cancellationToken);

        var ordinal = 0;
        foreach (var member in members)
        {
            // Npgsql's synchronous binary writer batches rows; awaited
            // per-field async writes are an order of magnitude slower at the
            // 50,000-member bound.
            importer.StartRow();
            importer.Write(customerId, NpgsqlDbType.Uuid);
            importer.Write(operationId, NpgsqlDbType.Uuid);
            importer.Write(member.Id, NpgsqlDbType.Uuid);
            importer.Write(member.Version, NpgsqlDbType.Integer);
            importer.Write(member.AccountId, NpgsqlDbType.Uuid);
            importer.Write(member.StatementId, NpgsqlDbType.Uuid);
            importer.Write(ordinal, NpgsqlDbType.Integer);
            ordinal++;
        }

        importer.Complete();
    }

    private static async Task UpdateMembersAsync(NpgsqlConnection connection,
                                                 NpgsqlTransaction transaction,
                                                 Guid customerId,
                                                 Guid[] transactionIds,
                                                 TransactionAction action,
                                                 CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE transactions
            SET category_id = CASE WHEN @set_category THEN @category_id ELSE category_id END,
                is_hidden_by_user = CASE WHEN @set_hidden THEN @hidden ELSE is_hidden_by_user END,
                version = version + 1,
                updated_at = clock_timestamp()
            WHERE customer_id = @customer_id AND id = ANY(@ids)
            """;
        command.Parameters.AddWithValue("set_category", action.CategoryId is not null);
        command.Parameters.AddWithValue(
            "category_id",
            NpgsqlDbType.Uuid,
            (object?)action.CategoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("set_hidden", action.IsHiddenByUser is not null);
        command.Parameters.AddWithValue(
            "hidden",
            NpgsqlDbType.Boolean,
            (object?)action.IsHiddenByUser ?? DBNull.Value);
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue(
            "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, transactionIds);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != transactionIds.Length)
        {
            throw new TransactionStoreException(TransactionErrorCodes.SelectionStale);
        }
    }

    private static async Task MutateTagLinksAsync(NpgsqlConnection connection,
                                                  NpgsqlTransaction transaction,
                                                  Guid customerId,
                                                  Guid[] transactionIds,
                                                  TransactionAction action,
                                                  CancellationToken cancellationToken)
    {
        if (action.AddTagIds.Length > 0)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO transaction_merchant_tags (customer_id, transaction_id, merchant_tag_id)
                SELECT @customer_id, target.id, add.tag_id
                FROM unnest(@ids) AS target(id)
                CROSS JOIN unnest(@add_ids) AS add(tag_id)
                ON CONFLICT (transaction_id, merchant_tag_id) DO NOTHING
                """;
            insert.Parameters.AddWithValue("customer_id", customerId);
            insert.Parameters.AddWithValue(
                "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, transactionIds);
            insert.Parameters.AddWithValue(
                "add_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, action.AddTagIds);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        if (action.RemoveTagIds.Length > 0)
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM transaction_merchant_tags j
                USING unnest(@ids) AS target(id)
                WHERE j.customer_id = @customer_id
                  AND j.transaction_id = target.id
                  AND j.merchant_tag_id = ANY(@remove_ids)
                """;
            delete.Parameters.AddWithValue("customer_id", customerId);
            delete.Parameters.AddWithValue(
                "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, transactionIds);
            delete.Parameters.AddWithValue(
                "remove_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, action.RemoveTagIds);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static bool IsChanged(MemberRow member,
                                  TransactionAction action)
    {
        if (action.CategoryId is not null && action.CategoryId.Value != member.CategoryId)
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

    private static void ValidateTagLimit(MemberRow member,
                                         TransactionAction action)
    {
        var existing = member.TagIds.ToHashSet();
        var adds = action.AddTagIds.ToHashSet();
        var removes = action.RemoveTagIds.ToHashSet();
        var resulting = existing.Union(adds).Except(removes).Count();
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

    private static async Task EnsureRestoreAllowedAsync(NpgsqlConnection connection,
                                                        NpgsqlTransaction transaction,
                                                        Guid customerId,
                                                        IReadOnlyCollection<MemberRow> members,
                                                        TransactionAction action,
                                                        CancellationToken cancellationToken)
    {
        if (action.IsHiddenByUser != false)
        {
            return;
        }

        var hiddenStatementIds = members
            .Where(member => member.StatementHidden)
            .Select(member => member.StatementId)
            .Distinct()
            .Take(5)
            .ToArray();
        if (hiddenStatementIds.Length == 0)
        {
            return;
        }

        var links = new List<Dictionary<string, object?>>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, statement_label
            FROM statement_imports
            WHERE customer_id = @customer_id AND id = ANY(@ids)
            ORDER BY id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue(
            "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, hiddenStatementIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            links.Add(new Dictionary<string, object?>
            {
                ["id"] = reader.GetGuid(0),
                ["statement_label"] = reader.GetString(1),
            });
        }

        throw new TransactionStoreException(
            TransactionErrorCodes.ParentHidden,
            new Dictionary<string, object?> { ["statements"] = links });
    }

    private static async Task<List<ManifestRow>> ReadManifestAsync(NpgsqlConnection connection,
                                                                   NpgsqlTransaction transaction,
                                                                   Guid customerId,
                                                                   Guid operationId,
                                                                   CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT transaction_id, transaction_version, financial_account_id, statement_import_id
            FROM transaction_operation_items
            WHERE customer_id = @customer_id AND operation_id = @operation_id
            ORDER BY transaction_id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("operation_id", operationId);
        var manifest = new List<ManifestRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            manifest.Add(new ManifestRow(
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetGuid(2),
                reader.GetGuid(3)));
        }
        return manifest;
    }

    private static async Task<OperationHeader?> ReadHeaderAsync(NpgsqlConnection connection,
                                                                NpgsqlTransaction transaction,
                                                                Guid customerId,
                                                                Guid operationId,
                                                                bool forUpdate,
                                                                CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT customer_id, operation_id, status, selection_mode, request_digest,
                   filter_digest, confirmation_hash, action::text, target_stamps::text,
                   selected_count, changed_count, unchanged_count,
                   captured_at, expires_at, applied_at, terminal_at
            FROM transaction_operations
            WHERE customer_id = @customer_id AND operation_id = @operation_id
            {(forUpdate ? "FOR UPDATE" : string.Empty)}
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("operation_id", operationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new OperationHeader(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.GetFieldValue<DateTime>(12),
            reader.GetFieldValue<DateTime>(13),
            reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTime>(14),
            reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTime>(15));
    }

    private async Task<TransactionOperationSummaryResponse> BuildPreparedSummaryAsync(NpgsqlConnection connection,
                                                                                      NpgsqlTransaction transaction,
                                                                                      OperationHeader header,
                                                                                      CancellationToken cancellationToken)
    {
        var action = DeserializeAction(header.ActionJson);
        var stored = DeserializeTargets(header.TargetJson);
        var resolved = await ResolveActionAsync(
            connection, transaction, header.CustomerId, action, stored, cancellationToken);
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

    private async Task<TransactionOperationActionResponse> ResolveActionAsync(NpgsqlConnection connection,
                                                                              NpgsqlTransaction transaction,
                                                                              Guid customerId,
                                                                              TransactionAction action,
                                                                              StoredTargets stored,
                                                                              CancellationToken cancellationToken)
    {
        var stale = false;
        TransactionCategoryRef? category = null;
        if (action.CategoryId is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, display_name, kind, is_system, is_archived, updated_at
                FROM categories
                WHERE customer_id = @customer_id AND id = @id
                """;
            command.Parameters.AddWithValue("customer_id", customerId);
            command.Parameters.AddWithValue("id", action.CategoryId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                stale = true;
            }
            else
            {
                var stamp = new StoredCategoryStamp(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(4),
                    FormatStamp(reader.GetFieldValue<DateTime>(5)));
                category = new TransactionCategoryRef(
                    stamp.Id,
                    stamp.DisplayName,
                    stamp.Kind,
                    reader.GetBoolean(3),
                    stamp.IsArchived);
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
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, display_name, is_archived, updated_at
                FROM merchant_tags
                WHERE customer_id = @customer_id AND id = ANY(@ids)
                """;
            command.Parameters.AddWithValue("customer_id", customerId);
            command.Parameters.AddWithValue(
                "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, tagIds);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var stamp = new StoredTagStamp(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetBoolean(2),
                    FormatStamp(reader.GetFieldValue<DateTime>(3)));
                current[stamp.Id] = new TransactionTagRef(
                    stamp.Id, stamp.DisplayName, stamp.IsArchived);
                currentStamps[stamp.Id] = stamp;
            }
        }

        var expectedStamps = stored.AddTags.Concat(stored.RemoveTags)
            .ToDictionary(tag => tag.Id);
        if (expectedStamps.Count != currentStamps.Count ||
            expectedStamps.Any(pair =>
                !currentStamps.TryGetValue(pair.Key, out var stamp) || stamp != pair.Value))
        {
            stale = true;
        }

        TransactionTagRef[] RefsFor(Guid[] ids) => ids
            .Where(current.ContainsKey)
            .Select(id => current[id])
            .ToArray();

        return new TransactionOperationActionResponse(
            category,
            RefsFor(action.AddTagIds),
            RefsFor(action.RemoveTagIds),
            action.IsHiddenByUser,
            stale);
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

    private static async Task ExpireHeaderAsync(NpgsqlConnection connection,
                                                NpgsqlTransaction transaction,
                                                Guid customerId,
                                                Guid operationId,
                                                CancellationToken cancellationToken)
    {
        await using (var items = connection.CreateCommand())
        {
            items.Transaction = transaction;
            items.CommandText = """
                DELETE FROM transaction_operation_items
                WHERE customer_id = @customer_id AND operation_id = @operation_id
                """;
            items.Parameters.AddWithValue("customer_id", customerId);
            items.Parameters.AddWithValue("operation_id", operationId);
            await items.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE transaction_operations
            SET status = 'Expired',
                terminal_at = clock_timestamp(),
                action = '{}'::jsonb,
                target_stamps = '{}'::jsonb,
                updated_at = clock_timestamp()
            WHERE customer_id = @customer_id AND operation_id = @operation_id
              AND status = 'Prepared'
            """;
        update.Parameters.AddWithValue("customer_id", customerId);
        update.Parameters.AddWithValue("operation_id", operationId);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<DateTime> MarkAppliedAsync(NpgsqlConnection connection,
                                                         NpgsqlTransaction transaction,
                                                         Guid customerId,
                                                         Guid operationId,
                                                         CancellationToken cancellationToken)
    {
        await using (var items = connection.CreateCommand())
        {
            items.Transaction = transaction;
            items.CommandText = """
                DELETE FROM transaction_operation_items
                WHERE customer_id = @customer_id AND operation_id = @operation_id
                """;
            items.Parameters.AddWithValue("customer_id", customerId);
            items.Parameters.AddWithValue("operation_id", operationId);
            await items.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE transaction_operations
            SET status = 'Applied',
                applied_at = clock_timestamp(),
                terminal_at = clock_timestamp(),
                action = '{}'::jsonb,
                target_stamps = '{}'::jsonb,
                updated_at = clock_timestamp()
            WHERE customer_id = @customer_id AND operation_id = @operation_id
            RETURNING applied_at
            """;
        update.Parameters.AddWithValue("customer_id", customerId);
        update.Parameters.AddWithValue("operation_id", operationId);
        return (DateTime)(await update.ExecuteScalarAsync(cancellationToken))!;
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
        if (!string.Equals(header.ConfirmationHash, confirmationHash, StringComparison.Ordinal) ||
            header.SelectedCount != expectedSelectedCount)
        {
            throw new TransactionStoreException(TransactionErrorCodes.IdempotencyConflict);
        }
        return new TransactionOperationApplyResponse(
            header.OperationId,
            header.Status,
            header.SelectedCount,
            header.ChangedCount,
            header.UnchangedCount,
            header.AppliedAt is null ? null : FormatTimestamp(header.AppliedAt.Value),
            replayed);
    }

    private static TransactionAction DeserializeAction(string json)
    {
        var stored = JsonSerializer.Deserialize<StoredAction>(json, _storedJson)
            ?? throw new TransactionStoreException(TransactionErrorCodes.InvalidRequest);
        return new TransactionAction(
            stored.CategoryId, stored.AddTagIds, stored.RemoveTagIds, stored.IsHiddenByUser);
    }

    private static StoredTargets DeserializeTargets(string json) =>
        JsonSerializer.Deserialize<StoredTargets>(json, _storedJson)
        ?? throw new TransactionStoreException(TransactionErrorCodes.InvalidRequest);

    private static string RequestDigest(TransactionSelectionPlan selection,
                                        TransactionAction action)
    {
        if (selection.Mode == "explicit")
        {
            var selectionCanonical = string.Join(
                '\n',
                selection.ExplicitMembers
                    .OrderBy(member => member.Id)
                    .Select(member => $"{member.Id:N}:{member.ExpectedVersion}"));
            return Sha256(string.Join(
                '|',
                "v1",
                "mode=explicit",
                $"selection={Sha256(selectionCanonical)}",
                $"action={action.Canonical()}"));
        }

        return Sha256(string.Join(
            '|',
            "v1",
            "mode=all_matching",
            $"filters={Sha256(selection.Filters!.Canonical())}",
            $"action={action.Canonical()}"));
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
            parts.Add(string.Join(
                '|',
                "c",
                targets.Category.Id.ToString("N"),
                Encode(targets.Category.DisplayName),
                targets.Category.Kind,
                targets.Category.IsArchived,
                targets.Category.UpdatedAt));
        }
        foreach (var tag in targets.AddTags.OrderBy(tag => tag.Id))
        {
            parts.Add(string.Join(
                '|',
                "a",
                tag.Id.ToString("N"),
                Encode(tag.DisplayName),
                tag.IsArchived,
                tag.UpdatedAt));
        }
        foreach (var tag in targets.RemoveTags.OrderBy(tag => tag.Id))
        {
            parts.Add(string.Join(
                '|',
                "r",
                tag.Id.ToString("N"),
                Encode(tag.DisplayName),
                tag.IsArchived,
                tag.UpdatedAt));
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
        var payload = string.Join(
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

    private static async Task SetReadOnlyAsync(NpgsqlConnection connection,
                                               NpgsqlTransaction transaction,
                                               CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SET TRANSACTION READ ONLY";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ValidateFilterOwnershipAsync(NpgsqlConnection connection,
                                                           NpgsqlTransaction transaction,
                                                           Guid customerId,
                                                           TransactionFilter filter,
                                                           CancellationToken cancellationToken)
    {
        if (filter.FinancialAccountId is not null)
        {
            await RequireOwnedAsync(
                connection, transaction, customerId, "financial_accounts",
                filter.FinancialAccountId.Value, cancellationToken);
        }
        if (filter.StatementImportId is not null)
        {
            await RequireOwnedAsync(
                connection, transaction, customerId, "statement_imports",
                filter.StatementImportId.Value, cancellationToken);
        }
        if (filter.CategoryId is not null)
        {
            await RequireOwnedAsync(
                connection, transaction, customerId, "categories",
                filter.CategoryId.Value, cancellationToken);
        }
        if (filter.MerchantTagId is not null)
        {
            await RequireOwnedAsync(
                connection, transaction, customerId, "merchant_tags",
                filter.MerchantTagId.Value, cancellationToken);
        }
    }

    internal static async Task RequireOwnedAsync(NpgsqlConnection connection,
                                                 NpgsqlTransaction? transaction,
                                                 Guid customerId,
                                                 string table,
                                                 Guid id,
                                                 CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT 1 FROM {table} WHERE customer_id = @customer_id AND id = @id";
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", id);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }
    }

    private async Task<List<RowData>> ReadRowsAsync(NpgsqlConnection connection,
                                                    NpgsqlTransaction transaction,
                                                    Guid customerId,
                                                    TransactionFilter filter,
                                                    string orderClause,
                                                    int limit,
                                                    long offset,
                                                    bool includeVisibility,
                                                    CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var where = TransactionSql.BuildWhere(filter, command, includeVisibility);
        command.CommandText = $"""
            SELECT {RowColumns}
            FROM transactions t
            {TransactionSql.Joins}
            WHERE {where}
            ORDER BY {orderClause}
            LIMIT @limit OFFSET @offset
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);

        var rawRows = new List<RawRow>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rawRows.Add(ReadRawRow(reader));
            }
        }

        var tags = await LoadAssignedTagsAsync(
            connection,
            transaction,
            customerId,
            rawRows.Select(row => row.Id).ToArray(),
            cancellationToken);
        return rawRows.Select(raw => MapRow(raw, tags)).ToList();
    }

    private async Task<List<RowData>> ReadRowsByIdsAsync(NpgsqlConnection connection,
                                                         NpgsqlTransaction transaction,
                                                         Guid customerId,
                                                         Guid[] ids,
                                                         CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {RowColumns}
            FROM transactions t
            {TransactionSql.Joins}
            WHERE t.customer_id = @customer_id AND t.id = ANY(@ids)
            ORDER BY t.id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, ids);

        var rawRows = new List<RawRow>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rawRows.Add(ReadRawRow(reader));
            }
        }

        var tags = await LoadAssignedTagsAsync(
            connection,
            transaction,
            customerId,
            rawRows.Select(row => row.Id).ToArray(),
            cancellationToken);
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
                                 Guid StatementId,
                                 string StatementLabel,
                                 string StatementSourceType,
                                 bool StatementHidden,
                                 bool HiddenByUser,
                                 int? SourcePage,
                                 int SourceRowNumber);

    private const string RowColumns = """
        t.id, t.version, t.transaction_date, t.description, t.normalized_description, t.amount,
        a.id, a.display_name, a.is_archived,
        c.id, c.display_name, c.kind, c.is_system, c.is_archived,
        s.id, s.statement_label, s.source_type, s.is_hidden,
        t.is_hidden_by_user, t.source_page, t.source_row_number
        """;

    private static RawRow ReadRawRow(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetInt32(1),
        reader.GetFieldValue<DateOnly>(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetDecimal(5),
        reader.GetGuid(6),
        reader.GetString(7),
        reader.GetBoolean(8),
        reader.GetGuid(9),
        reader.GetString(10),
        reader.GetString(11),
        reader.GetBoolean(12),
        reader.GetBoolean(13),
        reader.GetGuid(14),
        reader.GetString(15),
        reader.GetString(16),
        reader.GetBoolean(17),
        reader.GetBoolean(18),
        reader.IsDBNull(19) ? null : reader.GetInt32(19),
        reader.GetInt32(20));

    private static RowData MapRow(RawRow raw,
                                  IReadOnlyDictionary<Guid, TransactionTagRef[]> tags)
    {
        var amount = TransactionRequestParser.FormatAmount(raw.Amount);
        var direction = TransactionRequestParser.DirectionOf(raw.Amount);
        var effectiveHidden = raw.StatementHidden || raw.HiddenByUser;
        var reason = raw.StatementHidden && raw.HiddenByUser
            ? "both"
            : raw.StatementHidden
                ? "statement"
                : raw.HiddenByUser
                    ? "individual"
                    : "none";

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
            tags.GetValueOrDefault(raw.Id) ?? [],
            new TransactionStatementRef(
                raw.StatementId,
                raw.StatementLabel,
                raw.StatementSourceType,
                raw.StatementHidden),
            raw.HiddenByUser,
            effectiveHidden,
            reason);

        var tagIds = (tags.GetValueOrDefault(raw.Id) ?? [])
            .Select(tag => tag.Id)
            .ToArray();
        return new RowData(response, raw.NormalizedDescription, tagIds, raw.SourcePage, raw.SourceRowNumber);
    }

    private static async Task<Dictionary<Guid, TransactionTagRef[]>> LoadAssignedTagsAsync(NpgsqlConnection connection,
                                                                                           NpgsqlTransaction transaction,
                                                                                           Guid customerId,
                                                                                           IReadOnlyCollection<Guid> transactionIds,
                                                                                           CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, TransactionTagRef[]>();
        if (transactionIds.Count == 0)
        {
            return result;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT j.transaction_id, m.id, m.display_name, m.is_archived
            FROM transaction_merchant_tags j
            JOIN merchant_tags m
              ON m.customer_id = j.customer_id AND m.id = j.merchant_tag_id
            WHERE j.customer_id = @customer_id AND j.transaction_id = ANY(@ids)
            ORDER BY m.display_name, m.id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue(
            "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, transactionIds.ToArray());

        var collected = new Dictionary<Guid, List<TransactionTagRef>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var transactionId = reader.GetGuid(0);
            if (!collected.TryGetValue(transactionId, out var list))
            {
                list = [];
                collected[transactionId] = list;
            }
            list.Add(new TransactionTagRef(
                reader.GetGuid(1), reader.GetString(2), reader.GetBoolean(3)));
        }

        foreach (var (id, list) in collected)
        {
            result[id] = list.ToArray();
        }
        return result;
    }

    private static async Task<IReadOnlyList<(Guid Id, string DisplayName, string[] Tokens)>>
        LoadActiveTagsAsync(NpgsqlConnection connection,
                            NpgsqlTransaction transaction,
                            Guid customerId,
                            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, display_name, normalized_name
            FROM merchant_tags
            WHERE customer_id = @customer_id AND is_archived = FALSE
            ORDER BY normalized_name, id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        var tags = new List<(Guid Id, string DisplayName, string[] Tokens)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tags.Add((
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2).Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }
        return tags;
    }
}
