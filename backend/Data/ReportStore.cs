using System.Globalization;
using argus.Server.Data.Entities;
using argus.Server.Http;
using argus.Server.Reporting;
using argus.Server.Transactions;
using Microsoft.EntityFrameworkCore;

namespace argus.Server.Data;

/// <summary>
/// One repeatable-read snapshot for the Overview, category, merchant-tag and
/// comparison reports. Every aggregate and detail page is bound to the same
/// admitted context, the same owned transaction predicate and one final
/// authority check before commit.
/// </summary>
public sealed class ReportStore(IDbContextFactory<ArgusDbContext> contextFactory, TimeProvider timeProvider, IReportFaultInjector faults)
{
    private const string CoverageSql = """
        SELECT lower(r)::text AS "Start", (upper(r) - 1)::text AS "End"
        FROM (
            SELECT unnest(range_agg(daterange(start_date, end_date, '[]'))) AS r
            FROM statement_imports
            WHERE customer_id = {0}
              AND NOT is_hidden
              AND start_date <= {2}::date
              AND end_date >= {1}::date
        ) merged
        ORDER BY lower(r)
        """;

    private const string AccountCoverageSql = """
        SELECT lower(r)::text AS "Start", (upper(r) - 1)::text AS "End"
        FROM (
            SELECT unnest(range_agg(daterange(start_date, end_date, '[]'))) AS r
            FROM statement_imports
            WHERE customer_id = {0}
              AND financial_account_id = {1}
              AND NOT is_hidden
              AND start_date <= {3}::date
              AND end_date >= {2}::date
        ) merged
        ORDER BY lower(r)
        """;

    private readonly IDbContextFactory<ArgusDbContext> _contextFactory = contextFactory;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly IReportFaultInjector _faults = faults;

    public async Task<ReportResponse> QueryAsync(FinanceAuthority authority,
                                                 ReportQueryPlan plan,
                                                 CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        DateOnly today = ReportingClock.Today(_timeProvider);
        DateTime asOf = _timeProvider.GetUtcNow().UtcDateTime;

        await using var scope = await CustomerDbScope.OpenAsync(
            _contextFactory, authority, System.Data.IsolationLevel.RepeatableRead, exclusiveCustomerLock: false, workspaceTimeouts: true, cancellationToken);
        var context = scope.Context;

        if (plan.FinancialAccountId is Guid accountId)
        {
            await TransactionReadQueries.RequireOwnedAsync(context, customerId, "financial_accounts", accountId, cancellationToken);
        }
        if (plan.CategoryId is Guid categoryId)
        {
            await TransactionReadQueries.RequireOwnedAsync(context, customerId, "categories", categoryId, cancellationToken);
        }
        if (plan.MerchantTagId is Guid tagId)
        {
            await TransactionReadQueries.RequireOwnedAsync(context, customerId, "merchant_tags", tagId, cancellationToken);
        }

        FinancialAccount? account = plan.FinancialAccountId is Guid accountFilter
            ? await context.FinancialAccounts.AsNoTracking().SingleAsync(item => item.CustomerId == customerId && item.Id == accountFilter, cancellationToken)
            : null;
        Category? focusCategory = plan.CategoryId is Guid categoryFilter
            ? await context.Categories.AsNoTracking().SingleAsync(item => item.CustomerId == customerId && item.Id == categoryFilter, cancellationToken)
            : null;
        MerchantTag? focusTag = plan.MerchantTagId is Guid tagFilter
            ? await context.MerchantTags.AsNoTracking().SingleAsync(item => item.CustomerId == customerId && item.Id == tagFilter, cancellationToken)
            : null;
        Category? systemCategory = await context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CustomerId == customerId && item.IsSystem, cancellationToken);

        List<SalaryCycleStartRef> starts = await context.SalaryCycleStarts
            .AsNoTracking()
            .Where(start => start.CustomerId == customerId)
            .OrderBy(start => start.StartDate)
            .ThenBy(start => start.Id)
            .Select(start => new SalaryCycleStartRef(start.Id, start.StartDate))
            .ToListAsync(cancellationToken);
        long sequenceRevision = await context.SalaryCycleStates
            .AsNoTracking()
            .Where(state => state.CustomerId == customerId)
            .Select(state => state.Revision)
            .SingleOrDefaultAsync(cancellationToken);

        bool salaryMode = string.Equals(plan.Period?.Mode ?? "salary_cycle", "salary_cycle", StringComparison.Ordinal);
        PeriodResolution resolution = ReportingPeriods.Resolve(plan.Period, today, starts);
        if (plan.PeriodNotFound || resolution.NotFound)
        {
            throw new ReportStoreException(ReportErrorCodes.NotFound);
        }
        if (resolution.InvalidReason is not null)
        {
            throw new ReportStoreException(ReportErrorCodes.InvalidRequest);
        }

        ReportFocusResponse focus = new(
            plan.View,
            focusCategory?.Id,
            focusCategory?.DisplayName,
            focusCategory?.Kind,
            focusCategory?.IsArchived,
            focusTag?.Id,
            focusTag?.DisplayName,
            focusTag?.IsArchived,
            plan.Untagged,
            account?.Id,
            account?.DisplayName,
            account?.IsArchived,
            plan.Direction);

        long inventory = await context.Transactions.LongCountAsync(item => item.CustomerId == customerId, cancellationToken);
        string todayText = FormatDate(today);
        string revisionText = sequenceRevision.ToString(CultureInfo.InvariantCulture);

        if (resolution.Period is null)
        {
            await scope.CommitAsync(cancellationToken);

            var setup = new ReportSetupResponse(
                resolution.SetupReason ?? "no_salary_boundaries",
                inventory == 0,
                inventory == 0
                    ? "Import a statement first, then confirm a salary receipt date. Salary-cycle reporting needs at least one eligible receipt."
                    : "Confirm a salary receipt date to start salary-cycle reporting, or switch to Calendar or Custom dates.",
                ["calendar", "custom"]);

            return new ReportResponse(
                "setup_required",
                Timestamps.Format(asOf),
                todayText,
                ReportingClock.TimeZoneId,
                revisionText,
                plan.View,
                "salary_cycle",
                null,
                null,
                focus,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                setup,
                _policyLabels);
        }

