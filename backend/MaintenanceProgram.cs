using argus.Server.Configuration;
using argus.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace argus.Server;

/// <summary>
/// The migrate and seed commands. They run offline against an explicitly
/// assumed owner session and never start routes, cleanup services or the
/// normal host configuration path.
/// </summary>
public static class MaintenanceProgram
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            MaintenanceSettings settings = MaintenanceSettings.Load();
            await ValidateMaintenanceRoleAsync(settings.DatabaseUrl, cancellationToken);

            var coordinator = new InitializationCoordinator(settings.DatabaseUrl, NoopInitializationFaultInjector.Instance);
            await coordinator.InitializeAsync(cancellationToken);

            if (args.Contains("seed", StringComparer.Ordinal))
            {
                using var loggerFactory = LoggerFactory.Create(builder => builder
                    .SetMinimumLevel(LogLevel.Information)
                    .AddSimpleConsole(options => options.SingleLine = true));
                await Seed.RunAsync(
                    settings.DatabaseUrl,
                    settings.SeedDevPassword,
                    settings.SeedUserPassword,
                    loggerFactory.CreateLogger("seed"),
                    cancellationToken);
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    /// <summary>
    /// Requires the maintenance child to log in as the migrator and assume the
    /// owner role. A superuser, owner login or runtime login is rejected.
    /// </summary>
    public static async Task ValidateMaintenanceRoleAsync(string databaseUrl, CancellationToken cancellationToken = default)
    {
        var options = PersistenceOptions.Build(databaseUrl);
        await using var context = new ArgusDbContext(options);
        string sessionUser = await context.Database
            .SqlQueryRaw<string>("SELECT session_user AS \"Value\"")
            .SingleAsync(cancellationToken);
        string currentUser = await context.Database
            .SqlQueryRaw<string>("SELECT current_user AS \"Value\"")
            .SingleAsync(cancellationToken);
        bool sessionIsSuperuser = await context.Database
            .SqlQueryRaw<bool>("SELECT rolsuper AS \"Value\" FROM pg_roles WHERE rolname = session_user")
            .SingleAsync(cancellationToken);

        if (sessionIsSuperuser ||
            !string.Equals(sessionUser, DatabaseIsolationContract.MigratorRole, StringComparison.Ordinal) ||
            !string.Equals(currentUser, DatabaseIsolationContract.OwnerRole, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"maintenance must run as {DatabaseIsolationContract.MigratorRole} with role={DatabaseIsolationContract.OwnerRole}");
        }
    }
}
