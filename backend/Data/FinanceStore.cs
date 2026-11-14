using System.Data;
using System.Text;
using argus.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace argus.Server.Data;

public sealed record FinancialAccountRecord(Guid Id,
                                            string DisplayName,
                                            string? Institution,
                                            string Currency,
                                            bool IsArchived,
                                            DateTime? ArchivedAt,
                                            long TransactionCount,
                                            DateTime CreatedAt,
                                            DateTime UpdatedAt);

public sealed record CategoryRecord(Guid Id,
                                    string DisplayName,
                                    string Kind,
                                    bool IsSystem,
                                    bool IsDefault,
                                    bool IsArchived,
                                    DateTime? ArchivedAt,
                                    int SortOrder,
                                    long TransactionCount,
                                    DateTime CreatedAt,
                                    DateTime UpdatedAt);

public sealed record MerchantTagRecord(Guid Id,
                                       string DisplayName,
                                       bool IsArchived,
                                       DateTime? ArchivedAt,
                                       long TransactionCount,
                                       DateTime CreatedAt,
                                       DateTime UpdatedAt);

public sealed record MerchantAliasRecord(Guid Id,
                                         string DisplayAlias,
                                         Guid MerchantTagId,
                                         string TagDisplayName,
                                         Guid? CategoryId,
                                         string? CategoryDisplayName,
                                         DateTime CreatedAt,
                                         DateTime UpdatedAt);

public abstract record AccountOutcome
{
    public sealed record Success(FinancialAccountRecord Account) : AccountOutcome;

    public sealed record InvalidName : AccountOutcome;

    public sealed record NotFound : AccountOutcome;
}

public abstract record CategoryOutcome
{
    public sealed record Success(CategoryRecord Category) : CategoryOutcome;

    public sealed record InvalidName : CategoryOutcome;

    public sealed record InvalidKind : CategoryOutcome;

    public sealed record NotFound : CategoryOutcome;

    public sealed record DuplicateName : CategoryOutcome;

    public sealed record SystemCategoryImmutable : CategoryOutcome;

    public sealed record KindChangeConfirmationRequired : CategoryOutcome;
}

public abstract record CategoryDeleteOutcome
{
    public sealed record Success(long ReassignedTransactionCount, Guid UncategorisedCategoryId) : CategoryDeleteOutcome;

    public sealed record NotFound : CategoryDeleteOutcome;

    public sealed record SystemCategoryImmutable : CategoryDeleteOutcome;
}

public abstract record MerchantTagOutcome
{
    public sealed record Success(MerchantTagRecord Tag) : MerchantTagOutcome;

    public sealed record InvalidName : MerchantTagOutcome;

    public sealed record NotFound : MerchantTagOutcome;

    public sealed record DuplicateName : MerchantTagOutcome;
}

public abstract record MerchantAliasOutcome
{
    public sealed record Success(MerchantAliasRecord Alias) : MerchantAliasOutcome;

    public sealed record InvalidName : MerchantAliasOutcome;

    public sealed record DuplicateName : MerchantAliasOutcome;

    public sealed record LimitReached : MerchantAliasOutcome;

    public sealed record TagNotFound : MerchantAliasOutcome;

    public sealed record CategoryNotFound : MerchantAliasOutcome;

    public sealed record NotFound : MerchantAliasOutcome;
}

public abstract record MerchantTagDeleteOutcome
{
    public sealed record Success(long UnassignedTransactionCount) : MerchantTagDeleteOutcome;

    public sealed record NotFound : MerchantTagDeleteOutcome;
}

/// <summary>
/// Customer-scoped financial-entity persistence. Every query and write filters
/// on <c>customer_id</c>; no read or write can reach another customer's row.
/// </summary>
public sealed class FinanceStore(IDbContextFactory<ArgusDbContext> contextFactory)
{
    public const int MaxNameRunes = 80;
    public const int MaxMerchantAliases = 100;

    public static readonly (string Name, string Kind, bool IsSystem, int SortOrder)[] DefaultCategories =
        CategoryDefaults.All;

    // --- Name normalisation ------------------------------------------------

