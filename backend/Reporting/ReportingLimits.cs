namespace argus.Server.Reporting;

/// <summary>
/// Frozen reporting/salary transport limits. They are deliberately separate
/// from the transaction workspace limits so a report change cannot widen an
/// existing import or classification contract.
/// </summary>
public static class ReportingLimits
{
    public const int ReportBodyBytes = 16 * 1024;

    public const int SalaryBodyBytes = 16 * 1024;

    public const int PageSize = 25;

    public const int RankSize = 5;

    public const int CategoryChangePageSize = 25;

    public const int CategoryChangeInitialSize = 10;

    public const int ReportReadsPerWindow = 120;

    public const int SalaryMutationsPerWindow = 30;

    public const int MaxExcludedCategoryIds = 6;

    public const int MaxSalaryStartPageSize = 25;
}
