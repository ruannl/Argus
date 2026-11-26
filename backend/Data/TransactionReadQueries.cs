using argus.Server.Data.Entities;
using argus.Server.Transactions;
using Microsoft.EntityFrameworkCore;

namespace argus.Server.Data;

/// <summary>
/// The one owned transaction projection and filter-ownership gate shared by
/// the transaction list and the reporting snapshot. Reports never redefine
/// visibility, category or tag predicates in a second place.
/// </summary>
public static class TransactionReadQueries
{
    public sealed record TransactionRowData(TransactionResponse Response,
                                            string NormalizedDescription,
                                            Guid[] TagIds,
                                            int? SourcePage,
                                            int SourceRowNumber);

    public static async Task ValidateFilterOwnershipAsync(ArgusDbContext context,
                                                          Guid customerId,
                                                          TransactionFilter filter,
                                                          CancellationToken cancellationToken)
    {
        if (filter.FinancialAccountId is not null)
        {
            await RequireOwnedAsync(context, customerId, "financial_accounts", filter.FinancialAccountId.Value, cancellationToken);
        }
        if (filter.StatementImportId is not null)
        {
            await RequireOwnedAsync(context, customerId, "statement_imports", filter.StatementImportId.Value, cancellationToken);
        }
        if (filter.CategoryId is not null)
        {
            await RequireOwnedAsync(context, customerId, "categories", filter.CategoryId.Value, cancellationToken);
        }
        if (filter.MerchantTagId is not null)
        {
            await RequireOwnedAsync(context, customerId, "merchant_tags", filter.MerchantTagId.Value, cancellationToken);
        }
        if (filter.NormalizedExcludedCategoryIds.Length > 0)
        {
            Guid[] excluded = filter.NormalizedExcludedCategoryIds;
            int owned = await context.Categories.CountAsync(
                category => category.CustomerId == customerId && excluded.Contains(category.Id),
                cancellationToken);
            if (owned != excluded.Length)
            {
                throw new TransactionStoreException(TransactionErrorCodes.NotFound);
            }
        }
    }

    public static async Task RequireOwnedAsync(ArgusDbContext context,
                                               Guid customerId,
                                               string table,
                                               Guid id,
                                               CancellationToken cancellationToken)
    {
        bool owned = table switch
        {
            "financial_accounts" => await context.FinancialAccounts.AnyAsync(item => item.CustomerId == customerId && item.Id == id, cancellationToken),
            "statement_imports" => await context.StatementImports.AnyAsync(item => item.CustomerId == customerId && item.Id == id, cancellationToken),
            "categories" => await context.Categories.AnyAsync(item => item.CustomerId == customerId && item.Id == id, cancellationToken),
            "merchant_tags" => await context.MerchantTags.AnyAsync(item => item.CustomerId == customerId && item.Id == id, cancellationToken),
            "salary_cycle_starts" => await context.SalaryCycleStarts.AnyAsync(item => item.CustomerId == customerId && item.Id == id, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(table), "unsupported owned table"),
        };
        if (!owned)
        {
            throw new TransactionStoreException(TransactionErrorCodes.NotFound);
        }
    }

