using System.Globalization;
using System.Text;

namespace argus.Server.Transactions;

/// <summary>Frozen transaction workspace error codes mapped to fixed HTTP statuses.</summary>
public static class TransactionErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string EmptySelection = "empty_selection";
    public const string CategoryUnavailable = "category_unavailable";
    public const string TagUnavailable = "tag_unavailable";
    public const string TagLimitExceeded = "tag_limit_exceeded";
    public const string SelectionTooLarge = "selection_too_large";
    public const string RequestTooLarge = "request_too_large";
    public const string VersionConflict = "version_conflict";
    public const string SelectionStale = "selection_stale";
    public const string ClassificationChanged = "classification_changed";
    public const string ParentHidden = "parent_hidden";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string OperationBusy = "operation_busy";
    public const string OperationExpired = "operation_expired";
    public const string OperationAbandoned = "operation_abandoned";
    public const string OperationQuota = "operation_quota";
    public const string NotFound = "not_found";
    public const string RateLimited = "rate_limited";
    public const string Unavailable = "unavailable";
}

public enum TransactionVisibility
{
    Visible,
    Hidden,
    All,
}

/// <summary>Validated, owned-scope transaction filter. Search stays in bodies only.</summary>
public sealed record TransactionFilter(string? Search,
                                       DateOnly? StartDate,
                                       DateOnly? EndDate,
                                       Guid? FinancialAccountId,
                                       Guid? StatementImportId,
                                       Guid? CategoryId,
                                       Guid? MerchantTagId,
                                       bool Untagged,
                                       string? CategoryKind,
                                       string? Direction,
                                       TransactionVisibility Visibility)
{
    public static TransactionFilter Default { get; } = new(
        null, null, null, null, null, null, null, false, null, null, TransactionVisibility.Visible);

    public string Canonical()
    {
        string search = Search is null ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(Search));

        return string.Join(
            '|',
            $"search={search}",
            $"start={Format(StartDate)}",
            $"end={Format(EndDate)}",
            $"account={FinancialAccountId?.ToString("N") ?? string.Empty}",
            $"statement={StatementImportId?.ToString("N") ?? string.Empty}",
            $"category={CategoryId?.ToString("N") ?? string.Empty}",
            $"tag={MerchantTagId?.ToString("N") ?? string.Empty}",
            $"untagged={Untagged}",
            $"kind={CategoryKind ?? string.Empty}",
            $"direction={Direction ?? string.Empty}",
            $"visibility={Visibility}");
    }

    private static string Format(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>The full validated query plan after field and enum whitelisting.</summary>
public sealed record TransactionQueryPlan(TransactionFilter Filter,
                                          int Page,
                                          int PageSize,
                                          string SortBy,
                                          string SortDirection);

/// <summary>Single-tag or Untagged filter; never both.</summary>
public enum MerchantTagFilterKind
{
    None,
    Tag,
    Untagged,
}

public sealed record TransactionAccountRef(Guid Id, string DisplayName, bool IsArchived);

public sealed record TransactionCategoryRef(Guid Id,
                                            string DisplayName,
                                            string Kind,
                                            bool IsSystem,
                                            bool IsArchived);

public sealed record TransactionTagRef(Guid Id, string DisplayName, bool IsArchived);

public sealed record TransactionStatementRef(Guid Id,
                                             string StatementLabel,
                                             string SourceType,
                                             bool IsHidden);

/// <summary>
/// Public transaction row. Amounts travel as exact invariant strings with two
/// fractional digits; callers never receive a JSON number for money.
/// </summary>
public sealed record TransactionResponse(Guid Id,
                                         int Version,
                                         string TransactionDate,
                                         string Description,
                                         string Amount,
                                         string Currency,
                                         string Direction,
                                         TransactionAccountRef FinancialAccount,
                                         TransactionCategoryRef Category,
                                         TransactionTagRef[] MerchantTags,
                                         TransactionStatementRef StatementImport,
                                         bool IsHiddenByUser,
                                         bool EffectiveHidden,
                                         string HiddenReason);

public sealed record TransactionPageResult(TransactionResponse[] Transactions,
                                           long InventoryCount,
                                           long MatchingVisibleCount,
                                           long MatchingHiddenCount,
                                           long MatchingTotal,
                                           long FilteredTotal,
                                           int Page,
                                           int PageSize,
                                           string SortBy,
                                           string SortDirection);

public sealed record TransactionDetailResult(TransactionResponse Transaction,
                                             TransactionTagRef[] SuggestedTags,
                                             bool HasMoreSuggestions,
                                             int? SourcePage,
                                             int SourceRowNumber);

public sealed record TransactionMutationResult(TransactionResponse Transaction,
                                               int SelectedCount,
                                               int ChangedCount,
                                               int UnchangedCount);

/// <summary>Validated metadata action shared by single edits and bulk operations.</summary>
public sealed record TransactionAction(Guid? CategoryId,
                                       Guid[] AddTagIds,
                                       Guid[] RemoveTagIds,
                                       bool? IsHiddenByUser)
{
    public bool IsEmpty =>
        CategoryId is null && AddTagIds.Length == 0 && RemoveTagIds.Length == 0 &&
        IsHiddenByUser is null;

    public string Canonical() => string.Join(
        '|',
        $"category={CategoryId?.ToString("N") ?? string.Empty}",
        $"add={string.Join(',', AddTagIds.Select(id => id.ToString("N")).Order(StringComparer.Ordinal))}",
        $"remove={string.Join(',', RemoveTagIds.Select(id => id.ToString("N")).Order(StringComparer.Ordinal))}",
        $"hidden={IsHiddenByUser?.ToString() ?? string.Empty}");
}

public sealed record TransactionSelectionMember(Guid Id, int ExpectedVersion);

public sealed record TransactionSelectionPlan(string Mode, TransactionSelectionMember[] ExplicitMembers, TransactionFilter? Filters);

public static class TransactionRequestParser
{
    public static bool TryParseQuery(TransactionQueryRequest request, out TransactionQueryPlan plan, out string error)
    {
        plan = new TransactionQueryPlan(TransactionFilter.Default, 1, 25, "date", "desc");
        error = TransactionErrorCodes.InvalidRequest;

        int page = request.Page ?? 1;
        if (page < 1)
        {
            return false;
        }

        int pageSize = request.PageSize ?? TransactionLimits.DefaultPageSize;
        if (!TransactionLimits.PageSizes.Contains(pageSize))
        {
            return false;
        }

        string sortBy = request.SortBy ?? "date";
        if (sortBy is not ("date" or "amount" or "description"))
        {
            return false;
        }

        string sortDirection = request.SortDirection ?? "desc";
        if (sortDirection is not ("asc" or "desc"))
        {
            return false;
        }

        if (!TryParseFilter(request.Filters, out var filter))
        {
            return false;
        }

        plan = new TransactionQueryPlan(filter, page, pageSize, sortBy, sortDirection);
        error = string.Empty;

        return true;
    }

    public static bool TryParseFilter(TransactionFiltersRequest? request, out TransactionFilter filter)
    {
        filter = TransactionFilter.Default;
        if (request is null)
        {
            return true;
        }

        string? search = null;
        if (request.Search is not null)
        {
            string trimmed = request.Search.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                int scalars = 0;
                foreach (var _ in trimmed.EnumerateRunes())
                {
                    scalars++;
                    if (scalars > TransactionLimits.MaxSearchScalars)
                    {
                        return false;
                    }
                }
                search = trimmed;
            }
        }

        if (!TryParseDate(request.StartDate, out var startDate) || !TryParseDate(request.EndDate, out var endDate))
        {
            return false;
        }

        if (startDate is not null && endDate is not null && startDate > endDate)
        {
            return false;
        }

        Guid? merchantTagId = null;
        bool untagged = false;
        if (request.MerchantTag is not null)
        {
            if (string.Equals(request.MerchantTag, "untagged", StringComparison.Ordinal))
            {
                untagged = true;
            }
            else if (Guid.TryParse(request.MerchantTag, out var tagId) && tagId != Guid.Empty)
            {
                merchantTagId = tagId;
            }
            else
            {
                return false;
            }
        }

        if (request.CategoryKind is not null && request.CategoryKind is not ("Income" or "Expense" or "Transfer" or "Excluded" or "Uncategorised"))
        {
            return false;
        }

        if (request.Direction is not null && request.Direction is not ("in" or "out" or "zero"))
        {
            return false;
        }

        var visibility = request.Visibility switch
        {
            null or "visible" => TransactionVisibility.Visible,
            "hidden" => TransactionVisibility.Hidden,
            "all" => TransactionVisibility.All,
            _ => (TransactionVisibility?)null,
        };
        if (visibility is null)
        {
            return false;
        }

        if (request.FinancialAccountId == Guid.Empty || request.StatementImportId == Guid.Empty || request.CategoryId == Guid.Empty || merchantTagId == Guid.Empty)
        {
            return false;
        }

        filter = new TransactionFilter(search, startDate, endDate, request.FinancialAccountId, request.StatementImportId, request.CategoryId, merchantTagId, untagged, request.CategoryKind, request.Direction, visibility.Value);

        return true;
    }

    public static bool TryParseAction(TransactionChangesRequest? request, out TransactionAction action)
    {
        action = new TransactionAction(null, [], [], null);
        if (request is null)
        {
            return false;
        }

        if (request.CategoryId == Guid.Empty)
        {
            return false;
        }

        var add = request.AddTagIds ?? [];
        var remove = request.RemoveTagIds ?? [];
        if (add.Length > TransactionLimits.MaxTagIdsPerRequest || remove.Length > TransactionLimits.MaxTagIdsPerRequest)
        {
            return false;
        }

        if (add.Any(id => id == Guid.Empty) || remove.Any(id => id == Guid.Empty))
        {
            return false;
        }

        var addSet = add.ToHashSet();
        var removeSet = remove.ToHashSet();
        if (addSet.Count != add.Length || removeSet.Count != remove.Length)
        {
            return false;
        }

        if (addSet.Overlaps(removeSet))
        {
            return false;
        }

        action = new TransactionAction(request.CategoryId, addSet.Order().ToArray(), removeSet.Order().ToArray(), request.IsHiddenByUser);

        return !action.IsEmpty;
    }

    public static bool TryParseSelection(TransactionSelectionRequest? request, out TransactionSelectionPlan plan)
    {
        plan = new TransactionSelectionPlan("explicit", [], null);
        if (request?.Mode == "explicit")
        {
            if (request.Filters is not null || request.Transactions is null || request.Transactions.Length == 0 || request.Transactions.Length > TransactionLimits.MaxExplicitSelection)
            {
                return false;
            }

            var members = new List<TransactionSelectionMember>(request.Transactions.Length);
            var seen = new HashSet<Guid>();
            foreach (var item in request.Transactions)
            {
                if (item.Id is null || item.ExpectedVersion is null || item.ExpectedVersion < 1)
                {
                    return false;
                }

                if (item.Id.Value == Guid.Empty || !seen.Add(item.Id.Value))
                {
                    return false;
                }

                members.Add(new TransactionSelectionMember(item.Id.Value, item.ExpectedVersion.Value));
            }

            plan = new TransactionSelectionPlan("explicit", members.ToArray(), null);
            return true;
        }

        if (request?.Mode == "all_matching")
        {
            if (request.Transactions is not null || !TryParseFilter(request.Filters, out var filter))
            {
                return false;
            }

            plan = new TransactionSelectionPlan("all_matching", [], filter);
            return true;
        }

        return false;
    }

    public static bool TryParseDate(string? value, out DateOnly? date)
    {
        date = null;
        if (value is null)
        {
            return true;
        }

        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        date = parsed;

        return true;
    }

    /// <summary>
    /// Exact invariant amount string. The database value is read as decimal;
    /// negative zero is normalized to zero and two fractional digits are kept.
    /// </summary>
    public static string FormatAmount(decimal amount) =>
        amount == 0m ? "0.00" : amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static string DirectionOf(decimal amount) =>
        amount > 0m ? "in" : amount < 0m ? "out" : "zero";
}

