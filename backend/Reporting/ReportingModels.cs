using argus.Server.Transactions;

namespace argus.Server.Reporting;

/// <summary>Frozen reporting error codes and their fixed HTTP statuses.</summary>
public static class ReportErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string NotFound = "not_found";
    public const string RequestTooLarge = "request_too_large";
    public const string Unauthorized = "unauthorized";
    public const string RateLimited = "rate_limited";
    public const string ReceiptIneligible = "receipt_ineligible";
    public const string VersionConflict = "version_conflict";
    public const string CycleSequenceChanged = "cycle_sequence_changed";
    public const string ReportBusy = "report_busy";
    public const string Unavailable = "unavailable";
}

// --- Request bodies ---------------------------------------------------------

public sealed record ReportPeriodRequest(string? Mode,
                                         string? Preset,
                                         string? Month,
                                         string? StartDate,
                                         string? EndDate,
                                         Guid? CycleStartId);

public sealed record ReportFiltersRequest(Guid? FinancialAccountId,
                                          Guid? CategoryId,
                                          string? MerchantTag,
                                          string? Direction);

public sealed record ReportQueryRequest(string? View,
                                        ReportPeriodRequest? Period,
                                        ReportFiltersRequest? Filters,
                                        int? TagInPage,
                                        int? TagOutPage,
                                        int? TransactionsPage,
                                        int? CoveragePage,
                                        int? ComparisonCoveragePage,
                                        int? CategoryChangesPage);

public sealed record SalaryCyclePreviewRequest(string? Action,
                                              Guid? TransactionId,
                                              int? ExpectedTransactionVersion,
                                              Guid? StartId);

public sealed record SalaryCycleConfirmRequest(Guid? TransactionId,
                                              int? ExpectedTransactionVersion,
                                              string? ExpectedRevision,
                                              bool? Confirm);

public sealed record SalaryCycleRemoveRequest(string? ExpectedRevision, bool? Confirm);

public sealed record SalaryCycleListQuery(string? BeforeDate);

public sealed record ReportQueryPlan(string View,
                                     ReportPeriodRequest? Period,
                                     bool PeriodNotFound,
                                     Guid? FinancialAccountId,
                                     Guid? CategoryId,
                                     Guid? MerchantTagId,
                                     bool Untagged,
                                     string? Direction,
                                     int TagInPage,
                                     int TagOutPage,
                                     int TransactionsPage,
                                     int CoveragePage,
                                     int ComparisonCoveragePage,
                                     int CategoryChangesPage);

// --- Period resolution ------------------------------------------------------

public sealed record SalaryCycleStartRef(Guid Id, DateOnly StartDate);

public sealed record ResolvedPeriod(string Mode,
                                    string? Preset,
                                    string Label,
                                    DateOnly Start,
                                    DateOnly End,
                                    bool IsPartial,
                                    bool IsOpenSalaryCycle,
                                    bool IsFuture,
                                    Guid? CycleStartId,
                                    string? Note);

public sealed record PeriodResolution(ResolvedPeriod? Period,
                                      string? InvalidReason,
                                      string? SetupReason,
                                      bool NotFound);

public sealed record ComparisonResolution(ResolvedPeriod? Period,
                                          string Label,
                                          bool ShorterPrior,
                                          string? UnavailableReason);

public sealed record TrendBucket(DateOnly Start, DateOnly End, string Label, string Granularity);

// --- Responses --------------------------------------------------------------

public sealed record ReportPeriodResponse(string Mode,
                                          string? Preset,
                                          string Label,
                                          string StartDate,
                                          string EndDate,
                                          bool IsPartial,
                                          bool IsFuture,
                                          bool IsOpenSalaryCycle,
                                          Guid? CycleStartId,
                                          string? Note);

public sealed record ReportComparisonResponse(ReportPeriodResponse? Period,
                                              string Label,
                                              bool ShorterPrior,
                                              string? UnavailableReason,
                                              bool HasIncludedData);

public sealed record ReportMoneyResponse(string MoneyIn,
                                         string MoneyOut,
                                         string Net,
                                         long IncludedCount,
                                         long ZeroCount);

