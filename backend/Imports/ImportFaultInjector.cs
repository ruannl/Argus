namespace argus.Server.Imports;

/// <summary>
/// Explicit test seam for durable-boundary failures. Production registers the
/// no-op implementation; tests may replace it to fail after a named step.
/// </summary>
public interface IImportFaultInjector
{
    void OnStep(string step);
}

public sealed class NoopImportFaultInjector : IImportFaultInjector
{
    public static readonly NoopImportFaultInjector Instance = new();

    public void OnStep(string step)
    {
    }
}