// --- Raw HTTP bodies --------------------------------------------------------

public sealed record TransactionFiltersRequest(string? Search,
                                               string? StartDate,
                                               string? EndDate,
                                               Guid? FinancialAccountId,
                                               Guid? StatementImportId,
                                               Guid? CategoryId,
                                               string? MerchantTag,
                                               string? CategoryKind,
                                               string? Direction,
                                               string? Visibility);

public sealed record TransactionQueryRequest(TransactionFiltersRequest? Filters,
                                             int? Page,
                                             int? PageSize,
                                             string? SortBy,
                                             string? SortDirection);

public sealed record TransactionChangesRequest(Guid? CategoryId,
                                               Guid[]? AddTagIds,
                                               Guid[]? RemoveTagIds,
                                               bool? IsHiddenByUser);

public sealed record TransactionSelectionItem(Guid? Id, int? ExpectedVersion);

public sealed record TransactionSelectionRequest(string? Mode, TransactionSelectionItem[]? Transactions, TransactionFiltersRequest? Filters);

public sealed record TransactionPatchRequest(int? ExpectedVersion, TransactionChangesRequest? Changes, bool? Confirm);

public sealed record TransactionPrepareRequest(Guid? OperationId, TransactionSelectionRequest? Selection, TransactionChangesRequest? Changes);