public sealed record ReportDiagnosticResponse(long Count,
                                              string MoneyIn,
                                              string MoneyOut,
                                              string Absolute);

public sealed record ReportFilterDescriptor(string StartDate,
                                            string EndDate,
                                            Guid? FinancialAccountId,
                                            Guid? CategoryId,
                                            string? MerchantTag,
                                            string? CategoryKind,
                                            string? Direction,
                                            string Visibility,
                                            string? CalculationScope,
                                            Guid[]? ExcludedCategoryIds,
                                            bool? CategoryConfirmed = null);

public sealed record ReportGapResponse(string StartDate, string EndDate, long Days);

public sealed record ReportGapPageResponse(ReportGapResponse[] Items,
                                           long Total,
                                           int Page,
                                           int PageSize,
                                           bool HasMore,
                                           int? NextPage,
                                           long GapDays);

public sealed record ReportCoverageResponse(long TotalDays,
                                            long CoveredDays,
                                            string? CoveredPercentage,
                                            bool Partial,
                                            ReportGapPageResponse Gaps);

public sealed record ReportCoveragePairResponse(ReportCoverageResponse Current,
                                                ReportCoverageResponse? Prior);

public sealed record ReportCategoryRankResponse(Guid Id,
                                                string DisplayName,
                                                string Kind,
                                                bool IsArchived,
                                                string Amount,
                                                long Count,
                                                string? Share,
                                                string? PriorAmount,
                                                string? ChangePercentage,
                                                ReportFilterDescriptor Descriptor);

public sealed record ReportOtherCategoriesResponse(string Amount,
                                                   long Count,
                                                   Guid[] ExcludedCategoryIds,
                                                   string? PriorAmount,
                                                   string? ChangePercentage,
                                                   ReportFilterDescriptor Descriptor);

public sealed record ReportCategoryDirectionResponse(ReportCategoryRankResponse[] Ranks,
                                                     ReportCategoryRankResponse? Uncategorised,
                                                     ReportOtherCategoriesResponse Other,
                                                     ReportCategoryRankResponse[] OtherRanks,
                                                     ReportCategoryRankResponse[] AllRanks);

public sealed record ReportCategoryBreakdownResponse(ReportCategoryDirectionResponse MoneyIn,
                                                     ReportCategoryDirectionResponse MoneyOut);

public sealed record ReportGroupTotalsResponse(string Kind,
                                               string MoneyIn,
                                               string MoneyOut,
                                               string Net,
                                               long Count);

public sealed record ReportUntaggedResponse(string Amount,
                                            long Count,
                                            ReportFilterDescriptor Descriptor);

public sealed record ReportOtherTagsResponse(string Direction,
                                             ReportTagRankResponse[] Items,
                                             long Total,
                                             int Page,
                                             int PageSize,
                                             bool HasMore,
                                             int? NextPage);

public sealed record ReportTagRankResponse(Guid Id,
                                           string DisplayName,
                                           bool IsArchived,
                                           string Amount,
                                           long Count,
                                           string? Share,
                                           string? PriorAmount,
                                           string? ChangePercentage,
                                           ReportFilterDescriptor Descriptor);

public sealed record ReportTagDirectionResponse(ReportTagRankResponse[] Ranks,
                                                ReportUntaggedResponse Untagged,
                                                ReportOtherTagsResponse Other,
                                                ReportTagRankResponse[] AllRanks);

public sealed record ReportTagBreakdownResponse(ReportTagDirectionResponse MoneyIn,
                                                ReportTagDirectionResponse MoneyOut,
                                                bool OverlapDisclosure);

public sealed record ReportBucketResponse(string StartDate,
                                          string EndDate,
                                          string Label,
                                          string Granularity,
                                          string MoneyIn,
                                          string MoneyOut,
                                          string? MoneyInShare,
                                          string? MoneyOutShare,
                                          long Count,
                                          string Coverage,
                                          ReportFilterDescriptor MoneyInDescriptor,
                                          ReportFilterDescriptor MoneyOutDescriptor);

