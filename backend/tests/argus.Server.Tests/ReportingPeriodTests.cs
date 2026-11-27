using argus.Server.Reporting;

namespace argus.Server.Tests;

public sealed class ReportingPeriodTests
{
    private static readonly SalaryCycleStartRef _startA = new(Guid.Parse("00000000-0000-0000-0000-00000000000a"), new DateOnly(2026, 1, 25));
    private static readonly SalaryCycleStartRef _startB = new(Guid.Parse("00000000-0000-0000-0000-00000000000b"), new DateOnly(2026, 2, 24));
    private static readonly SalaryCycleStartRef _startC = new(Guid.Parse("00000000-0000-0000-0000-00000000000c"), new DateOnly(2026, 3, 25));

    [Fact]
    public void SalaryCycleUsesSuccessorMinusOneAndLatestOpenEndsToday()
    {
        var today = new DateOnly(2026, 4, 10);
        SalaryCycleStartRef[] starts = [_startA, _startB, _startC];

        PeriodResolution latest = ReportingPeriods.Resolve(null, today, starts);
        Assert.NotNull(latest.Period);
        Assert.Equal("salary_cycle", latest.Period!.Mode);
        Assert.Equal(new DateOnly(2026, 3, 25), latest.Period.Start);
        Assert.Equal(today, latest.Period.End);
        Assert.True(latest.Period.IsOpenSalaryCycle);
        Assert.True(latest.Period.IsPartial);
        Assert.Equal("Salary cycle to date", latest.Period.Label);

        PeriodResolution earlier = ReportingPeriods.Resolve(
            new ReportPeriodRequest("salary_cycle", null, null, null, null, _startA.Id),
            today,
            starts);
        Assert.Equal(new DateOnly(2026, 1, 25), earlier.Period!.Start);
        Assert.Equal(new DateOnly(2026, 2, 23), earlier.Period.End);
        Assert.False(earlier.Period.IsOpenSalaryCycle);
        Assert.Equal("Salary cycle", earlier.Period.Label);
    }

    [Fact]
    public void SalaryCycleTrendBucketsEndAtTheSelectedCycle()
    {
        var today = new DateOnly(2026, 4, 10);
        SalaryCycleStartRef[] starts = [_startA, _startB, _startC];
        PeriodResolution latest = ReportingPeriods.Resolve(null, today, starts);
        Assert.NotNull(latest.Period);

        IReadOnlyList<TrendBucket> buckets = ReportingPeriods.BuildSalaryCycleBuckets(latest.Period!, today, starts);
        Assert.Equal(3, buckets.Count);
        Assert.Equal(new DateOnly(2026, 1, 25), buckets[0].Start);
        Assert.Equal(new DateOnly(2026, 2, 23), buckets[0].End);
        Assert.Equal("25 Jan – 23 Feb 2026", buckets[0].Label);
        Assert.Equal(new DateOnly(2026, 2, 24), buckets[1].Start);
        Assert.Equal(new DateOnly(2026, 3, 24), buckets[1].End);
        Assert.Equal(new DateOnly(2026, 3, 25), buckets[2].Start);
        Assert.Equal(today, buckets[2].End);
        Assert.Equal("salary_cycle", buckets[2].Granularity);

        PeriodResolution earlier = ReportingPeriods.Resolve(
            new ReportPeriodRequest("salary_cycle", null, null, null, null, _startB.Id),
            today,
            starts);
        Assert.NotNull(earlier.Period);
        IReadOnlyList<TrendBucket> earlierBuckets = ReportingPeriods.BuildSalaryCycleBuckets(earlier.Period!, today, starts);
        Assert.Equal(2, earlierBuckets.Count);
        Assert.Equal(new DateOnly(2026, 1, 25), earlierBuckets[0].Start);
        Assert.Equal(new DateOnly(2026, 2, 23), earlierBuckets[0].End);
        Assert.Equal(new DateOnly(2026, 2, 24), earlierBuckets[1].Start);
        Assert.Equal(new DateOnly(2026, 3, 24), earlierBuckets[1].End);
    }

