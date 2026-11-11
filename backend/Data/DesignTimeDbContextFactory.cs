using argus.Server.Configuration;
using Microsoft.EntityFrameworkCore.Design;

namespace argus.Server.Data;

/// <summary>
/// Non-applying design-time factory. It does not start the host, load account
/// seeding, or require mail/application secrets. The interceptor makes any
/// attempt to open a connection or execute a command fail closed.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ArgusDbContext>
{
    public ArgusDbContext CreateDbContext(string[] args)
    {
        string databaseUrl = Environment.GetEnvironmentVariable(Brand.EnvPrefix + "DATABASE_URL") ?? AppSettings.DefaultDatabaseUrl;
        var options = PersistenceOptions.Build(
            databaseUrl,
            initializer: false,
            interceptors: [new DenyDatabaseConnectionInterceptor(), new DenyDatabaseCommandInterceptor()]);

        return new ArgusDbContext(options);
    }
}
