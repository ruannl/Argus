namespace argus.Server.Reporting;

/// <summary>
/// Bounded reporting fault point used only by tests to observe the
/// repeatable-read snapshot between aggregate and breakdown reads.
/// </summary>
public enum ReportFaultPoints
{
    AfterTotals,
}

public interface IReportFaultInjector
{
    void OnStep(ReportFaultPoints point);
}

public sealed class NoopReportFaultInjector : IReportFaultInjector
{
    public static NoopReportFaultInjector Instance { get; } = new();

    public void OnStep(ReportFaultPoints point)
    {
    }
}