    [Fact]
    public void SalaryCycleTrendKeepsTheSixMostRecentCycles()
    {
        var today = new DateOnly(2026, 9, 30);
        SalaryCycleStartRef[] starts = Enumerable.Range(0, 8)
            .Select(index => new SalaryCycleStartRef(Guid.NewGuid(), new DateOnly(2025, 12, 25).AddMonths(index)))
            .ToArray();
        SalaryCycleStartRef selected = starts[^1];
        PeriodResolution period = ReportingPeriods.Resolve(
            new ReportPeriodRequest("salary_cycle", null, null, null, null, selected.Id),
            today,
            starts);
        Assert.NotNull(period.Period);

        IReadOnlyList<TrendBucket> buckets = ReportingPeriods.BuildSalaryCycleBuckets(period.Period!, today, starts);
        Assert.Equal(6, buckets.Count);
        Assert.Equal(starts[^6].StartDate, buckets[0].Start);
        Assert.Equal(selected.StartDate, buckets[^1].Start);
        Assert.Equal(today, buckets[^1].End);
    }

    [Fact]
    public void IrregularReceiptGapsUseOnlyConfirmedDates()
    {
        var today = new DateOnly(2026, 5, 1);
        SalaryCycleStartRef[] starts =
        [
            new(Guid.NewGuid(), new DateOnly(2025, 12, 20)),
            new(Guid.NewGuid(), new DateOnly(2026, 2, 7)),
            new(Guid.NewGuid(), new DateOnly(2026, 4, 3)),
        ];

        PeriodResolution middle = ReportingPeriods.Resolve(
            new ReportPeriodRequest("salary_cycle", null, null, null, null, starts[1].Id),
            today,
            starts);
        Assert.Equal(new DateOnly(2026, 2, 7), middle.Period!.Start);
        Assert.Equal(new DateOnly(2026, 4, 2), middle.Period.End);
        Assert.False(middle.Period.IsPartial);
    }

    [Fact]
    public void NoConfirmedStartsRequiresSetup()
    {
        PeriodResolution resolution = ReportingPeriods.Resolve(null, new DateOnly(2026, 1, 1), []);

        Assert.Null(resolution.Period);
        Assert.Equal("no_salary_boundaries", resolution.SetupReason);
        Assert.False(resolution.NotFound);
    }

    [Fact]
    public void MissingForeignOrFutureStartIsNotFound()
    {
        var today = new DateOnly(2026, 3, 1);
        SalaryCycleStartRef future = new(Guid.NewGuid(), new DateOnly(2026, 4, 1));

        PeriodResolution missing = ReportingPeriods.Resolve(
            new ReportPeriodRequest("salary_cycle", null, null, null, null, Guid.NewGuid()),
            today,
            [_startA]);

        Assert.True(missing.NotFound);
        Assert.Null(missing.Period);

        PeriodResolution futureResolution = ReportingPeriods.Resolve(
            new ReportPeriodRequest("salary_cycle", null, null, null, null, future.Id),
            today,
            [_startA, future]);
        Assert.True(futureResolution.NotFound);
    }

    [Fact]
    public void SameDateStartsNormalizeToOneBoundary()
    {
        var today = new DateOnly(2026, 2, 20);
        SalaryCycleStartRef first = new(Guid.Parse("00000000-0000-0000-0000-000000000001"), new DateOnly(2026, 1, 10));
        SalaryCycleStartRef sameDate = new(Guid.Parse("00000000-0000-0000-0000-000000000002"), new DateOnly(2026, 1, 10));
        SalaryCycleStartRef later = new(Guid.Parse("00000000-0000-0000-0000-000000000003"), new DateOnly(2026, 2, 10));

        PeriodResolution resolution = ReportingPeriods.Resolve(
            new ReportPeriodRequest("salary_cycle", null, null, null, null, sameDate.Id),
            today,
            [first, sameDate, later]);
        ComparisonResolution comparison = ReportingPeriods.Compare(resolution.Period!, today, [first, sameDate, later]);

        Assert.Equal(new DateOnly(2026, 1, 10), resolution.Period!.Start);
        Assert.Equal(new DateOnly(2026, 2, 9), resolution.Period.End);
        Assert.Null(comparison.Period);
        Assert.Equal("insufficient_salary_boundaries", comparison.UnavailableReason);
    }

    [Fact]
    public void ClosedCycleComparesPreviousActualCycle()
    {
        var today = new DateOnly(2026, 4, 10);
        SalaryCycleStartRef[] starts = [_startA, _startB, _startC];
        PeriodResolution current = ReportingPeriods.Resolve(
            new ReportPeriodRequest("salary_cycle", null, null, null, null, _startB.Id),
            today,
            starts);

        ComparisonResolution comparison = ReportingPeriods.Compare(current.Period!, today, starts);

        Assert.NotNull(comparison.Period);
        Assert.Equal(new DateOnly(2026, 1, 25), comparison.Period!.Start);
        Assert.Equal(new DateOnly(2026, 2, 23), comparison.Period.End);
        Assert.False(comparison.ShorterPrior);
        Assert.Null(comparison.UnavailableReason);
    }

