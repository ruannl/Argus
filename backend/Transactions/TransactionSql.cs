using System.Text;
using Npgsql;

namespace argus.Server.Transactions;

/// <summary>
/// One parameterized owned predicate builder shared by the transaction list,
/// the matching counts and the frozen all-matching preparation. Tags use
/// EXISTS so multi-tag rows never multiply counts.
/// </summary>
public static class TransactionSql
{
    public static readonly string Joins = """
        JOIN financial_accounts a
          ON a.customer_id = t.customer_id AND a.id = t.financial_account_id
        JOIN categories c
          ON c.customer_id = t.customer_id AND c.id = t.category_id
        JOIN statement_imports s
          ON s.customer_id = t.customer_id AND s.id = t.statement_import_id
        """;

    public static string BuildWhere(TransactionFilter filter,
                                    NpgsqlCommand command,
                                    bool includeVisibility)
    {
        var clauses = new List<string> { "t.customer_id = @customer_id" };

        if (filter.Search is not null)
        {
            command.Parameters.AddWithValue(
                "search", "%" + EscapeLike(filter.Search) + "%");
            clauses.Add("""
                (t.description ILIKE @search ESCAPE '\'
                 OR EXISTS (
                    SELECT 1
                    FROM transaction_merchant_tags js
                    JOIN merchant_tags ms
                      ON ms.customer_id = js.customer_id AND ms.id = js.merchant_tag_id
                    WHERE js.customer_id = t.customer_id
                      AND js.transaction_id = t.id
                      AND ms.display_name ILIKE @search ESCAPE '\'))
                """);
        }

        if (filter.StartDate is not null)
        {
            command.Parameters.AddWithValue("start_date", filter.StartDate.Value);
            clauses.Add("t.transaction_date >= @start_date");
        }
        if (filter.EndDate is not null)
        {
            command.Parameters.AddWithValue("end_date", filter.EndDate.Value);
            clauses.Add("t.transaction_date <= @end_date");
        }
        if (filter.FinancialAccountId is not null)
        {
            command.Parameters.AddWithValue("account_id", filter.FinancialAccountId.Value);
            clauses.Add("t.financial_account_id = @account_id");
        }
        if (filter.StatementImportId is not null)
        {
            command.Parameters.AddWithValue("statement_id", filter.StatementImportId.Value);
            clauses.Add("t.statement_import_id = @statement_id");
        }
        if (filter.CategoryId is not null)
        {
            command.Parameters.AddWithValue("category_id", filter.CategoryId.Value);
            clauses.Add("t.category_id = @category_id");
        }
        if (filter.MerchantTagId is not null)
        {
            command.Parameters.AddWithValue("merchant_tag_id", filter.MerchantTagId.Value);
            clauses.Add("""
                EXISTS (
                    SELECT 1
                    FROM transaction_merchant_tags jf
                    WHERE jf.customer_id = t.customer_id
                      AND jf.transaction_id = t.id
                      AND jf.merchant_tag_id = @merchant_tag_id)
                """);
        }
        if (filter.Untagged)
        {
            clauses.Add("""
                NOT EXISTS (
                    SELECT 1
                    FROM transaction_merchant_tags ju
                    WHERE ju.customer_id = t.customer_id
                      AND ju.transaction_id = t.id)
                """);
        }
        if (filter.CategoryKind is not null)
        {
            command.Parameters.AddWithValue("category_kind", filter.CategoryKind);
            clauses.Add("c.kind = @category_kind");
        }
        if (filter.Direction is not null)
        {
            clauses.Add(filter.Direction switch
            {
                "in" => "t.amount > 0",
                "out" => "t.amount < 0",
                "zero" => "t.amount = 0",
                _ => "TRUE",
            });
        }

        if (includeVisibility)
        {
            switch (filter.Visibility)
            {
                case TransactionVisibility.Visible:
                    clauses.Add("NOT (s.is_hidden OR t.is_hidden_by_user)");
                    break;
                case TransactionVisibility.Hidden:
                    clauses.Add("(s.is_hidden OR t.is_hidden_by_user)");
                    break;
            }
        }

        return string.Join(" AND ", clauses);
    }

    public static string VisibilityClause(TransactionVisibility visibility) => visibility switch
    {
        TransactionVisibility.Visible => "NOT (s.is_hidden OR t.is_hidden_by_user)",
        TransactionVisibility.Hidden => "(s.is_hidden OR t.is_hidden_by_user)",
        _ => "TRUE",
    };

    public static string OrderClause(string sortBy,
                                     string sortDirection)
    {
        var direction = sortDirection == "asc" ? "ASC" : "DESC";
        var column = sortBy switch
        {
            "amount" => "t.amount",
            "description" => "t.description",
            _ => "t.transaction_date",
        };
        return $"{column} {direction}, t.id {direction}";
    }

    /// <summary>Escapes the three ILIKE metacharacters; the whole string stays one literal substring.</summary>
    public static string EscapeLike(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value)
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
