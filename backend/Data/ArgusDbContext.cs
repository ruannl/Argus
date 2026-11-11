using System.Text;
using argus.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace argus.Server.Data;

public sealed class ArgusDbContext(DbContextOptions<ArgusDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<SchemaMigration> SchemaMigrations => Set<SchemaMigration>();
    public DbSet<DataMigration> DataMigrations => Set<DataMigration>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MerchantTag> MerchantTags => Set<MerchantTag>();
    public DbSet<MerchantAlias> MerchantAliases => Set<MerchantAlias>();
    public DbSet<StatementImport> StatementImports => Set<StatementImport>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionMerchantTag> TransactionMerchantTags => Set<TransactionMerchantTag>();
    public DbSet<ImportQuotaLock> ImportQuotaLocks => Set<ImportQuotaLock>();
    public DbSet<ImportReview> ImportReviews => Set<ImportReview>();
    public DbSet<ImportReviewRow> ImportReviewRows => Set<ImportReviewRow>();
    public DbSet<ImportCommitReceipt> ImportCommitReceipts => Set<ImportCommitReceipt>();
    public DbSet<TransactionOperation> TransactionOperations => Set<TransactionOperation>();
    public DbSet<TransactionOperationItem> TransactionOperationItems => Set<TransactionOperationItem>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The legacy schema deliberately has no automatic foreign-key indexes.
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ArgusDbContext).Assembly);

        // The legacy schema uses snake_case column names. Keep one mechanical
        // mapping instead of repeating HasColumnName on every property.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (char.IsUpper(character))
            {
                if (index > 0)
                {
                    builder.Append('_');
                }
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
