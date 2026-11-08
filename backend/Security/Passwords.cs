using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace argus.Server.Security;

/// <summary>
/// Argon2id password hashing at the frozen parameters: 64 MiB memory,
/// 3 iterations, 1 lane. The stored value is self-describing so parameters can
/// evolve without breaking existing hashes.
/// </summary>
public static class PasswordHasher
{
    public const int MemoryKib = 64 * 1024;
    public const int Iterations = 3;
    public const int Parallelism = 1;
    private const int HashBytes = 32;
    private const int SaltBytes = 16;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(Encoding.UTF8.GetBytes(password), salt, MemoryKib, Iterations, Parallelism);
        return $"argon2id$v=19$m={MemoryKib},t={Iterations},p={Parallelism}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 5 || parts[0] != "argon2id" || parts[1] != "v=19")
        {
            return false;
        }

        var parameters = parts[2].Split(',');
        if (parameters.Length != 3
            || !TryParseParameter(parameters[0], "m", out var memoryKib)
            || !TryParseParameter(parameters[1], "t", out var iterations)
            || !TryParseParameter(parameters[2], "p", out var parallelism))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Derive(Encoding.UTF8.GetBytes(password), salt, memoryKib, iterations, parallelism);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(byte[] password,
                                 byte[] salt,
                                 int memoryKib,
                                 int iterations,
                                 int parallelism)
    {
        using var argon2 = new Argon2id(password)
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon2.GetBytes(HashBytes);
    }

    private static bool TryParseParameter(string text, string name, out int value)
    {
        value = 0;
        return text.StartsWith(name + "=", StringComparison.Ordinal)
            && int.TryParse(text[(name.Length + 1)..], out value)
            && value > 0;
    }
}

public static class Tokens
{
    public static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static bool HasValidShape(string? token) =>
        token is { Length: 64 }
        && token.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>
/// Breached-password screening. Unavailable results are fail-open, matching
/// the frozen kit's documented residual limit.
/// </summary>
public interface IPwnedScreener
{
    Task<bool?> IsPwnedAsync(string password, CancellationToken cancellationToken = default);
}
