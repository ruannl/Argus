using argus.Server.Finance;
using Npgsql;
using NpgsqlTypes;

namespace argus.Server.Data;

/// <summary>
/// Loads suggestion inputs for a customer: the tags and categories they chose
/// for the same generic merchant key, plus their explicit alias rules.
/// Read-only and bounded by the supplied keys.
/// </summary>
public static class SuggestionLoader
{
    public static async Task<SuggestionData> LoadAsync(NpgsqlConnection connection,
                                                       NpgsqlTransaction? transaction,
                                                       Guid customerId,
                                                       IReadOnlyCollection<string> merchantKeys,
                                                       CancellationToken cancellationToken)
    {
        var tagCounts = new Dictionary<string, Dictionary<Guid, int>>(StringComparer.Ordinal);
        var categoryCounts = new Dictionary<string, Dictionary<Guid, (string Name, int Count)>>(StringComparer.Ordinal);

        string[] keys = merchantKeys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length > 0)
        {
            await LoadTagCountsAsync(connection, transaction, customerId, keys, tagCounts, cancellationToken);
            await LoadCategoryCountsAsync(connection, transaction, customerId, keys, categoryCounts, cancellationToken);
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

        return new SuggestionData(history, await LoadAliasesAsync(connection, transaction, customerId, cancellationToken));
    }

    private static async Task LoadTagCountsAsync(NpgsqlConnection connection,
                                                 NpgsqlTransaction? transaction,
                                                 Guid customerId,
                                                 string[] keys,
                                                 Dictionary<string, Dictionary<Guid, int>> tagCounts,
                                                 CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT t.merchant_key, j.merchant_tag_id, COUNT(*)
            FROM transactions t
            JOIN transaction_merchant_tags j
              ON j.customer_id = t.customer_id AND j.transaction_id = t.id
            JOIN merchant_tags mt
              ON mt.customer_id = j.customer_id AND mt.id = j.merchant_tag_id AND mt.is_archived = FALSE
            WHERE t.customer_id = @customer_id AND t.merchant_key = ANY(@keys)
            GROUP BY t.merchant_key, j.merchant_tag_id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("keys", NpgsqlDbType.Array | NpgsqlDbType.Text, keys);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string key = reader.GetString(0);
            if (!tagCounts.TryGetValue(key, out Dictionary<Guid, int>? counts))
            {
                counts = [];
                tagCounts[key] = counts;
            }
            counts[reader.GetGuid(1)] = (int)reader.GetInt64(2);
        }
    }

    private static async Task LoadCategoryCountsAsync(NpgsqlConnection connection,
                                                      NpgsqlTransaction? transaction,
                                                      Guid customerId,
                                                      string[] keys,
                                                      Dictionary<string, Dictionary<Guid, (string Name, int Count)>> categoryCounts,
                                                      CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT t.merchant_key, t.category_id, c.display_name, COUNT(*)
            FROM transactions t
            JOIN categories c
              ON c.customer_id = t.customer_id AND c.id = t.category_id
            WHERE t.customer_id = @customer_id AND t.merchant_key = ANY(@keys)
              AND c.kind <> 'Uncategorised' AND c.is_archived = FALSE
            GROUP BY t.merchant_key, t.category_id, c.display_name
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("keys", NpgsqlDbType.Array | NpgsqlDbType.Text, keys);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string key = reader.GetString(0);
            if (!categoryCounts.TryGetValue(key, out Dictionary<Guid, (string Name, int Count)>? counts))
            {
                counts = [];
                categoryCounts[key] = counts;
            }
            counts[reader.GetGuid(1)] = (reader.GetString(2), (int)reader.GetInt64(3));
        }
    }

    private static async Task<List<AliasRule>> LoadAliasesAsync(NpgsqlConnection connection,
                                                               NpgsqlTransaction? transaction,
                                                               Guid customerId,
                                                               CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT a.id, a.normalized_alias, a.merchant_tag_id,
                   CASE WHEN c.is_archived = FALSE THEN a.category_id ELSE NULL END
            FROM merchant_aliases a
            JOIN merchant_tags mt
              ON mt.customer_id = a.customer_id AND mt.id = a.merchant_tag_id AND mt.is_archived = FALSE
            LEFT JOIN categories c
              ON c.customer_id = a.customer_id AND c.id = a.category_id
            WHERE a.customer_id = @customer_id
            ORDER BY a.normalized_alias, a.id
            """;
        command.Parameters.AddWithValue("customer_id", customerId);

        var aliases = new List<AliasRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string normalizedAlias = reader.GetString(1);
            aliases.Add(new AliasRule(
                reader.GetGuid(0),
                normalizedAlias.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3)));
        }

        return aliases;
    }
}
