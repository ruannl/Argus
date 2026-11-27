using System.Globalization;

namespace argus.Server.Reporting;

/// <summary>
/// Pure period, comparison and trend-bucket rules shared by the report store
/// and the database-free phase-1 tests. Imported transaction dates are
/// calendar dates and are never shifted by the reporting timezone.
/// </summary>
public static class ReportingPeriods
{
    public static PeriodResolution Resolve(ReportPeriodRequest? request,
                                           DateOnly today,
                                           IReadOnlyList<SalaryCycleStartRef> starts)
    {
        if (!ReportRequestParser.TryParsePeriod(request, out string? invalidReason, out _, out _))
        {
            return new PeriodResolution(null, invalidReason, null, false);
        }

        string mode = request?.Mode ?? "salary_cycle";
        switch (mode)
        {
            case "salary_cycle":
                return ResolveSalary(request, today, starts);

            case "calendar":
                return ResolveCalendar(request!, today);

            case "custom":
                ReportRequestParser.TryParseDate(request!.StartDate, out DateOnly? start);
                ReportRequestParser.TryParseDate(request.EndDate, out DateOnly? end);
                var custom = new ResolvedPeriod(
                    "custom",
                    null,
                    "Custom dates",
                    start!.Value,
                    end!.Value,
                    IsPartial: end.Value > today,
                    IsOpenSalaryCycle: false,
                    IsFuture: end.Value > today,
                    CycleStartId: null,
                    Note: end.Value > today ? "The selected custom range extends beyond today." : null);

                return new PeriodResolution(custom, null, null, false);

            default:
                return new PeriodResolution(null, "period mode is invalid", null, false);
        }
    }

    private static PeriodResolution ResolveSalary(ReportPeriodRequest? request,
                                                  DateOnly today,
                                                  IReadOnlyList<SalaryCycleStartRef> starts)
    {
        List<SalaryCycleStartRef> ordered = NormalizeStarts(starts);
        SalaryCycleStartRef? selected;
        if (request?.CycleStartId is Guid requestedId)
        {
            SalaryCycleStartRef? requested = starts.FirstOrDefault(start => start.Id == requestedId);
            if (requested is null || requested.StartDate > today || ordered.Count == 0)
            {
                return new PeriodResolution(null, null, null, true);
            }

            selected = ordered.First(start => start.StartDate == requested.StartDate);
        }
        else if (ordered.Count == 0)
        {
            return new PeriodResolution(null, null, "no_salary_boundaries", false);
        }
        else
        {
            selected = ordered[^1];
        }

        int index = ordered.IndexOf(selected);
        SalaryCycleStartRef? successor = index + 1 < ordered.Count ? ordered[index + 1] : null;
        DateOnly startDate = selected!.StartDate;
        if (startDate > today)
        {
            return new PeriodResolution(null, null, null, true);
        }

        DateOnly endDate;
        if (successor is not null)
        {
            if (successor.StartDate <= startDate || !TryAddDays(successor.StartDate, -1, out endDate) || endDate < startDate)
            {
                return new PeriodResolution(null, "salary cycle boundaries are not ordered", null, false);
            }
        }
        else
        {
            endDate = today;
        }

        bool open = successor is null;
        var period = new ResolvedPeriod(
            "salary_cycle",
            null,
            open ? "Salary cycle to date" : "Salary cycle",
            startDate,
            endDate,
            IsPartial: open,
            IsOpenSalaryCycle: open,
            IsFuture: false,
            selected.Id,
            open ? "The latest confirmed cycle is open and ends today." : null);

        return new PeriodResolution(period, null, null, false);
    }

