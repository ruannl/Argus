using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace argus.Server.Data;

public sealed record InitializationState(
    IReadOnlyList<string> LegacyNames,
    IReadOnlyList<string> EfIds,
    int EffectivePrefix,
    bool CanonicalMarkerPresent);

/// <summary>
/// Always-run locked validator for both ledgers and the effective application
/// schema. It reads raw history rows so unknown or non-prefix entries cannot be
/// hidden by EF's pending-migration calculation, and it compares semantic
/// catalog definitions rather than object names alone.
/// </summary>
public static class SchemaValidator
{
    private const int MerchantKeyFunctionPrefix = 8;

    private const string MerchantKeyFunctionDefinition = """
        CREATE FUNCTION argus_merchant_key(description TEXT) RETURNS TEXT
        LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
            SELECT string_agg(token, ' ' ORDER BY ordinality)
            FROM (
                SELECT token, ordinality
                FROM unnest(regexp_split_to_array(lower(coalesce(description, '')), '[^a-z0-9]+'))
                     WITH ORDINALITY AS parts(token, ordinality)
                WHERE token <> ''
                  AND token !~ '^[0-9]+$'
                  AND token !~ '^(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)$'
                  AND length(token) > 1
                ORDER BY ordinality
                LIMIT 2
            ) AS significant
        $$;
        """;

    private static readonly Dictionary<string, int> _tablePrefixes = new(StringComparer.Ordinal)
    {
        ["customers"] = 1,
        ["sessions"] = 1,
        ["schema_migrations"] = 1,
        ["data_migrations"] = 1,
        ["email_verification_tokens"] = 2,
        ["password_reset_tokens"] = 3,
        ["financial_accounts"] = 5,
        ["categories"] = 5,
        ["merchant_tags"] = 5,
        ["statement_imports"] = 5,
        ["transactions"] = 5,
        ["transaction_merchant_tags"] = 5,
        ["import_quota_locks"] = 6,
        ["import_reviews"] = 6,
        ["import_review_rows"] = 6,
        ["import_commit_receipts"] = 6,
        ["transaction_operations"] = 7,
        ["transaction_operation_items"] = 7,
        ["merchant_aliases"] = 8,
    };

    private static readonly Dictionary<string, int> _columnPrefixes = new(StringComparer.Ordinal)
    {
        ["customers.failed_login_count"] = 4,
        ["customers.locked_until"] = 4,
        ["customers.categories_seeded_at"] = 5,
        ["financial_accounts.import_version"] = 6,
        ["statement_imports.version"] = 6,
        ["statement_imports.fingerprint_occurrence"] = 6,
        ["statement_imports.duplicate_policy_version"] = 6,
        ["statement_imports.possible_duplicate_count"] = 6,
        ["statement_imports.exact_override_count"] = 6,
        ["statement_imports.statement_override_count"] = 6,
        ["statement_imports.override_summary"] = 6,
        ["transactions.identity_version"] = 6,
        ["transactions.source_id_namespace"] = 6,
        ["transactions.source_id_value"] = 6,
        ["transactions.merchant_key"] = 8,
        ["transactions.category_confirmed"] = 10,
        ["transaction_merchant_tags.customer_id"] = 6,
        ["import_review_rows.suggested_category_id"] = 9,
        ["import_review_rows.selected_category_id"] = 9,
        ["import_review_rows.category_decided"] = 9,
    };

    private static readonly Dictionary<string, int> _indexPrefixes = new(StringComparer.Ordinal)
    {
        ["statement_imports_fingerprint_slot_uidx"] = 6,
        ["statement_imports_history_idx"] = 6,
        ["transactions_statement_children_idx"] = 6,
        ["transactions_near_match_idx"] = 6,
        ["transaction_merchant_tags_customer_tag_idx"] = 6,
        ["transaction_operations_customer_status_idx"] = 7,
        ["transaction_operations_terminal_idx"] = 7,
        ["transaction_operation_items_header_idx"] = 7,
        ["transactions_customer_date_idx"] = 7,
        ["transactions_customer_amount_idx"] = 7,
        ["transactions_customer_merchant_key_idx"] = 8,
        ["transactions_customer_unconfirmed_idx"] = 10,
    };

    private static readonly Dictionary<string, int> _constraintPrefixes = new(StringComparer.Ordinal)
    {
        ["statement_imports_version_check"] = 6,
        ["statement_imports_fingerprint_occurrence_check"] = 6,
        ["statement_imports_dates_check"] = 6,
        ["statement_imports_counts_check"] = 6,
        ["transactions_source_id_pair_check"] = 6,
        ["transactions_identity_version_check"] = 6,
        ["transactions_occurrence_index_check"] = 6,
        ["transactions_statement_account_fk"] = 6,
        ["transaction_merchant_tags_transaction_fk"] = 6,
        ["transaction_merchant_tags_tag_fk"] = 6,
        ["import_reviews_version_check"] = 6,
        ["import_reviews_dates_check"] = 6,
        ["import_reviews_counts_check"] = 6,
        ["import_reviews_currency_check"] = 6,
        ["import_reviews_status_check"] = 6,
        ["import_review_rows_source_id_pair_check"] = 6,
        ["import_review_rows_duplicate_state_check"] = 6,
        ["import_review_rows_within_file_state_check"] = 6,
        ["import_commit_receipts_state_check"] = 6,
        ["transaction_operations_status_check"] = 7,
        ["transaction_operations_selection_mode_check"] = 7,
        ["transaction_operations_selected_count_check"] = 7,
        ["transaction_operations_changed_count_check"] = 7,
        ["transaction_operations_unchanged_count_check"] = 7,
        ["transaction_operations_counts_check"] = 7,
        ["transaction_operations_expiry_check"] = 7,
        ["transaction_operations_applied_check"] = 7,
        ["transaction_operations_prepared_payload_check"] = 7,
        ["transaction_operation_items_transaction_version_check"] = 7,
        ["transaction_operation_items_ordinal_check"] = 7,
        ["merchant_aliases_alias_check"] = 8,
        ["merchant_aliases_customer_tag_fk"] = 11,
        ["merchant_aliases_customer_category_fk"] = 11,
    };

