using Npgsql;

namespace argus.Server.Data;

/// <summary>
/// Finds PostgreSQL errors through EF's wrapping exceptions so public error
/// translation and sanitized logging can keep their existing semantics.
/// </summary>
public static class PersistenceErrors
{
    public static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    public static bool IsUniqueViolation(Exception exception) =>
        FindPostgresException(exception)?.SqlState == PostgresErrorCodes.UniqueViolation;

    public static bool IsCheckViolation(Exception exception) =>
        FindPostgresException(exception)?.SqlState == PostgresErrorCodes.CheckViolation;
}