public sealed record ReportCategoryTrendMonthResponse(string StartDate,
                                                       string EndDate,
                                                       string Label,
                                                       bool IsPartial,
                                                       string Coverage);

public sealed record ReportCategoryTrendCellResponse(string Amount,
                                                      long Count,
                                                      ReportFilterDescriptor Descriptor);

public sealed record ReportCategoryTrendCategoryResponse(Guid Id,
                                                          string DisplayName,
                                                          string Kind,
                                                          bool IsArchived,
                                                          string MoneyOut,
                                                          long Count,
                                                          ReportCategoryTrendCellResponse[] Months);

public sealed record ReportCategoryTrendResponse(ReportCategoryTrendMonthResponse[] Months,
                                                  ReportCategoryTrendCategoryResponse[] Categories);

public sealed record ReportTotalsResponse(ReportMoneyResponse Money,
                                          ReportFilterDescriptor MoneyInDescriptor,
                                          ReportFilterDescriptor MoneyOutDescriptor,
                                          ReportFilterDescriptor NetDescriptor,
                                          ReportFilterDescriptor UncategorisedInDescriptor,
                                          ReportFilterDescriptor UncategorisedOutDescriptor,
                                          ReportFilterDescriptor UnconfirmedDescriptor,
                                          long UncategorisedCount,
                                          string UncategorisedMoneyIn,
                                          string UncategorisedMoneyOut,
                                          string? CategorisedCoveragePercentage,
                                          long UnconfirmedCategoryCount,
                                          long TaggedCount,
                                          long UntaggedCount,
                                          string UntaggedMoneyIn,
                                          string UntaggedMoneyOut,
                                          string? TagCoveragePercentage,
                                          ReportDiagnosticResponse Transfers,
                                          ReportDiagnosticResponse Excluded,
                                          ReportDiagnosticResponse Hidden,
                                          ReportFilterDescriptor HiddenDescriptor,
                                          ReportFilterDescriptor TransfersDescriptor,
                                          ReportFilterDescriptor ExcludedDescriptor);

public sealed record ReportFocusResponse(string Kind,
                                         Guid? CategoryId,
                                         string? CategoryName,
                                         string? CategoryKind,
                                         bool? CategoryArchived,
                                         Guid? MerchantTagId,
                                         string? MerchantTagName,
                                         bool? MerchantTagArchived,
                                         bool Untagged,
                                         Guid? FinancialAccountId,
                                         string? FinancialAccountName,
                                         bool? FinancialAccountArchived,
                                         string? Direction);

public sealed record ReportTransactionPageResponse(TransactionResponse[] Items,
                                                   long Total,
                                                   int Page,
                                                   int PageSize,
                                                   bool HasMore,
                                                   int? NextPage);

public sealed record ReportCategoryChangeResponse(Guid Id,
                                                  string DisplayName,
                                                  string Kind,
                                                  bool IsArchived,
                                                  string CurrentAmount,
                                                  string PriorAmount,
                                                  string Delta,
                                                  string? DeltaPercentage,
                                                  ReportFilterDescriptor CurrentDescriptor,
                                                  ReportFilterDescriptor PriorDescriptor);

public sealed record ReportCategoryChangePageResponse(ReportCategoryChangeResponse[] Items,
                                                      long Total,
                                                      int Page,
                                                      int PageSize,
                                                      bool HasMore,
                                                      int? NextPage);

public sealed record ReportCategoryChangesResponse(ReportCategoryChangePageResponse MoneyIn,
                                                   ReportCategoryChangePageResponse MoneyOut);

public sealed record ReportSetupResponse(string Reason,
                                         bool InventoryEmpty,
                                         string Message,
                                         string[] Alternatives);

