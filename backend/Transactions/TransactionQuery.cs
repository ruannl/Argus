using System.Text;
using argus.Server.Data;
using argus.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace argus.Server.Transactions;

/// <summary>
/// One typed owned predicate builder shared by the transaction list, the
/// matching counts and the frozen all-matching preparation. Tags use EXISTS so
/// multi-tag rows never multiply counts.
/// </summary>
public static class TransactionQuery
{
    public static IQueryable<Transaction> Apply(ArgusDbContext context,
                                                Guid customerId,
                                                TransactionFilter filter,
                                                bool includeVisibility)
    {
        IQueryable<Transaction> query = context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.CustomerId == customerId);

        if (filter.Search is not null)
        {
            string pattern = "%" + EscapeLike(filter.Search) + "%";
            query = query.Where(transaction =>
                EF.Functions.ILike(transaction.Description, pattern, "\\") ||
                context.TransactionMerchantTags.Any(link =>
                    link.CustomerId == transaction.CustomerId &&
                    link.TransactionId == transaction.Id &&
                    context.MerchantTags.Any(tag =>
                        tag.CustomerId == link.CustomerId &&
                        tag.Id == link.MerchantTagId &&
                        EF.Functions.ILike(tag.DisplayName, pattern, "\\"))));
        }

        if (filter.StartDate is not null)
        {
            query = query.Where(transaction => transaction.TransactionDate >= filter.StartDate.Value);
        }
        if (filter.EndDate is not null)
        {
            query = query.Where(transaction => transaction.TransactionDate <= filter.EndDate.Value);
        }
        if (filter.FinancialAccountId is not null)
        {
            query = query.Where(transaction => transaction.FinancialAccountId == filter.FinancialAccountId.Value);
        }
        if (filter.StatementImportId is not null)
        {
            query = query.Where(transaction => transaction.StatementImportId == filter.StatementImportId.Value);
        }
        if (filter.CategoryId is not null)
        {
            query = query.Where(transaction => transaction.CategoryId == filter.CategoryId.Value);
        }
        if (filter.MerchantTagId is not null)
        {
            query = query.Where(transaction =>
                context.TransactionMerchantTags.Any(link =>
                    link.CustomerId == transaction.CustomerId &&
                    link.TransactionId == transaction.Id &&
                    link.MerchantTagId == filter.MerchantTagId.Value));
        }
        if (filter.Untagged)
        {
            query = query.Where(transaction =>
                !context.TransactionMerchantTags.Any(link =>
                    link.CustomerId == transaction.CustomerId &&
                    link.TransactionId == transaction.Id));
        }
        if (filter.CategoryConfirmed is not null)
        {
            bool confirmed = filter.CategoryConfirmed.Value;
            query = query.Where(transaction => transaction.CategoryConfirmed == confirmed);
        }
        if (filter.CategoryKind is not null)
        {
            query = query.Where(transaction =>
                context.Categories.Any(category =>
                    category.CustomerId == transaction.CustomerId &&
                    category.Id == transaction.CategoryId &&
                    category.Kind == filter.CategoryKind));
        }
        if (filter.Direction is not null)
        {
            query = filter.Direction switch
            {
                "in" => query.Where(transaction => transaction.Amount > 0),
                "out" => query.Where(transaction => transaction.Amount < 0),
                "zero" => query.Where(transaction => transaction.Amount == 0),
                _ => query,
            };
        }

        if (includeVisibility)
        {
            query = filter.Visibility switch
            {
                TransactionVisibility.Visible => query.Where(transaction =>
                    !(context.StatementImports.Any(statement =>
                          statement.CustomerId == transaction.CustomerId &&
                          statement.Id == transaction.StatementImportId &&
                          statement.IsHidden) ||
                      transaction.IsHiddenByUser)),
                TransactionVisibility.Hidden => query.Where(transaction =>
                    context.StatementImports.Any(statement =>
                        statement.CustomerId == transaction.CustomerId &&
                        statement.Id == transaction.StatementImportId &&
                        statement.IsHidden) ||
                    transaction.IsHiddenByUser),
                _ => query,
            };
        }

        return query;
    }

    /// <summary>
    /// Computes both visibility counts in one aggregate scan, matching the
    /// former FILTER query instead of issuing one statement per count.
    /// </summary>
    public static async Task<(long Visible, long Hidden)> CountsAsync(ArgusDbContext context,
                                                                       Guid customerId,
                                                                       TransactionFilter filter,
                                                                       CancellationToken cancellationToken)
    {
        var counts = await Apply(context, customerId, filter, includeVisibility: false)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Visible = group.LongCount(transaction =>
                    !(context.StatementImports.Any(statement =>
                          statement.CustomerId == transaction.CustomerId &&
                          statement.Id == transaction.StatementImportId &&
                          statement.IsHidden) ||
                      transaction.IsHiddenByUser)),
                Hidden = group.LongCount(transaction =>
                    context.StatementImports.Any(statement =>
                        statement.CustomerId == transaction.CustomerId &&
                        statement.Id == transaction.StatementImportId &&
                        statement.IsHidden) ||
                    transaction.IsHiddenByUser),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return counts is null ? (0L, 0L) : (counts.Visible, counts.Hidden);
    }

    public static IQueryable<Transaction> Order(IQueryable<Transaction> query, string sortBy, string sortDirection)
    {
        bool ascending = sortDirection == "asc";
        return sortBy switch
        {
            "amount" => ascending
                ? query.OrderBy(transaction => transaction.Amount).ThenBy(transaction => transaction.Id)
                : query.OrderByDescending(transaction => transaction.Amount).ThenByDescending(transaction => transaction.Id),
            "description" => ascending
                ? query.OrderBy(transaction => transaction.Description).ThenBy(transaction => transaction.Id)
                : query.OrderByDescending(transaction => transaction.Description).ThenByDescending(transaction => transaction.Id),
            _ => ascending
                ? query.OrderBy(transaction => transaction.TransactionDate).ThenBy(transaction => transaction.Id)
                : query.OrderByDescending(transaction => transaction.TransactionDate).ThenByDescending(transaction => transaction.Id),
        };
    }

    /// <summary>Escapes the three ILIKE metacharacters; the whole string stays one literal substring.</summary>
    public static string EscapeLike(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (char character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '%':
                    builder.Append("\\%");
                    break;
                case '_':
                    builder.Append("\\_");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }
}
