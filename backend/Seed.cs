using argus.Server.Configuration;
using argus.Server.Data;
using argus.Server.Security;

namespace argus.Server;

/// <summary>
/// Seeds the two local development accounts. Idempotent: re-running keeps
/// existing accounts and never resets a password.
/// </summary>
public static class Seed
{
    public static async Task RunAsync(
        Db db,
        AppSettings settings,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await db.MigrateAsync(cancellationToken);
        await SeedAccountAsync(
            db,
            logger,
            settings.SeedDevPassword,
            "dev@example.test",
            isAdmin: true,
            isLifetime: true,
            cancellationToken);
        await SeedAccountAsync(
            db,
            logger,
            settings.SeedUserPassword,
            "user@example.test",
            isAdmin: false,
            isLifetime: false,
            cancellationToken);
    }

    private static async Task SeedAccountAsync(
        Db db,
        ILogger logger,
        string? pinnedPassword,
        string email,
        bool isAdmin,
        bool isLifetime,
        CancellationToken cancellationToken)
    {
        var password = string.IsNullOrEmpty(pinnedPassword) ? Tokens.NewToken() : pinnedPassword;
        var passwordHash = PasswordHasher.Hash(password);

        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO customers (
                id, canonical_email, password_hash, email_verified_at,
                subscriber_activated_at, next_billing_at, is_lifetime, is_admin,
                created_at, updated_at, failed_login_count, locked_until
            )
            VALUES (@id, @email, @hash, NOW(), NOW(), NULL, @lifetime, @admin, NOW(), NOW(), 0, NULL)
            ON CONFLICT (canonical_email) DO NOTHING
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("hash", passwordHash);
        command.Parameters.AddWithValue("lifetime", isLifetime);
        command.Parameters.AddWithValue("admin", isAdmin);

        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (inserted == 1)
        {
            Console.WriteLine($"seed: created {email} password {password}");
            logger.LogInformation("seed_account email={Email} outcome={Outcome}", email, "created");
        }
        else
        {
            Console.WriteLine($"seed: kept existing account {email}");
            logger.LogInformation("seed_account email={Email} outcome={Outcome}", email, "kept");
        }
    }
}