public sealed record ReportResponse(string Status,
                                    string AsOf,
                                    string Today,
                                    string Timezone,
                                    string SequenceRevision,
                                    string View,
                                    string PeriodMode,
                                    ReportPeriodResponse? Period,
                                    ReportComparisonResponse? Comparison,
                                    ReportFocusResponse Focus,
                                    ReportTotalsResponse? Current,
                                    ReportTotalsResponse? Prior,
                                    ReportCategoryBreakdownResponse? Categories,
                                    ReportTagBreakdownResponse? Tags,
                                    ReportBucketResponse[]? Trend,
                                    ReportCategoryTrendResponse? CategoryTrend,
                                    ReportBucketResponse[]? SalaryCycleTrend,
                                    ReportCoveragePairResponse? Coverage,
                                    ReportGroupTotalsResponse? FocusGroup,
                                    ReportTransactionPageResponse? Transactions,
                                    ReportCategoryChangesResponse? CategoryChanges,
                                    ReportSetupResponse? Setup,
                                    string[] PolicyLabels);

// --- Salary cycle responses -------------------------------------------------

public sealed record SalaryCycleBoundaryResponse(Guid Id, string StartDate, string CreatedAt);

public sealed record SalaryCycleListResponse(string Revision,
                                             SalaryCycleBoundaryResponse[] Starts,
                                             string? NextBeforeDate);

public sealed record SalaryCycleSourceResponse(Guid Id,
                                               int Version,
                                               string TransactionDate,
                                               string Description,
                                               string Amount,
                                               string Currency,
                                               TransactionAccountRef FinancialAccount,
                                               TransactionCategoryRef Category,
                                               bool CategoryConfirmed);

public sealed record SalaryCycleIntervalResponse(string StartDate, string EndDate, string Label);

public sealed record SalaryCyclePreviewResponse(string Action,
                                                string Revision,
                                                string? Date,
                                                SalaryCycleSourceResponse? Source,
                                                SalaryCycleIntervalResponse? ProposedInterval,
                                                SalaryCycleIntervalResponse[] AffectedIntervals,
                                                string[] Warnings,
                                                bool RetentionDisclosure,
                                                bool AlreadyConfirmed);

public sealed record SalaryCycleConfirmResponse(Guid Id,
                                                string StartDate,
                                                string Revision,
                                                string Status);

public sealed record SalaryCycleRemoveResponse(Guid RemovedId,
                                               string RemovedDate,
                                               string Revision);

// --- Parsing ---------------------------------------------------------------

public static class ReportRequestParser
{
    public static bool TryParsePeriod(ReportPeriodRequest? request,
                                      out string? invalidReason,
                                      out string? setupReason,
                                      out bool notFound)
    {
        invalidReason = null;
        setupReason = null;
        notFound = false;

        if (request is null)
        {
            return true;
        }

        string mode = request.Mode ?? "salary_cycle";
        switch (mode)
        {
            case "salary_cycle":
                if (request.Preset is not null || request.Month is not null || request.StartDate is not null || request.EndDate is not null)
                {
                    invalidReason = "salary_cycle accepts only cycle_start_id";
                    return false;
                }
                if (request.CycleStartId == Guid.Empty)
                {
                    invalidReason = "cycle_start_id is empty";
                    return false;
                }
                return true;

            case "calendar":
                if (request.CycleStartId is not null || request.StartDate is not null || request.EndDate is not null)
                {
                    invalidReason = "calendar does not accept custom dates or a cycle start";
                    return false;
                }
                if (request.Preset is "month")
                {
                    if (!TryParseMonth(request.Month, out _))
                    {
                        invalidReason = "month must be YYYY-MM";
                        return false;
                    }
                    return true;
                }
                if (request.Preset is not ("month_to_date" or "previous_month" or "last_three_months" or "last_six_months"))
                {
                    invalidReason = "calendar preset is invalid";
                    return false;
                }
                if (request.Month is not null)
                {
                    invalidReason = "month is only valid with preset=month";
                    return false;
                }
                return true;

            case "custom":
                if (request.Preset is not null || request.Month is not null || request.CycleStartId is not null)
                {
                    invalidReason = "custom accepts only start_date and end_date";
                    return false;
                }
                if (!TryParseDate(request.StartDate, out DateOnly? start) || !TryParseDate(request.EndDate, out DateOnly? end) || start is null || end is null || start > end)
                {
                    invalidReason = "custom requires an inclusive start_date and end_date";
                    return false;
                }
                return true;

            default:
                invalidReason = "period mode is invalid";
                return false;
        }
    }

