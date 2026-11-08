using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace argus.Server.Configuration;

/// <summary>
/// Fail-closed configuration. Values come from prefixed environment variables
/// first, then from <c>backend/.env</c> (never committed) for local development,
/// then from non-secret defaults. Unprefixed aliases are deliberately ignored.
/// </summary>
public sealed class AppSettings
{
    public required string DatabaseUrl { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string ApiSecret { get; init; }
    public required string FrontendUrl { get; init; }
    public required string LogLevel { get; init; }
    public required string LogFormat { get; init; }
    public required string ResendApiKey { get; init; }
    public required string ResendFrom { get; init; }
    public required string BillingWebhookSecret { get; init; }
    public string? SeedDevPassword { get; init; }
    public string? SeedUserPassword { get; init; }

    [SuppressMessage("Major Code Smell", "S1075",
        Justification = "Local development default; ARGUS_DATABASE_URL overrides it.")]
    public const string DefaultDatabaseUrl = "postgres://postgres@127.0.0.1:5432/argus";

    private static readonly string[] _allowedLogLevels = ["trace", "debug", "info", "warn", "error"];
    private static readonly string[] _allowedLogFormats = ["pretty", "json"];

    public static AppSettings Load()
    {
        var envFile = EnvFile.ReadIfPresent(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

        string? Raw(string name)
        {
            string key = Brand.EnvPrefix + name;
            string? fromEnvironment = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(fromEnvironment))
            {
                return fromEnvironment;
            }

            return envFile is not null && envFile.TryGetValue(key, out string? fromFile) ? fromFile : null;
        }

        string Required(string name)
        {
            string? value = Raw(name);
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException($"{Brand.EnvPrefix}{name} is required");
            }

            return value;
        }

        string databaseUrl = Raw("DATABASE_URL") ?? DefaultDatabaseUrl;
        ValidateDatabaseUrl(databaseUrl);

        string apiSecret = Required("API_SECRET");
        RequireMinimumBytes(apiSecret, 32, "API_SECRET");

        string frontendUrl = Required("FRONTEND_URL");
        if (!IsValidOrigin(frontendUrl))
        {
            throw new InvalidOperationException($"{Brand.EnvPrefix}FRONTEND_URL must be an absolute http(s) origin without a path");
        }
        frontendUrl = frontendUrl.TrimEnd('/');

        string resendApiKey = Required("RESEND_API_KEY");
        RequireMinimumBytes(resendApiKey, 20, "RESEND_API_KEY");

        string resendFrom = Required("RESEND_FROM");
        if (!IsMailbox(resendFrom))
        {
            throw new InvalidOperationException($"{Brand.EnvPrefix}RESEND_FROM must be a local@domain mailbox");
        }

        string billingSecret = Required("BILLING_WEBHOOK_SECRET");
        RequireMinimumBytes(billingSecret, 32, "BILLING_WEBHOOK_SECRET");

        string logLevel = Raw("LOG_LEVEL") ?? "info";
        if (!_allowedLogLevels.Contains(logLevel))
        {
            throw new InvalidOperationException($"{Brand.EnvPrefix}LOG_LEVEL must be one of {string.Join(", ", _allowedLogLevels)}");
        }

        string logFormat = Raw("LOG_FORMAT") ?? "pretty";
        if (!_allowedLogFormats.Contains(logFormat))
        {
            throw new InvalidOperationException($"{Brand.EnvPrefix}LOG_FORMAT must be one of {string.Join(", ", _allowedLogFormats)}");
        }

        if (!int.TryParse(Raw("PORT") ?? "3001", out int port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{Brand.EnvPrefix}PORT must be a valid TCP port");
        }

        return new AppSettings
        {
            DatabaseUrl = databaseUrl,
            Host = Raw("HOST") ?? "127.0.0.1",
            Port = port,
            ApiSecret = apiSecret,
            FrontendUrl = frontendUrl,
            LogLevel = logLevel,
            LogFormat = logFormat,
            ResendApiKey = resendApiKey,
            ResendFrom = resendFrom,
            BillingWebhookSecret = billingSecret,
            SeedDevPassword = Raw("SEED_DEV_PASSWORD"),
            SeedUserPassword = Raw("SEED_USER_PASSWORD"),
        };
    }

    private static void RequireMinimumBytes(string value, int minimum, string name)
    {
        if (Encoding.UTF8.GetByteCount(value) < minimum)
        {
            throw new InvalidOperationException($"{Brand.EnvPrefix}{name} must be at least {minimum} bytes");
        }
    }

    private static bool IsValidOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && (uri.AbsolutePath == "/" || uri.AbsolutePath.Length == 0);
    }

    private static bool IsMailbox(string value)
    {
        int at = value.IndexOf('@');

        return at > 0 && at < value.Length - 1 && value.IndexOf('@', at + 1) == -1;
    }

    /// <summary>
    /// A non-local database URL must carry a password and require TLS. This
    /// mirrors the frozen Rust kit's fail-closed database configuration.
    /// </summary>
    private static void ValidateDatabaseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            throw new InvalidOperationException($"{Brand.EnvPrefix}DATABASE_URL must be a postgres:// URL");
        }

        string host = uri.Host.ToLowerInvariant();
        bool isLocal = host is "localhost" or "127.0.0.1" or "::1" or "[::1]";
        if (isLocal)
        {
            return;
        }

        string userInfo = uri.UserInfo;
        bool hasPassword = userInfo.Contains(':') && userInfo[(userInfo.IndexOf(':') + 1)..].Length > 0;
        if (!hasPassword)
        {
            throw new InvalidOperationException($"{Brand.EnvPrefix}DATABASE_URL must include a password for a non-local host");
        }

        string? sslmode = QueryValue(uri.Query, "sslmode");
        if (sslmode is not ("require" or "verify-full"))
        {
            throw new InvalidOperationException($"{Brand.EnvPrefix}DATABASE_URL must set sslmode=require or sslmode=verify-full for a non-local host");
        }
    }

    private static string? QueryValue(string query, string name)
    {
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (pair[..separator].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..]).ToLowerInvariant();
            }
        }

        return null;
    }
}

internal static class EnvFile
{
    /// <summary>
    /// Reads a KEY=VALUE file. Missing files are not an error. Values in the
    /// process environment always win, which callers enforce.
    /// </summary>
    public static Dictionary<string, string>? ReadIfPresent(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            int separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string key = trimmed[..separator].Trim();
            string value = trimmed[(separator + 1)..].Trim();
            values[key] = value;
        }

        return values;
    }
}