    private static readonly Dictionary<string, int> _uniqueConstraintPrefixes = new(StringComparer.Ordinal)
    {
        ["statement_imports_customer_account_id_key"] = 6,
        ["transactions_customer_id_key"] = 6,
        ["transactions_identity_occurrence_key"] = 6,
        ["transactions_statement_row_key"] = 6,
        ["import_review_rows_review_id_row_id_key"] = 6,
        ["import_reviews_customer_id_id_key"] = 6,
        ["import_commit_receipts_customer_id_operation_id_key"] = 6,
    };

    public static async Task<InitializationState> ValidateAsync(ArgusDbContext context,
                                                                bool requireLedgers,
                                                                CancellationToken cancellationToken)
    {
        bool legacyExists = await RelationExistsAsync(context, "schema_migrations", cancellationToken);
        bool efHistoryExists = await RelationExistsAsync(context, "\"__EFMigrationsHistory\"", cancellationToken);
        bool dataExists = await RelationExistsAsync(context, "data_migrations", cancellationToken);

        if (requireLedgers && (!legacyExists || !dataExists))
        {
            throw new InvalidOperationException("schema_migrations and data_migrations must exist after initialization");
        }

        if (legacyExists)
        {
            await ValidateLedgerColumnsAsync(context, "schema_migrations", cancellationToken);
        }
        if (dataExists)
        {
            await ValidateLedgerColumnsAsync(context, "data_migrations", cancellationToken);
        }
        if (efHistoryExists)
        {
            await ValidateEfHistoryColumnsAsync(context, cancellationToken);
        }

        List<string> legacyNames = legacyExists
            ? await ReadStringColumnAsync(context, "SELECT name AS \"Value\" FROM schema_migrations ORDER BY applied_at, name", cancellationToken)
            : [];
        List<string> efIds = efHistoryExists
            ? await ReadStringColumnAsync(context, "SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"", cancellationToken)
            : [];
        List<string> dataNames = dataExists
            ? await ReadStringColumnAsync(context, "SELECT name AS \"Value\" FROM data_migrations ORDER BY applied_at, name", cancellationToken)
            : [];

        legacyNames = OrderLegacyNames(legacyNames);
        ValidatePrefix(legacyNames, MigrationCatalog.LegacyNames, "schema_migrations");
        ValidatePrefix(efIds, MigrationCatalog.EfIds, "__EFMigrationsHistory");

        foreach (string dataName in dataNames)
        {
            if (!string.Equals(dataName, LegacyMigrations.CanonicalBackfillMarker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"unknown data_migrations entry '{dataName}'");
            }
        }

        int effectivePrefix = Math.Max(legacyNames.Count, efIds.Count);
        if (effectivePrefix == 0 && dataNames.Count > 0)
        {
            throw new InvalidOperationException("canonical data marker exists without schema progress");
        }

        await ValidateSchemaAsync(context, effectivePrefix, requireLedgers, cancellationToken);
        if (effectivePrefix >= MigrationCatalog.LatestPrefix)
        {
            await IsolationSchemaValidator.ValidateAsync(context, requireRuntimeLogin: false, cancellationToken);
        }

        return new InitializationState(
            legacyNames,
            efIds,
            effectivePrefix,
            dataNames.Contains(LegacyMigrations.CanonicalBackfillMarker, StringComparer.Ordinal));
    }

    private static List<string> OrderLegacyNames(List<string> names)
    {
        foreach (string name in names)
        {
            if (LegacyMigrations.OrderForLegacyName(name) is null)
            {
                throw new InvalidOperationException($"unknown schema_migrations entry '{name}'");
            }
        }

        return names.OrderBy(name => LegacyMigrations.OrderForLegacyName(name)!.Value).ToList();
    }

    private static void ValidatePrefix(IReadOnlyList<string> names, IReadOnlyList<string> expected, string label)
    {
        if (names.Count > expected.Count)
        {
            throw new InvalidOperationException($"{label} contains more entries than the supported migration set");
        }

        for (int index = 0; index < names.Count; index++)
        {
            if (!string.Equals(names[index], expected[index], StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label} is not a supported ordered prefix at position {index + 1}");
            }
        }
    }

    private static async Task ValidateSchemaAsync(ArgusDbContext context,
                                                  int prefix,
                                                  bool requireLedgers,
                                                  CancellationToken cancellationToken)
    {
        IRelationalModel model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        Catalog catalog = await ReadCatalogAsync(context, cancellationToken);
        ExpectedSchema expected = BuildExpected(model, prefix, requireLedgers, catalog);

        foreach (string table in catalog.Tables)
        {
            if (!expected.Tables.Contains(table))
            {
                throw new InvalidOperationException($"schema drift: unexpected table '{table}' for effective prefix {prefix}");
            }
        }
        foreach (string table in expected.Tables)
        {
            if (!catalog.Tables.Contains(table))
            {
                throw new InvalidOperationException($"schema drift: expected table '{table}' is missing for effective prefix {prefix}");
            }
        }

        foreach (ExpectedColumn column in expected.Columns)
        {
            CatalogColumn? actual = catalog.Columns.FirstOrDefault(item =>
                string.Equals(item.Table, column.Table, StringComparison.Ordinal) &&
                string.Equals(item.Column, column.Column, StringComparison.Ordinal));
            if (actual is null)
            {
                throw new InvalidOperationException($"schema drift: column '{column.Table}.{column.Column}' is missing");
            }

            ValidateColumn(column, actual);
        }
        foreach (CatalogColumn actual in catalog.Columns)
        {
            if (!expected.Columns.Any(column =>
                    string.Equals(column.Table, actual.Table, StringComparison.Ordinal) &&
                    string.Equals(column.Column, actual.Column, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"schema drift: unexpected column '{actual.Table}.{actual.Column}'");
            }
        }

        foreach (ExpectedIndex index in expected.Indexes)
        {
            CatalogIndex? actual = catalog.Indexes.FirstOrDefault(item => string.Equals(item.Name, index.Name, StringComparison.Ordinal));
            if (actual is null)
            {
                throw new InvalidOperationException($"schema drift: index '{index.Name}' is missing");
            }

            ValidateIndex(index, actual);
        }
        foreach (CatalogIndex actual in catalog.Indexes)
        {
            if (!expected.Indexes.Any(index => string.Equals(index.Name, actual.Name, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"schema drift: unexpected index '{actual.Name}'");
            }
        }

        foreach (ExpectedCheck check in expected.Checks)
        {
            CatalogCheck? actual = catalog.Checks.FirstOrDefault(item => string.Equals(item.Name, check.Name, StringComparison.Ordinal));
            if (actual is null)
            {
                throw new InvalidOperationException($"schema drift: check constraint '{check.Name}' is missing");
            }
            if (!string.Equals(actual.Table, check.Table, StringComparison.Ordinal) ||
                !string.Equals(NormalizeExpression(actual.Sql), check.NormalizedSql, StringComparison.Ordinal) ||
                !actual.IsValidated)
            {
                throw new InvalidOperationException(
                    $"schema drift: check constraint '{check.Name}' definition changed " +
                    $"(actual {NormalizeExpression(actual.Sql)}, expected {check.NormalizedSql}, validated {actual.IsValidated})");
            }
        }
        foreach (CatalogCheck actual in catalog.Checks)
        {
            if (!expected.Checks.Any(check => string.Equals(check.Name, actual.Name, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"schema drift: unexpected check constraint '{actual.Name}'");
            }
        }

        foreach (ExpectedForeignKey foreignKey in expected.ForeignKeys)
        {
            CatalogForeignKey? actual = catalog.ForeignKeys.FirstOrDefault(item => string.Equals(item.Name, foreignKey.Name, StringComparison.Ordinal));
            if (actual is null)
            {
                throw new InvalidOperationException($"schema drift: foreign key '{foreignKey.Name}' is missing");
            }
            if (!string.Equals(actual.Table, foreignKey.Table, StringComparison.Ordinal) ||
                !string.Equals(actual.Columns, string.Join(',', foreignKey.Columns), StringComparison.Ordinal) ||
                !string.Equals(actual.PrincipalSchema, foreignKey.PrincipalSchema, StringComparison.Ordinal) ||
                !string.Equals(actual.PrincipalTable, foreignKey.PrincipalTable, StringComparison.Ordinal) ||
                !string.Equals(actual.PrincipalColumns, string.Join(',', foreignKey.PrincipalColumns), StringComparison.Ordinal) ||
                !string.Equals(actual.OnDelete, foreignKey.OnDelete, StringComparison.Ordinal) ||
                !actual.IsValidated)
            {
                throw new InvalidOperationException($"schema drift: foreign key '{foreignKey.Name}' definition changed");
            }
        }
        foreach (CatalogForeignKey actual in catalog.ForeignKeys)
        {
            if (!expected.ForeignKeys.Any(foreignKey => string.Equals(foreignKey.Name, actual.Name, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"schema drift: unexpected foreign key '{actual.Name}'");
            }
        }

        foreach (ExpectedUniqueConstraint unique in expected.UniqueConstraints)
        {
            CatalogUniqueConstraint? actual = catalog.UniqueConstraints.FirstOrDefault(item => string.Equals(item.Name, unique.Name, StringComparison.Ordinal));
            if (actual is null)
            {
                throw new InvalidOperationException($"schema drift: unique constraint '{unique.Name}' is missing");
            }
            if (!string.Equals(actual.Table, unique.Table, StringComparison.Ordinal) ||
                !string.Equals(actual.Kind, unique.Kind, StringComparison.Ordinal) ||
                !string.Equals(actual.Columns, string.Join(',', unique.Columns), StringComparison.Ordinal) ||
                !actual.IsValidated)
            {
                throw new InvalidOperationException(
                    $"schema drift: unique constraint '{unique.Name}' definition changed " +
                    $"(actual {actual.Table}/{actual.Kind}/{actual.Columns}, expected {unique.Table}/{unique.Kind}/{string.Join(',', unique.Columns)})");
            }
        }
        foreach (CatalogUniqueConstraint actual in catalog.UniqueConstraints)
        {
            if (!expected.UniqueConstraints.Any(unique => string.Equals(unique.Name, actual.Name, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"schema drift: unexpected unique constraint '{actual.Name}'");
            }
        }

        if (expected.MerchantKeyFunction)
        {
            if (string.IsNullOrWhiteSpace(catalog.MerchantKeyFunction))
            {
                throw new InvalidOperationException("schema drift: argus_merchant_key function is missing");
            }
            if (!string.Equals(NormalizeFunction(catalog.MerchantKeyFunction), NormalizeFunction(MerchantKeyFunctionDefinition), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("schema drift: argus_merchant_key function definition changed");
            }
        }
        else if (!string.IsNullOrWhiteSpace(catalog.MerchantKeyFunction))
        {
            throw new InvalidOperationException("schema drift: argus_merchant_key function exists before its migration prefix");
        }
    }

    private static ExpectedSchema BuildExpected(IRelationalModel model,
                                                int prefix,
                                                bool requireLedgers,
                                                Catalog catalog)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        var columns = new List<ExpectedColumn>();
        var indexes = new List<ExpectedIndex>();
        var checks = new List<ExpectedCheck>();
        var foreignKeys = new List<ExpectedForeignKey>();
        var uniqueConstraints = new List<ExpectedUniqueConstraint>();

        foreach (ITable table in model.Tables)
        {
            int tablePrefix = TablePrefix(table.Name);
            bool isLedger = table.Name is "schema_migrations" or "data_migrations";
            if (!isLedger && tablePrefix > prefix)
            {
                continue;
            }
            if (isLedger && !requireLedgers && !catalog.Tables.Contains(table.Name))
            {
                continue;
            }

            tables.Add(table.Name);

            foreach (IColumn column in table.Columns)
            {
                if (isLedger || ColumnPrefix(table.Name, column.Name) <= prefix)
                {
                    columns.Add(new ExpectedColumn(
                        table.Name,
                        column.Name,
                        NormalizeType(column.StoreType),
                        column.IsNullable,
                        ExpectedDefault(column),
                        column.ComputedColumnSql is null ? null : NormalizeExpression(column.ComputedColumnSql),
                        column.IsStored == true));
                }
            }

            foreach (ITableIndex index in table.Indexes)
            {
                if (IndexPrefix(table.Name, index.Name!) <= prefix)
                {
                    indexes.Add(new ExpectedIndex(
                        index.Name!,
                        table.Name,
                        index.Columns.Select(column => column.Name).ToArray(),
                        index.IsUnique,
                        NormalizeExpression(index.Filter),
                        Descending(index)));
                }
            }

            foreach (IReadOnlyCheckConstraint check in table.CheckConstraints)
            {
                if (ConstraintPrefix(table.Name, check.Name!) <= prefix)
                {
                    checks.Add(new ExpectedCheck(check.Name!, table.Name, NormalizeExpression(check.Sql) ?? string.Empty));
                }
            }

            foreach (IForeignKeyConstraint foreignKey in table.ForeignKeyConstraints)
            {
                if (ConstraintPrefix(table.Name, foreignKey.Name!) <= prefix)
                {
                    foreignKeys.Add(new ExpectedForeignKey(
                        foreignKey.Name!,
                        table.Name,
                        foreignKey.Columns.Select(column => column.Name).ToArray(),
                        catalog.Schema,
                        foreignKey.PrincipalTable.Name,
                        foreignKey.PrincipalColumns.Select(column => column.Name).ToArray(),
                        ReferentialActionCode(foreignKey.OnDeleteAction)));
                }
            }

            if (table.PrimaryKey is not null)
            {
                uniqueConstraints.Add(new ExpectedUniqueConstraint(
                    table.PrimaryKey.Name!,
                    table.Name,
                    "p",
                    table.PrimaryKey.Columns.Select(column => column.Name).ToArray()));
            }

            foreach (IUniqueConstraint unique in table.UniqueConstraints)
            {
                if (table.PrimaryKey is not null &&
                    string.Equals(unique.Name, table.PrimaryKey.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (UniqueConstraintPrefix(table.Name, unique.Name!) <= prefix)
                {
                    uniqueConstraints.Add(new ExpectedUniqueConstraint(
                        unique.Name!,
                        table.Name,
                        "u",
                        unique.Columns.Select(column => column.Name).ToArray()));
                }
            }
        }

        if (catalog.Tables.Contains("__EFMigrationsHistory"))
        {
            tables.Add("__EFMigrationsHistory");
        }

        return new ExpectedSchema(
            tables,
            columns,
            indexes,
            checks,
            foreignKeys,
            uniqueConstraints,
            prefix >= MerchantKeyFunctionPrefix);
    }

    private static void ValidateColumn(ExpectedColumn expected, CatalogColumn actual)
    {
        if (!string.Equals(NormalizeType(actual.StoreType), expected.StoreType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"schema drift: column '{expected.Table}.{expected.Column}' has type '{actual.StoreType}', expected '{expected.StoreType}'");
        }
        if (actual.IsNullable != expected.IsNullable)
        {
            throw new InvalidOperationException($"schema drift: column '{expected.Table}.{expected.Column}' nullability differs");
        }
        if (!string.Equals(NormalizeDefault(actual.DefaultSql), expected.DefaultSql, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"schema drift: column '{expected.Table}.{expected.Column}' default changed");
        }
        if (actual.IsGenerated != expected.IsGenerated)
        {
            throw new InvalidOperationException($"schema drift: column '{expected.Table}.{expected.Column}' generated status changed");
        }
        if (expected.IsGenerated &&
            !string.Equals(NormalizeExpression(actual.GeneratedSql), expected.GeneratedSql, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"schema drift: column '{expected.Table}.{expected.Column}' generated expression changed");
        }
    }

    private static void ValidateIndex(ExpectedIndex expected, CatalogIndex actual)
    {
        if (!string.Equals(actual.Table, expected.Table, StringComparison.Ordinal) ||
            !string.Equals(actual.Columns, string.Join(',', expected.Columns), StringComparison.Ordinal) ||
            actual.IsUnique != expected.IsUnique ||
            !string.Equals(NormalizeExpression(actual.Filter), expected.Filter, StringComparison.Ordinal) ||
            !string.Equals(actual.Descending, expected.Descending, StringComparison.Ordinal) ||
            !actual.IsValid)
        {
            throw new InvalidOperationException($"schema drift: index '{expected.Name}' definition changed");
        }
    }

    private static async Task<Catalog> ReadCatalogAsync(ArgusDbContext context, CancellationToken cancellationToken)
    {
        var tables = (await ReadStringColumnAsync(
            context,
            "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'",
            cancellationToken)).ToHashSet(StringComparer.Ordinal);

        if (tables.Count == 0)
        {
            return new Catalog(tables, [], [], [], [], [], string.Empty, string.Empty);
        }

        var columns = await context.Database.SqlQueryRaw<CatalogColumn>("""
            SELECT c.relname AS "Table",
                   a.attname AS "Column",
                   format_type(a.atttypid, a.atttypmod) AS "StoreType",
                   NOT a.attnotnull AS "IsNullable",
                   CASE WHEN a.attgenerated = '' THEN pg_get_expr(d.adbin, d.adrelid) END AS "DefaultSql",
                   CASE WHEN a.attgenerated = 's' THEN pg_get_expr(d.adbin, d.adrelid) END AS "GeneratedSql",
                   a.attgenerated = 's' AS "IsGenerated"
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
            WHERE n.nspname = current_schema()
              AND c.relkind = 'r'
              AND c.relname <> '__EFMigrationsHistory'
              AND a.attnum > 0
              AND NOT a.attisdropped
            ORDER BY c.relname, a.attnum
            """).ToListAsync(cancellationToken);

        var indexes = await context.Database.SqlQueryRaw<CatalogIndex>("""
            SELECT i.relname AS "Name",
                   t.relname AS "Table",
                   (SELECT string_agg(a.attname, ',' ORDER BY k.ord)
                    FROM unnest(ix.indkey) WITH ORDINALITY k(attnum, ord)
                    JOIN pg_attribute a ON a.attrelid = ix.indrelid AND a.attnum = k.attnum) AS "Columns",
                   ix.indisunique AS "IsUnique",
                   pg_get_expr(ix.indpred, ix.indrelid) AS "Filter",
                   (SELECT string_agg(CASE WHEN pg_index_column_has_property(ix.indexrelid, k.ord::int, 'desc') THEN '1' ELSE '0' END, '' ORDER BY k.ord)
                    FROM unnest(ix.indkey) WITH ORDINALITY k(attnum, ord)) AS "Descending",
                   ix.indisvalid AS "IsValid"
            FROM pg_index ix
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_class t ON t.oid = ix.indrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = current_schema()
              AND t.relname <> '__EFMigrationsHistory'
              AND NOT EXISTS (
                  SELECT 1 FROM pg_constraint c
                  WHERE c.conindid = ix.indexrelid AND c.contype IN ('p', 'u'))
            ORDER BY i.relname
            """).ToListAsync(cancellationToken);

        var checks = await context.Database.SqlQueryRaw<CatalogCheck>("""
            SELECT c.conname AS "Name",
                   t.relname AS "Table",
                   pg_get_expr(c.conbin, c.conrelid) AS "Sql",
                   c.convalidated AS "IsValidated"
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = current_schema()
              AND t.relname <> '__EFMigrationsHistory'
              AND c.contype = 'c'
            ORDER BY c.conname
            """).ToListAsync(cancellationToken);

        var foreignKeys = await context.Database.SqlQueryRaw<CatalogForeignKey>("""
            SELECT c.conname AS "Name",
                   t.relname AS "Table",
                   (SELECT string_agg(a.attname, ',' ORDER BY k.ord)
                    FROM unnest(c.conkey) WITH ORDINALITY k(attnum, ord)
                    JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum) AS "Columns",
                   pn.nspname AS "PrincipalSchema",
                   p.relname AS "PrincipalTable",
                   (SELECT string_agg(a.attname, ',' ORDER BY k.ord)
                    FROM unnest(c.confkey) WITH ORDINALITY k(attnum, ord)
                    JOIN pg_attribute a ON a.attrelid = c.confrelid AND a.attnum = k.attnum) AS "PrincipalColumns",
                   c.confdeltype::text AS "OnDelete",
                   c.convalidated AS "IsValidated"
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_class p ON p.oid = c.confrelid
            JOIN pg_namespace pn ON pn.oid = p.relnamespace
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = current_schema()
              AND t.relname <> '__EFMigrationsHistory'
              AND c.contype = 'f'
            ORDER BY c.conname
            """).ToListAsync(cancellationToken);

        var uniqueConstraints = await context.Database.SqlQueryRaw<CatalogUniqueConstraint>("""
            SELECT c.conname AS "Name",
                   t.relname AS "Table",
                   c.contype::text AS "Kind",
                   (SELECT string_agg(a.attname, ',' ORDER BY k.ord)
                    FROM unnest(c.conkey) WITH ORDINALITY k(attnum, ord)
                    JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum) AS "Columns",
                   c.convalidated AS "IsValidated"
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = current_schema()
              AND t.relname <> '__EFMigrationsHistory'
              AND c.contype IN ('p', 'u')
            ORDER BY c.conname
            """).ToListAsync(cancellationToken);

        List<string> functionDefinitions = await ReadStringColumnAsync(
            context,
            "SELECT pg_get_functiondef(p.oid) AS \"Value\" FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = current_schema() AND p.proname = 'argus_merchant_key'",
            cancellationToken);
        List<string> schemas = await ReadStringColumnAsync(context, "SELECT current_schema() AS \"Value\"", cancellationToken);

        return new Catalog(
            tables,
            columns,
            indexes,
            checks,
            foreignKeys,
            uniqueConstraints,
            functionDefinitions.FirstOrDefault() ?? string.Empty,
            schemas.FirstOrDefault() ?? string.Empty);
    }

    private static string NormalizeType(string value) =>
        value.Replace("timestamp with time zone", "timestamptz", StringComparison.OrdinalIgnoreCase)
             .Replace(" ", string.Empty, StringComparison.Ordinal)
             .ToLowerInvariant();

    private static string? ExpectedDefault(IColumn column)
    {
        if (column.DefaultValueSql is not null)
        {
            return NormalizeDefault(column.DefaultValueSql);
        }
        if (column.DefaultValue is null)
        {
            return null;
        }

        return column.DefaultValue switch
        {
            bool boolean => boolean ? "true" : "false",
            int integer => integer.ToString(CultureInfo.InvariantCulture),
            long longValue => longValue.ToString(CultureInfo.InvariantCulture),
            string text => NormalizeDefault($"'{text}'"),
            _ => NormalizeDefault(Convert.ToString(column.DefaultValue, CultureInfo.InvariantCulture) ?? string.Empty),
        };
    }

    private static string? NormalizeDefault(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return NormalizeSqlTokens(value, removeCreateKeywords: false, removeFunctionDelimiters: false);
    }

    private static string? NormalizeExpression(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return NormalizeSqlTokens(value, removeCreateKeywords: false, removeFunctionDelimiters: false);
    }

    private static string NormalizeFunction(string value) =>
        NormalizeSqlTokens(value, removeCreateKeywords: true, removeFunctionDelimiters: true);

    private static string NormalizeSqlTokens(string value,
                                             bool removeCreateKeywords,
                                             bool removeFunctionDelimiters)
    {
        string normalized = MaskQuotedText(value, out Dictionary<string, string> quotedText, out HashSet<string> stringLiterals);
        if (removeCreateKeywords)
        {
            normalized = Regex.Replace(normalized, @"[a-zA-Z_][a-zA-Z0-9_]*\.argus_merchant_key", "argus_merchant_key", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            normalized = normalized.Replace("$function$", "$$", StringComparison.Ordinal);
        }
        else
        {
            // Canonicalize the ANY(ARRAY[...]) form PostgreSQL uses for IN lists.
            // Function bodies are compared as stored and are not rewritten.
            normalized = Regex.Replace(normalized, @"=\s*ANY\s*\(\s*ARRAY\s*\[", "IN (", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            normalized = Regex.Replace(normalized, @"\]\s*\)", ")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        List<string> tokens = TokenizeSql(normalized);
        if (!removeCreateKeywords)
        {
            tokens = RemoveRedundantLiteralCasts(tokens, stringLiterals);
        }

        var result = new List<string>();
        bool inFunctionHeader = removeCreateKeywords;
        foreach (string token in tokens)
        {
            if (token.StartsWith('\u0001'))
            {
                result.Add(token);
                continue;
            }

            if (token.StartsWith('\''))
            {
                result.Add(token);
                continue;
            }

            string lower = token.ToLowerInvariant();
            if (lower is "$$" or ";")
            {
                continue;
            }

            if (inFunctionHeader)
            {
                if (lower is "create" or "or" or "replace")
                {
                    continue;
                }

                inFunctionHeader = false;
            }

            result.Add(lower);
        }

        if (!removeCreateKeywords)
        {
            RemoveRedundantParentheses(result);
        }

        for (int index = 0; index < result.Count; index++)
        {
            if (quotedText.TryGetValue(result[index], out string? literal))
            {
                result[index] = literal;
            }
        }

        return string.Join(' ', result);
    }

    private static List<string> RemoveRedundantLiteralCasts(List<string> tokens,
                                                            HashSet<string> stringLiterals)
    {
        var result = new List<string>(tokens.Count);
        for (int index = 0; index < tokens.Count; index++)
        {
            string token = tokens[index];
            if (!string.Equals(token, "::", StringComparison.Ordinal) ||
                result.Count == 0 ||
                !stringLiterals.Contains(result[^1]))
            {
                result.Add(token);
                continue;
            }

            int typeIndex = index + 1;
            if (typeIndex >= tokens.Count)
            {
                result.Add(token);
                continue;
            }

            string type = tokens[typeIndex].ToLowerInvariant();
            int typeTokens;
            if (type == "text")
            {
                typeTokens = 1;
            }
            else if (type == "character" &&
                     typeIndex + 1 < tokens.Count &&
                     string.Equals(tokens[typeIndex + 1], "varying", StringComparison.OrdinalIgnoreCase))
            {
                typeTokens = 2;
            }
            else
            {
                result.Add(token);
                continue;
            }

            int afterType = typeIndex + typeTokens;
            if (afterType < tokens.Count && tokens[afterType] is "(" or "[" or "::")
            {
                // A type modifier, array suffix, or further cast means the
                // conversion is not a display-only cast on the literal.
                result.Add(token);
                continue;
            }

            index = afterType - 1;
        }

        return result;
    }

    private static string MaskQuotedText(string value,
                                         out Dictionary<string, string> quotedText,
                                         out HashSet<string> stringLiterals)
    {
        quotedText = new Dictionary<string, string>(StringComparer.Ordinal);
        stringLiterals = new HashSet<string>(StringComparer.Ordinal);
        var builder = new StringBuilder(value.Length);
        int index = 0;
        int quotedIndex = 0;
        while (index < value.Length)
        {
            char character = value[index];
            if (character is not '\'' and not '"')
            {
                builder.Append(character);
                index++;
                continue;
            }

            int start = index;
            char quote = character;
            index++;
            while (index < value.Length)
            {
                if (value[index] != quote)
                {
                    index++;
                    continue;
                }

                if (index + 1 < value.Length && value[index + 1] == quote)
                {
                    index += 2;
                    continue;
                }

                index++;
                break;
            }

            string placeholder = $"\u0001argus_quoted_{Guid.NewGuid():N}_{quotedIndex}\u0001";
            quotedIndex++;
            quotedText.Add(placeholder, value[start..index]);
            if (quote == '\'')
            {
                stringLiterals.Add(placeholder);
            }
            builder.Append(' ');
            builder.Append(placeholder);
            builder.Append(' ');
        }

        return builder.ToString();
    }

    private static List<string> TokenizeSql(string value)
    {
        var tokens = new List<string>();
        int index = 0;
        while (index < value.Length)
        {
            char character = value[index];
            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            if (character == ':' && index + 1 < value.Length && value[index + 1] == ':')
            {
                tokens.Add("::");
                index += 2;
                continue;
            }

            if (character == '\'')
            {
                int start = index;
                index++;
                while (index < value.Length)
                {
                    if (value[index] == '\'')
                    {
                        if (index + 1 < value.Length && value[index + 1] == '\'')
                        {
                            index += 2;
                            continue;
                        }

                        index++;
                        break;
                    }

                    index++;
                }

                tokens.Add(value[start..index]);
                continue;
            }

            if (character is '(' or ')' or ',' or '[' or ']' or ';')
            {
                tokens.Add(character.ToString());
                index++;
                continue;
            }

            int tokenStart = index;
            while (index < value.Length &&
                   !char.IsWhiteSpace(value[index]) &&
                   value[index] is not '(' and not ')' and not ',' and not '[' and not ']' and not ';' and not '\'')
            {
                index++;
            }

            tokens.Add(value[tokenStart..index]);
        }

        return tokens;
    }

    private static void RemoveRedundantParentheses(List<string> tokens)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int index = 0; index < tokens.Count; index++)
            {
                if (!string.Equals(tokens[index], "(", StringComparison.Ordinal))
                {
                    continue;
                }

                int depth = 0;
                int close = -1;
                for (int cursor = index; cursor < tokens.Count; cursor++)
                {
                    if (string.Equals(tokens[cursor], "(", StringComparison.Ordinal))
                    {
                        depth++;
                    }
                    else if (string.Equals(tokens[cursor], ")", StringComparison.Ordinal))
                    {
                        depth--;
                        if (depth == 0)
                        {
                            close = cursor;
                            break;
                        }
                    }
                }

                if (close == -1)
                {
                    continue;
                }

                if (index == 0 && close == tokens.Count - 1)
                {
                    tokens.RemoveAt(close);
                    tokens.RemoveAt(index);
                    changed = true;
                    break;
                }

                bool hasNested = false;
                bool hasBoolean = false;
                for (int cursor = index + 1; cursor < close; cursor++)
                {
                    if (string.Equals(tokens[cursor], "(", StringComparison.Ordinal))
                    {
                        hasNested = true;
                        break;
                    }
                    if (tokens[cursor] is "and" or "or")
                    {
                        hasBoolean = true;
                    }
                }

                if (hasNested)
                {
                    continue;
                }

                if (!hasBoolean)
                {
                    tokens.RemoveAt(close);
                    tokens.RemoveAt(index);
                    changed = true;
                    break;
                }
            }
        }
    }

    private static string Descending(ITableIndex index)
    {
        int count = index.Columns.Count;
        if (index.IsDescending is null || index.IsDescending.Count == 0)
        {
            return new string('0', count);
        }

        return string.Concat(index.IsDescending.Select(value => value ? '1' : '0'));
    }

    private static string ReferentialActionCode(Microsoft.EntityFrameworkCore.Migrations.ReferentialAction action) => action switch
    {
        Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Cascade => "c",
        Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict => "r",
        Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.SetNull => "n",
        Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.SetDefault => "d",
        _ => "a",
    };

    private static int TablePrefix(string table) =>
        _tablePrefixes.TryGetValue(table, out int prefix)
            ? prefix
            : throw new InvalidOperationException($"unknown model table '{table}'");

    private static int ColumnPrefix(string table, string column) =>
        _columnPrefixes.TryGetValue($"{table}.{column}", out int prefix) ? prefix : TablePrefix(table);

    private static int IndexPrefix(string table, string index) =>
        _indexPrefixes.TryGetValue(index, out int prefix) ? prefix : TablePrefix(table);

    private static int ConstraintPrefix(string table, string constraint) =>
        _constraintPrefixes.TryGetValue(constraint, out int prefix) ? prefix : TablePrefix(table);

    private static int UniqueConstraintPrefix(string table, string constraint) =>
        _uniqueConstraintPrefixes.TryGetValue(constraint, out int prefix) ? prefix : TablePrefix(table);

    private static async Task ValidateLedgerColumnsAsync(ArgusDbContext context, string table, CancellationToken cancellationToken)
    {
        await RequireColumnsAsync(context, table, cancellationToken, "name", "applied_at");
    }

    private static async Task ValidateEfHistoryColumnsAsync(ArgusDbContext context, CancellationToken cancellationToken)
    {
        await RequireColumnsAsync(context, "__EFMigrationsHistory", cancellationToken, "MigrationId", "ProductVersion");
    }

    private static async Task RequireColumnsAsync(ArgusDbContext context,
                                                  string table,
                                                  CancellationToken cancellationToken,
                                                  params string[] columns)
    {
        var actual = (await ReadStringColumnAsync(
            context,
            "SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = {0}",
            cancellationToken,
            table)).ToHashSet(StringComparer.Ordinal);

        foreach (string column in columns)
        {
            if (!actual.Contains(column))
            {
                throw new InvalidOperationException($"schema drift: column '{table}.{column}' is missing");
            }
        }
    }

    private static async Task<bool> RelationExistsAsync(ArgusDbContext context, string relation, CancellationToken cancellationToken)
    {
        int value = await context.Database
            .SqlQueryRaw<int>("SELECT CASE WHEN to_regclass({0}) IS NULL THEN 0 ELSE 1 END AS \"Value\"", relation)
            .SingleAsync(cancellationToken);

        return value == 1;
    }

    private static async Task<List<string>> ReadStringColumnAsync(ArgusDbContext context,
                                                                  string sql,
                                                                  CancellationToken cancellationToken,
                                                                  params object[] parameters)
    {
        IQueryable<string> query = parameters.Length == 0
            ? context.Database.SqlQueryRaw<string>(sql)
            : context.Database.SqlQueryRaw<string>(sql, parameters);

        return await query.ToListAsync(cancellationToken);
    }

    private sealed class Catalog
    {
        public Catalog(HashSet<string> tables,
                       List<CatalogColumn> columns,
                       List<CatalogIndex> indexes,
                       List<CatalogCheck> checks,
                       List<CatalogForeignKey> foreignKeys,
                       List<CatalogUniqueConstraint> uniqueConstraints,
                       string merchantKeyFunction,
                       string schema)
        {
            Tables = tables;
            Columns = columns;
            Indexes = indexes;
            Checks = checks;
            ForeignKeys = foreignKeys;
            UniqueConstraints = uniqueConstraints;
            MerchantKeyFunction = merchantKeyFunction;
            Schema = schema;
        }

        public HashSet<string> Tables { get; }
        public List<CatalogColumn> Columns { get; }
        public List<CatalogIndex> Indexes { get; }
        public List<CatalogCheck> Checks { get; }
        public List<CatalogForeignKey> ForeignKeys { get; }
        public List<CatalogUniqueConstraint> UniqueConstraints { get; }
        public string MerchantKeyFunction { get; }
        public string Schema { get; }
    }

    private sealed class CatalogColumn
    {
        public string Table { get; set; } = string.Empty;
        public string Column { get; set; } = string.Empty;
        public string StoreType { get; set; } = string.Empty;
        public bool IsNullable { get; set; }
        public string? DefaultSql { get; set; }
        public string? GeneratedSql { get; set; }
        public bool IsGenerated { get; set; }
    }

    private sealed class CatalogIndex
    {
        public string Name { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public string Columns { get; set; } = string.Empty;
        public bool IsUnique { get; set; }
        public string? Filter { get; set; }
        public string Descending { get; set; } = string.Empty;
        public bool IsValid { get; set; }
    }

    private sealed class CatalogCheck
    {
        public string Name { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
        public bool IsValidated { get; set; }
    }

    private sealed class CatalogForeignKey
    {
        public string Name { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public string Columns { get; set; } = string.Empty;
        public string PrincipalSchema { get; set; } = string.Empty;
        public string PrincipalTable { get; set; } = string.Empty;
        public string PrincipalColumns { get; set; } = string.Empty;
        public string OnDelete { get; set; } = string.Empty;
        public bool IsValidated { get; set; }
    }

    private sealed class CatalogUniqueConstraint
    {
        public string Name { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Columns { get; set; } = string.Empty;
        public bool IsValidated { get; set; }
    }

    private sealed record ExpectedSchema(
        HashSet<string> Tables,
        List<ExpectedColumn> Columns,
        List<ExpectedIndex> Indexes,
        List<ExpectedCheck> Checks,
        List<ExpectedForeignKey> ForeignKeys,
        List<ExpectedUniqueConstraint> UniqueConstraints,
        bool MerchantKeyFunction);

    private sealed record ExpectedColumn(
        string Table,
        string Column,
        string StoreType,
        bool IsNullable,
        string? DefaultSql,
        string? GeneratedSql,
        bool IsGenerated);

    private sealed record ExpectedIndex(
        string Name,
        string Table,
        IReadOnlyList<string> Columns,
        bool IsUnique,
        string? Filter,
        string Descending);

    private sealed record ExpectedCheck(string Name, string Table, string NormalizedSql);

    private sealed record ExpectedForeignKey(
        string Name,
        string Table,
        IReadOnlyList<string> Columns,
        string PrincipalSchema,
        string PrincipalTable,
        IReadOnlyList<string> PrincipalColumns,
        string OnDelete);

    private sealed record ExpectedUniqueConstraint(
        string Name,
        string Table,
        string Kind,
        IReadOnlyList<string> Columns);
}