    /// <summary>
    /// Trims, collapses internal whitespace runs to one space, and rejects
    /// empty, control-character, or oversized names. Display and normalized
    /// forms are returned together so both writers agree.
    /// </summary>
    public static bool TryNormalizeName(string? input,
                                        out string displayName,
                                        out string normalizedName,
                                        int maxRunes = MaxNameRunes)
    {
        displayName = string.Empty;
        normalizedName = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (input.EnumerateRunes().Any(rune => Rune.IsControl(rune)))
        {
            return false;
        }

        var builder = new StringBuilder(input.Length);
        bool pendingSpace = false;
        foreach (var rune in input.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                if (builder.Length > 0)
                {
                    pendingSpace = true;
                }
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(rune.ToString());
        }

        string collapsed = builder.ToString();
        if (collapsed.Length == 0 || CountRunes(collapsed) > maxRunes)
        {
            return false;
        }

        displayName = collapsed;
        normalizedName = collapsed.ToLowerInvariant();

        return true;
    }

    /// <summary>Null or whitespace means "no institution"; anything else is normalized.</summary>
    public static bool TryNormalizeInstitution(string? input, out string? institution, int maxRunes = MaxNameRunes)
    {
        institution = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        if (!TryNormalizeName(input, out string? displayName, out _, maxRunes))
        {
            return false;
        }

        institution = displayName;

        return true;
    }

    public static bool IsUserCategoryKind(string? kind) => kind is "Expense" or "Income" or "Transfer" or "Excluded";

