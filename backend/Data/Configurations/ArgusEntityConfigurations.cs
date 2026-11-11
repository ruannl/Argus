using argus.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace argus.Server.Data.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(customer => customer.Id).HasName("customers_pkey");
        builder.Property(customer => customer.Id).ValueGeneratedNever();
        builder.Property(customer => customer.CanonicalEmail).HasColumnType("text").IsRequired();
        builder.HasAlternateKey(customer => customer.CanonicalEmail).HasName("customers_canonical_email_key");
        builder.Property(customer => customer.PasswordHash).HasColumnType("text").IsRequired();
        builder.Property(customer => customer.EmailVerifiedAt).HasColumnType("timestamp with time zone");
        builder.Property(customer => customer.SubscriberActivatedAt).HasColumnType("timestamp with time zone");
        builder.Property(customer => customer.NextBillingAt).HasColumnType("timestamp with time zone");
        builder.Property(customer => customer.IsLifetime).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(customer => customer.IsAdmin).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(customer => customer.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(customer => customer.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(customer => customer.FailedLoginCount).HasColumnType("integer").HasDefaultValue(0).IsRequired();
        builder.Property(customer => customer.LockedUntil).HasColumnType("timestamp with time zone");
        builder.Property(customer => customer.CategoriesSeededAt).HasColumnType("timestamp with time zone");
    }
}

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(session => session.Id).HasName("sessions_pkey");
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.Property(session => session.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(session => session.TokenHash).HasColumnType("text").IsRequired();
        builder.HasAlternateKey(session => session.TokenHash).HasName("sessions_token_hash_key");
        builder.Property(session => session.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(session => session.RevokedAt).HasColumnType("timestamp with time zone");
        builder.Property(session => session.LastSeenAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(session => session.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(session => session.CustomerId).HasDatabaseName("sessions_customer_id_idx");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(session => session.CustomerId)
            .HasConstraintName("sessions_customer_id_fkey")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class EmailVerificationTokenConfiguration : IEntityTypeConfiguration<EmailVerificationToken>
{
    public void Configure(EntityTypeBuilder<EmailVerificationToken> builder)
    {
        builder.ToTable("email_verification_tokens");
        builder.HasKey(token => token.Id).HasName("email_verification_tokens_pkey");
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(token => token.TokenHash).HasColumnType("text").IsRequired();
        builder.HasAlternateKey(token => token.TokenHash).HasName("email_verification_tokens_token_hash_key");
        builder.Property(token => token.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(token => token.ConsumedAt).HasColumnType("timestamp with time zone");
        builder.Property(token => token.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(token => token.CustomerId).HasDatabaseName("email_verification_tokens_customer_id_idx");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(token => token.CustomerId)
            .HasConstraintName("email_verification_tokens_customer_id_fkey")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");
        builder.HasKey(token => token.Id).HasName("password_reset_tokens_pkey");
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(token => token.TokenHash).HasColumnType("text").IsRequired();
        builder.HasAlternateKey(token => token.TokenHash).HasName("password_reset_tokens_token_hash_key");
        builder.Property(token => token.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(token => token.ConsumedAt).HasColumnType("timestamp with time zone");
        builder.Property(token => token.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(token => token.CustomerId).HasDatabaseName("password_reset_tokens_customer_id_idx");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(token => token.CustomerId)
            .HasConstraintName("password_reset_tokens_customer_id_fkey")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class SchemaMigrationConfiguration : IEntityTypeConfiguration<SchemaMigration>
{
    public void Configure(EntityTypeBuilder<SchemaMigration> builder)
    {
        builder.ToTable("schema_migrations");
        builder.HasKey(migration => migration.Name).HasName("schema_migrations_pkey");
        builder.Property(migration => migration.Name).HasColumnType("text").IsRequired();
        builder.Property(migration => migration.AppliedAt).HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();
    }
}

public sealed class DataMigrationConfiguration : IEntityTypeConfiguration<DataMigration>
{
    public void Configure(EntityTypeBuilder<DataMigration> builder)
    {
        builder.ToTable("data_migrations");
        builder.HasKey(migration => migration.Name).HasName("data_migrations_pkey");
        builder.Property(migration => migration.Name).HasColumnType("text").IsRequired();
        builder.Property(migration => migration.AppliedAt).HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();
    }
}

public sealed class FinancialAccountConfiguration : IEntityTypeConfiguration<FinancialAccount>
{
    public void Configure(EntityTypeBuilder<FinancialAccount> builder)
    {
        builder.ToTable("financial_accounts", table => table.HasCheckConstraint("financial_accounts_currency_check", "currency = 'ZAR'"));
        builder.HasKey(account => account.Id).HasName("financial_accounts_pkey");
        builder.Property(account => account.Id).ValueGeneratedNever();
        builder.Property(account => account.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(account => account.DisplayName).HasColumnType("text").IsRequired();
        builder.Property(account => account.Institution).HasColumnType("text");
        builder.Property(account => account.Currency).HasColumnType("text").HasDefaultValue("ZAR").IsRequired();
        builder.Property(account => account.IsArchived).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(account => account.ArchivedAt).HasColumnType("timestamp with time zone");
        builder.Property(account => account.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(account => account.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(account => account.ImportVersion).HasColumnType("bigint").HasDefaultValue(0L).IsRequired();
        builder.HasAlternateKey(account => new { account.CustomerId, account.Id }).HasName("financial_accounts_customer_id_id_key");
        builder.HasIndex(account => account.CustomerId).HasDatabaseName("financial_accounts_customer_id_idx");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(account => account.CustomerId)
            .HasConstraintName("financial_accounts_customer_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories", table => table.HasCheckConstraint(
            "categories_kind_check",
            "(is_system = TRUE AND kind = 'Uncategorised' AND is_archived = FALSE) OR (is_system = FALSE AND kind IN ('Income', 'Expense', 'Transfer', 'Excluded'))"));
        builder.HasKey(category => category.Id).HasName("categories_pkey");
        builder.Property(category => category.Id).ValueGeneratedNever();
        builder.Property(category => category.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(category => category.DisplayName).HasColumnType("text").IsRequired();
        builder.Property(category => category.NormalizedName).HasColumnType("text").IsRequired();
        builder.Property(category => category.Kind).HasColumnType("text").IsRequired();
        builder.Property(category => category.IsSystem).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(category => category.IsDefault).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(category => category.IsArchived).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(category => category.ArchivedAt).HasColumnType("timestamp with time zone");
        builder.Property(category => category.SortOrder).HasColumnType("integer").HasDefaultValue(100).IsRequired();
        builder.Property(category => category.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(category => category.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasAlternateKey(category => new { category.CustomerId, category.Id }).HasName("categories_customer_id_id_key");
        builder.HasAlternateKey(category => new { category.CustomerId, category.NormalizedName }).HasName("categories_customer_id_normalized_name_key");
        builder.HasIndex(category => category.CustomerId, "categories_customer_id_idx")
            .HasDatabaseName("categories_customer_id_idx");
        builder.HasIndex(category => category.CustomerId, "categories_customer_system_uidx")
            .HasDatabaseName("categories_customer_system_uidx")
            .IsUnique()
            .HasFilter("is_system = true");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(category => category.CustomerId)
            .HasConstraintName("categories_customer_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MerchantTagConfiguration : IEntityTypeConfiguration<MerchantTag>
{
    public void Configure(EntityTypeBuilder<MerchantTag> builder)
    {
        builder.ToTable("merchant_tags");
        builder.HasKey(tag => tag.Id).HasName("merchant_tags_pkey");
        builder.Property(tag => tag.Id).ValueGeneratedNever();
        builder.Property(tag => tag.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(tag => tag.DisplayName).HasColumnType("text").IsRequired();
        builder.Property(tag => tag.NormalizedName).HasColumnType("text").IsRequired();
        builder.Property(tag => tag.IsArchived).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(tag => tag.ArchivedAt).HasColumnType("timestamp with time zone");
        builder.Property(tag => tag.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(tag => tag.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasAlternateKey(tag => new { tag.CustomerId, tag.Id }).HasName("merchant_tags_customer_id_id_key");
        builder.HasAlternateKey(tag => new { tag.CustomerId, tag.NormalizedName }).HasName("merchant_tags_customer_id_normalized_name_key");
        builder.HasIndex(tag => tag.CustomerId).HasDatabaseName("merchant_tags_customer_id_idx");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(tag => tag.CustomerId)
            .HasConstraintName("merchant_tags_customer_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MerchantAliasConfiguration : IEntityTypeConfiguration<MerchantAlias>
{
    public void Configure(EntityTypeBuilder<MerchantAlias> builder)
    {
        builder.ToTable("merchant_aliases", table => table.HasCheckConstraint(
            "merchant_aliases_alias_check",
            "btrim(normalized_alias) <> ''"));
        builder.HasKey(alias => alias.Id).HasName("merchant_aliases_pkey");
        builder.Property(alias => alias.Id).ValueGeneratedNever();
        builder.Property(alias => alias.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(alias => alias.DisplayAlias).HasColumnType("text").IsRequired();
        builder.Property(alias => alias.NormalizedAlias).HasColumnType("text").IsRequired();
        builder.Property(alias => alias.MerchantTagId).HasColumnType("uuid").IsRequired();
        builder.Property(alias => alias.CategoryId).HasColumnType("uuid");
        builder.Property(alias => alias.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(alias => alias.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(alias => new { alias.CustomerId, alias.NormalizedAlias })
            .HasDatabaseName("merchant_aliases_customer_alias_key")
            .IsUnique();
        builder.HasIndex(alias => alias.CustomerId).HasDatabaseName("merchant_aliases_customer_idx");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(alias => alias.CustomerId)
            .HasConstraintName("merchant_aliases_customer_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MerchantTag>()
            .WithMany()
            .HasForeignKey(alias => alias.MerchantTagId)
            .HasConstraintName("merchant_aliases_merchant_tag_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(alias => alias.CategoryId)
            .HasConstraintName("merchant_aliases_category_id_fkey")
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class StatementImportConfiguration : IEntityTypeConfiguration<StatementImport>
{
    public void Configure(EntityTypeBuilder<StatementImport> builder)
    {
        builder.ToTable("statement_imports", table =>
        {
            table.HasCheckConstraint("statement_imports_version_check", "version > 0");
            table.HasCheckConstraint("statement_imports_fingerprint_occurrence_check", "fingerprint_occurrence > 0");
            table.HasCheckConstraint("statement_imports_dates_check", "start_date <= end_date");
            table.HasCheckConstraint(
                "statement_imports_counts_check",
                "total_rows >= 0 AND imported_count >= 0 AND suppressed_count >= 0 AND skipped_count >= 0 AND possible_duplicate_count >= 0 AND exact_override_count >= 0 AND statement_override_count >= 0 AND total_rows = imported_count + suppressed_count + skipped_count");
        });
        builder.HasKey(import => import.Id).HasName("statement_imports_pkey");
        builder.Property(import => import.Id).ValueGeneratedNever();
        builder.Property(import => import.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(import => import.FinancialAccountId).HasColumnType("uuid").IsRequired();
        builder.Property(import => import.SourceType).HasColumnType("text").IsRequired();
        builder.Property(import => import.StatementLabel).HasColumnType("text").IsRequired();
        builder.Property(import => import.StatementFingerprint).HasColumnType("text").IsRequired();
        builder.Property(import => import.StartDate).HasColumnType("date").IsRequired();
        builder.Property(import => import.EndDate).HasColumnType("date").IsRequired();
        builder.Property(import => import.TotalRows).HasColumnType("integer").HasDefaultValue(0).IsRequired();
        builder.Property(import => import.ImportedCount).HasColumnType("integer").HasDefaultValue(0).IsRequired();
        builder.Property(import => import.SuppressedCount).HasColumnType("integer").HasDefaultValue(0).IsRequired();
        builder.Property(import => import.SkippedCount).HasColumnType("integer").HasDefaultValue(0).IsRequired();
        builder.Property(import => import.IsHidden).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(import => import.HiddenAt).HasColumnType("timestamp with time zone");
        builder.Property(import => import.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(import => import.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(import => import.Version).HasColumnType("integer").HasDefaultValue(1).IsRequired();
        builder.Property(import => import.FingerprintOccurrence).HasColumnType("integer").HasDefaultValue(1).IsRequired();
        builder.Property(import => import.DuplicatePolicyVersion).HasColumnType("integer").HasDefaultValue(1).IsRequired();
        builder.Property(import => import.PossibleDuplicateCount).HasColumnType("integer").HasDefaultValue(0).IsRequired();
        builder.Property(import => import.ExactOverrideCount).HasColumnType("integer").HasDefaultValue(0).IsRequired();
        builder.Property(import => import.StatementOverrideCount).HasColumnType("integer").HasDefaultValue(0).IsRequired();
        builder.Property(import => import.OverrideSummary).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.HasAlternateKey(import => new { import.CustomerId, import.Id }).HasName("statement_imports_customer_id_id_key");
        builder.HasAlternateKey(import => new { import.CustomerId, import.FinancialAccountId, import.Id }).HasName("statement_imports_customer_account_id_key");
        builder.HasIndex(import => new { import.CustomerId, import.FinancialAccountId, import.StatementFingerprint, import.FingerprintOccurrence })
            .HasDatabaseName("statement_imports_fingerprint_slot_uidx")
            .IsUnique();
        builder.HasIndex(import => new { import.CustomerId, import.CreatedAt, import.Id })
            .HasDatabaseName("statement_imports_history_idx")
            .IsDescending(false, true, true);
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(import => import.CustomerId)
            .HasConstraintName("statement_imports_customer_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FinancialAccount>()
            .WithMany()
            .HasForeignKey(import => new { import.CustomerId, import.FinancialAccountId })
            .HasPrincipalKey(account => new { account.CustomerId, account.Id })
            .HasConstraintName("statement_imports_customer_id_financial_account_id_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions", table =>
        {
            table.HasCheckConstraint("transactions_source_id_pair_check", "(source_id_namespace IS NULL) = (source_id_value IS NULL)");
            table.HasCheckConstraint("transactions_identity_version_check", "identity_version > 0");
            table.HasCheckConstraint("transactions_occurrence_index_check", "occurrence_index > 0");
        });
        builder.HasKey(transaction => transaction.Id).HasName("transactions_pkey");
        builder.Property(transaction => transaction.Id).ValueGeneratedNever();
        builder.Property(transaction => transaction.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(transaction => transaction.FinancialAccountId).HasColumnType("uuid").IsRequired();
        builder.Property(transaction => transaction.StatementImportId).HasColumnType("uuid").IsRequired();
        builder.Property(transaction => transaction.TransactionDate).HasColumnType("date").IsRequired();
        builder.Property(transaction => transaction.Description).HasColumnType("text").IsRequired();
        builder.Property(transaction => transaction.NormalizedDescription).HasColumnType("text").IsRequired();
        builder.Property(transaction => transaction.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(transaction => transaction.CategoryId).HasColumnType("uuid").IsRequired();
        builder.Property(transaction => transaction.SourceType).HasColumnType("text").IsRequired();
        builder.Property(transaction => transaction.SourcePage).HasColumnType("integer");
        builder.Property(transaction => transaction.SourceRowNumber).HasColumnType("integer").HasDefaultValue(1).IsRequired();
        builder.Property(transaction => transaction.IdentityKey).HasColumnType("text").IsRequired();
        builder.Property(transaction => transaction.OccurrenceIndex).HasColumnType("integer").HasDefaultValue(1).IsRequired();
        builder.Property(transaction => transaction.IsHiddenByUser).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(transaction => transaction.Version).HasColumnType("integer").HasDefaultValue(1).IsRequired();
        builder.Property(transaction => transaction.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(transaction => transaction.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(transaction => transaction.IdentityVersion).HasColumnType("integer").HasDefaultValue(1).IsRequired();
        builder.Property(transaction => transaction.SourceIdNamespace).HasColumnType("text");
        builder.Property(transaction => transaction.SourceIdValue).HasColumnType("text");
        builder.Property(transaction => transaction.MerchantKey)
            .HasColumnType("text")
            .HasComputedColumnSql("argus_merchant_key(normalized_description)", stored: true);
        builder.Property(transaction => transaction.CategoryConfirmed).HasColumnType("boolean").HasDefaultValue(true).IsRequired();
        builder.HasAlternateKey(transaction => new { transaction.CustomerId, transaction.Id }).HasName("transactions_customer_id_key");
        builder.HasAlternateKey(transaction => new { transaction.CustomerId, transaction.FinancialAccountId, transaction.IdentityKey, transaction.OccurrenceIndex })
            .HasName("transactions_identity_occurrence_key");
        builder.HasAlternateKey(transaction => new { transaction.CustomerId, transaction.StatementImportId, transaction.SourceRowNumber })
            .HasName("transactions_statement_row_key");
        builder.HasIndex(transaction => new { transaction.CustomerId, transaction.CategoryId }).HasDatabaseName("transactions_customer_category_idx");
        builder.HasIndex(transaction => new { transaction.CustomerId, transaction.FinancialAccountId }).HasDatabaseName("transactions_customer_account_idx");
        builder.HasIndex(transaction => new { transaction.CustomerId, transaction.StatementImportId, transaction.SourceRowNumber })
            .HasDatabaseName("transactions_statement_children_idx");
        builder.HasIndex(transaction => new { transaction.CustomerId, transaction.FinancialAccountId, transaction.Amount, transaction.TransactionDate })
            .HasDatabaseName("transactions_near_match_idx");
        builder.HasIndex(transaction => new { transaction.CustomerId, transaction.TransactionDate, transaction.Id })
            .HasDatabaseName("transactions_customer_date_idx");
        builder.HasIndex(transaction => new { transaction.CustomerId, transaction.Amount, transaction.Id })
            .HasDatabaseName("transactions_customer_amount_idx");
        builder.HasIndex(transaction => new { transaction.CustomerId, transaction.MerchantKey, transaction.TransactionDate })
            .HasDatabaseName("transactions_customer_merchant_key_idx");
        builder.HasIndex(transaction => transaction.CustomerId)
            .HasDatabaseName("transactions_customer_unconfirmed_idx")
            .HasFilter("category_confirmed = false");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(transaction => transaction.CustomerId)
            .HasConstraintName("transactions_customer_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FinancialAccount>()
            .WithMany()
            .HasForeignKey(transaction => new { transaction.CustomerId, transaction.FinancialAccountId })
            .HasPrincipalKey(account => new { account.CustomerId, account.Id })
            .HasConstraintName("transactions_customer_id_financial_account_id_fkey")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StatementImport>()
            .WithMany()
            .HasForeignKey(transaction => new { transaction.CustomerId, transaction.StatementImportId })
            .HasPrincipalKey(import => new { import.CustomerId, import.Id })
            .HasConstraintName("transactions_customer_id_statement_import_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StatementImport>()
            .WithMany()
            .HasForeignKey(transaction => new { transaction.CustomerId, transaction.FinancialAccountId, transaction.StatementImportId })
            .HasPrincipalKey(import => new { import.CustomerId, import.FinancialAccountId, import.Id })
            .HasConstraintName("transactions_statement_account_fk")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(transaction => new { transaction.CustomerId, transaction.CategoryId })
            .HasPrincipalKey(category => new { category.CustomerId, category.Id })
            .HasConstraintName("transactions_customer_id_category_id_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TransactionMerchantTagConfiguration : IEntityTypeConfiguration<TransactionMerchantTag>
{
    public void Configure(EntityTypeBuilder<TransactionMerchantTag> builder)
    {
        builder.ToTable("transaction_merchant_tags");
        builder.HasKey(link => new { link.TransactionId, link.MerchantTagId }).HasName("transaction_merchant_tags_pkey");
        builder.Property(link => link.TransactionId).HasColumnType("uuid").IsRequired();
        builder.Property(link => link.MerchantTagId).HasColumnType("uuid").IsRequired();
        builder.Property(link => link.AssignedAt).HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();
        builder.Property(link => link.CustomerId).HasColumnType("uuid").IsRequired();
        builder.HasIndex(link => link.MerchantTagId).HasDatabaseName("transaction_merchant_tags_tag_idx");
        builder.HasIndex(link => new { link.CustomerId, link.MerchantTagId }).HasDatabaseName("transaction_merchant_tags_customer_tag_idx");
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(link => link.TransactionId)
            .HasConstraintName("transaction_merchant_tags_transaction_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(link => new { link.CustomerId, link.TransactionId })
            .HasPrincipalKey(transaction => new { transaction.CustomerId, transaction.Id })
            .HasConstraintName("transaction_merchant_tags_transaction_fk")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MerchantTag>()
            .WithMany()
            .HasForeignKey(link => link.MerchantTagId)
            .HasConstraintName("transaction_merchant_tags_merchant_tag_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MerchantTag>()
            .WithMany()
            .HasForeignKey(link => new { link.CustomerId, link.MerchantTagId })
            .HasPrincipalKey(tag => new { tag.CustomerId, tag.Id })
            .HasConstraintName("transaction_merchant_tags_tag_fk")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ImportQuotaLockConfiguration : IEntityTypeConfiguration<ImportQuotaLock>
{
    public void Configure(EntityTypeBuilder<ImportQuotaLock> builder)
    {
        builder.ToTable("import_quota_locks");
        builder.HasKey(lockRow => lockRow.CustomerId).HasName("import_quota_locks_pkey");
        builder.Property(lockRow => lockRow.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(lockRow => lockRow.LockedAt).HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(lockRow => lockRow.CustomerId)
            .HasConstraintName("import_quota_locks_customer_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ImportReviewConfiguration : IEntityTypeConfiguration<ImportReview>
{
    public void Configure(EntityTypeBuilder<ImportReview> builder)
    {
        builder.ToTable("import_reviews", table =>
        {
            table.HasCheckConstraint("import_reviews_version_check", "version > 0");
            table.HasCheckConstraint("import_reviews_dates_check", "(start_date IS NULL AND end_date IS NULL) OR start_date <= end_date");
            table.HasCheckConstraint(
                "import_reviews_counts_check",
                "total_rows >= 0 AND imported_count >= 0 AND suppressed_count >= 0 AND skipped_count >= 0 AND possible_duplicate_count >= 0 AND exact_override_count >= 0 AND statement_override_count >= 0 AND total_rows = imported_count + suppressed_count + skipped_count");
            table.HasCheckConstraint("import_reviews_currency_check", "currency = 'ZAR'");
            table.HasCheckConstraint("import_reviews_status_check", "status IN ('Prepared', 'Abandoned', 'Expired')");
        });
        builder.HasKey(review => review.Id).HasName("import_reviews_pkey");
        builder.Property(review => review.Id).ValueGeneratedNever();
        builder.Property(review => review.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(review => review.FinancialAccountId).HasColumnType("uuid").IsRequired();
        builder.Property(review => review.SourceKind).HasColumnType("text").IsRequired();
        builder.Property(review => review.ParserConfig).HasColumnType("jsonb").IsRequired();
        builder.Property(review => review.Currency).HasColumnType("text").HasDefaultValue("ZAR").IsRequired();
        builder.Property(review => review.StartDate).HasColumnType("date");
        builder.Property(review => review.EndDate).HasColumnType("date");
        builder.Property(review => review.Version).HasColumnType("integer").HasDefaultValue(1).IsRequired();
        builder.Property(review => review.Status).HasColumnType("text").HasDefaultValue("Prepared").IsRequired();
        builder.Property(review => review.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(review => review.AccountImportVersion).HasColumnType("bigint").IsRequired();
        builder.Property(review => review.DuplicatePolicyVersion).HasColumnType("integer").IsRequired();
        builder.Property(review => review.StatementFingerprint).HasColumnType("text").IsRequired();
        builder.Property(review => review.SummaryHash).HasColumnType("text").IsRequired();
        builder.Property(review => review.TotalRows).HasColumnType("integer").IsRequired();
        builder.Property(review => review.ImportedCount).HasColumnType("integer").IsRequired();
        builder.Property(review => review.SuppressedCount).HasColumnType("integer").IsRequired();
        builder.Property(review => review.SkippedCount).HasColumnType("integer").IsRequired();
        builder.Property(review => review.PossibleDuplicateCount).HasColumnType("integer").IsRequired();
        builder.Property(review => review.ExactOverrideCount).HasColumnType("integer").IsRequired();
        builder.Property(review => review.StatementOverride).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(review => review.StatementOverrideCount).HasColumnType("integer").HasDefaultValue(0).IsRequired();
        builder.Property(review => review.PayloadBytes).HasColumnType("bigint").IsRequired();
        builder.Property(review => review.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(review => review.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasAlternateKey(review => new { review.CustomerId, review.Id }).HasName("import_reviews_customer_id_id_key");
        builder.HasIndex(review => new { review.CustomerId, review.Status, review.ExpiresAt }).HasDatabaseName("import_reviews_customer_status_idx");
        builder.HasIndex(review => new { review.CustomerId, review.FinancialAccountId }).HasDatabaseName("import_reviews_account_idx");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(review => review.CustomerId)
            .HasConstraintName("import_reviews_customer_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FinancialAccount>()
            .WithMany()
            .HasForeignKey(review => new { review.CustomerId, review.FinancialAccountId })
            .HasPrincipalKey(account => new { account.CustomerId, account.Id })
            .HasConstraintName("import_reviews_customer_id_financial_account_id_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ImportReviewRowConfiguration : IEntityTypeConfiguration<ImportReviewRow>
{
    public void Configure(EntityTypeBuilder<ImportReviewRow> builder)
    {
        builder.ToTable("import_review_rows", table =>
        {
            table.HasCheckConstraint("import_review_rows_source_id_pair_check", "(source_id_namespace IS NULL) = (source_id_value IS NULL)");
            table.HasCheckConstraint("import_review_rows_duplicate_state_check", "duplicate_state IN ('none', 'suppressed', 'override', 'within_file', 'suppressed_within_file')");
            table.HasCheckConstraint("import_review_rows_within_file_state_check", "within_file_state IN ('none', 'pending', 'keep', 'skip')");
        });
        builder.HasKey(row => row.RowId).HasName("import_review_rows_pkey");
        builder.Property(row => row.RowId).ValueGeneratedNever();
        builder.Property(row => row.ReviewId).HasColumnType("uuid").IsRequired();
        builder.Property(row => row.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(row => row.SourceLine).HasColumnType("integer").IsRequired();
        builder.Property(row => row.SourcePage).HasColumnType("integer");
        builder.Property(row => row.SourceRowNumber).HasColumnType("integer").IsRequired();
        builder.Property(row => row.RawValues).HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.TransactionDate).HasColumnType("date");
        builder.Property(row => row.Description).HasColumnType("text").IsRequired();
        builder.Property(row => row.NormalizedDescription).HasColumnType("text").IsRequired();
        builder.Property(row => row.AmountCents).HasColumnType("bigint");
        builder.Property(row => row.SourceIdNamespace).HasColumnType("text");
        builder.Property(row => row.SourceIdValue).HasColumnType("text");
        builder.Property(row => row.ValidationCodes).HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.Warnings).HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.DuplicateState).HasColumnType("text").HasDefaultValue("none").IsRequired();
        builder.Property(row => row.DuplicateReasons).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(row => row.MatchingTransactionIds).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(row => row.MatchingImportIds).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(row => row.NearCandidates).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(row => row.NearCandidateTotal).HasColumnType("integer").HasDefaultValue(0).IsRequired();
        builder.Property(row => row.WithinFileGroup).HasColumnType("text");
        builder.Property(row => row.WithinFileState).HasColumnType("text").HasDefaultValue("none").IsRequired();
        builder.Property(row => row.Skip).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(row => row.Override).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(row => row.SuggestedTagIds).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(row => row.SelectedTagIds).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(row => row.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(row => row.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(row => row.SuggestedCategoryId).HasColumnType("uuid");
        builder.Property(row => row.SelectedCategoryId).HasColumnType("uuid");
        builder.Property(row => row.CategoryDecided).HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.HasAlternateKey(row => new { row.ReviewId, row.RowId }).HasName("import_review_rows_review_id_row_id_key");
        builder.HasIndex(row => new { row.ReviewId, row.SourceRowNumber }).HasDatabaseName("import_review_rows_review_idx");
        builder.HasOne<ImportReview>()
            .WithMany()
            .HasForeignKey(row => new { row.CustomerId, row.ReviewId })
            .HasPrincipalKey(review => new { review.CustomerId, review.Id })
            .HasConstraintName("import_review_rows_customer_id_review_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ImportCommitReceiptConfiguration : IEntityTypeConfiguration<ImportCommitReceipt>
{
    public void Configure(EntityTypeBuilder<ImportCommitReceipt> builder)
    {
        builder.ToTable("import_commit_receipts", table => table.HasCheckConstraint(
            "import_commit_receipts_state_check",
            "state IN ('Committed', 'Deleted')"));
        builder.HasKey(receipt => receipt.Id).HasName("import_commit_receipts_pkey");
        builder.Property(receipt => receipt.Id).ValueGeneratedNever();
        builder.Property(receipt => receipt.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(receipt => receipt.OperationId).HasColumnType("uuid").IsRequired();
        builder.Property(receipt => receipt.FinancialAccountId).HasColumnType("uuid").IsRequired();
        builder.Property(receipt => receipt.RequestDigest).HasColumnType("text").IsRequired();
        builder.Property(receipt => receipt.State).HasColumnType("text").IsRequired();
        builder.Property(receipt => receipt.StatementImportId).HasColumnType("uuid");
        builder.Property(receipt => receipt.Result).HasColumnType("jsonb").IsRequired();
        builder.Property(receipt => receipt.DeletionDigest).HasColumnType("text");
        builder.Property(receipt => receipt.DeletionExpectedCount).HasColumnType("bigint");
        builder.Property(receipt => receipt.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(receipt => receipt.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasAlternateKey(receipt => new { receipt.CustomerId, receipt.OperationId }).HasName("import_commit_receipts_customer_id_operation_id_key");
        builder.HasIndex(receipt => new { receipt.CustomerId, receipt.OperationId }).HasDatabaseName("import_commit_receipts_customer_idx");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(receipt => receipt.CustomerId)
            .HasConstraintName("import_commit_receipts_customer_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FinancialAccount>()
            .WithMany()
            .HasForeignKey(receipt => new { receipt.CustomerId, receipt.FinancialAccountId })
            .HasPrincipalKey(account => new { account.CustomerId, account.Id })
            .HasConstraintName("import_commit_receipts_customer_id_financial_account_id_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TransactionOperationConfiguration : IEntityTypeConfiguration<TransactionOperation>
{
    public void Configure(EntityTypeBuilder<TransactionOperation> builder)
    {
        builder.ToTable("transaction_operations", table =>
        {
            table.HasCheckConstraint("transaction_operations_status_check", "status IN ('Prepared', 'Applied', 'Abandoned', 'Expired')");
            table.HasCheckConstraint("transaction_operations_selection_mode_check", "selection_mode IN ('explicit', 'all_matching')");
            table.HasCheckConstraint("transaction_operations_selected_count_check", "selected_count >= 0");
            table.HasCheckConstraint("transaction_operations_changed_count_check", "changed_count >= 0");
            table.HasCheckConstraint("transaction_operations_unchanged_count_check", "unchanged_count >= 0");
            table.HasCheckConstraint("transaction_operations_counts_check", "selected_count = changed_count + unchanged_count");
            table.HasCheckConstraint("transaction_operations_expiry_check", "expires_at > captured_at");
            table.HasCheckConstraint("transaction_operations_applied_check", "(status = 'Applied') = (applied_at IS NOT NULL)");
            table.HasCheckConstraint("transaction_operations_prepared_payload_check", "status <> 'Prepared' OR (confirmation_hash IS NOT NULL AND action <> '{}'::jsonb)");
        });
        builder.HasKey(operation => new { operation.CustomerId, operation.OperationId }).HasName("transaction_operations_pkey");
        builder.Property(operation => operation.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(operation => operation.OperationId).HasColumnType("uuid").IsRequired();
        builder.Property(operation => operation.Status).HasColumnType("text").IsRequired();
        builder.Property(operation => operation.SelectionMode).HasColumnType("text").IsRequired();
        builder.Property(operation => operation.RequestDigest).HasColumnType("text").IsRequired();
        builder.Property(operation => operation.FilterDigest).HasColumnType("text");
        builder.Property(operation => operation.ConfirmationHash).HasColumnType("text");
        builder.Property(operation => operation.Action).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(operation => operation.TargetStamps).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(operation => operation.SelectedCount).HasColumnType("integer").IsRequired();
        builder.Property(operation => operation.ChangedCount).HasColumnType("integer").IsRequired();
        builder.Property(operation => operation.UnchangedCount).HasColumnType("integer").IsRequired();
        builder.Property(operation => operation.CapturedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(operation => operation.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(operation => operation.AppliedAt).HasColumnType("timestamp with time zone");
        builder.Property(operation => operation.TerminalAt).HasColumnType("timestamp with time zone");
        builder.Property(operation => operation.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(operation => operation.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(operation => new { operation.CustomerId, operation.Status, operation.ExpiresAt })
            .HasDatabaseName("transaction_operations_customer_status_idx");
        builder.HasIndex(operation => new { operation.CustomerId, operation.TerminalAt })
            .HasDatabaseName("transaction_operations_terminal_idx")
            .IsDescending(false, true)
            .HasFilter("status <> 'Prepared'");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(operation => operation.CustomerId)
            .HasConstraintName("transaction_operations_customer_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TransactionOperationItemConfiguration : IEntityTypeConfiguration<TransactionOperationItem>
{
    public void Configure(EntityTypeBuilder<TransactionOperationItem> builder)
    {
        builder.ToTable("transaction_operation_items", table =>
        {
            table.HasCheckConstraint("transaction_operation_items_transaction_version_check", "transaction_version > 0");
            table.HasCheckConstraint("transaction_operation_items_ordinal_check", "ordinal >= 0");
        });
        builder.HasKey(item => new { item.CustomerId, item.OperationId, item.TransactionId })
            .HasName("transaction_operation_items_pkey");
        builder.Property(item => item.CustomerId).HasColumnType("uuid").IsRequired();
        builder.Property(item => item.OperationId).HasColumnType("uuid").IsRequired();
        builder.Property(item => item.TransactionId).HasColumnType("uuid").IsRequired();
        builder.Property(item => item.TransactionVersion).HasColumnType("integer").IsRequired();
        builder.Property(item => item.FinancialAccountId).HasColumnType("uuid").IsRequired();
        builder.Property(item => item.StatementImportId).HasColumnType("uuid").IsRequired();
        builder.Property(item => item.Ordinal).HasColumnType("integer").IsRequired();
        builder.HasIndex(item => new { item.CustomerId, item.OperationId, item.Ordinal })
            .HasDatabaseName("transaction_operation_items_header_idx");
        builder.HasOne<TransactionOperation>()
            .WithMany()
            .HasForeignKey(item => new { item.CustomerId, item.OperationId })
            .HasConstraintName("transaction_operation_items_customer_id_operation_id_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
