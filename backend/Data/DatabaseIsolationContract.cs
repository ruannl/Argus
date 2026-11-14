namespace argus.Server.Data;

/// <summary>
/// One authoritative inventory of the customer-isolation objects, roles and
/// grants. Provisioning, migrations, startup validation and tests read this
/// contract so a changed name or relation cannot silently drift.
/// </summary>
public static class DatabaseIsolationContract
{
    public const string OwnerRole = Brand.ProductSlug + "_owner";
    public const string MigratorRole = Brand.ProductSlug + "_migrator";
    public const string RuntimeRole = Brand.ProductSlug + "_runtime";

    public const string AuthorityFunction = "argus_current_customer_id";
    public const string ImportCleanupFunction = "argus_cleanup_import_reviews";
    public const string TransactionCleanupFunction = "argus_cleanup_transaction_operations";
    public const string MerchantKeyFunction = "argus_merchant_key";

    public const string CustomerSetting = "argus.customer_id";
    public const string SessionHashSetting = "argus.session_hash";
    public const string TransactionSetting = "argus.transaction_id";

    public const string RuntimePolicy = "argus_runtime_customer";
    public const string OwnerPolicy = "argus_owner_maintenance";

    public static readonly IReadOnlyList<string> ProtectedTables =
    [
        "financial_accounts",
        "categories",
        "merchant_tags",
        "merchant_aliases",
        "statement_imports",
        "transactions",
        "transaction_merchant_tags",
        "import_quota_locks",
        "import_reviews",
        "import_review_rows",
        "import_commit_receipts",
        "transaction_operations",
        "transaction_operation_items",
    ];

    public static readonly IReadOnlyList<string> ControlPlaneTables =
    [
        "customers",
        "sessions",
        "email_verification_tokens",
        "password_reset_tokens",
    ];

    public static readonly IReadOnlyList<string> LedgerTables =
    [
        "schema_migrations",
        "data_migrations",
        "__EFMigrationsHistory",
    ];

    /// <summary>
    /// Columns runtime may update. customer_id, primary keys, creation
    /// timestamps and financial identity columns are deliberately absent.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> RuntimeUpdateColumns =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["financial_accounts"] = ["display_name", "institution", "is_archived", "archived_at", "import_version", "updated_at"],
            ["categories"] = ["display_name", "normalized_name", "kind", "is_default", "is_archived", "archived_at", "sort_order", "updated_at"],
            ["merchant_tags"] = ["display_name", "normalized_name", "is_archived", "archived_at", "updated_at"],
            ["merchant_aliases"] = ["display_alias", "normalized_alias", "merchant_tag_id", "category_id", "updated_at"],
            ["statement_imports"] = ["is_hidden", "hidden_at", "version", "updated_at"],
            ["transactions"] = ["category_id", "category_confirmed", "is_hidden_by_user", "version", "updated_at"],
            ["import_quota_locks"] = ["locked_at"],
            ["import_reviews"] =
            [
                "parser_config", "start_date", "end_date", "statement_fingerprint", "summary_hash",
                "total_rows", "imported_count", "suppressed_count", "skipped_count",
                "possible_duplicate_count", "exact_override_count", "statement_override",
                "statement_override_count", "payload_bytes", "status", "version",
                "account_import_version", "updated_at",
            ],
            ["import_review_rows"] =
            [
                "raw_values", "transaction_date", "description", "normalized_description", "amount_cents",
                "duplicate_state", "duplicate_reasons", "matching_transaction_ids", "matching_import_ids",
                "near_candidates", "near_candidate_total", "within_file_group", "within_file_state",
                "skip", "override", "suggested_tag_ids", "selected_tag_ids", "suggested_category_id",
                "selected_category_id", "category_decided", "validation_codes", "warnings", "updated_at",
            ],
            ["import_commit_receipts"] = ["state", "result", "deletion_digest", "deletion_expected_count", "updated_at"],
            ["transaction_operations"] = ["status", "applied_at", "terminal_at", "action", "target_stamps", "updated_at"],
        };

    /// <summary>
    /// Tables runtime may delete from. Retained receipts and audit ledgers are
    /// intentionally absent.
    /// </summary>
    public static readonly IReadOnlyList<string> RuntimeDeleteTables =
    [
        "financial_accounts",
        "categories",
        "merchant_tags",
        "merchant_aliases",
        "statement_imports",
        "transactions",
        "transaction_merchant_tags",
        "import_quota_locks",
        "import_reviews",
        "import_review_rows",
        "transaction_operation_items",
    ];

    public static readonly IReadOnlyList<string> RuntimeInsertTables =
    [
        "financial_accounts",
        "categories",
        "merchant_tags",
        "merchant_aliases",
        "statement_imports",
        "transactions",
        "transaction_merchant_tags",
        "import_quota_locks",
        "import_reviews",
        "import_review_rows",
        "import_commit_receipts",
        "transaction_operations",
        "transaction_operation_items",
    ];

    public static readonly string[] CustomerInsertColumns =
    [
        "id", "canonical_email", "password_hash", "created_at", "updated_at",
    ];

    public static readonly string[] CustomerUpdateColumns =
    [
        "password_hash", "email_verified_at", "subscriber_activated_at", "next_billing_at",
        "failed_login_count", "locked_until", "updated_at", "categories_seeded_at",
    ];
}
