using System.Text.Json;
using Microsoft.EntityFrameworkCore;

#nullable disable

namespace argus.Server.Data.Migrations;

/// <summary>
/// Frozen Phase 2 target-model baseline for migrations 0001-0010. Keeping this
/// outside the generated snapshot prevents an ordinary scaffold from deleting
/// the helper the designers depend on. Do not update this baseline when the
/// live model changes; new migrations get their own target metadata.
/// </summary>
public static class ArgusPhase2MigrationModel
{
    public static void Build(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.4")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("argus.Server.Data.Entities.Category", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<DateTime?>("ArchivedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("archived_at");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<string>("DisplayName")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("display_name");

                b.Property<bool>("IsArchived")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("is_archived");

                b.Property<bool>("IsDefault")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("is_default");

                b.Property<bool>("IsSystem")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("is_system");

                b.Property<string>("Kind")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("kind");

                b.Property<string>("NormalizedName")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("normalized_name");

                b.Property<int>("SortOrder")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(100)
                    .HasColumnName("sort_order");

                b.Property<DateTime>("UpdatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.HasKey("Id")
                    .HasName("categories_pkey");

                b.HasAlternateKey("CustomerId", "Id")
                    .HasName("categories_customer_id_id_key");

                b.HasAlternateKey("CustomerId", "NormalizedName")
                    .HasName("categories_customer_id_normalized_name_key");

                b.HasIndex(new[] { "CustomerId" }, "categories_customer_id_idx")
                    .HasDatabaseName("categories_customer_id_idx");

                b.HasIndex(new[] { "CustomerId" }, "categories_customer_system_uidx")
                    .IsUnique()
                    .HasDatabaseName("categories_customer_system_uidx")
                    .HasFilter("is_system = true");

                b.ToTable("categories", null, t =>
                    {
                        t.HasCheckConstraint("categories_kind_check", "(is_system = TRUE AND kind = 'Uncategorised' AND is_archived = FALSE) OR (is_system = FALSE AND kind IN ('Income', 'Expense', 'Transfer', 'Excluded'))");
                    });
            });

