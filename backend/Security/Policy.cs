using System.Reflection;
using System.Text;

namespace argus.Server.Security;

/// <summary>
/// Email and password policy shared by every account flow. Rules mirror the
/// frozen kit: canonical email is trim + lowercase, at most 320 bytes, with a
/// non-empty local part and domain; passwords are at least 12 Unicode scalar
/// values, at most 1024 bytes, and not on the embedded common-password list.
/// </summary>
public sealed class Policy
{
    private readonly HashSet<string> _commonPasswords;

    public Policy()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("argus.Server.data.common-passwords.txt") ?? throw new InvalidOperationException("embedded common-passwords.txt is missing");
        using var reader = new StreamReader(stream);

        _commonPasswords = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            _commonPasswords.Add(trimmed.ToLowerInvariant());
        }
    }

    public int CommonPasswordCount => _commonPasswords.Count;

    public string? CanonicalizeEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return null;
        }

        string canonical = email.Trim().ToLowerInvariant();
        if (Encoding.UTF8.GetByteCount(canonical) > 320)
        {
            return null;
        }

        int at = canonical.IndexOf('@');
        if (at <= 0 || at == canonical.Length - 1)
        {
            return null;
        }

        return canonical;
    }

    public bool ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        if (password.EnumerateRunes().Count() < 12)
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(password) > 1024)
        {
            return false;
        }

        return !_commonPasswords.Contains(password.ToLowerInvariant());
    }
}
