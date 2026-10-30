namespace argus.Server.Security;

/// <summary>
/// In-process sliding-window rate limiting with the frozen bucket numbers.
/// This is deliberately per-process and in-memory; that residual limit is
/// documented behaviour, not an oversight.
/// </summary>
public sealed class RateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int SweepThreshold = 1024;

    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<DateTime>> _buckets = new(StringComparer.Ordinal);

    public bool CheckIp(string ip) => Check($"ip:{ip}", 120);

    public bool CheckEmail(string canonicalEmail) =>
        Check($"email:{canonicalEmail}", 5) && Check("email_global", 60);

    public bool CheckLogin(string loginKey) =>
        Check($"login:{loginKey}", 10) && Check("login_global", 60);

    public bool CheckResetRequest(string resetKey) =>
        Check($"reset_request:{resetKey}", 5) && Check("reset_request_global", 60);

    public bool CheckVerifyToken(string tokenHash) => Check($"verify_token:{tokenHash}", 10);

    public bool CheckResetConsume(string tokenHash) => Check($"reset_consume:{tokenHash}", 10);

    private bool Check(string bucket, int limit)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (_buckets.Count > SweepThreshold)
            {
                Sweep(now);
            }

            if (!_buckets.TryGetValue(bucket, out var timestamps))
            {
                timestamps = new Queue<DateTime>();
                _buckets[bucket] = timestamps;
            }

            while (timestamps.Count > 0 && now - timestamps.Peek() > Window)
            {
                timestamps.Dequeue();
            }

            if (timestamps.Count >= limit)
            {
                return false;
            }

            timestamps.Enqueue(now);
            return true;
        }
    }

    private void Sweep(DateTime now)
    {
        var stale = _buckets
            .Where(entry => entry.Value.Count == 0 || now - entry.Value.Peek() > Window)
            .Select(entry => entry.Key)
            .ToList();
        foreach (var key in stale)
        {
            _buckets.Remove(key);
        }
    }
}
