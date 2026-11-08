using System.Security.Cryptography;
using System.Text;

namespace argus.Server.Security;

/// <summary>
/// Breached-password screening using the Pwned Passwords range API with
/// SHA-1 k-anonymity: only the first five hex characters of the hash leave
/// the process. Network failures are fail-open and logged, matching the
/// frozen kit's documented residual limit.
/// </summary>
public sealed class HibpScreener(ILogger<HibpScreener> logger) : IPwnedScreener
{
    private const string Endpoint = "https://api.pwnedpasswords.com/range/";
    private const int NetworkTimeoutMs = 2000;

    private static readonly HttpClient _client = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<bool?> IsPwnedAsync(string password,
                                          CancellationToken cancellationToken = default)
    {
        try
        {
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
            var prefix = hash[..5];
            var suffix = hash[5..];

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(NetworkTimeoutMs);

            using var response = await _client.GetAsync(Endpoint + prefix, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("pwned.check_unavailable status={Status}", (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            foreach (var line in body.Split('\n'))
            {
                var separator = line.IndexOf(':');
                if (separator > 0 && line[..separator].Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "pwned.check_unavailable");
            return null;
        }
    }
}
