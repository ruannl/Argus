using argus.Server.Finance;
using Microsoft.EntityFrameworkCore;

namespace argus.Server.Data;

/// <summary>
/// Loads suggestion inputs for a customer: the tags and categories they chose
/// for the same generic merchant key, plus their explicit alias rules.
/// Read-only and bounded by the supplied keys and the alias limit.
/// </summary>
public static class SuggestionLoader
{
    public static async Task<SuggestionData> LoadAsync(ArgusDbContext context,
                                                       Guid customerId,
                                                       IReadOnlyCollection<string> merchantKeys,
                                                       CancellationToken cancellationToken)
    {
        var tagCounts = new Dictionary<string, Dictionary<Guid, int>>(StringComparer.Ordinal);
        var categoryCounts = new Dictionary<string, Dictionary<Guid, (string Name, int Count)>>(StringComparer.Ordinal);

        string[] keys = merchantKeys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length > 0)
        {
            var tagRows = await (
                from transaction in context.Transactions
                join link in context.TransactionMerchantTags
                    on new { transaction.CustomerId, Id = transaction.Id } equals new { link.CustomerId, Id = link.TransactionId }
                join tag in context.MerchantTags
                    on new { link.CustomerId, Id = link.MerchantTagId } equals new { tag.CustomerId, tag.Id }
                where transaction.CustomerId == customerId &&
                      keys.Contains(transaction.MerchantKey!) &&
                      !tag.IsArchived
                group link by new { transaction.MerchantKey, link.MerchantTagId } into grouped
                select new
                {
                    grouped.Key.MerchantKey,
                    grouped.Key.MerchantTagId,
                    Count = grouped.Count(),
                }).ToListAsync(cancellationToken);
            foreach (var row in tagRows)
            {
                string key = row.MerchantKey!;
                if (!tagCounts.TryGetValue(key, out Dictionary<Guid, int>? counts))
                {
                    counts = [];
                    tagCounts[key] = counts;
                }
                counts[row.MerchantTagId] = row.Count;
            }

            var categoryRows = await (
                from transaction in context.Transactions
                join category in context.Categories
                    on new { transaction.CustomerId, Id = transaction.CategoryId } equals new { category.CustomerId, category.Id }
                where transaction.CustomerId == customerId &&
                      keys.Contains(transaction.MerchantKey!) &&
                      transaction.CategoryConfirmed &&
                      category.Kind != "Uncategorised" &&
                      !category.IsArchived
                group transaction by new { transaction.MerchantKey, transaction.CategoryId, category.DisplayName } into grouped
                select new
                {
                    grouped.Key.MerchantKey,
                    grouped.Key.CategoryId,
                    grouped.Key.DisplayName,
                    Count = grouped.Count(),
                }).ToListAsync(cancellationToken);
            foreach (var row in categoryRows)
            {
                string key = row.MerchantKey!;
                if (!categoryCounts.TryGetValue(key, out Dictionary<Guid, (string Name, int Count)>? counts))
                {
                    counts = [];
                    categoryCounts[key] = counts;
                }
                counts[row.CategoryId] = (row.DisplayName, row.Count);
            }
        }

        var history = new Dictionary<string, HistoryRule>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            var tags = tagCounts.TryGetValue(key, out Dictionary<Guid, int>? counts)
                ? new Dictionary<Guid, int>(counts)
                : new Dictionary<Guid, int>();
            Guid? category = null;
            if (categoryCounts.TryGetValue(key, out Dictionary<Guid, (string Name, int Count)>? categories))
            {
                category = categories
                    .OrderByDescending(entry => entry.Value.Count)
                    .ThenBy(entry => entry.Value.Name, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Key)
                    .Select(entry => (Guid?)entry.Key)
                    .FirstOrDefault();
            }
            history[key] = new HistoryRule(tags, category);
        }

        return new SuggestionData(history, await LoadAliasesAsync(context, customerId, cancellationToken));
    }


    private static async Task<List<AliasRule>> LoadAliasesAsync(ArgusDbContext context,
                                                                Guid customerId,
                                                                CancellationToken cancellationToken)
    {
        var aliases = await (
            from alias in context.MerchantAliases.AsNoTracking()
            join tag in context.MerchantTags
                on new { alias.CustomerId, Id = alias.MerchantTagId } equals new { tag.CustomerId, tag.Id }
            join category in context.Categories
                on new { alias.CustomerId, Id = alias.CategoryId } equals new { category.CustomerId, Id = (Guid?)category.Id } into categories
            from category in categories.DefaultIfEmpty()
            where alias.CustomerId == customerId && !tag.IsArchived
            orderby alias.NormalizedAlias, alias.Id
            select new
            {
                alias.Id,
                alias.NormalizedAlias,
                alias.MerchantTagId,
                CategoryId = category == null || category.IsArchived ? null : alias.CategoryId,
            })
            .Take(FinanceStore.MaxMerchantAliases)
            .ToListAsync(cancellationToken);

        return aliases.Select(alias => new AliasRule(
            alias.Id,
            alias.NormalizedAlias.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            alias.MerchantTagId,
            alias.CategoryId)).ToList();
    }
}
