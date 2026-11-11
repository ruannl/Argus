using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using argus.Server.Imports;
using Microsoft.EntityFrameworkCore;

namespace argus.Server.Data;

/// <summary>
/// The one-time canonical identity and statement-fingerprint backfill. It is an
/// explicit data step after EF schema migration, not an EF migration and not a
/// UseSeeding callback. The existing data_migrations marker remains the only
/// data-progress authority.
/// </summary>
public static class DataBackfills
{
    public static async Task RunAsync(ArgusDbContext context,
                                      IInitializationFaultInjector faultInjector,
                                      CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        int marker = await context.Database
            .SqlQueryRaw<int>("SELECT CASE WHEN EXISTS (SELECT 1 FROM data_migrations WHERE name = {0}) THEN 1 ELSE 0 END AS \"Value\"", LegacyMigrations.CanonicalBackfillMarker)
            .SingleAsync(cancellationToken);
        if (marker == 1)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var transactions = await context.Database.SqlQueryRaw<BackfillTransactionRow>("""
            SELECT id AS "Id",
                   customer_id AS "CustomerId",
                   financial_account_id AS "FinancialAccountId",
                   transaction_date AS "TransactionDate",
                   (amount * 100)::bigint AS "Cents",
                   normalized_description AS "NormalizedDescription",
                   created_at AS "CreatedAt"
            FROM transactions
            ORDER BY customer_id, financial_account_id, created_at, id
            """).ToListAsync(cancellationToken);

        foreach (BackfillTransactionRow row in transactions)
        {
            row.IdentityKey = StatementIdentity.ComputeIdentityKey(row.TransactionDate, row.Cents, row.NormalizedDescription);
        }

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE transactions SET identity_key = 'tmp:' || id::text, occurrence_index = 1, identity_version = 1",
            cancellationToken);

        foreach (var group in transactions.GroupBy(row => (row.CustomerId, row.FinancialAccountId, row.IdentityKey)))
        {
            int occurrence = 1;
            foreach (BackfillTransactionRow row in group.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id))
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE transactions
                    SET identity_key = {row.IdentityKey},
                        identity_version = 1,
                        occurrence_index = {occurrence}
                    WHERE id = {row.Id}
                    """, cancellationToken);
                occurrence++;
            }
        }

        var statements = await context.Database.SqlQueryRaw<BackfillStatementRow>("""
            SELECT id AS "Id",
                   customer_id AS "CustomerId",
                   financial_account_id AS "AccountId",
                   start_date AS "StartDate",
                   end_date AS "EndDate",
                   created_at AS "CreatedAt"
            FROM statement_imports
            ORDER BY customer_id, financial_account_id, created_at, id
            """).ToListAsync(cancellationToken);

        var statementsById = statements.ToDictionary(statement => statement.Id);
        var importRows = await context.Database.SqlQueryRaw<BackfillImportRow>("""
            SELECT statement_import_id AS "StatementImportId",
                   id AS "Id",
                   transaction_date AS "TransactionDate",
                   description AS "Description",
                   normalized_description AS "NormalizedDescription",
                   (amount * 100)::bigint AS "Cents",
                   source_page AS "SourcePage",
                   source_row_number AS "SourceRowNumber"
            FROM transactions
            WHERE statement_import_id IS NOT NULL
            ORDER BY statement_import_id, source_row_number, id
            """).ToListAsync(cancellationToken);

        foreach (BackfillImportRow row in importRows)
        {
            if (!statementsById.TryGetValue(row.StatementImportId, out BackfillStatementRow? statement))
            {
                continue;
            }

            int sourceRowNumber = row.SourceRowNumber ?? 1;
            statement.Rows.Add(new ParsedStatementRow(
                row.Id,
                sourceRowNumber,
                row.SourcePage,
                sourceRowNumber,
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["date"] = row.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["description"] = row.Description,
                    ["amount"] = StatementText.FormatCents(row.Cents),
                },
                row.TransactionDate,
                row.Description,
                row.NormalizedDescription,
                row.Cents,
                [],
                [],
                null,
                null));
        }

        foreach (BackfillStatementRow statement in statements)
        {
            statement.Fingerprint = StatementIdentity.ComputeStatementFingerprint(statement.Rows, statement.StartDate, statement.EndDate);
        }

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE statement_imports SET statement_fingerprint = 'tmp:' || id::text, fingerprint_occurrence = 1",
            cancellationToken);

        await faultInjector.AfterBackfillStagingAsync(cancellationToken);

        foreach (var group in statements.GroupBy(statement => (statement.CustomerId, statement.AccountId, statement.Fingerprint)))
        {
            int occurrence = 1;
            foreach (BackfillStatementRow statement in group.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id))
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE statement_imports
                    SET statement_fingerprint = {statement.Fingerprint},
                        fingerprint_occurrence = {occurrence}
                    WHERE id = {statement.Id}
                    """, cancellationToken);
                occurrence++;
            }
        }

        await faultInjector.BeforeBackfillMarkerAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO data_migrations (name) VALUES ({LegacyMigrations.CanonicalBackfillMarker})",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed class BackfillTransactionRow
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public Guid FinancialAccountId { get; set; }
        public DateOnly TransactionDate { get; set; }
        public long Cents { get; set; }
        public string NormalizedDescription { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        [NotMapped]
        public string IdentityKey { get; set; } = string.Empty;
    }

    private sealed class BackfillStatementRow
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public Guid AccountId { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public DateTime CreatedAt { get; set; }

        [NotMapped]
        public List<ParsedStatementRow> Rows { get; } = [];

        [NotMapped]
        public string Fingerprint { get; set; } = string.Empty;
    }

    private sealed class BackfillImportRow
    {
        public Guid StatementImportId { get; set; }
        public Guid Id { get; set; }
        public DateOnly TransactionDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string NormalizedDescription { get; set; } = string.Empty;
        public long Cents { get; set; }
        public int? SourcePage { get; set; }
        public int? SourceRowNumber { get; set; }
    }
}