        modelBuilder.Entity("argus.Server.Data.Entities.Customer", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<string>("CanonicalEmail")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("canonical_email");

                b.Property<DateTime?>("CategoriesSeededAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("categories_seeded_at");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<DateTime?>("EmailVerifiedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("email_verified_at");

                b.Property<int>("FailedLoginCount")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0)
                    .HasColumnName("failed_login_count");

                b.Property<bool>("IsAdmin")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("is_admin");

                b.Property<bool>("IsLifetime")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("is_lifetime");

                b.Property<DateTime?>("LockedUntil")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("locked_until");

                b.Property<DateTime?>("NextBillingAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("next_billing_at");

                b.Property<string>("PasswordHash")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("password_hash");

                b.Property<DateTime?>("SubscriberActivatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("subscriber_activated_at");

                b.Property<DateTime>("UpdatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.HasKey("Id")
                    .HasName("customers_pkey");

                b.HasAlternateKey("CanonicalEmail")
                    .HasName("customers_canonical_email_key");

                b.ToTable("customers", (string)null);
            });

        modelBuilder.Entity("argus.Server.Data.Entities.DataMigration", b =>
            {
                b.Property<string>("Name")
                    .HasColumnType("text")
                    .HasColumnName("name");

                b.Property<DateTime>("AppliedAt")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("applied_at")
                    .HasDefaultValueSql("now()");

                b.HasKey("Name")
                    .HasName("data_migrations_pkey");

                b.ToTable("data_migrations", (string)null);
            });

        modelBuilder.Entity("argus.Server.Data.Entities.EmailVerificationToken", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<DateTime?>("ConsumedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("consumed_at");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<DateTime>("ExpiresAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("expires_at");

                b.Property<string>("TokenHash")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("token_hash");

                b.HasKey("Id")
                    .HasName("email_verification_tokens_pkey");

                b.HasAlternateKey("TokenHash")
                    .HasName("email_verification_tokens_token_hash_key");

                b.HasIndex("CustomerId")
                    .HasDatabaseName("email_verification_tokens_customer_id_idx");

                b.ToTable("email_verification_tokens", (string)null);
            });

        modelBuilder.Entity("argus.Server.Data.Entities.FinancialAccount", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<DateTime?>("ArchivedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("archived_at");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<string>("Currency")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("text")
                    .HasDefaultValue("ZAR")
                    .HasColumnName("currency");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<string>("DisplayName")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("display_name");

                b.Property<long>("ImportVersion")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("bigint")
                    .HasDefaultValue(0L)
                    .HasColumnName("import_version");

                b.Property<string>("Institution")
                    .HasColumnType("text")
                    .HasColumnName("institution");

                b.Property<bool>("IsArchived")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("is_archived");

                b.Property<DateTime>("UpdatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.HasKey("Id")
                    .HasName("financial_accounts_pkey");

                b.HasAlternateKey("CustomerId", "Id")
                    .HasName("financial_accounts_customer_id_id_key");

                b.HasIndex("CustomerId")
                    .HasDatabaseName("financial_accounts_customer_id_idx");

                b.ToTable("financial_accounts", null, t =>
                    {
                        t.HasCheckConstraint("financial_accounts_currency_check", "currency = 'ZAR'");
                    });
            });

        modelBuilder.Entity("argus.Server.Data.Entities.ImportCommitReceipt", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<string>("DeletionDigest")
                    .HasColumnType("text")
                    .HasColumnName("deletion_digest");

                b.Property<long?>("DeletionExpectedCount")
                    .HasColumnType("bigint")
                    .HasColumnName("deletion_expected_count");

                b.Property<Guid>("FinancialAccountId")
                    .HasColumnType("uuid")
                    .HasColumnName("financial_account_id");

                b.Property<Guid>("OperationId")
                    .HasColumnType("uuid")
                    .HasColumnName("operation_id");

                b.Property<string>("RequestDigest")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("request_digest");

                b.Property<JsonDocument>("Result")
                    .IsRequired()
                    .HasColumnType("jsonb")
                    .HasColumnName("result");

                b.Property<string>("State")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("state");

                b.Property<Guid?>("StatementImportId")
                    .HasColumnType("uuid")
                    .HasColumnName("statement_import_id");

                b.Property<DateTime>("UpdatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.HasKey("Id")
                    .HasName("import_commit_receipts_pkey");

                b.HasAlternateKey("CustomerId", "OperationId")
                    .HasName("import_commit_receipts_customer_id_operation_id_key");

                b.HasIndex("CustomerId", "OperationId")
                    .HasDatabaseName("import_commit_receipts_customer_idx");

                b.ToTable("import_commit_receipts", null, t =>
                    {
                        t.HasCheckConstraint("import_commit_receipts_state_check", "state IN ('Committed', 'Deleted')");
                    });
            });

        modelBuilder.Entity("argus.Server.Data.Entities.ImportQuotaLock", b =>
            {
                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<DateTime>("LockedAt")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("locked_at")
                    .HasDefaultValueSql("now()");

                b.HasKey("CustomerId")
                    .HasName("import_quota_locks_pkey");

                b.ToTable("import_quota_locks", (string)null);
            });

        modelBuilder.Entity("argus.Server.Data.Entities.ImportReview", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<long>("AccountImportVersion")
                    .HasColumnType("bigint")
                    .HasColumnName("account_import_version");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<string>("Currency")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("text")
                    .HasDefaultValue("ZAR")
                    .HasColumnName("currency");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<int>("DuplicatePolicyVersion")
                    .HasColumnType("integer")
                    .HasColumnName("duplicate_policy_version");

                b.Property<DateOnly?>("EndDate")
                    .HasColumnType("date")
                    .HasColumnName("end_date");

                b.Property<int>("ExactOverrideCount")
                    .HasColumnType("integer")
                    .HasColumnName("exact_override_count");

                b.Property<DateTime>("ExpiresAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("expires_at");

                b.Property<Guid>("FinancialAccountId")
                    .HasColumnType("uuid")
                    .HasColumnName("financial_account_id");

                b.Property<int>("ImportedCount")
                    .HasColumnType("integer")
                    .HasColumnName("imported_count");

                b.Property<JsonDocument>("ParserConfig")
                    .IsRequired()
                    .HasColumnType("jsonb")
                    .HasColumnName("parser_config");

                b.Property<long>("PayloadBytes")
                    .HasColumnType("bigint")
                    .HasColumnName("payload_bytes");

                b.Property<int>("PossibleDuplicateCount")
                    .HasColumnType("integer")
                    .HasColumnName("possible_duplicate_count");

                b.Property<int>("SkippedCount")
                    .HasColumnType("integer")
                    .HasColumnName("skipped_count");

                b.Property<string>("SourceKind")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("source_kind");

                b.Property<DateOnly?>("StartDate")
                    .HasColumnType("date")
                    .HasColumnName("start_date");

                b.Property<string>("StatementFingerprint")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("statement_fingerprint");

                b.Property<bool>("StatementOverride")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("statement_override");

                b.Property<int>("StatementOverrideCount")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0)
                    .HasColumnName("statement_override_count");

                b.Property<string>("Status")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("text")
                    .HasDefaultValue("Prepared")
                    .HasColumnName("status");

                b.Property<string>("SummaryHash")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("summary_hash");

                b.Property<int>("SuppressedCount")
                    .HasColumnType("integer")
                    .HasColumnName("suppressed_count");

                b.Property<int>("TotalRows")
                    .HasColumnType("integer")
                    .HasColumnName("total_rows");

                b.Property<DateTime>("UpdatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.Property<int>("Version")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(1)
                    .HasColumnName("version");

                b.HasKey("Id")
                    .HasName("import_reviews_pkey");

                b.HasAlternateKey("CustomerId", "Id")
                    .HasName("import_reviews_customer_id_id_key");

                b.HasIndex("CustomerId", "FinancialAccountId")
                    .HasDatabaseName("import_reviews_account_idx");

                b.HasIndex("CustomerId", "Status", "ExpiresAt")
                    .HasDatabaseName("import_reviews_customer_status_idx");

                b.ToTable("import_reviews", null, t =>
                    {
                        t.HasCheckConstraint("import_reviews_counts_check", "total_rows >= 0 AND imported_count >= 0 AND suppressed_count >= 0 AND skipped_count >= 0 AND possible_duplicate_count >= 0 AND exact_override_count >= 0 AND statement_override_count >= 0 AND total_rows = imported_count + suppressed_count + skipped_count");

                        t.HasCheckConstraint("import_reviews_currency_check", "currency = 'ZAR'");

                        t.HasCheckConstraint("import_reviews_dates_check", "(start_date IS NULL AND end_date IS NULL) OR start_date <= end_date");

                        t.HasCheckConstraint("import_reviews_status_check", "status IN ('Prepared', 'Abandoned', 'Expired')");

                        t.HasCheckConstraint("import_reviews_version_check", "version > 0");
                    });
            });

        modelBuilder.Entity("argus.Server.Data.Entities.ImportReviewRow", b =>
            {
                b.Property<Guid>("RowId")
                    .HasColumnType("uuid")
                    .HasColumnName("row_id");

                b.Property<long?>("AmountCents")
                    .HasColumnType("bigint")
                    .HasColumnName("amount_cents");

                b.Property<bool>("CategoryDecided")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("category_decided");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<string>("Description")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("description");

                b.Property<JsonDocument>("DuplicateReasons")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("jsonb")
                    .HasColumnName("duplicate_reasons")
                    .HasDefaultValueSql("'[]'::jsonb");

                b.Property<string>("DuplicateState")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("text")
                    .HasDefaultValue("none")
                    .HasColumnName("duplicate_state");

                b.Property<JsonDocument>("MatchingImportIds")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("jsonb")
                    .HasColumnName("matching_import_ids")
                    .HasDefaultValueSql("'[]'::jsonb");

                b.Property<JsonDocument>("MatchingTransactionIds")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("jsonb")
                    .HasColumnName("matching_transaction_ids")
                    .HasDefaultValueSql("'[]'::jsonb");

                b.Property<int>("NearCandidateTotal")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0)
                    .HasColumnName("near_candidate_total");

                b.Property<JsonDocument>("NearCandidates")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("jsonb")
                    .HasColumnName("near_candidates")
                    .HasDefaultValueSql("'[]'::jsonb");

                b.Property<string>("NormalizedDescription")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("normalized_description");

                b.Property<bool>("Override")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("override");

                b.Property<JsonDocument>("RawValues")
                    .IsRequired()
                    .HasColumnType("jsonb")
                    .HasColumnName("raw_values");

                b.Property<Guid>("ReviewId")
                    .HasColumnType("uuid")
                    .HasColumnName("review_id");

                b.Property<Guid?>("SelectedCategoryId")
                    .HasColumnType("uuid")
                    .HasColumnName("selected_category_id");

                b.Property<JsonDocument>("SelectedTagIds")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("jsonb")
                    .HasColumnName("selected_tag_ids")
                    .HasDefaultValueSql("'[]'::jsonb");

                b.Property<bool>("Skip")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("skip");

                b.Property<string>("SourceIdNamespace")
                    .HasColumnType("text")
                    .HasColumnName("source_id_namespace");

                b.Property<string>("SourceIdValue")
                    .HasColumnType("text")
                    .HasColumnName("source_id_value");

                b.Property<int>("SourceLine")
                    .HasColumnType("integer")
                    .HasColumnName("source_line");

                b.Property<int?>("SourcePage")
                    .HasColumnType("integer")
                    .HasColumnName("source_page");

                b.Property<int>("SourceRowNumber")
                    .HasColumnType("integer")
                    .HasColumnName("source_row_number");

                b.Property<Guid?>("SuggestedCategoryId")
                    .HasColumnType("uuid")
                    .HasColumnName("suggested_category_id");

                b.Property<JsonDocument>("SuggestedTagIds")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("jsonb")
                    .HasColumnName("suggested_tag_ids")
                    .HasDefaultValueSql("'[]'::jsonb");

                b.Property<DateOnly?>("TransactionDate")
                    .HasColumnType("date")
                    .HasColumnName("transaction_date");

                b.Property<DateTime>("UpdatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.Property<JsonDocument>("ValidationCodes")
                    .IsRequired()
                    .HasColumnType("jsonb")
                    .HasColumnName("validation_codes");

                b.Property<JsonDocument>("Warnings")
                    .IsRequired()
                    .HasColumnType("jsonb")
                    .HasColumnName("warnings");

                b.Property<string>("WithinFileGroup")
                    .HasColumnType("text")
                    .HasColumnName("within_file_group");

                b.Property<string>("WithinFileState")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("text")
                    .HasDefaultValue("none")
                    .HasColumnName("within_file_state");

                b.HasKey("RowId")
                    .HasName("import_review_rows_pkey");

                b.HasAlternateKey("ReviewId", "RowId")
                    .HasName("import_review_rows_review_id_row_id_key");

                b.HasIndex("ReviewId", "SourceRowNumber")
                    .HasDatabaseName("import_review_rows_review_idx");

                b.ToTable("import_review_rows", null, t =>
                    {
                        t.HasCheckConstraint("import_review_rows_duplicate_state_check", "duplicate_state IN ('none', 'suppressed', 'override', 'within_file', 'suppressed_within_file')");

                        t.HasCheckConstraint("import_review_rows_source_id_pair_check", "(source_id_namespace IS NULL) = (source_id_value IS NULL)");

                        t.HasCheckConstraint("import_review_rows_within_file_state_check", "within_file_state IN ('none', 'pending', 'keep', 'skip')");
                    });
            });

        modelBuilder.Entity("argus.Server.Data.Entities.MerchantAlias", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<Guid?>("CategoryId")
                    .HasColumnType("uuid")
                    .HasColumnName("category_id");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<string>("DisplayAlias")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("display_alias");

                b.Property<Guid>("MerchantTagId")
                    .HasColumnType("uuid")
                    .HasColumnName("merchant_tag_id");

                b.Property<string>("NormalizedAlias")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("normalized_alias");

                b.Property<DateTime>("UpdatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.HasKey("Id")
                    .HasName("merchant_aliases_pkey");

                b.HasIndex("CustomerId")
                    .HasDatabaseName("merchant_aliases_customer_idx");

                b.HasIndex("CustomerId", "NormalizedAlias")
                    .IsUnique()
                    .HasDatabaseName("merchant_aliases_customer_alias_key");

                b.ToTable("merchant_aliases", null, t =>
                    {
                        t.HasCheckConstraint("merchant_aliases_alias_check", "btrim(normalized_alias) <> ''");
                    });
            });

        modelBuilder.Entity("argus.Server.Data.Entities.MerchantTag", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<DateTime?>("ArchivedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("archived_at");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<string>("DisplayName")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("display_name");

                b.Property<bool>("IsArchived")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("is_archived");

                b.Property<string>("NormalizedName")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("normalized_name");

                b.Property<DateTime>("UpdatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.HasKey("Id")
                    .HasName("merchant_tags_pkey");

                b.HasAlternateKey("CustomerId", "Id")
                    .HasName("merchant_tags_customer_id_id_key");

                b.HasAlternateKey("CustomerId", "NormalizedName")
                    .HasName("merchant_tags_customer_id_normalized_name_key");

                b.HasIndex("CustomerId")
                    .HasDatabaseName("merchant_tags_customer_id_idx");

                b.ToTable("merchant_tags", (string)null);
            });

        modelBuilder.Entity("argus.Server.Data.Entities.PasswordResetToken", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<DateTime?>("ConsumedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("consumed_at");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<DateTime>("ExpiresAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("expires_at");

                b.Property<string>("TokenHash")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("token_hash");

                b.HasKey("Id")
                    .HasName("password_reset_tokens_pkey");

                b.HasAlternateKey("TokenHash")
                    .HasName("password_reset_tokens_token_hash_key");

                b.HasIndex("CustomerId")
                    .HasDatabaseName("password_reset_tokens_customer_id_idx");

                b.ToTable("password_reset_tokens", (string)null);
            });

        modelBuilder.Entity("argus.Server.Data.Entities.SchemaMigration", b =>
            {
                b.Property<string>("Name")
                    .HasColumnType("text")
                    .HasColumnName("name");

                b.Property<DateTime>("AppliedAt")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("applied_at")
                    .HasDefaultValueSql("now()");

                b.HasKey("Name")
                    .HasName("schema_migrations_pkey");

                b.ToTable("schema_migrations", (string)null);
            });

        modelBuilder.Entity("argus.Server.Data.Entities.Session", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<DateTime>("ExpiresAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("expires_at");

                b.Property<DateTime>("LastSeenAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("last_seen_at");

                b.Property<DateTime?>("RevokedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("revoked_at");

                b.Property<string>("TokenHash")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("token_hash");

                b.HasKey("Id")
                    .HasName("sessions_pkey");

                b.HasAlternateKey("TokenHash")
                    .HasName("sessions_token_hash_key");

                b.HasIndex("CustomerId")
                    .HasDatabaseName("sessions_customer_id_idx");

                b.ToTable("sessions", (string)null);
            });

        modelBuilder.Entity("argus.Server.Data.Entities.StatementImport", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<int>("DuplicatePolicyVersion")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(1)
                    .HasColumnName("duplicate_policy_version");

                b.Property<DateOnly>("EndDate")
                    .HasColumnType("date")
                    .HasColumnName("end_date");

                b.Property<int>("ExactOverrideCount")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0)
                    .HasColumnName("exact_override_count");

                b.Property<Guid>("FinancialAccountId")
                    .HasColumnType("uuid")
                    .HasColumnName("financial_account_id");

                b.Property<int>("FingerprintOccurrence")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(1)
                    .HasColumnName("fingerprint_occurrence");

                b.Property<DateTime?>("HiddenAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("hidden_at");

                b.Property<int>("ImportedCount")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0)
                    .HasColumnName("imported_count");

                b.Property<bool>("IsHidden")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("is_hidden");

                b.Property<JsonDocument>("OverrideSummary")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("jsonb")
                    .HasColumnName("override_summary")
                    .HasDefaultValueSql("'{}'::jsonb");

                b.Property<int>("PossibleDuplicateCount")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0)
                    .HasColumnName("possible_duplicate_count");

                b.Property<int>("SkippedCount")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0)
                    .HasColumnName("skipped_count");

                b.Property<string>("SourceType")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("source_type");

                b.Property<DateOnly>("StartDate")
                    .HasColumnType("date")
                    .HasColumnName("start_date");

                b.Property<string>("StatementFingerprint")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("statement_fingerprint");

                b.Property<string>("StatementLabel")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("statement_label");

                b.Property<int>("StatementOverrideCount")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0)
                    .HasColumnName("statement_override_count");

                b.Property<int>("SuppressedCount")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0)
                    .HasColumnName("suppressed_count");

                b.Property<int>("TotalRows")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0)
                    .HasColumnName("total_rows");

                b.Property<DateTime>("UpdatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.Property<int>("Version")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(1)
                    .HasColumnName("version");

                b.HasKey("Id")
                    .HasName("statement_imports_pkey");

                b.HasAlternateKey("CustomerId", "Id")
                    .HasName("statement_imports_customer_id_id_key");

                b.HasAlternateKey("CustomerId", "FinancialAccountId", "Id")
                    .HasName("statement_imports_customer_account_id_key");

                b.HasIndex("CustomerId", "CreatedAt", "Id")
                    .IsDescending(false, true, true)
                    .HasDatabaseName("statement_imports_history_idx");

                b.HasIndex("CustomerId", "FinancialAccountId", "StatementFingerprint", "FingerprintOccurrence")
                    .IsUnique()
                    .HasDatabaseName("statement_imports_fingerprint_slot_uidx");

                b.ToTable("statement_imports", null, t =>
                    {
                        t.HasCheckConstraint("statement_imports_counts_check", "total_rows >= 0 AND imported_count >= 0 AND suppressed_count >= 0 AND skipped_count >= 0 AND possible_duplicate_count >= 0 AND exact_override_count >= 0 AND statement_override_count >= 0 AND total_rows = imported_count + suppressed_count + skipped_count");

                        t.HasCheckConstraint("statement_imports_dates_check", "start_date <= end_date");

                        t.HasCheckConstraint("statement_imports_fingerprint_occurrence_check", "fingerprint_occurrence > 0");

                        t.HasCheckConstraint("statement_imports_version_check", "version > 0");
                    });
            });