public sealed record TransactionApplyRequest(string? ConfirmationHash, int? ExpectedSelectedCount, bool? Confirm);

public sealed record EmptyBodyRequest();

// --- Responses --------------------------------------------------------------

public sealed record TransactionOperationActionResponse(TransactionCategoryRef? Category,
                                                        TransactionTagRef[] AddTags,
                                                        TransactionTagRef[] RemoveTags,
                                                        bool? IsHiddenByUser,
                                                        bool Stale);

public sealed record TransactionOperationSummaryResponse(Guid OperationId,
                                                         string Status,
                                                         string SelectionMode,
                                                         string CapturedAt,
                                                         string ExpiresAt,
                                                         int SelectedCount,
                                                         int ChangedCount,
                                                         int UnchangedCount,
                                                         string? ConfirmationHash,
                                                         string? AppliedAt,
                                                         string? TerminalAt,
                                                         TransactionOperationActionResponse? Action);

public sealed record TransactionOperationPrepareResponse(TransactionOperationSummaryResponse Operation, bool Created);

public sealed record TransactionOperationApplyResponse(Guid OperationId,
                                                       string Status,
                                                       int SelectedCount,
                                                       int ChangedCount,
                                                       int UnchangedCount,
                                                       string? AppliedAt,
                                                       bool Replayed);

public sealed class TransactionStoreException : Exception
{
    public TransactionStoreException(string code, IReadOnlyDictionary<string, object?>? details = null)
        : base(code)
    {
        Code = code;
        Details = details ?? new Dictionary<string, object?>();
    }

    public string Code { get; }

    public IReadOnlyDictionary<string, object?> Details { get; }
}
