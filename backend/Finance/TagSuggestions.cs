namespace argus.Server.Finance;

/// <summary>
/// The single pure whole-token sequence matcher shared by statement review and
/// the transaction detail suggestions. Import outputs must not change: the
/// algorithm is moved here unchanged and both callers use the same ordering.
/// </summary>
public static class TagSuggestions
{
    public static IEnumerable<Guid> Match(string normalizedDescription,
                                          IReadOnlyList<(Guid Id, string[] Tokens)> tags)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescription))
        {
            yield break;
        }

        var descriptionTokens = normalizedDescription.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var (id, tagTokens) in tags)
        {
            if (tagTokens.Length == 0 || tagTokens.Length > descriptionTokens.Length)
            {
                continue;
            }

            var found = false;
            for (var start = 0; start <= descriptionTokens.Length - tagTokens.Length; start++)
            {
                var match = true;
                for (var offset = 0; offset < tagTokens.Length; offset++)
                {
                    if (!string.Equals(
                            descriptionTokens[start + offset], tagTokens[offset],
                            StringComparison.Ordinal))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    found = true;
                    break;
                }
            }

            if (found)
            {
                yield return id;
            }
        }
    }
}