    [Fact]
    public void OpenCycleComparesEqualElapsedDaysCappedAtPriorEnd()
    {
        var today = new DateOnly(2026, 3, 15);
        SalaryCycleStartRef january = new(Guid.NewGuid(), new DateOnly(2026, 1, 1));
        SalaryCycleStartRef february = new(Guid.NewGuid(), new DateOnly(2026, 2, 1));
        SalaryCycleStartRef[] starts = [january, february];

        PeriodResolution open = ReportingPeriods.Resolve(null, today, starts);
        ComparisonResolution comparison = ReportingPeriods.Compare(open.Period!, today, starts);

        Assert.Equal(new DateOnly(2026, 2, 1), open.Period!.Start);
        Assert.Equal(today, open.Period.End);
        Assert.Equal(new DateOnly(2026, 1, 1), comparison.Period!.Start);
        Assert.Equal(new DateOnly(2026, 1, 31), comparison.Period.End);
        Assert.True(comparison.ShorterPrior);
        Assert.Equal("Same elapsed days as previous cycle", comparison.Label);
    }

    [Fact]
    public void OpenCycleWithOneStartHasNoComparison()
    {
        var today = new DateOnly(2026, 3, 15);
        PeriodResolution open = ReportingPeriods.Resolve(null, today, [_startB]);
        ComparisonResolution comparison = ReportingPeriods.Compare(open.Period!, today, [_startB]);

        Assert.Null(comparison.Period);
        Assert.Equal("insufficient_salary_boundaries", comparison.UnavailableReason);
    }

    [Fact]
    public void CalendarMonthToDateClampsShorterPriorMonth()
    {
        PeriodResolution clipped = ReportingPeriods.Resolve(
            new ReportPeriodRequest("calendar", "month_to_date", null, null, null, null),
            new DateOnly(2026, 3, 31),
            []);
        ComparisonResolution clippedComparison = ReportingPeriods.Compare(
            clipped.Period!,
            new DateOnly(2026, 3, 31),
            []);

        Assert.Equal(new DateOnly(2026, 3, 1), clipped.Period!.Start);
        Assert.Equal(new DateOnly(2026, 3, 31), clipped.Period.End);
        Assert.True(clipped.Period.IsPartial);
        Assert.Equal(new DateOnly(2026, 2, 1), clippedComparison.Period!.Start);
        Assert.Equal(new DateOnly(2026, 2, 28), clippedComparison.Period.End);
        Assert.True(clippedComparison.ShorterPrior);
        Assert.Equal("Same period last month", clippedComparison.Label);

        PeriodResolution unclipped = ReportingPeriods.Resolve(
            new ReportPeriodRequest("calendar", "month_to_date", null, null, null, null),
            new DateOnly(2026, 3, 15),
            []);
        ComparisonResolution unclippedComparison = ReportingPeriods.Compare(
            unclipped.Period!,
            new DateOnly(2026, 3, 15),
            []);

        Assert.Equal(new DateOnly(2026, 2, 15), unclippedComparison.Period!.End);
        Assert.False(unclippedComparison.ShorterPrior);
    }

    [Fact]
    public void LeapFebruaryKeepsDay29WhenMatchingOrComparingWholeMonths()
    {
        DateOnly today = new(2024, 3, 30);
        ResolvedPeriod elapsed = ReportingPeriods.Resolve(new ReportPeriodRequest("calendar", "month_to_date", null, null, null, null), today, []).Period!;
        ComparisonResolution matched = ReportingPeriods.Compare(elapsed, today, []);
        Assert.Equal(new DateOnly(2024, 2, 1), matched.Period!.Start);
        Assert.Equal(new DateOnly(2024, 2, 29), matched.Period.End);
        Assert.Equal(29, ReportingPeriods.DaysInclusive(matched.Period.Start, matched.Period.End));
        Assert.True(matched.ShorterPrior);

        ResolvedPeriod full = ReportingPeriods.Resolve(new ReportPeriodRequest("calendar", "month", "2024-03", null, null, null), today.AddDays(1), []).Period!;
        ComparisonResolution complete = ReportingPeriods.Compare(full, today.AddDays(1), []);
        Assert.Equal(matched.Period.Start, complete.Period!.Start);
        Assert.Equal(matched.Period.End, complete.Period.End);
        Assert.False(complete.ShorterPrior);
    }

