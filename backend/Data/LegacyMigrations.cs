namespace argus.Server.Data;

/// <summary>
/// The immutable one-to-one mapping between the ten legacy schema migrations
/// and their EF migration identities. Existing schema_migrations rows stay
/// historical evidence; EF history becomes the schema-progress authority.
/// </summary>
public sealed record LegacyMigration(int Order, string LegacyName, string EfId, string ClassName);

public static class LegacyMigrations
{
    public const string CanonicalBackfillMarker = "0006_canonical_identity_backfill";

    public static readonly IReadOnlyList<LegacyMigration> All =
    [
        new(1, "0001_customer_accounts", "20261110000001_0001_customer_accounts", "M0001CustomerAccounts"),
        new(2, "0002_email_verification_tokens", "20261110000002_0002_email_verification_tokens", "M0002EmailVerificationTokens"),
        new(3, "0003_password_reset_tokens", "20261110000003_0003_password_reset_tokens", "M0003PasswordResetTokens"),
        new(4, "0004_failed_login_lockout", "20261110000004_0004_failed_login_lockout", "M0004FailedLoginLockout"),
        new(5, "0005_finance_foundation", "20261110000005_0005_finance_foundation", "M0005FinanceFoundation"),
        new(6, "0006_statement_import", "20261110000006_0006_statement_import", "M0006StatementImport"),
        new(7, "0007_transaction_workspace", "20261110000007_0007_transaction_workspace", "M0007TransactionWorkspace"),
        new(8, "0008_suggestion_learning", "20261110000008_0008_suggestion_learning", "M0008SuggestionLearning"),
        new(9, "0009_review_category_suggestion", "20261110000009_0009_review_category_suggestion", "M0009ReviewCategorySuggestion"),
        new(10, "0010_transaction_category_confirmation", "20261110000010_0010_transaction_category_confirmation", "M0010TransactionCategoryConfirmation"),
    ];

    public static int? OrderForLegacyName(string name) =>
        All.FirstOrDefault(migration => string.Equals(migration.LegacyName, name, StringComparison.Ordinal))?.Order;

    public static int? OrderForEfId(string id) =>
        All.FirstOrDefault(migration => string.Equals(migration.EfId, id, StringComparison.Ordinal))?.Order;

    public static string LegacyNameForEfId(string id) =>
        All.FirstOrDefault(migration => string.Equals(migration.EfId, id, StringComparison.Ordinal))?.LegacyName
        ?? throw new InvalidOperationException($"unknown EF migration id '{id}'");
}
