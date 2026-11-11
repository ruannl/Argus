using System.Diagnostics;

namespace argus.Server.Tests;

/// <summary>
/// Test-only Npgsql activity observer. It records driver-level command, COPY,
/// transaction-control and connection activities emitted on the Npgsql source.
/// It is deliberately independent of EF so the same method can measure the
/// legacy baseline and the converted implementation.
/// </summary>
internal sealed class NpgsqlActivityCounter : IDisposable
{
    private readonly object _gate = new();
    private readonly List<Activity> _activities = [];
    private readonly ActivityListener _listener;

    public NpgsqlActivityCounter()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_gate)
                {
                    _activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Reset()
    {
        lock (_gate)
        {
            _activities.Clear();
        }
    }

    public IReadOnlyList<ActivitySnapshot> Snapshot()
    {
        lock (_gate)
        {
            return _activities
                .Select(activity => new ActivitySnapshot(
                    activity.OperationName,
                    activity.Source.Name,
                    activity.Duration,
                    activity.Tags.Select(tag => new KeyValuePair<string, string?>(tag.Key, tag.Value)).ToArray()))
                .ToArray();
        }
    }

    public ActivityCounts Counts()
    {
        return ActivityCounts.From(Snapshot());
    }

    public void Dispose() => _listener.Dispose();
}

internal sealed record ActivitySnapshot(string OperationName,
                                       string SourceName,
                                       TimeSpan Duration,
                                       IReadOnlyList<KeyValuePair<string, string?>> Tags)
{
    public string? Text =>
        Tags.FirstOrDefault(tag => tag.Key == "db.query.text").Value;

    public string? Operation =>
        Tags.FirstOrDefault(tag => tag.Key == "db.operation.name").Value;
}

internal sealed record ActivityCounts(int Select,
                                      int Insert,
                                      int Update,
                                      int Delete,
                                      int Copy,
                                      int Begin,
                                      int Commit,
                                      int Rollback,
                                      int Savepoint,
                                      int Batch,
                                      int Other,
                                      int Opens)
{
    public int Statements => Select + Insert + Update + Delete + Copy + Batch + Other;
    public int TransactionControl => Begin + Commit + Rollback + Savepoint;

    public static ActivityCounts From(IReadOnlyList<ActivitySnapshot> activities)
    {
        int select = 0, insert = 0, update = 0, delete = 0, copy = 0;
        int begin = 0, commit = 0, rollback = 0, savepoint = 0, batch = 0, other = 0, opens = 0;
        foreach (ActivitySnapshot activity in activities)
        {
            string text = (activity.Text ?? string.Empty).TrimStart();
            string operation = activity.Operation ?? string.Empty;
            if (activity.SourceName != "Npgsql")
            {
                continue;
            }
            if (operation.StartsWith("COPY", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("COPY ", StringComparison.OrdinalIgnoreCase))
            {
                copy++;
                continue;
            }
            if (text.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                begin++;
                continue;
            }
            if (text.StartsWith("COMMIT", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("END", StringComparison.OrdinalIgnoreCase))
            {
                commit++;
                continue;
            }
            if (text.StartsWith("ROLLBACK", StringComparison.OrdinalIgnoreCase))
            {
                rollback++;
                continue;
            }
            if (text.StartsWith("SAVEPOINT", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("RELEASE SAVEPOINT", StringComparison.OrdinalIgnoreCase))
            {
                savepoint++;
                continue;
            }
            if (text.Count(c => c == ';') >= 1 &&
                text.Split(';', StringSplitOptions.RemoveEmptyEntries).Length > 1)
            {
                batch++;
                continue;
            }
            if (text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("TABLE ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("VALUES", StringComparison.OrdinalIgnoreCase))
            {
                select++;
                continue;
            }
            if (text.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
            {
                insert++;
                continue;
            }
            if (text.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                update++;
                continue;
            }
            if (text.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                delete++;
                continue;
            }
            if (activity.OperationName.Contains("connection", StringComparison.OrdinalIgnoreCase))
            {
                opens++;
                continue;
            }
            other++;
        }

        return new ActivityCounts(select, insert, update, delete, copy, begin, commit, rollback, savepoint, batch, other, opens);
    }
}

internal sealed record PerformanceSample(string Scenario,
                                         int Round,
                                         int Index,
                                         double ElapsedMs,
                                         ActivityCounts Counts,
                                         bool Correct);

internal static class PerformanceSummary
{
    public static string Format(string scenario, IReadOnlyList<PerformanceSample> samples)
    {
        if (samples.Count == 0)
        {
            return $"PERF scenario={scenario} samples=0 status=NOT RUN";
        }

        double[] elapsed = samples.Select(sample => sample.ElapsedMs).Order().ToArray();
        double median = Percentile(elapsed, 0.5);
        double p95 = Percentile(elapsed, 0.95);
        double min = elapsed[0];
        double max = elapsed[^1];
        double mean = elapsed.Average();
        double variance = elapsed.Select(value => Math.Pow(value - mean, 2)).Average();
        double stdev = Math.Sqrt(variance);
        int correct = samples.Count(sample => sample.Correct);
        ActivityCounts totals = Sum(samples.Select(sample => sample.Counts));

        return string.Join(
            ' ',
            $"PERF scenario={scenario}",
            $"samples={samples.Count}",
            $"correct={correct}",
            $"median_ms={median:F2}",
            $"p95_ms={p95:F2}",
            $"min_ms={min:F2}",
            $"max_ms={max:F2}",
            $"stdev_ms={stdev:F2}",
            $"select={totals.Select}",
            $"insert={totals.Insert}",
            $"update={totals.Update}",
            $"delete={totals.Delete}",
            $"copy={totals.Copy}",
            $"begin={totals.Begin}",
            $"commit={totals.Commit}",
            $"rollback={totals.Rollback}",
            $"savepoint={totals.Savepoint}",
            $"batch={totals.Batch}",
            $"other={totals.Other}",
            $"opens={totals.Opens}");
    }

    public static ActivityCounts Sum(IEnumerable<ActivityCounts> values)
    {
        ActivityCounts total = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        foreach (ActivityCounts value in values)
        {
            total = new ActivityCounts(
                total.Select + value.Select,
                total.Insert + value.Insert,
                total.Update + value.Update,
                total.Delete + value.Delete,
                total.Copy + value.Copy,
                total.Begin + value.Begin,
                total.Commit + value.Commit,
                total.Rollback + value.Rollback,
                total.Savepoint + value.Savepoint,
                total.Batch + value.Batch,
                total.Other + value.Other,
                total.Opens + value.Opens);
        }

        return total;
    }

    private static double Percentile(IReadOnlyList<double> ordered, double percentile)
    {
        if (ordered.Count == 0)
        {
            return 0;
        }
        int index = (int)Math.Ceiling(percentile * ordered.Count) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Count - 1)];
    }
}