        ResolvedPeriod period = resolution.Period;
        ComparisonResolution comparisonResolution = ReportingPeriods.Compare(period, today, starts);

        TransactionFilter includedFilter = FilterFor(period, plan, TransactionVisibility.Visible, "included", null);
        TransactionFilter allKindsVisible = FilterFor(period, plan, TransactionVisibility.Visible, null, null);
        TransactionFilter allKindsHidden = FilterFor(period, plan, TransactionVisibility.Hidden, null, null);

        long visibleAllKinds = await TransactionQuery.Apply(context, customerId, allKindsVisible, includeVisibility: true).LongCountAsync(cancellationToken);
        long hiddenCount = await TransactionQuery.Apply(context, customerId, allKindsHidden, includeVisibility: true).LongCountAsync(cancellationToken);

        TotalsBundle current = await LoadTotalsAsync(context, customerId, period, plan, systemCategory?.Id, cancellationToken);
        TotalsBundle? prior = comparisonResolution.Period is null
            ? null
            : await LoadTotalsAsync(context, customerId, comparisonResolution.Period, plan, systemCategory?.Id, cancellationToken);

        _faults.OnStep(ReportFaultPoints.AfterTotals);

        string status;
        if (inventory == 0)
        {
            status = "no_data";
        }
        else if (visibleAllKinds == 0 && hiddenCount > 0)
        {
            status = "all_hidden";
        }
        else if (visibleAllKinds == 0)
        {
            status = "no_matches";
        }
        else if (current.Core.Count == 0)
        {
            status = "excluded_only";
        }
        else
        {
            status = "ready";
        }

        bool includeBreakdowns = status == "ready";
        List<CategoryAmount> currentCategories = includeBreakdowns
            ? await LoadCategoryAmountsAsync(context, customerId, includedFilter, cancellationToken)
            : [];
        List<CategoryAmount> priorCategories = includeBreakdowns && comparisonResolution.Period is not null
            ? await LoadCategoryAmountsAsync(context, customerId, FilterFor(comparisonResolution.Period, plan, TransactionVisibility.Visible, "included", null), cancellationToken)
            : [];

        ReportCategoryBreakdownResponse? categories = includeBreakdowns
            ? BuildCategoryBreakdown(current, currentCategories, priorCategories, period, comparisonResolution.Period, plan, systemCategory)
            : null;

        List<TagAmount> currentTags = includeBreakdowns
            ? await LoadTagAmountsAsync(context, customerId, includedFilter, cancellationToken)
            : [];
        List<TagAmount> priorTags = includeBreakdowns && comparisonResolution.Period is not null
            ? await LoadTagAmountsAsync(context, customerId, FilterFor(comparisonResolution.Period, plan, TransactionVisibility.Visible, "included", null), cancellationToken)
            : [];
        ReportTagBreakdownResponse? tags = includeBreakdowns
            ? BuildTagBreakdown(current, currentTags, priorTags, period, comparisonResolution.Period, plan)
            : null;

        List<CoverageRange> currentCoverage = await LoadCoverageAsync(context, customerId, period, plan.FinancialAccountId, cancellationToken);
        ReportCoverageResponse currentCoverageResponse = BuildCoverage(period, currentCoverage, plan.CoveragePage);
        ReportCoverageResponse? priorCoverageResponse = null;
        if (comparisonResolution.Period is not null)
        {
            List<CoverageRange> priorCoverage = await LoadCoverageAsync(context, customerId, comparisonResolution.Period, plan.FinancialAccountId, cancellationToken);
            priorCoverageResponse = BuildCoverage(comparisonResolution.Period, priorCoverage, plan.ComparisonCoveragePage);
        }

        ReportBucketResponse[]? trend = includeBreakdowns
            ? await BuildTrendAsync(context, customerId, includedFilter, period, plan, currentCoverage, cancellationToken)
            : null;

        ReportComparisonResponse? comparisonResponse = null;
        ReportCategoryChangesResponse? categoryChanges = null;
        if (comparisonResolution.Period is null)
        {
            comparisonResponse = new ReportComparisonResponse(
                null,
                comparisonResolution.Label,
                comparisonResolution.ShorterPrior,
                comparisonResolution.UnavailableReason ?? "no_comparison",
                false);
        }
        else
        {
            bool hasPriorData = prior is not null && prior.Core.Count > 0;
            string? unavailable = comparisonResolution.UnavailableReason;
            if (unavailable is null && !hasPriorData)
            {
                unavailable = "no_prior_data";
            }

            comparisonResponse = new ReportComparisonResponse(
                ToPeriodResponse(comparisonResolution.Period),
                comparisonResolution.Label,
                comparisonResolution.ShorterPrior,
                unavailable,
                hasPriorData);

            if (includeBreakdowns && hasPriorData)
            {
                categoryChanges = BuildCategoryChanges(
                    currentCategories,
                    priorCategories,
                    period,
                    comparisonResolution.Period,
                    plan);
            }
        }

        ReportGroupTotalsResponse? focusGroup = includeBreakdowns && focusCategory is not null &&
                                                 focusCategory.Kind is "Income" or "Expense"
            ? BuildFocusGroup(currentCategories, focusCategory.Kind)
            : null;

