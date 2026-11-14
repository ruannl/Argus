namespace argus.Server.Data;

/// <summary>
/// Read-only startup validation for the normal web host. It never migrates,
/// backfills or opens an owner session; it rejects pending history, schema
/// drift, missing RLS or a privileged runtime identity before routes and
/// hosted services start.
/// </summary>
public static class RuntimeDatabaseValidator
{
    public static async Task ValidateAsync(string databaseUrl, CancellationToken cancellationToken = default)
    {
        var options = PersistenceOptions.Build(databaseUrl);
        await using var context = new ArgusDbContext(options);
        InitializationState state = await SchemaValidator.ValidateAsync(context, requireLedgers: true, cancellationToken);
        if (!state.CanonicalMarkerPresent)
        {
            throw new InvalidOperationException("database startup refused: canonical backfill is not complete");
        }

        await IsolationSchemaValidator.ValidateAsync(context, requireRuntimeLogin: true, cancellationToken);
    }
}
