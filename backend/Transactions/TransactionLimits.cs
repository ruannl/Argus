namespace argus.Server.Transactions;

/// <summary>
/// Frozen workspace limits shared by the HTTP surface, the store and the
/// contract tests. The frontend copies are advisory; these values are the
/// authority.
/// </summary>
public static class TransactionLimits
{
    public const int MaxSearchScalars = 200;

    public const int DefaultPageSize = 25;

    public static readonly int[] PageSizes = [25, 50, 100];

    public const int MaxExplicitSelection = 1_000;

    public const int MaxAllMatching = 50_000;

    public const int MaxTagIdsPerRequest = 100;

    public const int MaxTagsPerTransaction = 100;

    public const int MaxOpenOperations = 5;

    public const int MaxLiveManifestEntries = 100_000;

    public const int OperationExpiryMinutes = 15;

    public const int MaxPreparationsPerWindow = 20;

    public const int MaxSuggestions = 20;

    public const int QueryBodyBytes = 16 * 1024;

    public const int MutationBodyBytes = 256 * 1024;

    public const int LockTimeoutSeconds = 5;

    public const int StatementTimeoutSeconds = 30;
}
