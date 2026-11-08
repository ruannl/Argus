using System.Text;
using Npgsql;
using NpgsqlTypes;

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
    public sealed record Success(long ReassignedTransactionCount,
                                 Guid UncategorisedCategoryId) : CategoryDeleteOutcome;

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

public abstract record MerchantTagDeleteOutcome
{
    public sealed record Success(long UnassignedTransactionCount) : MerchantTagDeleteOutcome;

    public sealed record NotFound : MerchantTagDeleteOutcome;
}

/// <summary>
/// Customer-scoped financial-entity persistence. Every statement here filters
/// on <c>customer_id</c>; no read or write can reach another customer's row.
/// </summary>
public sealed class FinanceStore(Db db)
{
    public const int MaxNameRunes = 80;

    public static readonly (string Name, string Kind, bool IsSystem, int SortOrder)[] DefaultCategories =
    [
        ("Uncategorised", "Uncategorised", true, 0),
        ("Groceries", "Expense", false, 1),
        ("Dining and takeaways", "Expense", false, 2),
        ("Transport", "Expense", false, 3),
        ("Housing and rent", "Expense", false, 4),
        ("Home loan", "Expense", false, 5),
        ("Levies", "Expense", false, 6),
        ("Utilities", "Expense", false, 7),
        ("Health", "Expense", false, 8),
        ("Education and school fees", "Expense", false, 9),
        ("Shopping", "Expense", false, 10),
        ("Entertainment", "Expense", false, 11),
        ("Subscriptions", "Expense", false, 12),
        ("Fees and charges", "Expense", false, 13),
        ("Travel", "Expense", false, 14),
        ("Other expenses", "Expense", false, 15),
        ("Salary", "Income", false, 16),
        ("Other income", "Income", false, 17),
        ("Transfers", "Transfer", false, 18),
        ("Excluded", "Excluded", false, 19),
    ];

    private const string AccountColumns = """
        a.id, a.display_name, a.institution, a.currency, a.is_archived, a.archived_at,
        (SELECT COUNT(*) FROM transactions t
            WHERE t.customer_id = a.customer_id AND t.financial_account_id = a.id) AS transaction_count,
        a.created_at, a.updated_at
        """;

    private const string CategoryColumns = """
        c.id, c.display_name, c.kind, c.is_system, c.is_default, c.is_archived, c.archived_at,
        c.sort_order,
        (SELECT COUNT(*) FROM transactions t
            WHERE t.customer_id = c.customer_id AND t.category_id = c.id) AS transaction_count,
        c.created_at, c.updated_at
        """;

    private const string MerchantTagColumns = """
        m.id, m.display_name, m.is_archived, m.archived_at,
        (SELECT COUNT(*) FROM transaction_merchant_tags j
            JOIN transactions t ON t.id = j.transaction_id
            WHERE j.merchant_tag_id = m.id AND t.customer_id = m.customer_id) AS transaction_count,
        m.created_at, m.updated_at
        """;

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

        foreach (var rune in input.EnumerateRunes())
        {
            if (Rune.IsControl(rune))
            {
                return false;
            }
        }

        var builder = new StringBuilder(input.Length);
        var pendingSpace = false;
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

        var collapsed = builder.ToString();
        if (collapsed.Length == 0 || CountRunes(collapsed) > maxRunes)
        {
            return false;
        }