        ReportTransactionPageResponse? transactions = null;
        if (plan.View is "category" or "merchant_tag" && visibleAllKinds > 0)
        {
            bool excludedFocus = focusCategory is not null && focusCategory.Kind is "Transfer" or "Excluded";
            TransactionFilter detailFilter = FilterFor(
                period,
                plan,
                TransactionVisibility.Visible,
                excludedFocus ? null : "included",
                null);
            long detailTotal = await TransactionQuery.Apply(context, customerId, detailFilter, includeVisibility: true).LongCountAsync(cancellationToken);
            long offset = (long)(plan.TransactionsPage - 1) * ReportingLimits.PageSize;
            List<TransactionReadQueries.TransactionRowData> rows = offset >= detailTotal
                ? []
                : await TransactionReadQueries.ReadRowsAsync(
                    context,
                    customerId,
                    detailFilter,
                    "date",
                    "desc",
                    ReportingLimits.PageSize,
                    offset,
                    includeVisibility: true,
                    cancellationToken);
            bool hasMore = offset + rows.Count < detailTotal;
            transactions = new ReportTransactionPageResponse(
                rows.Select(row => row.Response).ToArray(),
                detailTotal,
                plan.TransactionsPage,
                ReportingLimits.PageSize,
                hasMore,
                hasMore ? plan.TransactionsPage + 1 : null);
        }

        await scope.CommitAsync(cancellationToken);

        ReportSetupResponse? setupResponse = null;
        if (status == "no_data")
        {
            setupResponse = new ReportSetupResponse(
                "no_inventory",
                true,
                "No imported transactions exist yet. Import a statement to populate the selected period.",
                ["imports"]);
        }

