using argus.Server.Data;

namespace argus.Server.Configuration;

/// <summary>
/// Maintenance-only configuration. It reads the process environment first and
/// an ignored <c>backend/.env.maintenance</c> file second. It never reads the
/// runtime <c>backend/.env</c>, so a normal host and a migrate/seed child do
/// not share elevated values.
/// </summary>
public sealed class MaintenanceSettings
{
    public required string DatabaseUrl { get; init; }
    public string? SeedDevPassword { get; init; }
    public string? SeedUserPassword { get; init; }

    public static MaintenanceSettings Load()
    {
        var envFile = EnvFile.ReadIfPresent(Path.Combine(Directory.GetCurrentDirectory(), ".env.maintenance"));

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

        string databaseUrl = Raw("DATABASE_URL")
            ?? throw new InvalidOperationException($"{Brand.EnvPrefix}DATABASE_URL is required for migrate/seed");
        AppSettings.ValidateDatabaseUrl(databaseUrl);

        string? configuredRole = QueryValue(new Uri(databaseUrl).Query, "role");
        if (configuredRole is not null &&
            !string.Equals(configuredRole, DatabaseIsolationContract.OwnerRole, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"maintenance DATABASE_URL role must be {DatabaseIsolationContract.OwnerRole}");
        }
        if (configuredRole is null)
        {
            string separator = databaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            databaseUrl = $"{databaseUrl}{separator}role={DatabaseIsolationContract.OwnerRole}";
        }

        return new MaintenanceSettings
        {
            DatabaseUrl = databaseUrl,
            SeedDevPassword = Raw("SEED_DEV_PASSWORD"),
            SeedUserPassword = Raw("SEED_USER_PASSWORD"),
        };
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
                return Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return null;
    }
}
