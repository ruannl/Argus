using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace argus.Server.Data;

/// <summary>
/// One shared provider/model/connection configuration for runtime, explicit
/// seed, the initialization coordinator and design-time tooling. The
/// initializer profile deliberately disables client pooling and multiplexing
/// because the coordinator holds one physical session for the whole protocol.
/// </summary>
public static class PersistenceOptions
{
    public static DbContextOptions<ArgusDbContext> Build(string databaseUrl,
                                                         bool initializer = false,
                                                         IEnumerable<IInterceptor>? interceptors = null)
    {
        string connectionString = BuildConnectionString(databaseUrl);
        if (initializer)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Pooling = false,
                Multiplexing = false,
            };
            connectionString = builder.ConnectionString;
        }

        var options = new DbContextOptionsBuilder<ArgusDbContext>();
        Configure(options, connectionString);
        if (interceptors is not null)
        {
            options.AddInterceptors(interceptors);
        }

        return options.Options;
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsAssembly(typeof(ArgusDbContext).Assembly.FullName);
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory");
        });
        options.EnableDetailedErrors(false);
        options.EnableSensitiveDataLogging(false);
    }

    /// <summary>
    /// Converts the provider-agnostic database URL into the Npgsql connection
    /// string. Unknown query options are ignored; sslmode and search_path are
    /// the supported overrides.
    /// </summary>
    public static string BuildConnectionString(string databaseUrl)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("database URL is not a valid URI");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
        };

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            string[] parts = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length == 2)
            {
                builder.Password = Uri.UnescapeDataString(parts[1]);
            }
        }

        string? sslmode = QueryValue(uri.Query, "sslmode");
        if (sslmode is not null)
        {
            builder.SslMode = sslmode switch
            {
                "disable" => SslMode.Disable,
                "prefer" => SslMode.Prefer,
                "require" => SslMode.Require,
                "verify-ca" => SslMode.VerifyCA,
                "verify-full" => SslMode.VerifyFull,
                _ => throw new InvalidOperationException($"unsupported sslmode '{sslmode}'"),
            };
        }

        string? searchPath = QueryValue(uri.Query, "search_path");
        if (searchPath is not null)
        {
            builder.SearchPath = searchPath;
        }

        string? role = QueryValue(uri.Query, "role");
        if (role is not null)
        {
            if (role.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
            {
                throw new InvalidOperationException("database URL role must be a simple identifier");
            }

            builder.Options = $"-c role={role}";
        }

        return builder.ConnectionString;
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