        return new ReportResponse(
            status,
            Timestamps.Format(asOf),
            todayText,
            ReportingClock.TimeZoneId,
            revisionText,
            plan.View,
            period.Mode,
            ToPeriodResponse(period),
            comparisonResponse,
            focus,
            status is "no_data" or "no_matches" ? null : current.Response,
            prior is not null && prior.Core.Count > 0 ? prior.Response : null,
            categories,
            tags,
            trend,
            new ReportCoveragePairResponse(currentCoverageResponse, priorCoverageResponse),
            focusGroup,
            transactions,
            categoryChanges,
            setupResponse,
            _policyLabels);
    }

    private static readonly string[] _policyLabels =
    [
        "Money in is the gross positive included amount; Money out is the gross magnitude of negative included amounts.",
        "Net is Money in minus Money out.",
        "Categories are additive; merchant tags may overlap and are never summed into a headline.",
        "Statement coverage is the union across the selected account(s), not a claim that each account is fully covered.",
        "Hidden transactions are excluded from every financial value and reported separately.",
        "Salary-cycle dates are confirmed reporting metadata, not detected paydays.",
        "This is a live view; concurrent data changes may require a refresh.",
    ];

    private sealed record Totals(decimal MoneyIn, decimal MoneyOut, decimal Net, long Count, long ZeroCount, long CountIn, long CountOut);

    private sealed record TotalsBundle(Totals Core, ReportTotalsResponse Response, Totals Untagged, long TaggedCount);

    private sealed record CategoryAmount(Guid Id,
                                         string DisplayName,
                                         string Kind,
                                         bool IsSystem,
                                         bool IsArchived,
                                         decimal MoneyIn,
                                         decimal MoneyOut,
                                         long CountIn,
                                         long CountOut);

    private sealed record TagAmount(Guid Id,
                                    string DisplayName,
                                    bool IsArchived,
                                    decimal MoneyIn,
                                    decimal MoneyOut,
                                    long CountIn,
                                    long CountOut);

    private sealed record DailyAmount(DateOnly Date, decimal MoneyIn, decimal MoneyOut, long Count);

    private sealed record CoverageRange(DateOnly Start, DateOnly End);

    private sealed record CoverageRangeRow
    {
        public string Start { get; set; } = string.Empty;
        public string End { get; set; } = string.Empty;
    }

    private async Task<TotalsBundle> LoadTotalsAsync(ArgusDbContext context,
                                                     Guid customerId,
                                                     ResolvedPeriod period,
                                                     ReportQueryPlan plan,
                                                     Guid? systemCategoryId,
                                                     CancellationToken cancellationToken)
    {
        TransactionFilter included = FilterFor(period, plan, TransactionVisibility.Visible, "included", null);
        Totals core = await AggregateAsync(context, customerId, included, cancellationToken);

        TransactionFilter? uncategorised = null;
        if (core.Count > 0)
        {
            uncategorised = FilterFor(period, plan, TransactionVisibility.Visible, "included", "Uncategorised");
        }
        Totals uncategorisedTotals = uncategorised is null
            ? new Totals(0m, 0m, 0m, 0, 0, 0, 0)
            : await AggregateAsync(context, customerId, uncategorised, cancellationToken);

        decimal classifiedMagnitude = uncategorised is null
            ? core.MoneyIn + core.MoneyOut
            : core.MoneyIn + core.MoneyOut - (uncategorisedTotals.MoneyIn + uncategorisedTotals.MoneyOut);

        long unconfirmed = await TransactionQuery.Apply(
                context,
                customerId,
                included with { CategoryConfirmed = false },
                includeVisibility: true)
            .LongCountAsync(cancellationToken);

        IQueryable<Transaction> taggedRows = TransactionQuery.Apply(
                context,
                customerId,
                included,
                includeVisibility: true)
            .Where(transaction => context.TransactionMerchantTags.Any(link =>
                link.CustomerId == transaction.CustomerId && link.TransactionId == transaction.Id));
        long tagged = await taggedRows.LongCountAsync(cancellationToken);
        decimal taggedMagnitude = await taggedRows
            .GroupBy(_ => 1)
            .Select(group => group.Sum(transaction => transaction.Amount > 0m ? transaction.Amount : -transaction.Amount))
            .SingleOrDefaultAsync(cancellationToken);

        TransactionFilter untaggedFilter = included with { Untagged = true };
        Totals untagged = await AggregateAsync(context, customerId, untaggedFilter, cancellationToken);

        ReportDiagnosticResponse transfers = await DiagnosticAsync(
            context, customerId, FilterFor(period, plan, TransactionVisibility.Visible, null, "Transfer"), cancellationToken);
        ReportDiagnosticResponse excluded = await DiagnosticAsync(
            context, customerId, FilterFor(period, plan, TransactionVisibility.Visible, null, "Excluded"), cancellationToken);
        ReportDiagnosticResponse hidden = await DiagnosticAsync(
            context, customerId, FilterFor(period, plan, TransactionVisibility.Hidden, null, null), cancellationToken);

        var response = new ReportTotalsResponse(
            new ReportMoneyResponse(
                TransactionRequestParser.FormatAmount(core.MoneyIn),
                TransactionRequestParser.FormatAmount(core.MoneyOut),
                TransactionRequestParser.FormatAmount(core.Net),
                core.Count,
                core.ZeroCount),
            Descriptor(period, plan, null, "in", "visible", "included", []),
            Descriptor(period, plan, null, "out", "visible", "included", []),
            Descriptor(period, plan, null, null, "visible", "included", []),
            Descriptor(period, plan, systemCategoryId, "in", "visible", "included", []),
            Descriptor(period, plan, systemCategoryId, "out", "visible", "included", []),
            Descriptor(period, plan, null, null, "visible", "included", [], categoryConfirmed: false),
            uncategorisedTotals.Count,
            TransactionRequestParser.FormatAmount(uncategorisedTotals.MoneyIn),
            TransactionRequestParser.FormatAmount(uncategorisedTotals.MoneyOut),
            ReportingPeriods.Share(classifiedMagnitude, core.MoneyIn + core.MoneyOut),
            unconfirmed,
            tagged,
            untagged.Count,
            TransactionRequestParser.FormatAmount(untagged.MoneyIn),
            TransactionRequestParser.FormatAmount(untagged.MoneyOut),
            ReportingPeriods.Share(taggedMagnitude, core.MoneyIn + core.MoneyOut),
            transfers,
            excluded,
            hidden,
            Descriptor(period, plan, null, null, "hidden", null, []),
            Descriptor(period, plan, null, null, "visible", null, [], categoryKind: "Transfer"),
            Descriptor(period, plan, null, null, "visible", null, [], categoryKind: "Excluded"));

        return new TotalsBundle(core, response, untagged, tagged);
    }

    private static async Task<Totals> AggregateAsync(ArgusDbContext context,
                                                     Guid customerId,
                                                     TransactionFilter filter,
                                                     CancellationToken cancellationToken)
    {
        Totals? totals = await TransactionQuery.Apply(context, customerId, filter, includeVisibility: true)
            .GroupBy(_ => 1)
            .Select(group => new Totals(
                group.Sum(transaction => transaction.Amount > 0m ? transaction.Amount : 0m),
                group.Sum(transaction => transaction.Amount < 0m ? -transaction.Amount : 0m),
                group.Sum(transaction => transaction.Amount),
                group.LongCount(),
                group.LongCount(transaction => transaction.Amount == 0m),
                group.LongCount(transaction => transaction.Amount > 0m),
                group.LongCount(transaction => transaction.Amount < 0m)))
            .SingleOrDefaultAsync(cancellationToken);

        return totals ?? new Totals(0m, 0m, 0m, 0, 0, 0, 0);
    }

    private static async Task<ReportDiagnosticResponse> DiagnosticAsync(ArgusDbContext context,
                                                                        Guid customerId,
                                                                        TransactionFilter filter,
                                                                        CancellationToken cancellationToken)
    {
        Totals totals = await AggregateAsync(context, customerId, filter, cancellationToken);

        return new ReportDiagnosticResponse(
            totals.Count,
            TransactionRequestParser.FormatAmount(totals.MoneyIn),
            TransactionRequestParser.FormatAmount(totals.MoneyOut),
            TransactionRequestParser.FormatAmount(totals.MoneyIn + totals.MoneyOut));
    }

    private static async Task<List<CategoryAmount>> LoadCategoryAmountsAsync(ArgusDbContext context,
                                                                             Guid customerId,
                                                                             TransactionFilter filter,
                                                                             CancellationToken cancellationToken) =>
        await (
            from transaction in TransactionQuery.Apply(context, customerId, filter, includeVisibility: true)
            join category in context.Categories
                on new { transaction.CustomerId, Id = transaction.CategoryId } equals new { category.CustomerId, Id = category.Id }
            group transaction by new { category.Id, category.DisplayName, category.Kind, category.IsSystem, category.IsArchived }
            into grouped
            select new CategoryAmount(
                grouped.Key.Id,
                grouped.Key.DisplayName,
                grouped.Key.Kind,
                grouped.Key.IsSystem,
                grouped.Key.IsArchived,
                grouped.Sum(transaction => transaction.Amount > 0m ? transaction.Amount : 0m),
                grouped.Sum(transaction => transaction.Amount < 0m ? -transaction.Amount : 0m),
                grouped.LongCount(transaction => transaction.Amount > 0m),
                grouped.LongCount(transaction => transaction.Amount < 0m)))
            .ToListAsync(cancellationToken);

    private static async Task<List<TagAmount>> LoadTagAmountsAsync(ArgusDbContext context,
                                                                   Guid customerId,
                                                                   TransactionFilter filter,
                                                                   CancellationToken cancellationToken) =>
        await (
            from transaction in TransactionQuery.Apply(context, customerId, filter, includeVisibility: true)
            join link in context.TransactionMerchantTags
                on new { transaction.CustomerId, Id = transaction.Id } equals new { link.CustomerId, Id = link.TransactionId }
            join tag in context.MerchantTags
                on new { link.CustomerId, Id = link.MerchantTagId } equals new { tag.CustomerId, Id = tag.Id }
            group transaction by new { tag.Id, tag.DisplayName, tag.IsArchived }
            into grouped
            select new TagAmount(
                grouped.Key.Id,
                grouped.Key.DisplayName,
                grouped.Key.IsArchived,
                grouped.Sum(transaction => transaction.Amount > 0m ? transaction.Amount : 0m),
                grouped.Sum(transaction => transaction.Amount < 0m ? -transaction.Amount : 0m),
                grouped.LongCount(transaction => transaction.Amount > 0m),
                grouped.LongCount(transaction => transaction.Amount < 0m)))
            .ToListAsync(cancellationToken);

    private ReportCategoryBreakdownResponse BuildCategoryBreakdown(TotalsBundle current,
                                                                   List<CategoryAmount> currentCategories,
                                                                   List<CategoryAmount> priorCategories,
                                                                   ResolvedPeriod period,
                                                                   ResolvedPeriod? priorPeriod,
                                                                   ReportQueryPlan plan,
                                                                   Category? systemCategory)
    {
        Dictionary<Guid, CategoryAmount> priorById = priorCategories.ToDictionary(item => item.Id);
        return new ReportCategoryBreakdownResponse(
            BuildCategoryDirection(current, currentCategories, priorById, period, priorPeriod, plan, systemCategory, "in"),
            BuildCategoryDirection(current, currentCategories, priorById, period, priorPeriod, plan, systemCategory, "out"));
    }

    private ReportCategoryDirectionResponse BuildCategoryDirection(TotalsBundle current,
                                                                   List<CategoryAmount> currentCategories,
                                                                   Dictionary<Guid, CategoryAmount> priorById,
                                                                   ResolvedPeriod period,
                                                                   ResolvedPeriod? priorPeriod,
                                                                   ReportQueryPlan plan,
                                                                   Category? systemCategory,
                                                                   string direction)
    {
        decimal total = direction == "in" ? current.Core.MoneyIn : current.Core.MoneyOut;
        List<CategoryAmount> amounts = currentCategories
            .Where(item => DirectionAmount(item, direction) > 0m)
            .OrderByDescending(item => DirectionAmount(item, direction))
            .ThenBy(item => item.Id)
            .ToList();

        CategoryAmount? uncategorised = amounts.FirstOrDefault(item => item.IsSystem);
        if (uncategorised is null && systemCategory is not null)
        {
            uncategorised = new CategoryAmount(
                systemCategory.Id,
                systemCategory.DisplayName,
                systemCategory.Kind,
                true,
                systemCategory.IsArchived,
                0m,
                0m,
                0,
                0);
        }
        List<CategoryAmount> ordinary = amounts.Where(item => !item.IsSystem).ToList();
        List<CategoryAmount> top = ordinary.Take(ReportingLimits.RankSize).ToList();
        List<CategoryAmount> remaining = ordinary.Skip(ReportingLimits.RankSize).ToList();

        ReportCategoryRankResponse[] ranks = top
            .Select(item => Rank(item, direction, total, priorById, period, priorPeriod, plan))
            .ToArray();

        decimal otherAmount = remaining.Sum(item => DirectionAmount(item, direction));
        long otherCount = remaining.Sum(item => DirectionCount(item, direction));
        HashSet<Guid> topIds = top.Select(item => item.Id).ToHashSet();
        decimal? otherPrior = priorPeriod is null
            ? null
            : priorById.Values
                .Where(item => !item.IsSystem && !topIds.Contains(item.Id))
                .Sum(item => DirectionAmount(item, direction));

        Guid[] excludedIds = [.. top.Select(item => item.Id), .. uncategorised is null ? [] : new[] { uncategorised.Id }];

        var other = new ReportOtherCategoriesResponse(
            TransactionRequestParser.FormatAmount(otherAmount),
            otherCount,
            excludedIds,
            otherPrior is null ? null : TransactionRequestParser.FormatAmount(otherPrior.Value),
            otherPrior is null ? null : ReportingPeriods.ChangePercentage(otherAmount, otherPrior.Value),
            Descriptor(period, plan, null, direction, "visible", "included", excludedIds));

        ReportCategoryRankResponse? uncategorisedRank = uncategorised is null
            ? null
            : Rank(uncategorised, direction, total, priorById, period, priorPeriod, plan);

        ReportCategoryRankResponse[] otherRanks = remaining
            .Select(item => Rank(item, direction, total, priorById, period, priorPeriod, plan))
            .ToArray();
        ReportCategoryRankResponse[] allRanks = amounts
            .Select(item => Rank(item, direction, total, priorById, period, priorPeriod, plan))
            .ToArray();

        return new ReportCategoryDirectionResponse(ranks, uncategorisedRank, other, otherRanks, allRanks);
    }

    private ReportCategoryRankResponse Rank(CategoryAmount item,
                                            string direction,
                                            decimal total,
                                            Dictionary<Guid, CategoryAmount> priorById,
                                            ResolvedPeriod period,
                                            ResolvedPeriod? priorPeriod,
                                            ReportQueryPlan plan)
    {
        decimal amount = DirectionAmount(item, direction);
        long count = DirectionCount(item, direction);
        decimal? prior = priorPeriod is not null && priorById.TryGetValue(item.Id, out CategoryAmount? priorItem)
            ? DirectionAmount(priorItem, direction)
            : priorPeriod is null ? null : 0m;

        return new ReportCategoryRankResponse(
            item.Id,
            item.DisplayName,
            item.Kind,
            item.IsArchived,
            TransactionRequestParser.FormatAmount(amount),
            count,
            ReportingPeriods.Share(amount, total),
            prior is null ? null : TransactionRequestParser.FormatAmount(prior.Value),
            prior is null ? null : ReportingPeriods.ChangePercentage(amount, prior.Value),
            Descriptor(period, plan, item.Id, direction, "visible", "included", []));
    }

    private ReportTagBreakdownResponse BuildTagBreakdown(TotalsBundle current,
                                                         List<TagAmount> currentTags,
                                                         List<TagAmount> priorTags,
                                                         ResolvedPeriod period,
                                                         ResolvedPeriod? priorPeriod,
                                                         ReportQueryPlan plan)
    {
        Dictionary<Guid, TagAmount> priorById = priorTags.ToDictionary(item => item.Id);
        return new ReportTagBreakdownResponse(
            BuildTagDirection(current, currentTags, priorById, period, priorPeriod, plan, "in"),
            BuildTagDirection(current, currentTags, priorById, period, priorPeriod, plan, "out"),
            OverlapDisclosure: true);
    }

    private ReportTagDirectionResponse BuildTagDirection(TotalsBundle current,
                                                         List<TagAmount> currentTags,
                                                         Dictionary<Guid, TagAmount> priorById,
                                                         ResolvedPeriod period,
                                                         ResolvedPeriod? priorPeriod,
                                                         ReportQueryPlan plan,
                                                         string direction)
    {
        decimal total = direction == "in" ? current.Core.MoneyIn : current.Core.MoneyOut;
        List<TagAmount> amounts = currentTags
            .Where(item => DirectionAmount(item, direction) > 0m)
            .OrderByDescending(item => DirectionAmount(item, direction))
            .ThenBy(item => item.Id)
            .ToList();
        if (plan.MerchantTagId is not null)
        {
            // A one-tag filter is the only expressible tag scope. Co-tag ranks
            // would represent rows carrying both tags, not the single-tag list
            // filter, so they are not offered as monetary drill-downs.
            amounts = amounts.Where(item => item.Id == plan.MerchantTagId.Value).ToList();
        }
        else if (plan.Untagged)
        {
            amounts = [];
        }
        List<TagAmount> top = amounts.Take(ReportingLimits.RankSize).ToList();
        List<TagAmount> remaining = amounts.Skip(ReportingLimits.RankSize).ToList();
        int page = direction == "in" ? plan.TagInPage : plan.TagOutPage;
        int offset = (page - 1) * ReportingLimits.PageSize;
        List<TagAmount> pageItems = remaining.Skip(offset).Take(ReportingLimits.PageSize).ToList();
        bool hasMore = offset + pageItems.Count < remaining.Count;

        ReportTagRankResponse[] ranks = top
            .Select(item => TagRank(item, direction, total, priorById, period, priorPeriod, plan))
            .ToArray();
        ReportTagRankResponse[] otherItems = pageItems
            .Select(item => TagRank(item, direction, total, priorById, period, priorPeriod, plan))
            .ToArray();

        Totals untagged = current.Untagged;

        ReportTagRankResponse[] allRanks = amounts
            .Select(item => TagRank(item, direction, total, priorById, period, priorPeriod, plan))
            .ToArray();

        return new ReportTagDirectionResponse(
            ranks,
            new ReportUntaggedResponse(
                TransactionRequestParser.FormatAmount(direction == "in" ? untagged.MoneyIn : untagged.MoneyOut),
                direction == "in" ? untagged.CountIn : untagged.CountOut,
                Descriptor(period, plan, null, direction, "visible", "included", [], untagged: true)),
            new ReportOtherTagsResponse(
                direction,
                otherItems,
                remaining.Count,
                page,
                ReportingLimits.PageSize,
                hasMore,
                hasMore ? page + 1 : null),
            allRanks);
    }

    private ReportTagRankResponse TagRank(TagAmount item,
                                          string direction,
                                          decimal total,
                                          Dictionary<Guid, TagAmount> priorById,
                                          ResolvedPeriod period,
                                          ResolvedPeriod? priorPeriod,
                                          ReportQueryPlan plan)
    {
        decimal amount = DirectionAmount(item, direction);
        decimal? prior = priorPeriod is not null && priorById.TryGetValue(item.Id, out TagAmount? priorItem)
            ? DirectionAmount(priorItem, direction)
            : priorPeriod is null ? null : 0m;

        return new ReportTagRankResponse(
            item.Id,
            item.DisplayName,
            item.IsArchived,
            TransactionRequestParser.FormatAmount(amount),
            DirectionCount(item, direction),
            ReportingPeriods.Share(amount, total),
            prior is null ? null : TransactionRequestParser.FormatAmount(prior.Value),
            prior is null ? null : ReportingPeriods.ChangePercentage(amount, prior.Value),
            Descriptor(period, plan, null, direction, "visible", "included", [], item.Id));
    }

    private async Task<ReportBucketResponse[]> BuildTrendAsync(ArgusDbContext context,
                                                               Guid customerId,
                                                               TransactionFilter filter,
                                                               ResolvedPeriod period,
                                                               ReportQueryPlan plan,
                                                               List<CoverageRange> coverage,
                                                               CancellationToken cancellationToken)
    {
        List<DailyAmount> daily = await TransactionQuery.Apply(context, customerId, filter, includeVisibility: true)
            .GroupBy(transaction => transaction.TransactionDate)
            .Select(group => new DailyAmount(
                group.Key,
                group.Sum(transaction => transaction.Amount > 0m ? transaction.Amount : 0m),
                group.Sum(transaction => transaction.Amount < 0m ? -transaction.Amount : 0m),
                group.LongCount()))
            .ToListAsync(cancellationToken);

        IReadOnlyList<TrendBucket> buckets = ReportingPeriods.BuildBuckets(period.Start, period.End);
        var responses = new List<ReportBucketResponse>(buckets.Count);
        var folded = new List<(TrendBucket Bucket, decimal MoneyIn, decimal MoneyOut, long Count)>();
        decimal maximum = 0m;
        foreach (TrendBucket bucket in buckets)
        {
            decimal moneyIn = 0m;
            decimal moneyOut = 0m;
            long count = 0;
            foreach (DailyAmount day in daily)
            {
                if (day.Date >= bucket.Start && day.Date <= bucket.End)
                {
                    moneyIn += day.MoneyIn;
                    moneyOut += day.MoneyOut;
                    count += day.Count;
                }
            }

            folded.Add((bucket, moneyIn, moneyOut, count));
            maximum = Math.Max(maximum, Math.Max(moneyIn, moneyOut));
        }

        foreach (var (bucket, moneyIn, moneyOut, count) in folded)
        {
            long bucketDays = ReportingPeriods.DaysInclusive(bucket.Start, bucket.End);
            long coveredDays = CoverageDays(bucket.Start, bucket.End, coverage);
            string state = coveredDays <= 0 ? "uncovered" : coveredDays >= bucketDays ? "covered" : "partial";

            responses.Add(new ReportBucketResponse(
                FormatDate(bucket.Start),
                FormatDate(bucket.End),
                bucket.Label,
                bucket.Granularity,
                TransactionRequestParser.FormatAmount(moneyIn),
                TransactionRequestParser.FormatAmount(moneyOut),
                ReportingPeriods.Share(moneyIn, maximum),
                ReportingPeriods.Share(moneyOut, maximum),
                count,
                state,
                Descriptor(bucket.Start, bucket.End, plan, null, "in", "visible", "included", []),
                Descriptor(bucket.Start, bucket.End, plan, null, "out", "visible", "included", [])));
        }

        return [.. responses];
    }

    private async Task<List<CoverageRange>> LoadCoverageAsync(ArgusDbContext context,
                                                              Guid customerId,
                                                              ResolvedPeriod period,
                                                              Guid? accountId,
                                                              CancellationToken cancellationToken)
    {
        List<CoverageRangeRow> rows = accountId is null
            ? await context.Database.SqlQueryRaw<CoverageRangeRow>(
                CoverageSql,
                customerId,
                period.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                period.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToListAsync(cancellationToken)
            : await context.Database.SqlQueryRaw<CoverageRangeRow>(
                AccountCoverageSql,
                customerId,
                accountId.Value,
                period.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                period.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToListAsync(cancellationToken);

        var clipped = rows
            .Select(row => new
            {
                Start = DateOnly.ParseExact(row.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                End = DateOnly.ParseExact(row.End, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            })
            .Select(row => new
            {
                Start = row.Start < period.Start ? period.Start : row.Start,
                End = row.End > period.End ? period.End : row.End,
            })
            .Where(row => row.Start <= row.End)
            .OrderBy(row => row.Start)
            .ThenBy(row => row.End)
            .ToList();

        var merged = new List<CoverageRange>();
        foreach (var row in clipped)
        {
            if (merged.Count == 0)
            {
                merged.Add(new CoverageRange(row.Start, row.End));
                continue;
            }

            CoverageRange last = merged[^1];
            bool adjacent = ReportingPeriods.TryAddDays(last.End, 1, out DateOnly next) && row.Start <= next;
            if (adjacent)
            {
                if (row.End > last.End)
                {
                    merged[^1] = last with { End = row.End };
                }
            }
            else
            {
                merged.Add(new CoverageRange(row.Start, row.End));
            }
        }

        return merged;
    }

    private static ReportCoverageResponse BuildCoverage(ResolvedPeriod period,
                                                        List<CoverageRange> coverage,
                                                        int page)
    {
        long totalDays = ReportingPeriods.DaysInclusive(period.Start, period.End);
        long coveredDays = 0;
        var gaps = new List<ReportGapResponse>();
        DateOnly cursor = period.Start;
        bool exhausted = false;
        foreach (CoverageRange range in coverage)
        {
            coveredDays += ReportingPeriods.DaysInclusive(range.Start, range.End);
            if (range.Start > cursor && ReportingPeriods.TryAddDays(range.Start, -1, out DateOnly gapEnd))
            {
                gaps.Add(new ReportGapResponse(FormatDate(cursor), FormatDate(gapEnd), ReportingPeriods.DaysInclusive(cursor, gapEnd)));
            }
            if (!ReportingPeriods.TryAddDays(range.End, 1, out DateOnly next))
            {
                // Coverage reaches the maximum representable date; there is no
                // next day to compare or invent a final gap for.
                exhausted = true;
                break;
            }
            cursor = next;
            if (cursor > period.End)
            {
                break;
            }
        }
        if (!exhausted && cursor <= period.End)
        {
            gaps.Add(new ReportGapResponse(FormatDate(cursor), FormatDate(period.End), ReportingPeriods.DaysInclusive(cursor, period.End)));
        }

        int offset = (page - 1) * ReportingLimits.PageSize;
        List<ReportGapResponse> pageItems = gaps.Skip(offset).Take(ReportingLimits.PageSize).ToList();
        bool hasMore = offset + pageItems.Count < gaps.Count;
        long gapDays = gaps.Sum(gap => gap.Days);

        return new ReportCoverageResponse(
            totalDays,
            coveredDays,
            ReportingPeriods.Share(coveredDays, totalDays),
            coveredDays < totalDays,
            new ReportGapPageResponse(
                pageItems.ToArray(),
                gaps.Count,
                page,
                ReportingLimits.PageSize,
                hasMore,
                hasMore ? page + 1 : null,
                gapDays));
    }

    private ReportCategoryChangesResponse BuildCategoryChanges(List<CategoryAmount> current,
                                                               List<CategoryAmount> prior,
                                                               ResolvedPeriod period,
                                                               ResolvedPeriod priorPeriod,
                                                               ReportQueryPlan plan)
    {
        return new ReportCategoryChangesResponse(
            BuildChangeDirection(current, prior, period, priorPeriod, plan, "in"),
            BuildChangeDirection(current, prior, period, priorPeriod, plan, "out"));
    }

    private ReportCategoryChangePageResponse BuildChangeDirection(List<CategoryAmount> current,
                                                                  List<CategoryAmount> prior,
                                                                  ResolvedPeriod period,
                                                                  ResolvedPeriod priorPeriod,
                                                                  ReportQueryPlan plan,
                                                                  string direction)
    {
        Dictionary<Guid, CategoryAmount> currentById = current.ToDictionary(item => item.Id);
        Dictionary<Guid, CategoryAmount> priorById = prior.ToDictionary(item => item.Id);
        var changes = new List<(CategoryAmount Category, decimal Current, decimal Prior, decimal Delta)>();
        foreach (Guid id in currentById.Keys.Union(priorById.Keys))
        {
            CategoryAmount category = currentById.GetValueOrDefault(id) ?? priorById[id];
            decimal currentAmount = currentById.TryGetValue(id, out CategoryAmount? currentItem) ? DirectionAmount(currentItem, direction) : 0m;
            decimal priorAmount = priorById.TryGetValue(id, out CategoryAmount? priorItem) ? DirectionAmount(priorItem, direction) : 0m;
            if (currentAmount == 0m && priorAmount == 0m)
            {
                continue;
            }
            changes.Add((category, currentAmount, priorAmount, currentAmount - priorAmount));
        }

        List<(CategoryAmount Category, decimal Current, decimal Prior, decimal Delta)> ordered = changes
            .OrderByDescending(item => Math.Abs(item.Delta))
            .ThenBy(item => item.Category.Id)
            .ToList();

        int page = plan.CategoryChangesPage;
        int offset = page == 0 ? 0 : ReportingLimits.CategoryChangeInitialSize + (page - 1) * ReportingLimits.CategoryChangePageSize;
        int pageSize = page == 0 ? ReportingLimits.CategoryChangeInitialSize : ReportingLimits.CategoryChangePageSize;
        List<(CategoryAmount Category, decimal Current, decimal Prior, decimal Delta)> pageItems = ordered
            .Skip(offset)
            .Take(pageSize)
            .ToList();

        return new ReportCategoryChangePageResponse(
            pageItems.Select(item => new ReportCategoryChangeResponse(
                item.Category.Id,
                item.Category.DisplayName,
                item.Category.Kind,
                item.Category.IsArchived,
                TransactionRequestParser.FormatAmount(item.Current),
                TransactionRequestParser.FormatAmount(item.Prior),
                TransactionRequestParser.FormatAmount(item.Delta),
                ReportingPeriods.ChangePercentage(item.Current, item.Prior),
                Descriptor(period, plan, item.Category.Id, direction, "visible", "included", []),
                Descriptor(priorPeriod, plan, item.Category.Id, direction, "visible", "included", []))).ToArray(),
            ordered.Count,
            page,
            pageSize,
            offset + pageItems.Count < ordered.Count,
            offset + pageItems.Count < ordered.Count ? page + 1 : null);
    }

    private static ReportGroupTotalsResponse BuildFocusGroup(List<CategoryAmount> categories, string kind)
    {
        List<CategoryAmount> group = categories.Where(item => !item.IsSystem && item.Kind == kind).ToList();
        decimal moneyIn = group.Sum(item => item.MoneyIn);
        decimal moneyOut = group.Sum(item => item.MoneyOut);

        decimal net = kind == "Expense" ? moneyOut - moneyIn : moneyIn - moneyOut;

        return new ReportGroupTotalsResponse(
            kind,
            TransactionRequestParser.FormatAmount(moneyIn),
            TransactionRequestParser.FormatAmount(moneyOut),
            TransactionRequestParser.FormatAmount(net),
            group.Sum(item => item.CountIn + item.CountOut));
    }

    private static decimal DirectionAmount(CategoryAmount item, string direction) =>
        direction == "in" ? item.MoneyIn : item.MoneyOut;

    private static long DirectionCount(CategoryAmount item, string direction) =>
        direction == "in" ? item.CountIn : item.CountOut;

    private static decimal DirectionAmount(TagAmount item, string direction) =>
        direction == "in" ? item.MoneyIn : item.MoneyOut;

    private static long DirectionCount(TagAmount item, string direction) =>
        direction == "in" ? item.CountIn : item.CountOut;

    private static long CoverageDays(DateOnly start, DateOnly end, List<CoverageRange> coverage)
    {
        long total = 0;
        foreach (CoverageRange range in coverage)
        {
            DateOnly overlapStart = range.Start > start ? range.Start : start;
            DateOnly overlapEnd = range.End < end ? range.End : end;
            if (overlapStart <= overlapEnd)
            {
                total += ReportingPeriods.DaysInclusive(overlapStart, overlapEnd);
            }
        }

        return total;
    }

    private TransactionFilter FilterFor(ResolvedPeriod period,
                                        ReportQueryPlan plan,
                                        TransactionVisibility visibility,
                                        string? calculationScope,
                                        string? categoryKind)
    {
        return new TransactionFilter(
            null,
            period.Start,
            period.End,
            plan.FinancialAccountId,
            null,
            plan.CategoryId,
            plan.MerchantTagId,
            plan.Untagged,
            null,
            categoryKind,
            plan.Direction,
            visibility,
            calculationScope,
            []);
    }

    private ReportFilterDescriptor Descriptor(ResolvedPeriod period,
                                              ReportQueryPlan plan,
                                              Guid? categoryId,
                                              string? direction,
                                              string visibility,
                                              string? calculationScope,
                                              Guid[] excludedCategoryIds,
                                              Guid? merchantTagId = null,
                                              string? categoryKind = null,
                                              bool untagged = false,
                                              bool? categoryConfirmed = null)
    {
        return new ReportFilterDescriptor(
            FormatDate(period.Start),
            FormatDate(period.End),
            plan.FinancialAccountId,
            categoryId ?? plan.CategoryId,
            untagged ? "untagged" : merchantTagId is null ? (plan.Untagged ? "untagged" : plan.MerchantTagId?.ToString("D")) : merchantTagId.Value.ToString("D"),
            categoryKind,
            plan.Direction ?? direction,
            visibility,
            calculationScope,
            excludedCategoryIds.Length == 0 ? null : excludedCategoryIds,
            categoryConfirmed);
    }

    private ReportFilterDescriptor Descriptor(DateOnly start,
                                              DateOnly end,
                                              ReportQueryPlan plan,
                                              Guid? categoryId,
                                              string? direction,
                                              string visibility,
                                              string? calculationScope,
                                              Guid[] excludedCategoryIds) =>
        new(
            FormatDate(start),
            FormatDate(end),
            plan.FinancialAccountId,
            categoryId ?? plan.CategoryId,
            plan.Untagged ? "untagged" : plan.MerchantTagId?.ToString("D"),
            null,
            plan.Direction ?? direction,
            visibility,
            calculationScope,
            excludedCategoryIds.Length == 0 ? null : excludedCategoryIds);

    private static ReportPeriodResponse ToPeriodResponse(ResolvedPeriod period) =>
        new(
            period.Mode,
            period.Preset,
            period.Label,
            FormatDate(period.Start),
            FormatDate(period.End),
            period.IsPartial,
            period.IsFuture,
            period.IsOpenSalaryCycle,
            period.CycleStartId,
            period.Note);

    private static string FormatDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public sealed class ReportStoreException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
