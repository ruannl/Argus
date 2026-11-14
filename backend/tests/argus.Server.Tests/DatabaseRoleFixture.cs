using System.Text.RegularExpressions;
using argus.Server.Data;
using Npgsql;

namespace argus.Server.Tests;

/// <summary>
/// Test-only cluster-role fixture. It provisions the production role model in
/// the disposable test database, using only local synthetic credentials. It
/// never runs against the development connection.
/// </summary>
public static class DatabaseRoleFixture
{
    private static readonly string _runtimePassword = Guid.NewGuid().ToString("N");
    private static readonly string _migratorPassword = Guid.NewGuid().ToString("N");
    private static readonly object _gate = new();
    private static bool _ensured;

    public static string RuntimePassword => _runtimePassword;

    public static string MigratorPassword => _migratorPassword;

    public static async Task EnsureAsync(string databaseUrl, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_ensured)
            {
                return;
            }
        }

        await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(databaseUrl));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DO $roles$
            DECLARE
                r record;
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{DatabaseIsolationContract.OwnerRole}') THEN
                    CREATE ROLE {DatabaseIsolationContract.OwnerRole} NOLOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT;
                ELSE
                    ALTER ROLE {DatabaseIsolationContract.OwnerRole} NOLOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{DatabaseIsolationContract.MigratorRole}') THEN
                    CREATE ROLE {DatabaseIsolationContract.MigratorRole} LOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT PASSWORD '{_migratorPassword}';
                ELSE
                    ALTER ROLE {DatabaseIsolationContract.MigratorRole} LOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT PASSWORD '{_migratorPassword}';
                END IF;

                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{DatabaseIsolationContract.RuntimeRole}') THEN
                    CREATE ROLE {DatabaseIsolationContract.RuntimeRole} LOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT NOCREATEDB NOCREATEROLE PASSWORD '{_runtimePassword}';
                ELSE
                    ALTER ROLE {DatabaseIsolationContract.RuntimeRole} LOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT NOCREATEDB NOCREATEROLE PASSWORD '{_runtimePassword}';
                END IF;

                FOR r IN SELECT roleid FROM pg_auth_members WHERE member = '{DatabaseIsolationContract.RuntimeRole}'::regrole LOOP
                    EXECUTE format('REVOKE %s FROM {DatabaseIsolationContract.RuntimeRole}', r.roleid::regrole);
                END LOOP;
            END
            $roles$;

            GRANT {DatabaseIsolationContract.OwnerRole} TO {DatabaseIsolationContract.MigratorRole};
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        lock (_gate)
        {
            _ensured = true;
        }
    }

    public static string RuntimeUrl(string databaseUrl) =>
        ReplaceUserInfo(databaseUrl, DatabaseIsolationContract.RuntimeRole, _runtimePassword);

    public static string MigratorUrl(string databaseUrl) =>
        ReplaceUserInfo(databaseUrl, DatabaseIsolationContract.MigratorRole, _migratorPassword);

    public static string OwnerUrl(string databaseUrl) =>
        AppendRole(databaseUrl, DatabaseIsolationContract.OwnerRole);

    /// <summary>
    /// Migration URL that logs in as the non-superuser migrator and explicitly
    /// assumes the owner role, matching the production maintenance path.
    /// </summary>
    public static string MigrationSchemaUrl(string databaseUrl, string schema) =>
        AppendRole(
            PersistenceTestSupport.SchemaUrl(MigratorUrl(databaseUrl), schema),
            DatabaseIsolationContract.OwnerRole);

    public static string RuntimeSchemaUrl(string databaseUrl, string schema) =>
        PersistenceTestSupport.SchemaUrl(RuntimeUrl(databaseUrl), schema);

    public static string WithSearchPath(string databaseUrl, string schema) =>
        PersistenceTestSupport.SchemaUrl(databaseUrl, schema);

    public static string AppendRole(string databaseUrl, string role)
    {
        if (databaseUrl.Contains("role=", StringComparison.Ordinal))
        {
            return databaseUrl;
        }

        string separator = databaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";

        return $"{databaseUrl}{separator}role={role}";
    }

    public static string ReplaceUserInfo(string databaseUrl, string userName, string password)
    {
        var uri = new Uri(databaseUrl);
        var builder = new UriBuilder(uri)
        {
            UserName = userName,
            Password = password,
        };

        return builder.Uri.ToString();
    }

    public static string SchemaFromUrl(string schemaUrl)
    {
        const string marker = "search_path=";
        int index = schemaUrl.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException("schema URL does not contain search_path");
        }

        string remainder = schemaUrl[(index + marker.Length)..];
        int separator = remainder.IndexOf('&', StringComparison.Ordinal);
        string encoded = separator < 0 ? remainder : remainder[..separator];

        return Uri.UnescapeDataString(encoded);
    }

    public static string DatabaseFromSchemaUrl(string schemaUrl)
    {
        const string marker = "search_path=";
        int index = schemaUrl.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException("schema URL does not contain search_path");
        }

        return schemaUrl[..(index - 1)];
    }

    public static string ValidateSchemaIdentifier(string schema)
    {
        if (!Regex.IsMatch(schema, "^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("test schema name must be a lowercase identifier");
        }

        return schema;
    }
}
