namespace argus.Server.Imports;

/// <summary>
/// Frozen service bounds for statement import. These are product limits, not
/// deployment tuning knobs, and deliberately have no environment surface.
/// </summary>
public static class ImportLimits
{
    public const int MaxFileBytes = 10_000_000;
    public const int MaxDecompressedZipBytes = 10_000_000;
    public const int MaxDataRows = 20_000;
    public const int MaxPdfPages = 200;
    public const int MaxExtractedCharacters = 2_000_000;
    public const int MaxParserOutputBytes = 20 * 1024 * 1024;

    public const int MaxMultipartBytes = 10_100_000;
    public const int MaxMultipartOptionsBytes = 64 * 1024;
    public const int MaxJsonBodyBytes = 256 * 1024;
    public const int MaxReviewDecisionsPerRequest = 200;
    public const int MaxPageSize = 100;
    public const int MaxPreviewRows = 20;
    public const int MaxDescriptionScalars = 4_096;

    public const int MaxOpenReviewsPerCustomer = 5;
    public const long MaxOpenReviewPayloadBytes = 50L * 1024 * 1024;
    public const int ReviewLifetimeHours = 24;
    public const int ReviewHeaderRetentionDays = 30;

    public const int ParserDeadlineSeconds = 30;
    public const int MaxConcurrentParsers = 2;
    public const long MaxPdfChildHeapBytes = 256L * 1024 * 1024;
    public const long MaxPdfChildResidentBytes = 384L * 1024 * 1024;
    public const int MaxPdfChildOutputBytes = 20 * 1024 * 1024;

    public const int InspectPreparePerCustomerPer15Minutes = 10;
    public const int DuplicatePolicyVersion = 1;
    public const int IdentityVersion = 1;
}
