using argus.Server.Data;
using argus.Server.Data.Entities;
using argus.Server.Security;
using Microsoft.EntityFrameworkCore;

namespace argus.Server;

/// <summary>
/// Seeds the two local development accounts under an explicit owner context.
/// Idempotent: re-running keeps existing accounts and never resets a password.
/// The caller supplies a maintenance URL that has already assumed the owner
/// role; this class never uses the normal runtime connection.
/// </summary>
public static class Seed
{
    public static async Task RunAsync(string maintenanceDatabaseUrl,
                                      string? seedDevPassword,
                                      string? seedUserPassword,
                                      ILogger logger,
                                      CancellationToken cancellationToken = default)
    {
        var options = PersistenceOptions.Build(maintenanceDatabaseUrl);
        await using var context = new ArgusDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await SeedAccountAsync(context, logger, seedDevPassword, "dev@example.test", isAdmin: true, isLifetime: true, cancellationToken);
        await SeedAccountAsync(context, logger, seedUserPassword, "user@example.test", isAdmin: false, isLifetime: false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task SeedAccountAsync(ArgusDbContext context,
                                               ILogger logger,
                                               string? pinnedPassword,
                                               string email,
                                               bool isAdmin,
                                               bool isLifetime,
                                               CancellationToken cancellationToken)
    {
        string password = string.IsNullOrEmpty(pinnedPassword) ? Tokens.NewToken() : pinnedPassword;
        string passwordHash = PasswordHasher.Hash(password);

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

        Guid customerId;
        if (inserted.Count == 1)
        {
            customerId = inserted[0].Id;
            Console.WriteLine($"seed: created {email} password {password}");
            logger.LogInformation("seed_account email={Email} outcome={Outcome}", email, "created");
        }
        else
        {
            Customer? existing = await context.Customers
                .AsTracking()
                .SingleOrDefaultAsync(customer => customer.CanonicalEmail == email, cancellationToken);
            if (existing is null)
            {
                throw new InvalidOperationException($"seed insert conflicted but '{email}' was not found");
            }

            customerId = existing.Id;
            Console.WriteLine($"seed: kept existing account {email}");
            logger.LogInformation("seed_account email={Email} outcome={Outcome}", email, "kept");
        }

        await CategoryDefaults.EnsureSeededAsync(context, customerId, customerLocked: false, cancellationToken);
    }
}
