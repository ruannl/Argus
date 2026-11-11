using argus.Server.Configuration;
using argus.Server.Data;
using argus.Server.Data.Entities;
using argus.Server.Security;
using Microsoft.EntityFrameworkCore;

namespace argus.Server;

/// <summary>
/// Seeds the two local development accounts. Idempotent: re-running keeps
/// existing accounts and never resets a password.
/// </summary>
public static class Seed
{
    public static async Task RunAsync(string databaseUrl,
                                      FinanceStore financeStore,
                                      IDbContextFactory<ArgusDbContext> contextFactory,
                                      AppSettings settings,
                                      ILogger logger,
                                      CancellationToken cancellationToken = default)
    {
        await new InitializationCoordinator(databaseUrl, NoopInitializationFaultInjector.Instance).InitializeAsync(cancellationToken);
        await SeedAccountAsync(financeStore, contextFactory, logger, settings.SeedDevPassword, "dev@example.test", isAdmin: true, isLifetime: true, cancellationToken);
        await SeedAccountAsync(financeStore, contextFactory, logger, settings.SeedUserPassword, "user@example.test", isAdmin: false, isLifetime: false, cancellationToken);
    }

    private static async Task SeedAccountAsync(FinanceStore financeStore,
                                               IDbContextFactory<ArgusDbContext> contextFactory,
                                               ILogger logger,
                                               string? pinnedPassword,
                                               string email,
                                               bool isAdmin,
                                               bool isLifetime,
                                               CancellationToken cancellationToken)
    {
        string password = string.IsNullOrEmpty(pinnedPassword) ? Tokens.NewToken() : pinnedPassword;
        string passwordHash = PasswordHasher.Hash(password);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        List<Customer> inserted = await context.Customers
            .FromSqlInterpolated($"""
                WITH inserted AS (
                    INSERT INTO customers (
                        id, canonical_email, password_hash, email_verified_at,
                        subscriber_activated_at, next_billing_at, is_lifetime, is_admin,
                        created_at, updated_at, failed_login_count, locked_until
                    )
                    VALUES (
                        {Guid.NewGuid()}, {email}, {passwordHash}, NOW(),
                        NOW(), NULL, {isLifetime}, {isAdmin}, NOW(), NOW(), 0, NULL
                    )
                    ON CONFLICT (canonical_email) DO NOTHING
                    RETURNING *
                )
                SELECT * FROM inserted
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);
        if (inserted.Count == 1)
        {
            Console.WriteLine($"seed: created {email} password {password}");
            logger.LogInformation("seed_account email={Email} outcome={Outcome}", email, "created");
            await financeStore.EnsureCategoriesSeededAsync(inserted[0].Id, cancellationToken);
            return;
        }

        Console.WriteLine($"seed: kept existing account {email}");
        logger.LogInformation("seed_account email={Email} outcome={Outcome}", email, "kept");
        Guid? customerId = await context.Customers
            .AsNoTracking()
            .Where(customer => customer.CanonicalEmail == email)
            .Select(customer => (Guid?)customer.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (customerId is Guid existingId)
        {
            await financeStore.EnsureCategoriesSeededAsync(existingId, cancellationToken);
        }
    }
}