        displayName = collapsed;
        normalizedName = collapsed.ToLowerInvariant();
        return true;
    }

    /// <summary>Null or whitespace means "no institution"; anything else is normalized.</summary>
    public static bool TryNormalizeInstitution(string? input,
                                               out string? institution,
                                               int maxRunes = MaxNameRunes)
    {
        institution = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        if (!TryNormalizeName(input, out var displayName, out _, maxRunes))
        {
            return false;
        }

        institution = displayName;
        return true;
    }

    public static bool IsUserCategoryKind(string? kind) => kind is "Expense" or "Income" or "Transfer" or "Excluded";

    private static int CountRunes(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            count++;
        }
        return count;
    }

    private static bool IsUniqueViolation(PostgresException exception) => exception.SqlState == PostgresErrorCodes.UniqueViolation;

    private static bool IsCheckViolation(PostgresException exception) => exception.SqlState == PostgresErrorCodes.CheckViolation;

    // --- Category seeding --------------------------------------------------

    /// <summary>
    /// Seeds the system bucket and the 19 default categories exactly once per
    /// customer. The customer row is locked so concurrent first-use requests
    /// serialize, and the under-lock re-check stops a second seeder from
    /// resurrecting defaults after the customer renamed or deleted them.
    /// </summary>
    public async Task EnsureCategoriesSeededAsync(Guid customerId,
                                                  CancellationToken cancellationToken = default)
    {
        await using (var fast = await db.OpenAsync(cancellationToken))
        {
            await using var check = fast.CreateCommand();
            check.CommandText = "SELECT categories_seeded_at FROM customers WHERE id = @customer_id";
            check.Parameters.AddWithValue("customer_id", customerId);
            var value = await check.ExecuteScalarAsync(cancellationToken);
            // No customer row: nothing to seed. Non-null value: already seeded.
            if (value is null || value is not DBNull)
            {
                return;
            }
        }

        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        bool alreadySeeded;
        await using (var lockRow = connection.CreateCommand())
        {
            lockRow.Transaction = transaction;
            lockRow.CommandText =
                "SELECT categories_seeded_at FROM customers WHERE id = @customer_id FOR UPDATE";
            lockRow.Parameters.AddWithValue("customer_id", customerId);
            var value = await lockRow.ExecuteScalarAsync(cancellationToken);
            if (value is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            alreadySeeded = value is not DBNull;
        }

        if (alreadySeeded)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        foreach (var (name, kind, isSystem, sortOrder) in DefaultCategories)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO categories (
                    id, customer_id, display_name, normalized_name, kind, is_system,
                    is_default, is_archived, archived_at, sort_order, created_at, updated_at
                )
                VALUES (
                    @id, @customer_id, @display_name, @normalized_name, @kind, @is_system,
                    TRUE, FALSE, NULL, @sort_order, NOW(), NOW()
                )
                ON CONFLICT (customer_id, normalized_name) DO NOTHING
                """;
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("customer_id", customerId);
            insert.Parameters.AddWithValue("display_name", name);
            insert.Parameters.AddWithValue("normalized_name", name.ToLowerInvariant());
            insert.Parameters.AddWithValue("kind", kind);
            insert.Parameters.AddWithValue("is_system", isSystem);
            insert.Parameters.AddWithValue("sort_order", sortOrder);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var markSeeded = connection.CreateCommand())
        {
            markSeeded.Transaction = transaction;
            markSeeded.CommandText =
                "UPDATE customers SET categories_seeded_at = NOW(), updated_at = NOW() WHERE id = @customer_id";
            markSeeded.Parameters.AddWithValue("customer_id", customerId);
            await markSeeded.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // --- Financial accounts ------------------------------------------------

    public async Task<IReadOnlyList<FinancialAccountRecord>> ListAccountsAsync(Guid customerId,
                                                                               CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {AccountColumns}
            FROM financial_accounts a
            WHERE a.customer_id = @customer_id
            ORDER BY a.is_archived, a.created_at, a.id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);

        var accounts = new List<FinancialAccountRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(MapAccount(reader));
        }
        return accounts;
    }

    public async Task<AccountOutcome> CreateAccountAsync(Guid customerId,
                                                         string? displayName,
                                                         string? institution,
                                                         CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeName(displayName, out var name, out _) ||
            !TryNormalizeInstitution(institution, out var normalizedInstitution))
        {
            return new AccountOutcome.InvalidName();
        }

        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO financial_accounts (
                id, customer_id, display_name, institution, currency, is_archived,
                archived_at, created_at, updated_at
            )
            VALUES (@id, @customer_id, @display_name, @institution, 'ZAR', FALSE, NULL, NOW(), NOW())
            RETURNING id
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("display_name", name);
        command.Parameters.AddWithValue("institution", NpgsqlDbType.Text, (object?)normalizedInstitution ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);

        var created = await FindAccountAsync(customerId, id, cancellationToken);
        return created is null ? new AccountOutcome.NotFound() : new AccountOutcome.Success(created);
    }

    /// <summary>
    /// Rename and optional institution update. When <paramref name="institutionProvided"/>
    /// is false the stored institution is preserved; when it is true, null or an
    /// empty value clears it.
    /// </summary>
    public async Task<AccountOutcome> UpdateAccountAsync(Guid customerId,
                                                         Guid id,
                                                         string? displayName,
                                                         bool institutionProvided,
                                                         string? institution,
                                                         CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeName(displayName, out var name, out _))
        {
            return new AccountOutcome.InvalidName();
        }

        string? normalizedInstitution = null;
        if (institutionProvided && !TryNormalizeInstitution(institution, out normalizedInstitution))
        {
            return new AccountOutcome.InvalidName();
        }

        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await LockAccountAsync(connection, transaction, customerId, id, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new AccountOutcome.NotFound();
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE financial_accounts
                SET display_name = @display_name,
                    institution = CASE WHEN @institution_provided THEN @institution ELSE institution END,
                    updated_at = NOW()
                WHERE customer_id = @customer_id AND id = @id
                """;
            update.Parameters.AddWithValue("display_name", name);
            update.Parameters.AddWithValue("institution_provided", institutionProvided);
            update.Parameters.AddWithValue("institution", NpgsqlDbType.Text, (object?)normalizedInstitution ?? DBNull.Value);
            update.Parameters.AddWithValue("customer_id", customerId);
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var updated = await FindAccountAsync(connection, transaction, customerId, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated is null ? new AccountOutcome.NotFound() : new AccountOutcome.Success(updated);
    }

    /// <summary>Archive or restore. Repeating the same transition is an idempotent success.</summary>
    public async Task<AccountOutcome> SetAccountArchivedAsync(Guid customerId,
                                                              Guid id,
                                                              bool archived,
                                                              CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await LockAccountAsync(connection, transaction, customerId, id, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new AccountOutcome.NotFound();
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE financial_accounts
                SET is_archived = @archived,
                    archived_at = CASE WHEN @archived THEN COALESCE(archived_at, NOW()) ELSE NULL END,
                    updated_at = NOW()
                WHERE customer_id = @customer_id AND id = @id
                """;
            update.Parameters.AddWithValue("archived", archived);
            update.Parameters.AddWithValue("customer_id", customerId);
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var updated = await FindAccountAsync(connection, transaction, customerId, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated is null ? new AccountOutcome.NotFound() : new AccountOutcome.Success(updated);
    }

    private async Task<FinancialAccountRecord?> FindAccountAsync(Guid customerId,
                                                                 Guid id,
                                                                 CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await FindAccountAsync(connection, null, customerId, id, cancellationToken);
    }

    private static async Task<FinancialAccountRecord?> FindAccountAsync(NpgsqlConnection connection,
                                                                        NpgsqlTransaction? transaction,
                                                                        Guid customerId,
                                                                        Guid id,
                                                                        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {AccountColumns}
            FROM financial_accounts a
            WHERE a.customer_id = @customer_id AND a.id = @id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapAccount(reader) : null;
    }

    private static async Task<bool> LockAccountAsync(NpgsqlConnection connection,
                                                     NpgsqlTransaction transaction,
                                                     Guid customerId,
                                                     Guid id,
                                                     CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM financial_accounts
            WHERE customer_id = @customer_id AND id = @id
            FOR UPDATE
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    // --- Categories --------------------------------------------------------

    public async Task<IReadOnlyList<CategoryRecord>> ListCategoriesAsync(Guid customerId,
                                                                         CancellationToken cancellationToken = default)
    {
        await EnsureCategoriesSeededAsync(customerId, cancellationToken);

        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {CategoryColumns}
            FROM categories c
            WHERE c.customer_id = @customer_id
            ORDER BY c.is_archived, c.sort_order, c.display_name, c.id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);

        var categories = new List<CategoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            categories.Add(MapCategory(reader));
        }
        return categories;
    }

    public async Task<CategoryOutcome> CreateCategoryAsync(Guid customerId,
                                                           string? displayName,
                                                           string? kind,
                                                           CancellationToken cancellationToken = default)
    {
        await EnsureCategoriesSeededAsync(customerId, cancellationToken);

        if (!TryNormalizeName(displayName, out var name, out var normalizedName))
        {
            return new CategoryOutcome.InvalidName();
        }
        if (!IsUserCategoryKind(kind))
        {
            return new CategoryOutcome.InvalidKind();
        }

        var id = Guid.NewGuid();
        try
        {
            await using var connection = await db.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO categories (
                    id, customer_id, display_name, normalized_name, kind, is_system,
                    is_default, is_archived, archived_at, sort_order, created_at, updated_at
                )
                VALUES (
                    @id, @customer_id, @display_name, @normalized_name, @kind, FALSE,
                    FALSE, FALSE, NULL, 100, NOW(), NOW()
                )
                """;
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("customer_id", customerId);
            command.Parameters.AddWithValue("display_name", name);
            command.Parameters.AddWithValue("normalized_name", normalizedName);
            command.Parameters.AddWithValue("kind", kind!);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsUniqueViolation(exception))
        {
            return new CategoryOutcome.DuplicateName();
        }
        catch (PostgresException exception) when (IsCheckViolation(exception))
        {
            return new CategoryOutcome.InvalidKind();
        }

        var created = await FindCategoryAsync(customerId, id, cancellationToken);
        return created is null ? new CategoryOutcome.NotFound() : new CategoryOutcome.Success(created);
    }

    public async Task<CategoryOutcome> UpdateCategoryAsync(Guid customerId,
                                                           Guid id,
                                                           string? displayName,
                                                           string? kind,
                                                           bool confirmKindChange,
                                                           CancellationToken cancellationToken = default)
    {
        await EnsureCategoriesSeededAsync(customerId, cancellationToken);

        if (!TryNormalizeName(displayName, out var name, out var normalizedName))
        {
            return new CategoryOutcome.InvalidName();
        }
        if (!IsUserCategoryKind(kind))
        {
            return new CategoryOutcome.InvalidKind();
        }

        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var state = await LockCategoryAsync(connection, transaction, customerId, id, cancellationToken);
        if (state is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CategoryOutcome.NotFound();
        }
        if (state.IsSystem)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CategoryOutcome.SystemCategoryImmutable();
        }
        if (!string.Equals(state.Kind, kind, StringComparison.Ordinal) && !confirmKindChange)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CategoryOutcome.KindChangeConfirmationRequired();
        }

        try
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE categories
                SET display_name = @display_name,
                    normalized_name = @normalized_name,
                    kind = @kind,
                    updated_at = NOW()
                WHERE customer_id = @customer_id AND id = @id
                """;
            update.Parameters.AddWithValue("display_name", name);
            update.Parameters.AddWithValue("normalized_name", normalizedName);
            update.Parameters.AddWithValue("kind", kind!);
            update.Parameters.AddWithValue("customer_id", customerId);
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CategoryOutcome.DuplicateName();
        }
        catch (PostgresException exception) when (IsCheckViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CategoryOutcome.InvalidKind();
        }

        var updated = await FindCategoryAsync(connection, transaction, customerId, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated is null ? new CategoryOutcome.NotFound() : new CategoryOutcome.Success(updated);
    }

    /// <summary>Archive or restore. Repeating the same transition is an idempotent success.</summary>
    public async Task<CategoryOutcome> SetCategoryArchivedAsync(Guid customerId,
                                                                Guid id,
                                                                bool archived,
                                                                CancellationToken cancellationToken = default)
    {
        await EnsureCategoriesSeededAsync(customerId, cancellationToken);

        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var state = await LockCategoryAsync(connection, transaction, customerId, id, cancellationToken);
        if (state is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CategoryOutcome.NotFound();
        }
        if (state.IsSystem)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CategoryOutcome.SystemCategoryImmutable();
        }

        try
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE categories
                SET is_archived = @archived,
                    archived_at = CASE WHEN @archived THEN COALESCE(archived_at, NOW()) ELSE NULL END,
                    updated_at = NOW()
                WHERE customer_id = @customer_id AND id = @id
                """;
            update.Parameters.AddWithValue("archived", archived);
            update.Parameters.AddWithValue("customer_id", customerId);
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsCheckViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CategoryOutcome.SystemCategoryImmutable();
        }

        var updated = await FindCategoryAsync(connection, transaction, customerId, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated is null ? new CategoryOutcome.NotFound() : new CategoryOutcome.Success(updated);
    }

    /// <summary>
    /// Deletes a non-system category and atomically reassigns its transactions
    /// to the customer's system <c>Uncategorised</c> bucket. No transaction row
    /// is ever deleted.
    /// </summary>
    public async Task<CategoryDeleteOutcome> DeleteCategoryAsync(Guid customerId,
                                                                 Guid id,
                                                                 CancellationToken cancellationToken = default)
    {
        await EnsureCategoriesSeededAsync(customerId, cancellationToken);

        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var state = await LockCategoryAsync(connection, transaction, customerId, id, cancellationToken);
        if (state is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CategoryDeleteOutcome.NotFound();
        }
        if (state.IsSystem)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CategoryDeleteOutcome.SystemCategoryImmutable();
        }

        var uncategorisedId = await FindUncategorisedIdAsync(connection, transaction, customerId, cancellationToken);
        if (uncategorisedId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CategoryDeleteOutcome.NotFound();
        }

        // Lock every affected transaction in ascending UUID order before the
        // broad reassignment. The category row is already locked, so the
        // workspace write order (classification before transactions) and this
        // deletion order cannot deadlock.
        await using (var lockRows = connection.CreateCommand())
        {
            lockRows.Transaction = transaction;
            lockRows.CommandText = """
                SELECT id
                FROM transactions
                WHERE customer_id = @customer_id AND category_id = @category_id
                ORDER BY id
                FOR UPDATE
                """;
            lockRows.Parameters.AddWithValue("customer_id", customerId);
            lockRows.Parameters.AddWithValue("category_id", id);
            await using var reader = await lockRows.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
            }
        }

        long reassigned;
        await using (var reassign = connection.CreateCommand())
        {
            reassign.Transaction = transaction;
            reassign.CommandText = """
                UPDATE transactions
                SET category_id = @uncategorised_id,
                    version = version + 1,
                    updated_at = NOW()
                WHERE customer_id = @customer_id AND category_id = @id
                """;
            reassign.Parameters.AddWithValue("uncategorised_id", uncategorisedId.Value);
            reassign.Parameters.AddWithValue("customer_id", customerId);
            reassign.Parameters.AddWithValue("id", id);
            reassigned = await reassign.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM categories WHERE customer_id = @customer_id AND id = @id";
            delete.Parameters.AddWithValue("customer_id", customerId);
            delete.Parameters.AddWithValue("id", id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new CategoryDeleteOutcome.Success(reassigned, uncategorisedId.Value);
    }

    private sealed record CategoryState(Guid Id,
                                        string Kind,
                                        bool IsSystem,
                                        bool IsArchived,
                                        DateTime? ArchivedAt);

    private static async Task<CategoryState?> LockCategoryAsync(NpgsqlConnection connection,
                                                                NpgsqlTransaction transaction,
                                                                Guid customerId,
                                                                Guid id,
                                                                CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, kind, is_system, is_archived, archived_at
            FROM categories
            WHERE customer_id = @customer_id AND id = @id
            FOR UPDATE
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new CategoryState(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTime>(4));
    }

    private static async Task<Guid?> FindUncategorisedIdAsync(NpgsqlConnection connection,
                                                              NpgsqlTransaction transaction,
                                                              Guid customerId,
                                                              CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT id FROM categories WHERE customer_id = @customer_id AND is_system = TRUE";
        command.Parameters.AddWithValue("customer_id", customerId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
    }

    private async Task<CategoryRecord?> FindCategoryAsync(Guid customerId,
                                                          Guid id,
                                                          CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await FindCategoryAsync(connection, null, customerId, id, cancellationToken);
    }

    private static async Task<CategoryRecord?> FindCategoryAsync(NpgsqlConnection connection,
                                                                 NpgsqlTransaction? transaction,
                                                                 Guid customerId,
                                                                 Guid id,
                                                                 CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {CategoryColumns}
            FROM categories c
            WHERE c.customer_id = @customer_id AND c.id = @id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapCategory(reader) : null;
    }

    // --- Merchant tags -----------------------------------------------------

    public async Task<IReadOnlyList<MerchantTagRecord>> ListMerchantTagsAsync(Guid customerId,
                                                                              CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {MerchantTagColumns}
            FROM merchant_tags m
            WHERE m.customer_id = @customer_id
            ORDER BY m.is_archived, m.display_name, m.id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);

        var tags = new List<MerchantTagRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tags.Add(MapMerchantTag(reader));
        }
        return tags;
    }

    public async Task<MerchantTagOutcome> CreateMerchantTagAsync(Guid customerId,
                                                                 string? displayName,
                                                                 CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeName(displayName, out var name, out var normalizedName))
        {
            return new MerchantTagOutcome.InvalidName();
        }

        var id = Guid.NewGuid();
        try
        {
            await using var connection = await db.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO merchant_tags (
                    id, customer_id, display_name, normalized_name, is_archived,
                    archived_at, created_at, updated_at
                )
                VALUES (@id, @customer_id, @display_name, @normalized_name, FALSE, NULL, NOW(), NOW())
                """;
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("customer_id", customerId);
            command.Parameters.AddWithValue("display_name", name);
            command.Parameters.AddWithValue("normalized_name", normalizedName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsUniqueViolation(exception))
        {
            return new MerchantTagOutcome.DuplicateName();
        }

        var created = await FindMerchantTagAsync(customerId, id, cancellationToken);
        return created is null
            ? new MerchantTagOutcome.NotFound()
            : new MerchantTagOutcome.Success(created);
    }

    public async Task<MerchantTagOutcome> UpdateMerchantTagAsync(Guid customerId,
                                                                 Guid id,
                                                                 string? displayName,
                                                                 CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeName(displayName, out var name, out var normalizedName))
        {
            return new MerchantTagOutcome.InvalidName();
        }

        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await LockMerchantTagAsync(connection, transaction, customerId, id, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new MerchantTagOutcome.NotFound();
        }

        try
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE merchant_tags
                SET display_name = @display_name,
                    normalized_name = @normalized_name,
                    updated_at = NOW()
                WHERE customer_id = @customer_id AND id = @id
                """;
            update.Parameters.AddWithValue("display_name", name);
            update.Parameters.AddWithValue("normalized_name", normalizedName);
            update.Parameters.AddWithValue("customer_id", customerId);
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new MerchantTagOutcome.DuplicateName();
        }

        var updated = await FindMerchantTagAsync(connection, transaction, customerId, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated is null
            ? new MerchantTagOutcome.NotFound()
            : new MerchantTagOutcome.Success(updated);
    }

    /// <summary>Archive or restore. Repeating the same transition is an idempotent success.</summary>
    public async Task<MerchantTagOutcome> SetMerchantTagArchivedAsync(Guid customerId,
                                                                      Guid id,
                                                                      bool archived,
                                                                      CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await LockMerchantTagAsync(connection, transaction, customerId, id, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new MerchantTagOutcome.NotFound();
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE merchant_tags
                SET is_archived = @archived,
                    archived_at = CASE WHEN @archived THEN COALESCE(archived_at, NOW()) ELSE NULL END,
                    updated_at = NOW()
                WHERE customer_id = @customer_id AND id = @id
                """;
            update.Parameters.AddWithValue("archived", archived);
            update.Parameters.AddWithValue("customer_id", customerId);
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var updated = await FindMerchantTagAsync(connection, transaction, customerId, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated is null
            ? new MerchantTagOutcome.NotFound()
            : new MerchantTagOutcome.Success(updated);
    }

    /// <summary>
    /// Deletes a merchant tag and atomically removes its transaction
    /// assignments. Transactions themselves are preserved.
    /// </summary>
    public async Task<MerchantTagDeleteOutcome> DeleteMerchantTagAsync(Guid customerId,
                                                                       Guid id,
                                                                       CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await LockMerchantTagAsync(connection, transaction, customerId, id, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new MerchantTagDeleteOutcome.NotFound();
        }

        // Lock the tagged transactions in ascending UUID order before the
        // version touch and link removal. The tag row is already locked; no
        // account lock is added, preserving the established entity order.
        await using (var lockRows = connection.CreateCommand())
        {
            lockRows.Transaction = transaction;
            lockRows.CommandText = """
                SELECT t.id
                FROM transactions t
                JOIN transaction_merchant_tags j
                  ON j.customer_id = t.customer_id AND j.transaction_id = t.id
                WHERE t.customer_id = @customer_id AND j.merchant_tag_id = @tag_id
                ORDER BY t.id
                FOR UPDATE OF t
                """;
            lockRows.Parameters.AddWithValue("customer_id", customerId);
            lockRows.Parameters.AddWithValue("tag_id", id);
            await using var reader = await lockRows.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
            }
        }

        long unassigned;
        await using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = """
                SELECT COUNT(*)
                FROM transaction_merchant_tags j
                JOIN transactions t ON t.id = j.transaction_id
                WHERE j.merchant_tag_id = @tag_id AND t.customer_id = @customer_id
                """;
            count.Parameters.AddWithValue("tag_id", id);
            count.Parameters.AddWithValue("customer_id", customerId);
            unassigned = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        await using (var touch = connection.CreateCommand())
        {
            touch.Transaction = transaction;
            touch.CommandText = """
                UPDATE transactions t
                SET version = version + 1, updated_at = NOW()
                WHERE t.customer_id = @customer_id
                  AND EXISTS (
                      SELECT 1 FROM transaction_merchant_tags j
                      WHERE j.transaction_id = t.id AND j.merchant_tag_id = @tag_id
                  )
                """;
            touch.Parameters.AddWithValue("customer_id", customerId);
            touch.Parameters.AddWithValue("tag_id", id);
            await touch.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var unassign = connection.CreateCommand())
        {
            unassign.Transaction = transaction;
            unassign.CommandText = "DELETE FROM transaction_merchant_tags WHERE merchant_tag_id = @tag_id";
            unassign.Parameters.AddWithValue("tag_id", id);
            await unassign.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM merchant_tags WHERE customer_id = @customer_id AND id = @id";
            delete.Parameters.AddWithValue("customer_id", customerId);
            delete.Parameters.AddWithValue("id", id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new MerchantTagDeleteOutcome.Success(unassigned);
    }

    private async Task<MerchantTagRecord?> FindMerchantTagAsync(Guid customerId,
                                                                Guid id,
                                                                CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await FindMerchantTagAsync(connection, null, customerId, id, cancellationToken);
    }

    private static async Task<MerchantTagRecord?> FindMerchantTagAsync(NpgsqlConnection connection,
                                                                       NpgsqlTransaction? transaction,
                                                                       Guid customerId,
                                                                       Guid id,
                                                                       CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {MerchantTagColumns}
            FROM merchant_tags m
            WHERE m.customer_id = @customer_id AND m.id = @id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapMerchantTag(reader) : null;
    }

    private static async Task<bool> LockMerchantTagAsync(NpgsqlConnection connection,
                                                         NpgsqlTransaction transaction,
                                                         Guid customerId,
                                                         Guid id,
                                                         CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM merchant_tags
            WHERE customer_id = @customer_id AND id = @id
            FOR UPDATE
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    // --- Mapping -----------------------------------------------------------

    private static FinancialAccountRecord MapAccount(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetString(3),
        reader.GetBoolean(4),
        reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTime>(5),
        reader.GetInt64(6),
        reader.GetFieldValue<DateTime>(7),
        reader.GetFieldValue<DateTime>(8));

    private static CategoryRecord MapCategory(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetBoolean(3),
        reader.GetBoolean(4),
        reader.GetBoolean(5),
        reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTime>(6),
        reader.GetInt32(7),
        reader.GetInt64(8),
        reader.GetFieldValue<DateTime>(9),
        reader.GetFieldValue<DateTime>(10));

    private static MerchantTagRecord MapMerchantTag(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetBoolean(2),
        reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTime>(3),
        reader.GetInt64(4),
        reader.GetFieldValue<DateTime>(5),
        reader.GetFieldValue<DateTime>(6));
}
