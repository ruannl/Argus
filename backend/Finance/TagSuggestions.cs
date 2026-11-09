namespace argus.Server.Finance;

/// <summary>
/// Suggests active merchant tags whose normalized name appears as a whole-token
/// sequence in the normalized transaction description. A statement word may be
/// a truncated prefix of a longer tag word (for example "PHARM" for
/// "Pharmacy"), but a longer statement word is never shortened to a tag word,
/// so "WOOLWORTHSX" does not suggest "Woolworths".
/// </summary>
public static class TagSuggestions
{
    private const int MinimumTagTokenLength = 5;
    private const int MinimumTruncatedTokenLength = 4;

    public static bool Matches(string normalizedDescription, IReadOnlyList<string> tagTokens)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescription) || tagTokens.Count == 0)
        {
            return false;
        }

        string[] descriptionTokens = normalizedDescription.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tagTokens.Count > descriptionTokens.Length)
        {
            return false;
        }

        for (int start = 0; start <= descriptionTokens.Length - tagTokens.Count; start++)
        {
            if (MatchSequenceScore(descriptionTokens, start, tagTokens) > 0)
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<Guid> Match(string normalizedDescription, IReadOnlyList<(Guid Id, string[] Tokens)> tags)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescription))
        {
            yield break;
        }

        string[] descriptionTokens = normalizedDescription.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = new List<(Guid Id, int Score, int Order)>();
        for (int order = 0; order < tags.Count; order++)
        {
            (Guid id, string[] tagTokens) = tags[order];
            if (tagTokens.Length == 0 || tagTokens.Length > descriptionTokens.Length)
            {
                continue;
            }

            int bestScore = 0;
            for (int start = 0; start <= descriptionTokens.Length - tagTokens.Length; start++)
            {
                int score = MatchSequenceScore(descriptionTokens, start, tagTokens);
                if (score > bestScore)
                {
                    bestScore = score;
                }
            }

            if (bestScore > 0)
            {
                matches.Add((id, bestScore, order));
            }
        }

        // Prefer tags with more exact words over tags matched only through a
        // truncated word. Equal scores keep the caller's deterministic order.
        foreach ((Guid id, _, _) in matches.OrderByDescending(match => match.Score).ThenBy(match => match.Order))
        {
            yield return id;
        }
    }

    private static int MatchSequenceScore(string[] descriptionTokens, int start, IReadOnlyList<string> tagTokens)
    {
        int score = 0;
        for (int offset = 0; offset < tagTokens.Count; offset++)
        {
            string descriptionToken = descriptionTokens[start + offset];
            string tagToken = tagTokens[offset];
            if (string.Equals(descriptionToken, tagToken, StringComparison.Ordinal))
            {
                score += 2;
                continue;
            }

            if (IsTruncatedPrefix(descriptionToken, tagToken))
            {
                score += 1;
                continue;
            }

            return 0;
        }

        return score;
    }

    private static bool IsTruncatedPrefix(string descriptionToken, string tagToken) =>
        descriptionToken.Length >= MinimumTruncatedTokenLength &&
        tagToken.Length >= MinimumTagTokenLength &&
        tagToken.StartsWith(descriptionToken, StringComparison.Ordinal);
}