    private static PeriodResolution ResolveCalendar(ReportPeriodRequest request, DateOnly today)
    {
        DateOnly currentMonth = MonthStart(today);
        switch (request.Preset)
        {
            case "month_to_date":
                return Ready("month_to_date", "Month to date", currentMonth, today, isPartial: true, isFuture: false, null);

            case "previous_month":
            {
                DateOnly start = currentMonth.AddMonths(-1);
                return Ready("previous_month", "Previous month", start, MonthEnd(start), isPartial: false, isFuture: false, null);
            }

            case "last_three_months":
                return Ready("last_three_months", "Last three months", currentMonth.AddMonths(-2), today, isPartial: true, isFuture: false,
                    "Includes the current partial month.");

            case "last_six_months":
                return Ready("last_six_months", "Last six months", currentMonth.AddMonths(-5), today, isPartial: true, isFuture: false,
                    "Includes the current partial month.");

            case "month":
            {
                ReportRequestParser.TryParseMonth(request.Month, out DateOnly monthStart);
                DateOnly end = MonthEnd(monthStart);
                bool future = monthStart > today;
                bool partial = end >= today && monthStart <= today;

                return Ready(
                    "month",
                    monthStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
                    monthStart,
                    end,
                    partial,
                    future,
                    future ? "The selected calendar month is in the future." : null);
            }

            default:
                return new PeriodResolution(null, "calendar preset is invalid", null, false);
        }
    }

    private static PeriodResolution Ready(string preset,
                                          string label,
                                          DateOnly start,
                                          DateOnly end,
                                          bool isPartial,
                                          bool isFuture,
                                          string? note) =>
        new(new ResolvedPeriod("calendar", preset, label, start, end, isPartial, false, isFuture, null, note),
            null,
            null,
            false);

    public static ComparisonResolution Compare(ResolvedPeriod period,
                                               DateOnly today,
                                               IReadOnlyList<SalaryCycleStartRef> starts)
    {
        switch (period.Mode)
        {
            case "salary_cycle":
                return CompareSalary(period, today, starts);

            case "calendar":
                return CompareCalendar(period);

            case "custom":
                return CompareEqualLength(period, "Previous equal-length period");

            default:
                return new ComparisonResolution(null, "Comparison", false, "period mode is invalid");
        }
    }

    // The category table compares the full confirmed predecessor even when
    // the current salary cycle is still open. Other report comparisons retain
    // their elapsed-day behaviour.
    internal static ComparisonResolution CompareCategoryTrend(ResolvedPeriod period,
                                                               DateOnly today,
                                                               IReadOnlyList<SalaryCycleStartRef> starts) =>
        period.Mode == "salary_cycle" ? CompareSalary(period, today, starts, matchElapsed: false) : Compare(period, today, starts);

    private static ComparisonResolution CompareSalary(ResolvedPeriod period,
                                                      DateOnly today,
                                                      IReadOnlyList<SalaryCycleStartRef> starts,
                                                      bool matchElapsed = true)
    {
        List<SalaryCycleStartRef> ordered = NormalizeStarts(starts);
        int index = ordered.FindIndex(start => start.Id == period.CycleStartId || start.StartDate == period.Start);
        if (index <= 0)
        {
            return new ComparisonResolution(null, "Previous salary cycle", false, "insufficient_salary_boundaries");
        }

        SalaryCycleStartRef previous = ordered[index - 1];
        if (period.IsOpenSalaryCycle && matchElapsed)
        {
            long elapsedDays = DaysInclusive(period.Start, today);
            if (elapsedDays < 1 || !TryAddDays(previous.StartDate, (int)elapsedDays - 1, out DateOnly rawEnd))
            {
                return new ComparisonResolution(null, "Same elapsed days as previous cycle", false, "date_boundary");
            }

            if (!TryAddDays(period.Start, -1, out DateOnly previousCycleEnd) || previousCycleEnd < previous.StartDate)
            {
                return new ComparisonResolution(null, "Same elapsed days as previous cycle", false, "date_boundary");
            }

            bool shorterPrior = rawEnd > previousCycleEnd;
            DateOnly end = shorterPrior ? previousCycleEnd : rawEnd;
            if (end < previous.StartDate)
            {
                return new ComparisonResolution(null, "Same elapsed days as previous cycle", false, "date_boundary");
            }

            return new ComparisonResolution(
                new ResolvedPeriod("salary_cycle", null, "Same elapsed days as previous cycle", previous.StartDate, end, false, false, false, previous.Id, null),
                "Same elapsed days as previous cycle",
                shorterPrior,
                null);
        }

        if (!TryAddDays(period.Start, -1, out DateOnly closedEnd) || closedEnd < previous.StartDate)
        {
            return new ComparisonResolution(null, "Previous salary cycle", false, "date_boundary");
        }

        return new ComparisonResolution(
            new ResolvedPeriod("salary_cycle", null, "Previous salary cycle", previous.StartDate, closedEnd, false, false, false, previous.Id, null),
            "Previous salary cycle",
            false,
            null);
    }