    public static bool TryParseView(string? view, out string normalized)
    {
        normalized = view ?? "overview";

        return normalized is "overview" or "category" or "merchant_tag" or "comparison";
    }

    public static bool TryParseFilters(ReportFiltersRequest? request,
                                       string view,
                                       out Guid? financialAccountId,
                                       out Guid? categoryId,
                                       out Guid? merchantTagId,
                                       out bool untagged,
                                       out string? direction)
    {
        financialAccountId = request?.FinancialAccountId;
        categoryId = request?.CategoryId;
        merchantTagId = null;
        untagged = false;
        direction = request?.Direction;

        if (financialAccountId == Guid.Empty || categoryId == Guid.Empty)
        {
            return false;
        }

        if (direction is not null && direction is not ("in" or "out" or "zero"))
        {
            return false;
        }

        if (request?.MerchantTag is not null)
        {
            if (request.MerchantTag == "untagged")
            {
                untagged = true;
            }
            else if (Guid.TryParse(request.MerchantTag, out Guid tagId) && tagId != Guid.Empty)
            {
                merchantTagId = tagId;
            }
            else
            {
                return false;
            }
        }

        // Category and merchant-tag detail require their own focus filter.
        // Overview and comparison accept either as an ordinary ANDed filter.
        if (view == "category" && categoryId is null)
        {
            return false;
        }
        if (view == "merchant_tag" && merchantTagId is null && !untagged)
        {
            return false;
        }

        return true;
    }

    public static bool TryParseReportQuery(ReportQueryRequest? request, out ReportQueryPlan plan)
    {
        plan = new ReportQueryPlan("overview", null, false, null, null, null, false, null, 1, 1, 1, 1, 1, 0);
        if (request is null || !TryParseView(request.View, out string view))
        {
            return false;
        }

        if (!TryParsePeriod(request.Period, out string? invalidReason, out _, out bool notFound) || invalidReason is not null)
        {
            return false;
        }

        if (!TryParseFilters(request.Filters, view, out Guid? accountId, out Guid? categoryId, out Guid? merchantTagId, out bool untagged, out string? direction))
        {
            return false;
        }

        int[] pages =
        [
            request.TagInPage ?? 1,
            request.TagOutPage ?? 1,
            request.TransactionsPage ?? 1,
            request.CoveragePage ?? 1,
            request.ComparisonCoveragePage ?? 1,
        ];
        if (pages.Any(page => page < 1 || page > 100_000))
        {
            return false;
        }

        int categoryChangesPage = request.CategoryChangesPage ?? 0;
        if (categoryChangesPage is < 0 or > 100_000)
        {
            return false;
        }

        plan = new ReportQueryPlan(
            view,
            request.Period,
            notFound,
            accountId,
            categoryId,
            merchantTagId,
            untagged,
            direction,
            pages[0],
            pages[1],
            pages[2],
            pages[3],
            pages[4],
            categoryChangesPage);

        return true;
    }

    public static bool TryParsePage(int? value, out int page)
    {
        page = value ?? 1;

        return page >= 1 && page <= 100_000;
    }

    public static bool TryParseDate(string? value, out DateOnly? date)
    {
        date = null;
        if (value is null)
        {
            return true;
        }

        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateOnly parsed))
        {
            return false;
        }

        date = parsed;

        return true;
    }

    public static bool TryParseMonth(string? value, out DateOnly monthStart)
    {
        monthStart = default;
        if (value is null || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9]{4}-[0-9]{2}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (!int.TryParse(value.AsSpan(0, 4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int year) ||
            !int.TryParse(value.AsSpan(5, 2), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int month) ||
            year is < 1 or > 9999 || month is < 1 or > 12)
        {
            return false;
        }

        monthStart = new DateOnly(year, month, 1);

        return true;
    }
}
