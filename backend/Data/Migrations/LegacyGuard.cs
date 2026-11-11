namespace argus.Server.Data.Migrations;

/// <summary>
/// A guarded legacy migration. When the exact legacy change is already
/// recorded, the guard validates the effective shape and skips its DDL; EF
/// still records its own history row in the same migration transaction.
/// </summary>
public static class LegacyGuard
{
    public static string Build(string legacyName, string validationCondition, string ddl) => $"""
DO $argus$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'schema_migrations')
       AND EXISTS (SELECT 1 FROM schema_migrations WHERE name = '{legacyName}') THEN
        IF NOT ({validationCondition}) THEN
            RAISE EXCEPTION 'legacy migration {legacyName} is recorded but the effective schema does not match';
        END IF;
    ELSE
{ddl}
    END IF;
END
$argus$;
""";
}
