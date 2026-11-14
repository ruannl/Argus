using argus.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace argus.Server.Data;

/// <summary>
/// The one source of default category names and the seed mechanics shared by
/// offline seeding and request-time lazy initialization. The caller owns the
/// transaction and, for request paths, the already-admitted customer lock.
/// </summary>
public static class CategoryDefaults
{
    public static readonly (string Name, string Kind, bool IsSystem, int SortOrder)[] All =
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

    /// <summary>
    /// Seeds the system bucket and the 19 default categories exactly once per
    /// customer. When <paramref name="customerLocked"/> is false this method
    /// takes the customer row lock itself; request scopes enter with the
    /// exclusive lock already held.
    /// </summary>
    public static async Task EnsureSeededAsync(ArgusDbContext context,
                                               Guid customerId,
                                               bool customerLocked,
                                               CancellationToken cancellationToken = default)
    {
        if (!customerLocked)
        {
            List<Guid> locked = await context.Database.SqlQuery<Guid>($"""
                SELECT id FROM customers WHERE id = {customerId} FOR UPDATE
                """).ToListAsync(cancellationToken);
            if (locked.Count == 0)
            {
                return;
            }
        }

        Customer? customer = await context.Customers.SingleOrDefaultAsync(item => item.Id == customerId, cancellationToken);
        if (customer is null || customer.CategoriesSeededAt is not null)
        {
            return;
        }

        foreach ((string name, string kind, bool isSystem, int sortOrder) in All)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO categories (
                    id, customer_id, display_name, normalized_name, kind, is_system,
                    is_default, is_archived, archived_at, sort_order, created_at, updated_at
                )
                VALUES (
                    {Guid.NewGuid()}, {customerId}, {name}, {name.ToLowerInvariant()}, {kind}, {isSystem},
                    TRUE, FALSE, NULL, {sortOrder}, NOW(), NOW()
                )
                ON CONFLICT (customer_id, normalized_name) DO NOTHING
                """, cancellationToken);
        }

        customer.CategoriesSeededAt = DateTime.UtcNow;
        customer.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }
}