    public static async Task<List<TransactionRowData>> ReadRowsAsync(ArgusDbContext context,
                                                                     Guid customerId,
                                                                     TransactionFilter filter,
                                                                     string sortBy,
                                                                     string sortDirection,
                                                                     int limit,
                                                                     long offset,
                                                                     bool includeVisibility,
                                                                     CancellationToken cancellationToken)
    {
        IQueryable<Transaction> ordered = TransactionQuery.Order(
            TransactionQuery.Apply(context, customerId, filter, includeVisibility),
            sortBy,
            sortDirection);
        List<RawRow> rawRows = await (
            from transaction in ordered
            join account in context.FinancialAccounts
                on new { transaction.CustomerId, Id = transaction.FinancialAccountId } equals new { account.CustomerId, Id = account.Id }
            join category in context.Categories
                on new { transaction.CustomerId, Id = transaction.CategoryId } equals new { category.CustomerId, Id = category.Id }
            join statement in context.StatementImports
                on new { transaction.CustomerId, Id = transaction.StatementImportId } equals new { statement.CustomerId, Id = statement.Id }
            select new RawRow(
                transaction.Id,
                transaction.Version,
                transaction.TransactionDate,
                transaction.Description,
                transaction.NormalizedDescription,
                transaction.Amount,
                account.Id,
                account.DisplayName,
                account.Institution,
                account.IsArchived,
                category.Id,
                category.DisplayName,
                category.Kind,
                category.IsSystem,
                category.IsArchived,
                transaction.CategoryConfirmed,
                statement.Id,
                statement.StatementLabel,
                statement.SourceType,
                statement.IsHidden,
                transaction.IsHiddenByUser,
                transaction.SourcePage,
                transaction.SourceRowNumber))
            .Skip((int)offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var tags = await LoadAssignedTagsAsync(context, customerId, rawRows.Select(row => row.Id).ToArray(), cancellationToken);

        return rawRows.Select(raw => MapRow(raw, tags)).ToList();
    }

    public static async Task<List<TransactionRowData>> ReadRowsByIdsAsync(ArgusDbContext context,
                                                                          Guid customerId,
                                                                          Guid[] ids,
                                                                          CancellationToken cancellationToken)
    {
        List<RawRow> rawRows = await (
            from transaction in context.Transactions.AsNoTracking()
            join account in context.FinancialAccounts
                on new { transaction.CustomerId, Id = transaction.FinancialAccountId } equals new { account.CustomerId, Id = account.Id }
            join category in context.Categories
                on new { transaction.CustomerId, Id = transaction.CategoryId } equals new { category.CustomerId, Id = category.Id }
            join statement in context.StatementImports
                on new { transaction.CustomerId, Id = transaction.StatementImportId } equals new { statement.CustomerId, Id = statement.Id }
            where transaction.CustomerId == customerId && ids.Contains(transaction.Id)
            orderby transaction.Id
            select new RawRow(
                transaction.Id,
                transaction.Version,
                transaction.TransactionDate,
                transaction.Description,
                transaction.NormalizedDescription,
                transaction.Amount,
                account.Id,
                account.DisplayName,
                account.Institution,
                account.IsArchived,
                category.Id,
                category.DisplayName,
                category.Kind,
                category.IsSystem,
                category.IsArchived,
                transaction.CategoryConfirmed,
                statement.Id,
                statement.StatementLabel,
                statement.SourceType,
                statement.IsHidden,
                transaction.IsHiddenByUser,
                transaction.SourcePage,
                transaction.SourceRowNumber))
            .ToListAsync(cancellationToken);

        var tags = await LoadAssignedTagsAsync(context, customerId, rawRows.Select(row => row.Id).ToArray(), cancellationToken);

        return rawRows.Select(raw => MapRow(raw, tags)).ToList();
    }

    private sealed record RawRow(Guid Id,
                                 int Version,
                                 DateOnly TransactionDate,
                                 string Description,
                                 string NormalizedDescription,
                                 decimal Amount,
                                 Guid AccountId,
                                 string AccountName,
                                 string? AccountInstitution,
                                 bool AccountArchived,
                                 Guid CategoryId,
                                 string CategoryName,
                                 string CategoryKind,
                                 bool CategorySystem,
                                 bool CategoryArchived,
                                 bool CategoryConfirmed,
                                 Guid StatementId,
                                 string StatementLabel,
                                 string StatementSourceType,
                                 bool StatementHidden,
                                 bool HiddenByUser,
                                 int? SourcePage,
                                 int SourceRowNumber);

    private static TransactionRowData MapRow(RawRow raw, IReadOnlyDictionary<Guid, TransactionTagRef[]> tags)
    {
        string amount = TransactionRequestParser.FormatAmount(raw.Amount);
        string direction = TransactionRequestParser.DirectionOf(raw.Amount);
        bool effectiveHidden = raw.StatementHidden || raw.HiddenByUser;
        string reason = raw.StatementHidden && raw.HiddenByUser ? "both" : raw.StatementHidden ? "statement" : raw.HiddenByUser ? "individual" : "none";

        var response = new TransactionResponse(
            raw.Id,
            raw.Version,
            raw.TransactionDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            raw.Description,
            amount,
            "ZAR",
            direction,
            new TransactionAccountRef(raw.AccountId, raw.AccountName, raw.AccountInstitution, raw.AccountArchived),
            new TransactionCategoryRef(
                raw.CategoryId,
                raw.CategoryName,
                raw.CategoryKind,
                raw.CategorySystem,
                raw.CategoryArchived),
            raw.CategoryConfirmed,
            tags.GetValueOrDefault(raw.Id) ?? [],
            new TransactionStatementRef(
                raw.StatementId,
                raw.StatementLabel,
                raw.StatementSourceType,
                raw.StatementHidden),
            raw.HiddenByUser,
            effectiveHidden,
            reason);

        var tagIds = (tags.GetValueOrDefault(raw.Id) ?? []).Select(tag => tag.Id).ToArray();

        return new TransactionRowData(response, raw.NormalizedDescription, tagIds, raw.SourcePage, raw.SourceRowNumber);
    }

    private static async Task<Dictionary<Guid, TransactionTagRef[]>> LoadAssignedTagsAsync(ArgusDbContext context,
                                                                                           Guid customerId,
                                                                                           IReadOnlyCollection<Guid> transactionIds,
                                                                                           CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, TransactionTagRef[]>();
        if (transactionIds.Count == 0)
        {
            return result;
        }

        var rows = await (
            from link in context.TransactionMerchantTags.AsNoTracking()
            join tag in context.MerchantTags
                on new { link.CustomerId, Id = link.MerchantTagId } equals new { tag.CustomerId, Id = tag.Id }
            where link.CustomerId == customerId && transactionIds.Contains(link.TransactionId)
            orderby tag.DisplayName, tag.Id
            select new { link.TransactionId, tag.Id, tag.DisplayName, tag.IsArchived })
            .ToListAsync(cancellationToken);
        var collected = new Dictionary<Guid, List<TransactionTagRef>>();
        foreach (var row in rows)
        {
            if (!collected.TryGetValue(row.TransactionId, out List<TransactionTagRef>? list))
            {
                list = [];
                collected[row.TransactionId] = list;
            }
            list.Add(new TransactionTagRef(row.Id, row.DisplayName, row.IsArchived));
        }

        foreach (var (id, list) in collected)
        {
            result[id] = list.ToArray();
        }

        return result;
    }
}