    private static int CountRunes(string value)
    {
        int count = 0;
        foreach (Rune _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    // --- Category seeding --------------------------------------------------

    /// <summary>
    /// Seeds the system bucket and the 19 default categories exactly once per
    /// customer. The customer row is locked so concurrent first-use requests
    /// serialize, and the under-lock re-check stops a second seeder from
    /// resurrecting defaults after the customer renamed or deleted them.
    /// </summary>
    public async Task EnsureCategoriesSeededAsync(FinanceAuthority authority, CancellationToken cancellationToken = default)
    {
        await using var scope = await CustomerDbScope.OpenExclusiveAsync(contextFactory, authority, cancellationToken);
        await CategoryDefaults.EnsureSeededAsync(scope.Context, authority.CustomerId, customerLocked: true, cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    // --- Financial accounts ------------------------------------------------

    public async Task<IReadOnlyList<FinancialAccountRecord>> ListAccountsAsync(FinanceAuthority authority, CancellationToken cancellationToken = default)
    {
        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);

        var accounts = await AccountRecords(scope.Context, authority.CustomerId).ToListAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return accounts;
    }

    public async Task<AccountOutcome> CreateAccountAsync(FinanceAuthority authority,
                                                         string? displayName,
                                                         string? institution,
                                                         CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        if (!TryNormalizeName(displayName, out string? name, out _) || !TryNormalizeInstitution(institution, out string? normalizedInstitution))
        {
            return new AccountOutcome.InvalidName();
        }

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);
        var context = scope.Context;
        context.FinancialAccounts.Add(new FinancialAccount
        {
            Id = id,
            CustomerId = customerId,
            DisplayName = name,
            Institution = normalizedInstitution,
            Currency = "ZAR",
            IsArchived = false,
            ArchivedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
            ImportVersion = 0,
        });
        await context.SaveChangesAsync(cancellationToken);

        FinancialAccountRecord? created = await FindAccountAsync(context, customerId, id, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return created is null ? new AccountOutcome.NotFound() : new AccountOutcome.Success(created);
    }

    /// <summary>
    /// Rename and optional institution update. When <paramref name="institutionProvided"/>
    /// is false the stored institution is preserved; when it is true, null or an
    /// empty value clears it.
    /// </summary>
    public async Task<AccountOutcome> UpdateAccountAsync(FinanceAuthority authority,
                                                         Guid id,
                                                         string? displayName,
                                                         bool institutionProvided,
                                                         string? institution,
                                                         CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        if (!TryNormalizeName(displayName, out string? name, out _))
        {
            return new AccountOutcome.InvalidName();
        }

        string? normalizedInstitution = null;
        if (institutionProvided && !TryNormalizeInstitution(institution, out normalizedInstitution))
        {
            return new AccountOutcome.InvalidName();
        }

        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);
        var context = scope.Context;
        if (!await LockAccountAsync(context, customerId, id, cancellationToken))
        {
            return new AccountOutcome.NotFound();
        }

        FinancialAccount account = await context.FinancialAccounts.SingleAsync(
            item => item.CustomerId == customerId && item.Id == id,
            cancellationToken);
        account.DisplayName = name;
        if (institutionProvided)
        {
            account.Institution = normalizedInstitution;
        }
        account.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        FinancialAccountRecord? updated = await FindAccountAsync(context, customerId, id, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return updated is null ? new AccountOutcome.NotFound() : new AccountOutcome.Success(updated);
    }

    /// <summary>Archive or restore. Repeating the same transition is an idempotent success.</summary>
    public async Task<AccountOutcome> SetAccountArchivedAsync(FinanceAuthority authority,
                                                              Guid id,
                                                              bool archived,
                                                              CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);
        var context = scope.Context;
        if (!await LockAccountAsync(context, customerId, id, cancellationToken))
        {
            return new AccountOutcome.NotFound();
        }

        FinancialAccount account = await context.FinancialAccounts.SingleAsync(
            item => item.CustomerId == customerId && item.Id == id,
            cancellationToken);
        account.IsArchived = archived;
        account.ArchivedAt = archived ? account.ArchivedAt ?? DateTime.UtcNow : null;
        account.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        FinancialAccountRecord? updated = await FindAccountAsync(context, customerId, id, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return updated is null ? new AccountOutcome.NotFound() : new AccountOutcome.Success(updated);
    }

    private static IQueryable<FinancialAccountRecord> AccountRecords(ArgusDbContext context, Guid customerId, Guid? id = null)
    {
        IQueryable<FinancialAccount> query = context.FinancialAccounts
            .AsNoTracking()
            .Where(account => account.CustomerId == customerId);
        if (id is not null)
        {
            query = query.Where(account => account.Id == id.Value);
        }

        return query
            .OrderBy(account => account.IsArchived)
            .ThenBy(account => account.CreatedAt)
            .ThenBy(account => account.Id)
            .Select(account => new FinancialAccountRecord(
            account.Id,
            account.DisplayName,
            account.Institution,
            account.Currency,
            account.IsArchived,
            account.ArchivedAt,
            context.Transactions.LongCount(transaction => transaction.CustomerId == account.CustomerId && transaction.FinancialAccountId == account.Id),
            account.CreatedAt,
            account.UpdatedAt));
    }

    private static async Task<FinancialAccountRecord?> FindAccountAsync(ArgusDbContext context,
                                                                        Guid customerId,
                                                                        Guid id,
                                                                        CancellationToken cancellationToken) =>
        await AccountRecords(context, customerId, id).SingleOrDefaultAsync(cancellationToken);

    private static async Task<bool> LockAccountAsync(ArgusDbContext context,
                                                     Guid customerId,
                                                     Guid id,
                                                     CancellationToken cancellationToken)
    {
        List<Guid> ids = await context.Database.SqlQuery<Guid>($"""
            SELECT id FROM financial_accounts WHERE customer_id = {customerId} AND id = {id} FOR UPDATE
            """).ToListAsync(cancellationToken);

        return ids.Count > 0;
    }

    // --- Categories --------------------------------------------------------

    public async Task<IReadOnlyList<CategoryRecord>> ListCategoriesAsync(FinanceAuthority authority, CancellationToken cancellationToken = default)
    {
        await using var scope = await CustomerDbScope.OpenExclusiveAsync(contextFactory, authority, cancellationToken);
        await CategoryDefaults.EnsureSeededAsync(scope.Context, authority.CustomerId, customerLocked: true, cancellationToken);
        var categories = await CategoryRecords(scope.Context, authority.CustomerId).ToListAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return categories;
    }

    public async Task<CategoryOutcome> CreateCategoryAsync(FinanceAuthority authority,
                                                           string? displayName,
                                                           string? kind,
                                                           CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        if (!TryNormalizeName(displayName, out string? name, out string? normalizedName))
        {
            return new CategoryOutcome.InvalidName();
        }

        if (!IsUserCategoryKind(kind))
        {
            return new CategoryOutcome.InvalidKind();
        }

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var scope = await CustomerDbScope.OpenExclusiveAsync(contextFactory, authority, cancellationToken);
        await CategoryDefaults.EnsureSeededAsync(scope.Context, customerId, customerLocked: true, cancellationToken);
        var context = scope.Context;
        try
        {
            context.Categories.Add(new Category
            {
                Id = id,
                CustomerId = customerId,
                DisplayName = name,
                NormalizedName = normalizedName,
                Kind = kind!,
                IsSystem = false,
                IsDefault = false,
                IsArchived = false,
                ArchivedAt = null,
                SortOrder = 100,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (PersistenceErrors.IsUniqueViolation(exception))
        {
            return new CategoryOutcome.DuplicateName();
        }
        catch (Exception exception) when (PersistenceErrors.IsCheckViolation(exception))
        {
            return new CategoryOutcome.InvalidKind();
        }

        CategoryRecord? created = await FindCategoryAsync(context, customerId, id, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return created is null ? new CategoryOutcome.NotFound() : new CategoryOutcome.Success(created);
    }

    public async Task<CategoryOutcome> UpdateCategoryAsync(FinanceAuthority authority,
                                                           Guid id,
                                                           string? displayName,
                                                           string? kind,
                                                           bool confirmKindChange,
                                                           CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        if (!TryNormalizeName(displayName, out string? name, out string? normalizedName))
        {
            return new CategoryOutcome.InvalidName();
        }

        if (!IsUserCategoryKind(kind))
        {
            return new CategoryOutcome.InvalidKind();
        }

        await using var scope = await CustomerDbScope.OpenExclusiveAsync(contextFactory, authority, cancellationToken);
        await CategoryDefaults.EnsureSeededAsync(scope.Context, customerId, customerLocked: true, cancellationToken);
        var context = scope.Context;
        Category? category = await LockCategoryAsync(context, customerId, id, cancellationToken);
        if (category is null)
        {
            return new CategoryOutcome.NotFound();
        }
        if (category.IsSystem)
        {
            return new CategoryOutcome.SystemCategoryImmutable();
        }
        if (!string.Equals(category.Kind, kind, StringComparison.Ordinal) && !confirmKindChange)
        {
            return new CategoryOutcome.KindChangeConfirmationRequired();
        }

        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE categories
                SET display_name = {name}, normalized_name = {normalizedName}, kind = {kind}, updated_at = NOW()
                WHERE customer_id = {customerId} AND id = {id}
                """, cancellationToken);
        }
        catch (Exception exception) when (PersistenceErrors.IsUniqueViolation(exception))
        {
            return new CategoryOutcome.DuplicateName();
        }
        catch (Exception exception) when (PersistenceErrors.IsCheckViolation(exception))
        {
            return new CategoryOutcome.InvalidKind();
        }

        CategoryRecord? updated = await FindCategoryAsync(context, customerId, id, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return updated is null ? new CategoryOutcome.NotFound() : new CategoryOutcome.Success(updated);
    }

    /// <summary>Archive or restore. Repeating the same transition is an idempotent success.</summary>
    public async Task<CategoryOutcome> SetCategoryArchivedAsync(FinanceAuthority authority,
                                                                Guid id,
                                                                bool archived,
                                                                CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        await using var scope = await CustomerDbScope.OpenExclusiveAsync(contextFactory, authority, cancellationToken);
        await CategoryDefaults.EnsureSeededAsync(scope.Context, customerId, customerLocked: true, cancellationToken);
        var context = scope.Context;
        Category? category = await LockCategoryAsync(context, customerId, id, cancellationToken);
        if (category is null)
        {
            return new CategoryOutcome.NotFound();
        }
        if (category.IsSystem)
        {
            return new CategoryOutcome.SystemCategoryImmutable();
        }

        try
        {
            category.IsArchived = archived;
            category.ArchivedAt = archived ? category.ArchivedAt ?? DateTime.UtcNow : null;
            category.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (PersistenceErrors.IsCheckViolation(exception))
        {
            return new CategoryOutcome.SystemCategoryImmutable();
        }

        CategoryRecord? updated = await FindCategoryAsync(context, customerId, id, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return updated is null ? new CategoryOutcome.NotFound() : new CategoryOutcome.Success(updated);
    }

    /// <summary>
    /// Deletes a non-system category and atomically reassigns its transactions
    /// to the customer's system <c>Uncategorised</c> bucket. No transaction row
    /// is ever deleted.
    /// </summary>
    public async Task<CategoryDeleteOutcome> DeleteCategoryAsync(FinanceAuthority authority, Guid id, CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        await using var scope = await CustomerDbScope.OpenExclusiveAsync(contextFactory, authority, cancellationToken);
        await CategoryDefaults.EnsureSeededAsync(scope.Context, customerId, customerLocked: true, cancellationToken);
        var context = scope.Context;
        Category? category = await LockCategoryAsync(context, customerId, id, cancellationToken);
        if (category is null)
        {
            return new CategoryDeleteOutcome.NotFound();
        }
        if (category.IsSystem)
        {
            return new CategoryDeleteOutcome.SystemCategoryImmutable();
        }

        Guid? uncategorisedId = await context.Categories
            .Where(item => item.CustomerId == customerId && item.IsSystem)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (uncategorisedId is null)
        {
            return new CategoryDeleteOutcome.NotFound();
        }

        await context.Database.SqlQuery<Guid>($"""
            SELECT id FROM transactions
            WHERE customer_id = {customerId} AND category_id = {id}
            ORDER BY id
            FOR UPDATE
            """).ToListAsync(cancellationToken);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE merchant_aliases
            SET category_id = NULL, updated_at = NOW()
            WHERE customer_id = {customerId} AND category_id = {id}
            """, cancellationToken);

        int reassigned = await context.Transactions
            .Where(item => item.CustomerId == customerId && item.CategoryId == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.CategoryId, uncategorisedId.Value)
                .SetProperty(item => item.Version, item => item.Version + 1)
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow), cancellationToken);
        await context.Categories
            .Where(item => item.CustomerId == customerId && item.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return new CategoryDeleteOutcome.Success(reassigned, uncategorisedId.Value);
    }

    private static IQueryable<CategoryRecord> CategoryRecords(ArgusDbContext context, Guid customerId, Guid? id = null)
    {
        IQueryable<Category> query = context.Categories
            .AsNoTracking()
            .Where(category => category.CustomerId == customerId);
        if (id is not null)
        {
            query = query.Where(category => category.Id == id.Value);
        }

        return query
            .OrderBy(category => category.IsArchived)
            .ThenBy(category => category.SortOrder)
            .ThenBy(category => category.DisplayName)
            .ThenBy(category => category.Id)
            .Select(category => new CategoryRecord(
            category.Id,
            category.DisplayName,
            category.Kind,
            category.IsSystem,
            category.IsDefault,
            category.IsArchived,
            category.ArchivedAt,
            category.SortOrder,
            context.Transactions.LongCount(transaction => transaction.CustomerId == category.CustomerId && transaction.CategoryId == category.Id),
            category.CreatedAt,
            category.UpdatedAt));
    }

    private static async Task<CategoryRecord?> FindCategoryAsync(ArgusDbContext context,
                                                                 Guid customerId,
                                                                 Guid id,
                                                                 CancellationToken cancellationToken) =>
        await CategoryRecords(context, customerId, id).SingleOrDefaultAsync(cancellationToken);

    private static async Task<Category?> LockCategoryAsync(ArgusDbContext context,
                                                           Guid customerId,
                                                           Guid id,
                                                           CancellationToken cancellationToken)
    {
        List<Guid> ids = await context.Database.SqlQuery<Guid>($"""
            SELECT id FROM categories WHERE customer_id = {customerId} AND id = {id} FOR UPDATE
            """).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return null;
        }

        return await context.Categories.SingleAsync(
            category => category.CustomerId == customerId && category.Id == id,
            cancellationToken);
    }

    // --- Merchant tags -----------------------------------------------------

    public async Task<IReadOnlyList<MerchantTagRecord>> ListMerchantTagsAsync(FinanceAuthority authority, CancellationToken cancellationToken = default)
    {
        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);

        var tags = await MerchantTagRecords(scope.Context, authority.CustomerId).ToListAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return tags;
    }

    public async Task<MerchantTagOutcome> CreateMerchantTagAsync(FinanceAuthority authority, string? displayName, CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        if (!TryNormalizeName(displayName, out string? name, out string? normalizedName))
        {
            return new MerchantTagOutcome.InvalidName();
        }

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);
        var context = scope.Context;
        try
        {
            context.MerchantTags.Add(new MerchantTag
            {
                Id = id,
                CustomerId = customerId,
                DisplayName = name,
                NormalizedName = normalizedName,
                IsArchived = false,
                ArchivedAt = null,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (PersistenceErrors.IsUniqueViolation(exception))
        {
            return new MerchantTagOutcome.DuplicateName();
        }

        MerchantTagRecord? created = await FindMerchantTagAsync(context, customerId, id, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return created is null ? new MerchantTagOutcome.NotFound() : new MerchantTagOutcome.Success(created);
    }

    public async Task<MerchantTagOutcome> UpdateMerchantTagAsync(FinanceAuthority authority,
                                                                 Guid id,
                                                                 string? displayName,
                                                                 CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        if (!TryNormalizeName(displayName, out string? name, out string? normalizedName))
        {
            return new MerchantTagOutcome.InvalidName();
        }

        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);
        var context = scope.Context;
        MerchantTag? tag = await LockMerchantTagAsync(context, customerId, id, cancellationToken);
        if (tag is null)
        {
            return new MerchantTagOutcome.NotFound();
        }

        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE merchant_tags
                SET display_name = {name}, normalized_name = {normalizedName}, updated_at = NOW()
                WHERE customer_id = {customerId} AND id = {id}
                """, cancellationToken);
        }
        catch (Exception exception) when (PersistenceErrors.IsUniqueViolation(exception))
        {
            return new MerchantTagOutcome.DuplicateName();
        }

        MerchantTagRecord? updated = await FindMerchantTagAsync(context, customerId, id, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return updated is null ? new MerchantTagOutcome.NotFound() : new MerchantTagOutcome.Success(updated);
    }

    /// <summary>Archive or restore. Repeating the same transition is an idempotent success.</summary>
    public async Task<MerchantTagOutcome> SetMerchantTagArchivedAsync(FinanceAuthority authority,
                                                                      Guid id,
                                                                      bool archived,
                                                                      CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);
        var context = scope.Context;
        MerchantTag? tag = await LockMerchantTagAsync(context, customerId, id, cancellationToken);
        if (tag is null)
        {
            return new MerchantTagOutcome.NotFound();
        }

        tag.IsArchived = archived;
        tag.ArchivedAt = archived ? tag.ArchivedAt ?? DateTime.UtcNow : null;
        tag.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        MerchantTagRecord? updated = await FindMerchantTagAsync(context, customerId, id, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return updated is null ? new MerchantTagOutcome.NotFound() : new MerchantTagOutcome.Success(updated);
    }

    /// <summary>
    /// Deletes a merchant tag and atomically removes its transaction
    /// assignments. Transactions themselves are preserved.
    /// </summary>
    public async Task<MerchantTagDeleteOutcome> DeleteMerchantTagAsync(FinanceAuthority authority, Guid id, CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);
        var context = scope.Context;
        MerchantTag? tag = await LockMerchantTagAsync(context, customerId, id, cancellationToken);
        if (tag is null)
        {
            return new MerchantTagDeleteOutcome.NotFound();
        }

        await context.Database.SqlQuery<Guid>($"""
            SELECT t.id
            FROM transactions t
            JOIN transaction_merchant_tags j
              ON j.customer_id = t.customer_id AND j.transaction_id = t.id
            WHERE t.customer_id = {customerId} AND j.merchant_tag_id = {id}
            ORDER BY t.id
            FOR UPDATE OF t
            """).ToListAsync(cancellationToken);

        long unassigned = await context.TransactionMerchantTags
            .LongCountAsync(
                link => link.MerchantTagId == id &&
                        context.Transactions.Any(transaction => transaction.Id == link.TransactionId && transaction.CustomerId == customerId),
                cancellationToken);
        await context.Transactions
            .Where(transaction => transaction.CustomerId == customerId &&
                                  context.TransactionMerchantTags.Any(link => link.TransactionId == transaction.Id && link.MerchantTagId == id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(transaction => transaction.Version, transaction => transaction.Version + 1)
                .SetProperty(transaction => transaction.UpdatedAt, DateTime.UtcNow), cancellationToken);
        await context.TransactionMerchantTags
            .Where(link => link.MerchantTagId == id)
            .ExecuteDeleteAsync(cancellationToken);
        await context.MerchantTags
            .Where(item => item.CustomerId == customerId && item.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return new MerchantTagDeleteOutcome.Success(unassigned);
    }

    private static IQueryable<MerchantTagRecord> MerchantTagRecords(ArgusDbContext context, Guid customerId, Guid? id = null)
    {
        IQueryable<MerchantTag> query = context.MerchantTags
            .AsNoTracking()
            .Where(tag => tag.CustomerId == customerId);
        if (id is not null)
        {
            query = query.Where(tag => tag.Id == id.Value);
        }

        return query
            .OrderBy(tag => tag.IsArchived)
            .ThenBy(tag => tag.DisplayName)
            .ThenBy(tag => tag.Id)
            .Select(tag => new MerchantTagRecord(
            tag.Id,
            tag.DisplayName,
            tag.IsArchived,
            tag.ArchivedAt,
            context.TransactionMerchantTags.LongCount(link =>
                link.MerchantTagId == tag.Id &&
                context.Transactions.Any(transaction => transaction.Id == link.TransactionId && transaction.CustomerId == tag.CustomerId)),
            tag.CreatedAt,
            tag.UpdatedAt));
    }

    private static async Task<MerchantTagRecord?> FindMerchantTagAsync(ArgusDbContext context,
                                                                       Guid customerId,
                                                                       Guid id,
                                                                       CancellationToken cancellationToken) =>
        await MerchantTagRecords(context, customerId, id).SingleOrDefaultAsync(cancellationToken);

    private static async Task<MerchantTag?> LockMerchantTagAsync(ArgusDbContext context,
                                                                 Guid customerId,
                                                                 Guid id,
                                                                 CancellationToken cancellationToken)
    {
        List<Guid> ids = await context.Database.SqlQuery<Guid>($"""
            SELECT id FROM merchant_tags WHERE customer_id = {customerId} AND id = {id} FOR UPDATE
            """).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return null;
        }

        return await context.MerchantTags.SingleAsync(
            tag => tag.CustomerId == customerId && tag.Id == id,
            cancellationToken);
    }

    // --- Merchant aliases --------------------------------------------------

    public async Task<IReadOnlyList<MerchantAliasRecord>> ListMerchantAliasesAsync(FinanceAuthority authority, CancellationToken cancellationToken = default)
    {
        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);

        var aliases = await AliasRecords(scope.Context, authority.CustomerId)
            .Take(MaxMerchantAliases)
            .ToListAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return aliases;
    }

    public async Task<MerchantAliasOutcome> CreateMerchantAliasAsync(FinanceAuthority authority,
                                                                     string? displayAlias,
                                                                     Guid? merchantTagId,
                                                                     Guid? categoryId,
                                                                     CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        if (!TryNormalizeName(displayAlias, out string? alias, out string? normalizedAlias) || merchantTagId is null)
        {
            return new MerchantAliasOutcome.InvalidName();
        }

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);
        var context = scope.Context;
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT pg_advisory_xact_lock(hashtextextended({customerId}::text, 0))
                """, cancellationToken);

            long existing = await context.MerchantAliases.LongCountAsync(
                item => item.CustomerId == customerId,
                cancellationToken);
            if (existing >= MaxMerchantAliases)
            {
                return new MerchantAliasOutcome.LimitReached();
            }
            if (!await AliasTagExistsAsync(context, customerId, merchantTagId.Value, cancellationToken))
            {
                return new MerchantAliasOutcome.TagNotFound();
            }
            if (categoryId is Guid selectedCategory && !await AliasCategoryExistsAsync(context, customerId, selectedCategory, cancellationToken))
            {
                return new MerchantAliasOutcome.CategoryNotFound();
            }

            context.MerchantAliases.Add(new MerchantAlias
            {
                Id = id,
                CustomerId = customerId,
                DisplayAlias = alias,
                NormalizedAlias = normalizedAlias,
                MerchantTagId = merchantTagId.Value,
                CategoryId = categoryId,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (PersistenceErrors.IsUniqueViolation(exception))
        {
            return new MerchantAliasOutcome.DuplicateName();
        }

        MerchantAliasRecord? created = await FindMerchantAliasAsync(context, customerId, id, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return created is null ? new MerchantAliasOutcome.NotFound() : new MerchantAliasOutcome.Success(created);
    }

    public async Task<MerchantAliasOutcome> UpdateMerchantAliasAsync(FinanceAuthority authority,
                                                                     Guid id,
                                                                     string? displayAlias,
                                                                     Guid? merchantTagId,
                                                                     Guid? categoryId,
                                                                     CancellationToken cancellationToken = default)
    {
        Guid customerId = authority.CustomerId;
        if (!TryNormalizeName(displayAlias, out string? alias, out string? normalizedAlias) || merchantTagId is null)
        {
            return new MerchantAliasOutcome.InvalidName();
        }

        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);
        var context = scope.Context;
        try
        {
            if (!await AliasTagExistsAsync(context, customerId, merchantTagId.Value, cancellationToken))
            {
                return new MerchantAliasOutcome.TagNotFound();
            }
            if (categoryId is Guid selectedCategory && !await AliasCategoryExistsAsync(context, customerId, selectedCategory, cancellationToken))
            {
                return new MerchantAliasOutcome.CategoryNotFound();
            }

            MerchantAlias? aliasRow = await context.MerchantAliases.SingleOrDefaultAsync(
                item => item.CustomerId == customerId && item.Id == id,
                cancellationToken);
            if (aliasRow is null)
            {
                return new MerchantAliasOutcome.NotFound();
            }

            int updated = await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE merchant_aliases
                SET display_alias = {alias},
                    normalized_alias = {normalizedAlias},
                    merchant_tag_id = {merchantTagId.Value},
                    category_id = {categoryId},
                    updated_at = NOW()
                WHERE customer_id = {customerId} AND id = {id}
                """, cancellationToken);
            if (updated == 0)
            {
                return new MerchantAliasOutcome.NotFound();
            }
        }
        catch (Exception exception) when (PersistenceErrors.IsUniqueViolation(exception))
        {
            return new MerchantAliasOutcome.DuplicateName();
        }

        MerchantAliasRecord? updatedAlias = await FindMerchantAliasAsync(context, customerId, id, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return updatedAlias is null ? new MerchantAliasOutcome.NotFound() : new MerchantAliasOutcome.Success(updatedAlias);
    }

    public async Task<bool> DeleteMerchantAliasAsync(FinanceAuthority authority, Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = await CustomerDbScope.OpenSharedAsync(contextFactory, authority, cancellationToken);
        int deleted = await scope.Context.MerchantAliases
            .Where(alias => alias.CustomerId == authority.CustomerId && alias.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return deleted > 0;
    }

    private static IQueryable<MerchantAliasRecord> AliasRecords(ArgusDbContext context, Guid customerId, Guid? id = null)
    {
        var source = from alias in context.MerchantAliases.AsNoTracking()
                     join tag in context.MerchantTags on new { alias.CustomerId, Id = alias.MerchantTagId } equals new { tag.CustomerId, tag.Id }
                     join category in context.Categories on new { alias.CustomerId, Id = alias.CategoryId } equals new { category.CustomerId, Id = (Guid?)category.Id } into categories
                     from category in categories.DefaultIfEmpty()
                     where alias.CustomerId == customerId
                     orderby alias.DisplayAlias, alias.Id
                     select new { Alias = alias, Tag = tag, Category = category };
        if (id is not null)
        {
            source = source.Where(item => item.Alias.Id == id.Value);
        }

        return source.Select(item => new MerchantAliasRecord(
            item.Alias.Id,
            item.Alias.DisplayAlias,
            item.Alias.MerchantTagId,
            item.Tag.DisplayName,
            item.Alias.CategoryId,
            item.Category == null ? null : item.Category.DisplayName,
            item.Alias.CreatedAt,
            item.Alias.UpdatedAt));
    }

    private static async Task<MerchantAliasRecord?> FindMerchantAliasAsync(ArgusDbContext context,
                                                                          Guid customerId,
                                                                          Guid id,
                                                                          CancellationToken cancellationToken) =>
        await AliasRecords(context, customerId, id).SingleOrDefaultAsync(cancellationToken);

    private static async Task<bool> AliasTagExistsAsync(ArgusDbContext context,
                                                        Guid customerId,
                                                        Guid merchantTagId,
                                                        CancellationToken cancellationToken)
    {
        List<Guid> ids = await context.Database.SqlQuery<Guid>($"""
            SELECT id FROM merchant_tags
            WHERE customer_id = {customerId} AND id = {merchantTagId} AND is_archived = FALSE
            FOR SHARE
            """).ToListAsync(cancellationToken);

        return ids.Count > 0;
    }

    private static async Task<bool> AliasCategoryExistsAsync(ArgusDbContext context,
                                                             Guid customerId,
                                                             Guid categoryId,
                                                             CancellationToken cancellationToken)
    {
        List<Guid> ids = await context.Database.SqlQuery<Guid>($"""
            SELECT id FROM categories
            WHERE customer_id = {customerId} AND id = {categoryId}
              AND is_system = FALSE AND is_archived = FALSE
            FOR SHARE
            """).ToListAsync(cancellationToken);

        return ids.Count > 0;
    }
}