    private static ComparisonResolution CompareCalendar(ResolvedPeriod period)
    {
        switch (period.Preset)
        {
            case "month_to_date":
            {
                if (!TryAddMonths(MonthStart(period.Start), -1, out DateOnly priorMonth))
                {
                    return new ComparisonResolution(null, "Same period last month", false, "date_boundary");
                }
                DateOnly priorMonthEnd = MonthEnd(priorMonth);
                int day = period.End.Day;
                bool shorterPrior = day > priorMonthEnd.Day;
                DateOnly priorEnd = new(priorMonth.Year, priorMonth.Month, Math.Min(day, priorMonthEnd.Day));

                return new ComparisonResolution(
                    new ResolvedPeriod("calendar", "month_to_date", "Same period last month", priorMonth, priorEnd, false, false, false, null, null),
                    "Same period last month",
                    shorterPrior,
                    null);
            }

            case "previous_month":
            case "month":
            {
                if (!TryAddMonths(MonthStart(period.Start), -1, out DateOnly prior))
                {
                    return new ComparisonResolution(null, "Previous calendar month", false, "date_boundary");
                }
                return new ComparisonResolution(
                    new ResolvedPeriod("calendar", "month", prior.ToString("MMMM yyyy", CultureInfo.InvariantCulture), prior, MonthEnd(prior), false, false, false, null, null),
                    "Previous calendar month",
                    false,
                    null);
            }

            default:
                return CompareEqualLength(period, "Previous equal-length period");
        }
    }

    private static ComparisonResolution CompareEqualLength(ResolvedPeriod period, string label)
    {
        long days = DaysInclusive(period.Start, period.End);
        if (days < 1 || !TryAddDays(period.Start, -1, out DateOnly end) || !TryAddDays(end, (int)-(days - 1), out DateOnly start))
        {
            return new ComparisonResolution(null, label, false, "date_boundary");
        }

        return new ComparisonResolution(
            new ResolvedPeriod(period.Mode, null, label, start, end, false, false, false, null, null),
            label,
            false,
            null);
    }

    /// <summary>
    /// Builds one bucket per confirmed salary cycle, ending at the cycle
    /// selected by <paramref name="period"/>. The latest cycle ends today;
    /// earlier cycles end the day before the next confirmed start. At most
    /// <paramref name="maximum"/> cycles are returned, oldest first.
    /// </summary>
    public static IReadOnlyList<TrendBucket> BuildSalaryCycleBuckets(ResolvedPeriod period,
                                                                     DateOnly today,
                                                                     IReadOnlyList<SalaryCycleStartRef> starts,
                                                                     int maximum = 6)
    {
        List<SalaryCycleStartRef> ordered = NormalizeStarts(starts);
        if (ordered.Count == 0 || maximum < 1)
        {
            return [];
        }

        int selectedIndex = period.CycleStartId is Guid cycleStartId
            ? ordered.FindIndex(start => start.Id == cycleStartId)
            : ordered.FindIndex(start => start.StartDate == period.Start);
        if (selectedIndex < 0)
        {
            return [];
        }

        int firstIndex = Math.Max(0, selectedIndex - maximum + 1);
        var buckets = new List<TrendBucket>(selectedIndex - firstIndex + 1);
        for (int index = firstIndex; index <= selectedIndex; index++)
        {
            DateOnly start = ordered[index].StartDate;
            DateOnly end;
            if (index + 1 < ordered.Count)
            {
                if (!TryAddDays(ordered[index + 1].StartDate, -1, out end) || end < start)
                {
                    continue;
                }
            }
            else
            {
                end = today;
            }

            if (end < start)
            {
                continue;
            }

            buckets.Add(new TrendBucket(start, end, FormatCycleLabel(start, end), "salary_cycle"));
        }

        return buckets;
    }