    [Fact]
    public void CalendarPreviousMonthAndMultiMonthRangesMatchTheBrief()
    {
        var today = new DateOnly(2026, 3, 15);

        PeriodResolution previous = ReportingPeriods.Resolve(
            new ReportPeriodRequest("calendar", "previous_month", null, null, null, null),
            today,
            []);
        ComparisonResolution previousComparison = ReportingPeriods.Compare(previous.Period!, today, []);
        Assert.Equal(new DateOnly(2026, 2, 1), previous.Period!.Start);
        Assert.Equal(new DateOnly(2026, 2, 28), previous.Period.End);
        Assert.False(previous.Period.IsPartial);
        Assert.Equal(new DateOnly(2026, 1, 1), previousComparison.Period!.Start);
        Assert.Equal(new DateOnly(2026, 1, 31), previousComparison.Period.End);

        PeriodResolution lastThree = ReportingPeriods.Resolve(
            new ReportPeriodRequest("calendar", "last_three_months", null, null, null, null),
            today,
            []);
        ComparisonResolution lastThreeComparison = ReportingPeriods.Compare(lastThree.Period!, today, []);
        Assert.Equal(new DateOnly(2026, 1, 1), lastThree.Period!.Start);
        Assert.Equal(today, lastThree.Period.End);
        Assert.True(lastThree.Period.IsPartial);
        Assert.Equal(new DateOnly(2025, 10, 19), lastThreeComparison.Period!.Start);
        Assert.Equal(new DateOnly(2025, 12, 31), lastThreeComparison.Period.End);

        PeriodResolution lastSix = ReportingPeriods.Resolve(
            new ReportPeriodRequest("calendar", "last_six_months", null, null, null, null),
            today,
            []);
        ComparisonResolution lastSixComparison = ReportingPeriods.Compare(lastSix.Period!, today, []);
        Assert.Equal(new DateOnly(2025, 10, 1), lastSix.Period!.Start);
        Assert.Equal(new DateOnly(2025, 4, 18), lastSixComparison.Period!.Start);
        Assert.Equal(new DateOnly(2025, 9, 30), lastSixComparison.Period.End);
    }

    [Fact]
    public void FullCalendarMonthAndCustomCompareAsSpecified()
    {
        var today = new DateOnly(2026, 3, 15);
        PeriodResolution month = ReportingPeriods.Resolve(
            new ReportPeriodRequest("calendar", "month", "2026-02", null, null, null),
            today,
            []);
        ComparisonResolution monthComparison = ReportingPeriods.Compare(month.Period!, today, []);
        Assert.Equal(new DateOnly(2026, 2, 1), month.Period!.Start);
        Assert.Equal(new DateOnly(2026, 2, 28), month.Period.End);
        Assert.Equal(new DateOnly(2026, 1, 1), monthComparison.Period!.Start);
        Assert.Equal(new DateOnly(2026, 1, 31), monthComparison.Period.End);

        PeriodResolution custom = ReportingPeriods.Resolve(
            new ReportPeriodRequest("custom", null, null, "2026-02-10", "2026-02-20", null),
            today,
            []);
        ComparisonResolution customComparison = ReportingPeriods.Compare(custom.Period!, today, []);
        Assert.Equal(new DateOnly(2026, 2, 10), custom.Period!.Start);
        Assert.Equal(new DateOnly(2026, 2, 20), custom.Period.End);
        Assert.False(custom.Period.IsPartial);
        Assert.Equal(new DateOnly(2026, 1, 30), customComparison.Period!.Start);
        Assert.Equal(new DateOnly(2026, 2, 9), customComparison.Period.End);
    }

    [Fact]
    public void FutureCustomRangeIsAllowedAndHonestlyLabelled()
    {
        PeriodResolution future = ReportingPeriods.Resolve(
            new ReportPeriodRequest("custom", null, null, "2026-12-01", "2026-12-31", null),
            new DateOnly(2026, 3, 1),
            []);

        Assert.Equal(new DateOnly(2026, 12, 1), future.Period!.Start);
        Assert.Equal(new DateOnly(2026, 12, 31), future.Period.End);
        Assert.True(future.Period.IsFuture);
        Assert.True(future.Period.IsPartial);
        Assert.False(string.IsNullOrEmpty(future.Period.Note));
    }

