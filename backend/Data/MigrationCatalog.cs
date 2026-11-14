namespace argus.Server.Data;

/// <summary>
/// The supported EF migration prefix. The ten legacy wrapper identities stay
/// immutable; every later migration is an EF-only ordered suffix. The legacy
/// schema_migrations ledger continues to represent only the ten historical
/// names.
/// </summary>
public static class MigrationCatalog
{
    public const string CustomerRowSecurityEfId = "20261114000011_0011_customer_row_security";

    public static readonly IReadOnlyList<string> EfIds =
        [.. LegacyMigrations.All.Select(migration => migration.EfId), CustomerRowSecurityEfId];

    public static readonly IReadOnlyList<string> LegacyNames =
        LegacyMigrations.All.Select(migration => migration.LegacyName).ToArray();

    public const int LegacyCount = 10;

    public const int LatestPrefix = 11;

    public static bool IsLegacyEfId(string id) =>
        LegacyMigrations.OrderForEfId(id) is not null;

    public static bool IsKnownEfId(string id) =>
        EfIds.Contains(id, StringComparer.Ordinal);

    public static bool IsSupportedEfPrefix(IReadOnlyList<string> ids)
    {
        if (ids.Count > EfIds.Count)
        {
            return false;
        }

        for (int index = 0; index < ids.Count; index++)
        {
            if (!string.Equals(ids[index], EfIds[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsSupportedLegacyPrefix(IReadOnlyList<string> names)
    {
        if (names.Count > LegacyNames.Count)
        {
            return false;
        }

        for (int index = 0; index < names.Count; index++)
        {
            if (!string.Equals(names[index], LegacyNames[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