    public static IReadOnlyList<TrendBucket> BuildBuckets(DateOnly start, DateOnly end)
    {
        long days = DaysInclusive(start, end);
        if (days < 1)
        {
            return [];
        }

        if (days <= 62)
        {
            var buckets = new List<TrendBucket>();
            DateOnly cursor = start;
            while (cursor <= end)
            {
                DateOnly bucketEnd = TryAddDays(cursor, 6, out DateOnly candidate) && candidate <= end ? candidate : end;
                buckets.Add(new TrendBucket(cursor, bucketEnd, FormatRange(cursor, bucketEnd), "week"));
                if (!TryAddDays(bucketEnd, 1, out cursor))
                {
                    break;
                }
            }

            return buckets;
        }

        int monthCount = (end.Year - start.Year) * 12 + end.Month - start.Month + 1;
        int group = Math.Max(1, (monthCount + 119) / 120);
        var monthly = new List<TrendBucket>();
        DateOnly monthCursor = MonthStart(start);
        while (monthCursor <= end)
        {
            int remainingMonths = (end.Year - monthCursor.Year) * 12 + end.Month - monthCursor.Month + 1;
            int span = Math.Min(group, remainingMonths);
            DateOnly groupEndMonth = monthCursor.AddMonths(span - 1);
            DateOnly bucketStart = monthCursor < start ? start : monthCursor;
            DateOnly groupEnd = MonthEnd(groupEndMonth);
            DateOnly bucketEnd = groupEnd > end ? end : groupEnd;
            string label = span == 1
                ? monthCursor.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
                : $"{monthCursor.ToString("MMM yyyy", CultureInfo.InvariantCulture)} – {groupEndMonth.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";
            monthly.Add(new TrendBucket(bucketStart, bucketEnd, label, "month"));

            if (!TryAddMonths(monthCursor, span, out DateOnly next))
            {
                break;
            }
            monthCursor = next;
        }

        return monthly;
    }

    public static bool TryAddMonths(DateOnly value, int months, out DateOnly result)
    {
        try
        {
            result = value.AddMonths(months);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            result = default;
            return false;
        }
    }

    public static bool TryAddDays(DateOnly value, int days, out DateOnly result)
    {
        long dayNumber = (long)value.DayNumber + days;
        if (dayNumber < DateOnly.MinValue.DayNumber || dayNumber > DateOnly.MaxValue.DayNumber)
        {
            result = default;
            return false;
        }

        result = DateOnly.FromDayNumber((int)dayNumber);
        return true;
    }

    public static long DaysInclusive(DateOnly start, DateOnly end) =>
        (long)end.DayNumber - start.DayNumber + 1;

    public static DateOnly MonthStart(DateOnly value) => new(value.Year, value.Month, 1);

    public static DateOnly MonthEnd(DateOnly value)
    {
        int days = DateTime.DaysInMonth(value.Year, value.Month);
        return new DateOnly(value.Year, value.Month, days);
    }

    public static decimal RoundOne(decimal value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero);

    /// <summary>One-decimal share; a zero denominator returns no percentage.</summary>
    public static string? Share(decimal part, decimal whole)
    {
        if (whole == 0m)
        {
            return null;
        }

        return FormatDecimal(RoundOne(part / whole * 100m));
    }

    /// <summary>
    /// Signed relative change against the prior magnitude. A zero prior
    /// returns no percentage; a negative Net retains its direction.
    /// </summary>
    public static string? ChangePercentage(decimal current, decimal prior)
    {
        if (prior == 0m)
        {
            return null;
        }

        return FormatDecimal(RoundOne((current - prior) / Math.Abs(prior) * 100m));
    }

    public static string FormatDecimal(decimal value) =>
        value == 0m ? "0.0" : value.ToString("0.0", CultureInfo.InvariantCulture);

    private static List<SalaryCycleStartRef> NormalizeStarts(IReadOnlyList<SalaryCycleStartRef> starts)
    {
        var seen = new HashSet<DateOnly>();
        var ordered = new List<SalaryCycleStartRef>(starts.Count);
        foreach (SalaryCycleStartRef start in starts.OrderBy(item => item.StartDate).ThenBy(item => item.Id))
        {
            if (seen.Add(start.StartDate))
            {
                ordered.Add(start);
            }
        }

        return ordered;
    }

    private static string FormatRange(DateOnly start, DateOnly end) =>
        $"{start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} – {end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    private static string FormatCycleLabel(DateOnly start, DateOnly end) =>
        start.Year == end.Year
            ? $"{start.ToString("dd MMM", CultureInfo.InvariantCulture)} – {end.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}"
            : $"{start.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)} – {end.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}";
}