    [Fact]
    public void LeapDayAndFebruaryEdgesUseRealMonthLengths()
    {
        var today = new DateOnly(2028, 3, 31);
        PeriodResolution previous = ReportingPeriods.Resolve(
            new ReportPeriodRequest("calendar", "previous_month", null, null, null, null),
            today,
            []);
        Assert.Equal(29, ReportingPeriods.DaysInclusive(previous.Period!.Start, previous.Period.End));

        PeriodResolution monthToDate = ReportingPeriods.Resolve(
            new ReportPeriodRequest("calendar", "month_to_date", null, null, null, null),
            today,
            []);
        ComparisonResolution comparison = ReportingPeriods.Compare(monthToDate.Period!, today, []);
        Assert.Equal(new DateOnly(2028, 2, 29), comparison.Period!.End);
        Assert.True(comparison.ShorterPrior);

        PeriodResolution february = ReportingPeriods.Resolve(
            new ReportPeriodRequest("calendar", "month", "2028-02", null, null, null),
            new DateOnly(2028, 1, 1),
            []);
        Assert.Equal(29, ReportingPeriods.DaysInclusive(february.Period!.Start, february.Period.End));
        Assert.True(february.Period.IsFuture);
        Assert.False(february.Period.IsPartial);
    }

    [Fact]
    public void RepresentableDateExtremaDoNotOverflow()
    {
        PeriodResolution earliest = ReportingPeriods.Resolve(
            new ReportPeriodRequest("custom", null, null, "0001-01-01", "0001-01-10", null),
            new DateOnly(2026, 1, 1),
            []);
        ComparisonResolution earliestComparison = ReportingPeriods.Compare(earliest.Period!, new DateOnly(2026, 1, 1), []);
        Assert.Null(earliestComparison.Period);
        Assert.Equal("date_boundary", earliestComparison.UnavailableReason);

        PeriodResolution latest = ReportingPeriods.Resolve(
            new ReportPeriodRequest("custom", null, null, "9999-12-01", "9999-12-31", null),
            new DateOnly(9999, 12, 31),
            []);
        ComparisonResolution latestComparison = ReportingPeriods.Compare(latest.Period!, new DateOnly(9999, 12, 31), []);
        Assert.Equal(new DateOnly(9999, 10, 31), latestComparison.Period!.Start);
        Assert.Equal(new DateOnly(9999, 11, 30), latestComparison.Period.End);

        IReadOnlyList<TrendBucket> buckets = ReportingPeriods.BuildBuckets(new DateOnly(9999, 12, 1), new DateOnly(9999, 12, 31));
        Assert.Equal(5, buckets.Count);
        Assert.Equal(new DateOnly(9999, 12, 31), buckets[^1].End);
    }

    [Fact]
    public void MinimumMonthToDateHasNoComparisonRatherThanOverflow()
    {
        var today = new DateOnly(1, 1, 15);
        PeriodResolution period = ReportingPeriods.Resolve(
            new ReportPeriodRequest("calendar", "month_to_date", null, null, null, null),
            today,
            []);

        Assert.NotNull(period.Period);
        Assert.Equal(new DateOnly(1, 1, 1), period.Period!.Start);
        Assert.Equal(today, period.Period.End);
        ComparisonResolution comparison = ReportingPeriods.Compare(period.Period, today, []);
        Assert.Null(comparison.Period);
        Assert.Equal("date_boundary", comparison.UnavailableReason);
    }

    [Fact]
    public void ExplicitMissingStartIsNotFoundEvenWhenTheSequenceIsEmpty()
    {
        PeriodResolution resolution = ReportingPeriods.Resolve(
            new ReportPeriodRequest("salary_cycle", null, null, null, null, Guid.NewGuid()),
            new DateOnly(2026, 1, 1),
            []);

        Assert.True(resolution.NotFound);
        Assert.Null(resolution.SetupReason);
    }

    [Fact]
    public void GroupedMonthBucketsDoNotOverflowAtTheMaximumYear()
    {
        IReadOnlyList<TrendBucket> buckets = ReportingPeriods.BuildBuckets(
            new DateOnly(9989, 12, 1),
            new DateOnly(9999, 12, 31));

        Assert.NotEmpty(buckets);
        Assert.True(buckets.Count <= 120);
        Assert.Equal(new DateOnly(9989, 12, 1), buckets[0].Start);
        Assert.Equal(new DateOnly(9999, 12, 31), buckets[^1].End);
        Assert.All(buckets, bucket => Assert.True(bucket.Start <= bucket.End));
    }