        modelBuilder.Entity("argus.Server.Data.Entities.Transaction", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<decimal>("Amount")
                    .HasPrecision(18, 2)
                    .HasColumnType("numeric(18,2)")
                    .HasColumnName("amount");

                b.Property<bool>("CategoryConfirmed")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(true)
                    .HasColumnName("category_confirmed");

                b.Property<Guid>("CategoryId")
                    .HasColumnType("uuid")
                    .HasColumnName("category_id");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<string>("Description")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("description");

                b.Property<Guid>("FinancialAccountId")
                    .HasColumnType("uuid")
                    .HasColumnName("financial_account_id");

                b.Property<string>("IdentityKey")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("identity_key");

                b.Property<int>("IdentityVersion")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(1)
                    .HasColumnName("identity_version");

                b.Property<bool>("IsHiddenByUser")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("is_hidden_by_user");

                b.Property<string>("MerchantKey")
                    .ValueGeneratedOnAddOrUpdate()
                    .HasColumnType("text")
                    .HasColumnName("merchant_key")
                    .HasComputedColumnSql("argus_merchant_key(normalized_description)", true);

                b.Property<string>("NormalizedDescription")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("normalized_description");

                b.Property<int>("OccurrenceIndex")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(1)
                    .HasColumnName("occurrence_index");

                b.Property<string>("SourceIdNamespace")
                    .HasColumnType("text")
                    .HasColumnName("source_id_namespace");

                b.Property<string>("SourceIdValue")
                    .HasColumnType("text")
                    .HasColumnName("source_id_value");

                b.Property<int?>("SourcePage")
                    .HasColumnType("integer")
                    .HasColumnName("source_page");

                b.Property<int>("SourceRowNumber")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(1)
                    .HasColumnName("source_row_number");

                b.Property<string>("SourceType")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("source_type");

                b.Property<Guid>("StatementImportId")
                    .HasColumnType("uuid")
                    .HasColumnName("statement_import_id");

                b.Property<DateOnly>("TransactionDate")
                    .HasColumnType("date")
                    .HasColumnName("transaction_date");

                b.Property<DateTime>("UpdatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.Property<int>("Version")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(1)
                    .HasColumnName("version");

                b.HasKey("Id")
                    .HasName("transactions_pkey");

                b.HasAlternateKey("CustomerId", "Id")
                    .HasName("transactions_customer_id_key");

                b.HasAlternateKey("CustomerId", "StatementImportId", "SourceRowNumber")
                    .HasName("transactions_statement_row_key");

                b.HasAlternateKey("CustomerId", "FinancialAccountId", "IdentityKey", "OccurrenceIndex")
                    .HasName("transactions_identity_occurrence_key");

                b.HasIndex("CustomerId")
                    .HasDatabaseName("transactions_customer_unconfirmed_idx")
                    .HasFilter("category_confirmed = false");

                b.HasIndex("CustomerId", "CategoryId")
                    .HasDatabaseName("transactions_customer_category_idx");

                b.HasIndex("CustomerId", "FinancialAccountId")
                    .HasDatabaseName("transactions_customer_account_idx");

                b.HasIndex("CustomerId", "Amount", "Id")
                    .HasDatabaseName("transactions_customer_amount_idx");

                b.HasIndex("CustomerId", "MerchantKey", "TransactionDate")
                    .HasDatabaseName("transactions_customer_merchant_key_idx");

                b.HasIndex("CustomerId", "StatementImportId", "SourceRowNumber")
                    .HasDatabaseName("transactions_statement_children_idx");

                b.HasIndex("CustomerId", "TransactionDate", "Id")
                    .HasDatabaseName("transactions_customer_date_idx");

                b.HasIndex("CustomerId", "FinancialAccountId", "Amount", "TransactionDate")
                    .HasDatabaseName("transactions_near_match_idx");

                b.ToTable("transactions", null, t =>
                    {
                        t.HasCheckConstraint("transactions_identity_version_check", "identity_version > 0");

                        t.HasCheckConstraint("transactions_occurrence_index_check", "occurrence_index > 0");

                        t.HasCheckConstraint("transactions_source_id_pair_check", "(source_id_namespace IS NULL) = (source_id_value IS NULL)");
                    });
            });

