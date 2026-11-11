using System.Text.Json;

namespace argus.Server.Data.Entities;

public sealed class Customer
{
    public Guid Id { get; set; }
    public string CanonicalEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime? EmailVerifiedAt { get; set; }
    public DateTime? SubscriberActivatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public bool IsLifetime { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? CategoriesSeededAt { get; set; }
}

public sealed class Session
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class EmailVerificationToken
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class SchemaMigration
{
    public string Name { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; }
}

public sealed class DataMigration
{
    public string Name { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; }
}

public sealed class FinancialAccount
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public string Currency { get; set; } = "ZAR";
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long ImportVersion { get; set; }
}

public sealed class Category
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class MerchantTag
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class MerchantAlias
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string DisplayAlias { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
    public Guid MerchantTagId { get; set; }
    public Guid? CategoryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class StatementImport
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid FinancialAccountId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string StatementLabel { get; set; } = string.Empty;
    public string StatementFingerprint { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int TotalRows { get; set; }
    public int ImportedCount { get; set; }
    public int SuppressedCount { get; set; }
    public int SkippedCount { get; set; }
    public bool IsHidden { get; set; }
    public DateTime? HiddenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Version { get; set; }
    public int FingerprintOccurrence { get; set; }
    public int DuplicatePolicyVersion { get; set; }
    public int PossibleDuplicateCount { get; set; }
    public int ExactOverrideCount { get; set; }
    public int StatementOverrideCount { get; set; }
    public JsonDocument OverrideSummary { get; set; } = JsonDocument.Parse("{}");
}

public sealed class Transaction
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid FinancialAccountId { get; set; }
    public Guid StatementImportId { get; set; }
    public DateOnly TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string NormalizedDescription { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid CategoryId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public int? SourcePage { get; set; }
    public int SourceRowNumber { get; set; }
    public string IdentityKey { get; set; } = string.Empty;
    public int OccurrenceIndex { get; set; }
    public bool IsHiddenByUser { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int IdentityVersion { get; set; }
    public string? SourceIdNamespace { get; set; }
    public string? SourceIdValue { get; set; }
    public string? MerchantKey { get; set; }
    public bool CategoryConfirmed { get; set; }
}

public sealed class TransactionMerchantTag
{
    public Guid TransactionId { get; set; }
    public Guid MerchantTagId { get; set; }
    public DateTime AssignedAt { get; set; }
    public Guid CustomerId { get; set; }
}

public sealed class ImportQuotaLock
{
    public Guid CustomerId { get; set; }
    public DateTime LockedAt { get; set; }
}

public sealed class ImportReview
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid FinancialAccountId { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public JsonDocument ParserConfig { get; set; } = JsonDocument.Parse("{}");
    public string Currency { get; set; } = "ZAR";
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = "Prepared";
    public DateTime ExpiresAt { get; set; }
    public long AccountImportVersion { get; set; }
    public int DuplicatePolicyVersion { get; set; }
    public string StatementFingerprint { get; set; } = string.Empty;
    public string SummaryHash { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int ImportedCount { get; set; }
    public int SuppressedCount { get; set; }
    public int SkippedCount { get; set; }
    public int PossibleDuplicateCount { get; set; }
    public int ExactOverrideCount { get; set; }
    public bool StatementOverride { get; set; }
    public int StatementOverrideCount { get; set; }
    public long PayloadBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ImportReviewRow
{
    public Guid RowId { get; set; }
    public Guid ReviewId { get; set; }
    public Guid CustomerId { get; set; }
    public int SourceLine { get; set; }
    public int? SourcePage { get; set; }
    public int SourceRowNumber { get; set; }
    public JsonDocument RawValues { get; set; } = JsonDocument.Parse("{}");
    public DateOnly? TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string NormalizedDescription { get; set; } = string.Empty;
    public long? AmountCents { get; set; }
    public string? SourceIdNamespace { get; set; }
    public string? SourceIdValue { get; set; }
    public JsonDocument ValidationCodes { get; set; } = JsonDocument.Parse("[]");
    public JsonDocument Warnings { get; set; } = JsonDocument.Parse("[]");
    public string DuplicateState { get; set; } = "none";
    public JsonDocument DuplicateReasons { get; set; } = JsonDocument.Parse("[]");
    public JsonDocument MatchingTransactionIds { get; set; } = JsonDocument.Parse("[]");
    public JsonDocument MatchingImportIds { get; set; } = JsonDocument.Parse("[]");
    public JsonDocument NearCandidates { get; set; } = JsonDocument.Parse("[]");
    public int NearCandidateTotal { get; set; }
    public string? WithinFileGroup { get; set; }
    public string WithinFileState { get; set; } = "none";
    public bool Skip { get; set; }
    public bool Override { get; set; }
    public JsonDocument SuggestedTagIds { get; set; } = JsonDocument.Parse("[]");
    public JsonDocument SelectedTagIds { get; set; } = JsonDocument.Parse("[]");
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? SuggestedCategoryId { get; set; }
    public Guid? SelectedCategoryId { get; set; }
    public bool CategoryDecided { get; set; }
}

public sealed class ImportCommitReceipt
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid OperationId { get; set; }
    public Guid FinancialAccountId { get; set; }
    public string RequestDigest { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public Guid? StatementImportId { get; set; }
    public JsonDocument Result { get; set; } = JsonDocument.Parse("{}");
    public string? DeletionDigest { get; set; }
    public long? DeletionExpectedCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class TransactionOperation
{
    public Guid CustomerId { get; set; }
    public Guid OperationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SelectionMode { get; set; } = string.Empty;
    public string RequestDigest { get; set; } = string.Empty;
    public string? FilterDigest { get; set; }
    public string? ConfirmationHash { get; set; }
    public JsonDocument Action { get; set; } = JsonDocument.Parse("{}");
    public JsonDocument TargetStamps { get; set; } = JsonDocument.Parse("{}");
    public int SelectedCount { get; set; }
    public int ChangedCount { get; set; }
    public int UnchangedCount { get; set; }
    public DateTime CapturedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AppliedAt { get; set; }
    public DateTime? TerminalAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class TransactionOperationItem
{
    public Guid CustomerId { get; set; }
    public Guid OperationId { get; set; }
    public Guid TransactionId { get; set; }
    public int TransactionVersion { get; set; }
    public Guid FinancialAccountId { get; set; }
    public Guid StatementImportId { get; set; }
    public int Ordinal { get; set; }
}
