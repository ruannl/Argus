using System.Text.RegularExpressions;

namespace argus.Server.Finance;

/// <summary>
/// Builds the generic merchant key used for history-based suggestions. It is
/// deliberately bank-agnostic: split on non-alphanumerics, drop numeric
/// fragments and month names, then keep the first two significant words.
/// Keep this in lockstep with <c>argus_merchant_key</c> in migration 0008.
/// </summary>
public static partial class MerchantKey
{
    private static readonly HashSet<string> _months =
    [
        "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec",
    ];

    public static string? FromDescription(string? normalizedDescription)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescription))
        {
            return null;
        }

        var tokens = new List<string>(2);
        foreach (string token in Separator().Split(normalizedDescription.ToLowerInvariant()))
        {
            if (token.Length <= 1 || token.All(IsAsciiDigit) || _months.Contains(token))
            {
                continue;
            }

            tokens.Add(token);
            if (tokens.Count == 2)
            {
                break;
            }
        }

        return tokens.Count == 0 ? null : string.Join(' ', tokens);
    }

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex Separator();
}