        modelBuilder.Entity("argus.Server.Data.Entities.TransactionMerchantTag", b =>
            {
                b.Property<Guid>("TransactionId")
                    .HasColumnType("uuid")
                    .HasColumnName("transaction_id");

                b.Property<Guid>("MerchantTagId")
                    .HasColumnType("uuid")
                    .HasColumnName("merchant_tag_id");

                b.Property<DateTime>("AssignedAt")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("assigned_at")
                    .HasDefaultValueSql("now()");

                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.HasKey("TransactionId", "MerchantTagId")
                    .HasName("transaction_merchant_tags_pkey");

                b.HasIndex("MerchantTagId")
                    .HasDatabaseName("transaction_merchant_tags_tag_idx");

                b.HasIndex("CustomerId", "MerchantTagId")
                    .HasDatabaseName("transaction_merchant_tags_customer_tag_idx");

                b.ToTable("transaction_merchant_tags", (string)null);
            });

        modelBuilder.Entity("argus.Server.Data.Entities.TransactionOperation", b =>
            {
                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<Guid>("OperationId")
                    .HasColumnType("uuid")
                    .HasColumnName("operation_id");

                b.Property<JsonDocument>("Action")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("jsonb")
                    .HasColumnName("action")
                    .HasDefaultValueSql("'{}'::jsonb");

                b.Property<DateTime?>("AppliedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("applied_at");

                b.Property<DateTime>("CapturedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("captured_at");

                b.Property<int>("ChangedCount")
                    .HasColumnType("integer")
                    .HasColumnName("changed_count");

                b.Property<string>("ConfirmationHash")
                    .HasColumnType("text")
                    .HasColumnName("confirmation_hash");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at");

                b.Property<DateTime>("ExpiresAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("expires_at");

                b.Property<string>("FilterDigest")
                    .HasColumnType("text")
                    .HasColumnName("filter_digest");

                b.Property<string>("RequestDigest")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("request_digest");

                b.Property<int>("SelectedCount")
                    .HasColumnType("integer")
                    .HasColumnName("selected_count");

                b.Property<string>("SelectionMode")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("selection_mode");

                b.Property<string>("Status")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("status");

                b.Property<JsonDocument>("TargetStamps")
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasColumnType("jsonb")
                    .HasColumnName("target_stamps")
                    .HasDefaultValueSql("'{}'::jsonb");

                b.Property<DateTime?>("TerminalAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("terminal_at");

                b.Property<int>("UnchangedCount")
                    .HasColumnType("integer")
                    .HasColumnName("unchanged_count");

                b.Property<DateTime>("UpdatedAt")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at");

                b.HasKey("CustomerId", "OperationId")
                    .HasName("transaction_operations_pkey");

                b.HasIndex("CustomerId", "TerminalAt")
                    .IsDescending(false, true)
                    .HasDatabaseName("transaction_operations_terminal_idx")
                    .HasFilter("status <> 'Prepared'");

                b.HasIndex("CustomerId", "Status", "ExpiresAt")
                    .HasDatabaseName("transaction_operations_customer_status_idx");

                b.ToTable("transaction_operations", null, t =>
                    {
                        t.HasCheckConstraint("transaction_operations_applied_check", "(status = 'Applied') = (applied_at IS NOT NULL)");

                        t.HasCheckConstraint("transaction_operations_changed_count_check", "changed_count >= 0");

                        t.HasCheckConstraint("transaction_operations_counts_check", "selected_count = changed_count + unchanged_count");

                        t.HasCheckConstraint("transaction_operations_expiry_check", "expires_at > captured_at");

                        t.HasCheckConstraint("transaction_operations_prepared_payload_check", "status <> 'Prepared' OR (confirmation_hash IS NOT NULL AND action <> '{}'::jsonb)");

                        t.HasCheckConstraint("transaction_operations_selected_count_check", "selected_count >= 0");

                        t.HasCheckConstraint("transaction_operations_selection_mode_check", "selection_mode IN ('explicit', 'all_matching')");

                        t.HasCheckConstraint("transaction_operations_status_check", "status IN ('Prepared', 'Applied', 'Abandoned', 'Expired')");

                        t.HasCheckConstraint("transaction_operations_unchanged_count_check", "unchanged_count >= 0");
                    });
            });

        modelBuilder.Entity("argus.Server.Data.Entities.TransactionOperationItem", b =>
            {
                b.Property<Guid>("CustomerId")
                    .HasColumnType("uuid")
                    .HasColumnName("customer_id");

                b.Property<Guid>("OperationId")
                    .HasColumnType("uuid")
                    .HasColumnName("operation_id");

                b.Property<Guid>("TransactionId")
                    .HasColumnType("uuid")
                    .HasColumnName("transaction_id");

                b.Property<Guid>("FinancialAccountId")
                    .HasColumnType("uuid")
                    .HasColumnName("financial_account_id");

                b.Property<int>("Ordinal")
                    .HasColumnType("integer")
                    .HasColumnName("ordinal");

                b.Property<Guid>("StatementImportId")
                    .HasColumnType("uuid")
                    .HasColumnName("statement_import_id");

                b.Property<int>("TransactionVersion")
                    .HasColumnType("integer")
                    .HasColumnName("transaction_version");

                b.HasKey("CustomerId", "OperationId", "TransactionId")
                    .HasName("transaction_operation_items_pkey");

                b.HasIndex("CustomerId", "OperationId", "Ordinal")
                    .HasDatabaseName("transaction_operation_items_header_idx");

                b.ToTable("transaction_operation_items", null, t =>
                    {
                        t.HasCheckConstraint("transaction_operation_items_ordinal_check", "ordinal >= 0");

                        t.HasCheckConstraint("transaction_operation_items_transaction_version_check", "transaction_version > 0");
                    });
            });

        modelBuilder.Entity("argus.Server.Data.Entities.Category", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("categories_customer_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.EmailVerificationToken", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.NoAction)
                    .IsRequired()
                    .HasConstraintName("email_verification_tokens_customer_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.FinancialAccount", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("financial_accounts_customer_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.ImportCommitReceipt", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("import_commit_receipts_customer_id_fkey");

                b.HasOne("argus.Server.Data.Entities.FinancialAccount", null)
                    .WithMany()
                    .HasForeignKey("CustomerId", "FinancialAccountId")
                    .HasPrincipalKey("CustomerId", "Id")
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired()
                    .HasConstraintName("import_commit_receipts_customer_id_financial_account_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.ImportQuotaLock", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("import_quota_locks_customer_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.ImportReview", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("import_reviews_customer_id_fkey");

                b.HasOne("argus.Server.Data.Entities.FinancialAccount", null)
                    .WithMany()
                    .HasForeignKey("CustomerId", "FinancialAccountId")
                    .HasPrincipalKey("CustomerId", "Id")
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired()
                    .HasConstraintName("import_reviews_customer_id_financial_account_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.ImportReviewRow", b =>
            {
                b.HasOne("argus.Server.Data.Entities.ImportReview", null)
                    .WithMany()
                    .HasForeignKey("CustomerId", "ReviewId")
                    .HasPrincipalKey("CustomerId", "Id")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("import_review_rows_customer_id_review_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.MerchantAlias", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Category", null)
                    .WithMany()
                    .HasForeignKey("CategoryId")
                    .OnDelete(DeleteBehavior.SetNull)
                    .HasConstraintName("merchant_aliases_category_id_fkey");

                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("merchant_aliases_customer_id_fkey");

                b.HasOne("argus.Server.Data.Entities.MerchantTag", null)
                    .WithMany()
                    .HasForeignKey("MerchantTagId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("merchant_aliases_merchant_tag_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.MerchantTag", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("merchant_tags_customer_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.PasswordResetToken", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.NoAction)
                    .IsRequired()
                    .HasConstraintName("password_reset_tokens_customer_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.Session", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.NoAction)
                    .IsRequired()
                    .HasConstraintName("sessions_customer_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.StatementImport", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("statement_imports_customer_id_fkey");

                b.HasOne("argus.Server.Data.Entities.FinancialAccount", null)
                    .WithMany()
                    .HasForeignKey("CustomerId", "FinancialAccountId")
                    .HasPrincipalKey("CustomerId", "Id")
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired()
                    .HasConstraintName("statement_imports_customer_id_financial_account_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.Transaction", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("transactions_customer_id_fkey");

                b.HasOne("argus.Server.Data.Entities.Category", null)
                    .WithMany()
                    .HasForeignKey("CustomerId", "CategoryId")
                    .HasPrincipalKey("CustomerId", "Id")
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired()
                    .HasConstraintName("transactions_customer_id_category_id_fkey");

                b.HasOne("argus.Server.Data.Entities.FinancialAccount", null)
                    .WithMany()
                    .HasForeignKey("CustomerId", "FinancialAccountId")
                    .HasPrincipalKey("CustomerId", "Id")
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired()
                    .HasConstraintName("transactions_customer_id_financial_account_id_fkey");

                b.HasOne("argus.Server.Data.Entities.StatementImport", null)
                    .WithMany()
                    .HasForeignKey("CustomerId", "StatementImportId")
                    .HasPrincipalKey("CustomerId", "Id")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("transactions_customer_id_statement_import_id_fkey");

                b.HasOne("argus.Server.Data.Entities.StatementImport", null)
                    .WithMany()
                    .HasForeignKey("CustomerId", "FinancialAccountId", "StatementImportId")
                    .HasPrincipalKey("CustomerId", "FinancialAccountId", "Id")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("transactions_statement_account_fk");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.TransactionMerchantTag", b =>
            {
                b.HasOne("argus.Server.Data.Entities.MerchantTag", null)
                    .WithMany()
                    .HasForeignKey("MerchantTagId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("transaction_merchant_tags_merchant_tag_id_fkey");

                b.HasOne("argus.Server.Data.Entities.Transaction", null)
                    .WithMany()
                    .HasForeignKey("TransactionId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("transaction_merchant_tags_transaction_id_fkey");

                b.HasOne("argus.Server.Data.Entities.MerchantTag", null)
                    .WithMany()
                    .HasForeignKey("CustomerId", "MerchantTagId")
                    .HasPrincipalKey("CustomerId", "Id")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("transaction_merchant_tags_tag_fk");

                b.HasOne("argus.Server.Data.Entities.Transaction", null)
                    .WithMany()
                    .HasForeignKey("CustomerId", "TransactionId")
                    .HasPrincipalKey("CustomerId", "Id")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("transaction_merchant_tags_transaction_fk");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.TransactionOperation", b =>
            {
                b.HasOne("argus.Server.Data.Entities.Customer", null)
                    .WithMany()
                    .HasForeignKey("CustomerId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("transaction_operations_customer_id_fkey");
            });

        modelBuilder.Entity("argus.Server.Data.Entities.TransactionOperationItem", b =>
            {
                b.HasOne("argus.Server.Data.Entities.TransactionOperation", null)
                    .WithMany()
                    .HasForeignKey("CustomerId", "OperationId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired()
                    .HasConstraintName("transaction_operation_items_customer_id_operation_id_fkey");
            });
#pragma warning restore 612, 618
    }
}
