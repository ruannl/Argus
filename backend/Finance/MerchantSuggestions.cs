namespace argus.Server.Finance;

public sealed record AliasRule(Guid Id, string[] Tokens, Guid TagId, Guid? CategoryId);

public sealed record HistoryRule(IReadOnlyDictionary<Guid, int> TagCounts, Guid? CategoryId);

public sealed record SuggestionData(IReadOnlyDictionary<string, HistoryRule> History,
                                    IReadOnlyList<AliasRule> Aliases)
{
    public static SuggestionData Empty { get; } = new(
        new Dictionary<string, HistoryRule>(StringComparer.Ordinal),
        []);
}

public sealed record SuggestionResult(Guid[] TagIds, Guid? CategoryId);

/// <summary>
/// Combines explicit user aliases, the user's own historical choices, and the
/// token fallback. Aliases win over history; history wins over token matching.
/// Suggestions are advisory and are capped for a bounded response.
/// </summary>
public static class MerchantSuggestions
{
    public static SuggestionResult Build(string normalizedDescription,
                                         IReadOnlyList<(Guid Id, string[] Tokens)> activeTags,
                                         SuggestionData data)
    {
        var ordered = new List<Guid>();
        var seen = new HashSet<Guid>();
        Guid? categoryId = null;

        void AddTag(Guid id)
        {
            if (seen.Add(id))
            {
                ordered.Add(id);
            }
        }

        foreach (AliasRule alias in data.Aliases)
        {
            if (!TagSuggestions.Matches(normalizedDescription, alias.Tokens))
            {
                continue;
            }

            AddTag(alias.TagId);
            categoryId ??= alias.CategoryId;
        }

        string? key = MerchantKey.FromDescription(normalizedDescription);
        if (key is not null && data.History.TryGetValue(key, out HistoryRule? history))
        {
            Dictionary<Guid, int> order = activeTags
                .Select((tag, index) => (tag.Id, index))
                .ToDictionary(entry => entry.Id, entry => entry.index);
            foreach (Guid tagId in history.TagCounts
                         .OrderByDescending(entry => entry.Value)
                         .ThenBy(entry => order.GetValueOrDefault(entry.Key, int.MaxValue))
                         .ThenBy(entry => entry.Key)
                         .Select(entry => entry.Key))
            {
                AddTag(tagId);
            }
            categoryId ??= history.CategoryId;
        }

        foreach (Guid tagId in TagSuggestions.Match(normalizedDescription, activeTags))
        {
            AddTag(tagId);
        }

        return new SuggestionResult(ordered.ToArray(), categoryId);
    }
}