    [Fact]
    public void BucketsStayBoundedAndClipEdges()
    {
        IReadOnlyList<TrendBucket> shortRange = ReportingPeriods.BuildBuckets(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));
        Assert.Equal(2, shortRange.Count);
        Assert.Equal("week", shortRange[0].Granularity);
        Assert.Equal(new DateOnly(2026, 1, 7), shortRange[0].End);
        Assert.Equal(new DateOnly(2026, 1, 8), shortRange[1].Start);
        Assert.Equal(new DateOnly(2026, 1, 10), shortRange[1].End);

        IReadOnlyList<TrendBucket> sixtyTwo = ReportingPeriods.BuildBuckets(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 3));
        Assert.Equal(9, sixtyTwo.Count);
        Assert.Equal(new DateOnly(2026, 3, 3), sixtyTwo[^1].End);

        IReadOnlyList<TrendBucket> longer = ReportingPeriods.BuildBuckets(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 4));
        Assert.Equal(3, longer.Count);
        Assert.All(longer, bucket => Assert.Equal("month", bucket.Granularity));

        IReadOnlyList<TrendBucket> multiYear = ReportingPeriods.BuildBuckets(new DateOnly(2000, 1, 1), new DateOnly(2026, 12, 31));
        Assert.True(multiYear.Count <= 120);
        Assert.True(multiYear.Count > 1);
        Assert.Equal(new DateOnly(2000, 1, 1), multiYear[0].Start);
        Assert.Equal(new DateOnly(2026, 12, 31), multiYear[^1].End);
    }

    [Fact]
    public void SharesAndSignedPercentagesUseOneDecimalAwayFromZero()
    {
        Assert.Equal("33.3", ReportingPeriods.Share(1m, 3m));
        Assert.Equal("66.7", ReportingPeriods.Share(2m, 3m));
        Assert.Equal("12.3", ReportingPeriods.FormatDecimal(ReportingPeriods.RoundOne(12.25m)));
        Assert.Equal("-12.3", ReportingPeriods.FormatDecimal(ReportingPeriods.RoundOne(-12.25m)));
        Assert.Null(ReportingPeriods.Share(0m, 0m));

        Assert.Equal("50.0", ReportingPeriods.ChangePercentage(150m, 100m));
        Assert.Equal("-50.0", ReportingPeriods.ChangePercentage(50m, 100m));
        Assert.Equal("50.0", ReportingPeriods.ChangePercentage(-50m, -100m));
        Assert.Equal("-200.0", ReportingPeriods.ChangePercentage(-100m, 100m));
        Assert.Null(ReportingPeriods.ChangePercentage(100m, 0m));
    }

    [Fact]
    public void ReportingTodayUsesJohannesburgMidnightBoundary()
    {
        Assert.Equal(
            new DateOnly(2026, 11, 14),
            ReportingClock.Today(new FixedTimeProvider(new DateTimeOffset(2026, 11, 14, 21, 59, 59, TimeSpan.Zero))));
        Assert.Equal(
            new DateOnly(2026, 11, 15),
            ReportingClock.Today(new FixedTimeProvider(new DateTimeOffset(2026, 11, 14, 22, 0, 0, TimeSpan.Zero))));
        Assert.Equal(
            new DateOnly(2026, 1, 1),
            ReportingClock.Today(new FixedTimeProvider(new DateTimeOffset(2025, 12, 31, 22, 30, 0, TimeSpan.Zero))));
    }

    [Theory]
    [InlineData("salary_cycle", "month_to_date", null, null, null)]
    [InlineData("calendar", "month_to_date", null, "2026-01-01", null)]
    [InlineData("calendar", "month", null, null, null)]
    [InlineData("calendar", "month", "2026-13", null, null)]
    [InlineData("calendar", "unknown", null, null, null)]
    [InlineData("custom", null, null, "2026-02-01", null)]
    [InlineData("custom", null, null, "2026-02-02", "2026-02-01")]
    [InlineData("unknown", null, null, null, null)]
    public void PeriodDiscriminantRejectsMixedOrMalformedFields(
        string mode,
        string? preset,
        string? month,
        string? start,
        string? end)
    {
        PeriodResolution resolution = ReportingPeriods.Resolve(
            new ReportPeriodRequest(mode, preset, month, start, end, null),
            new DateOnly(2026, 3, 1),
            [_startA]);

        Assert.Null(resolution.Period);
        Assert.False(string.IsNullOrEmpty(resolution.InvalidReason));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
