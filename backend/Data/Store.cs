using Npgsql;
using argus.Server.Security;

namespace argus.Server.Data;

/// <summary>
/// All account-authority persistence. One truth lives in PostgreSQL; every
/// statement here is the only writer for its fact.
/// </summary>
public sealed class Store(Db db)
{
    public const string CustomerColumns =
        "id, canonical_email, password_hash, email_verified_at, subscriber_activated_at, " +
        "next_billing_at, is_lifetime, is_admin, created_at, updated_at, failed_login_count, locked_until";

    public static readonly TimeSpan SessionAbsoluteTtl = TimeSpan.FromHours(12);
    public static readonly TimeSpan SessionIdleWindow = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan VerificationTokenTtl = TimeSpan.FromHours(24);
    public static readonly TimeSpan ResetTokenTtl = TimeSpan.FromHours(1);

    private static CustomerRecord MapCustomer(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTime>(3),
        reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTime>(4),
        reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTime>(5),
        reader.GetBoolean(6),
        reader.GetBoolean(7),
        reader.GetFieldValue<DateTime>(8),
        reader.GetFieldValue<DateTime>(9),
        reader.GetInt32(10),
        reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTime>(11));

    // --- Customers ----------------------------------------------------------

    public async Task<CustomerRecord?> FindCustomerByEmailAsync(
        string canonicalEmail,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {CustomerColumns} FROM customers WHERE canonical_email = @email";
        command.Parameters.AddWithValue("email", canonicalEmail);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapCustomer(reader) : null;
    }

    public async Task<CustomerRecord?> FindCustomerByIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {CustomerColumns} FROM customers WHERE id = @id";
        command.Parameters.AddWithValue("id", customerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapCustomer(reader) : null;
    }

    public async Task<(CustomerRecord Customer, bool Created)> InsertCustomerAsync(
        string canonicalEmail,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = $"""
                INSERT INTO customers (id, canonical_email, password_hash, created_at, updated_at)
                VALUES (@id, @email, @hash, NOW(), NOW())
                ON CONFLICT (canonical_email) DO NOTHING
                RETURNING {CustomerColumns}
                """;
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("email", canonicalEmail);
            insert.Parameters.AddWithValue("hash", passwordHash);
            await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return (MapCustomer(reader), true);
            }
        }

        var existing = await FindCustomerByEmailAsync(canonicalEmail, cancellationToken);
        return existing is null
            ? throw new InvalidOperationException("customer insert conflicted but no row exists")
            : (existing, false);
    }

    /// <summary>
    /// Adds one failed login. Returns true exactly when this failure crossed the
    /// threshold and locked the account for 900 seconds.
    /// </summary>
    public async Task<bool> RecordFailedLoginAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE customers
            SET failed_login_count = failed_login_count + 1,
                locked_until = CASE
                    WHEN failed_login_count + 1 >= 5 THEN NOW() + INTERVAL '900 seconds'
                    ELSE locked_until
                END,
                updated_at = NOW()
            WHERE id = @id
            RETURNING failed_login_count >= 5
            """;
        command.Parameters.AddWithValue("id", customerId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    // --- Sessions -----------------------------------------------------------

    /// <summary>
    /// Issues only against the exact encoded hash that the login handler
    /// verified. Holding the customer row through commit serializes issuance
    /// with password reset's hash replacement and session revocation.
    /// </summary>
    public async Task<(string Token, DateTime ExpiresAt)?> CreateSessionForVerifiedHashAsync(
        Guid customerId,
        string verifiedPasswordHash,
        CancellationToken cancellationToken = default)
    {
        var token = Tokens.NewToken();
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = """
                SELECT password_hash, email_verified_at, subscriber_activated_at, locked_until
                FROM customers WHERE id = @id FOR UPDATE
                """;
            lookup.Parameters.AddWithValue("id", customerId);
            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)
                || !string.Equals(reader.GetString(0), verifiedPasswordHash, StringComparison.Ordinal)
                || reader.IsDBNull(1)
                || reader.IsDBNull(2)
                || (!reader.IsDBNull(3) && reader.GetFieldValue<DateTime>(3) > DateTime.UtcNow))
            {
                await reader.CloseAsync();
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = """
                UPDATE customers
                SET failed_login_count = 0, locked_until = NULL, updated_at = NOW()
                WHERE id = @id
                """;
            clear.Parameters.AddWithValue("id", customerId);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        var now = DateTime.UtcNow;
        var expiresAt = now.Add(SessionAbsoluteTtl);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO sessions (id, customer_id, token_hash, expires_at, revoked_at, last_seen_at, created_at)
                VALUES (@id, @customer_id, @token_hash, @expires_at, NULL, @now, @now)
                """;
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("customer_id", customerId);
            insert.Parameters.AddWithValue("token_hash", Tokens.HashToken(token));
            insert.Parameters.AddWithValue("expires_at", expiresAt);
            insert.Parameters.AddWithValue("now", now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (token, expiresAt);
    }

    /// <summary>
    /// Returns the owning customer when the session is not revoked, not past its
    /// absolute expiry, and not idle beyond the idle window; touching the row
    /// and enforcing the idle check happen in one conditional UPDATE.
    /// </summary>
    public async Task<Guid?> ValidateAndTouchSessionAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE sessions
            SET last_seen_at = NOW()
            WHERE token_hash = @token_hash
              AND revoked_at IS NULL
              AND expires_at > NOW()
              AND last_seen_at > NOW() - INTERVAL '30 minutes'
            RETURNING customer_id
            """;
        command.Parameters.AddWithValue("token_hash", tokenHash);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid customerId ? customerId : null;
    }

    /// <summary>Revokes one session; returns the owner when it revoked a live row.</summary>
    public async Task<Guid?> RevokeSessionAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE sessions
            SET revoked_at = NOW()
            WHERE token_hash = @token_hash AND revoked_at IS NULL
            RETURNING customer_id
            """;
        command.Parameters.AddWithValue("token_hash", tokenHash);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid customerId ? customerId : null;
    }

    // --- Email verification tokens -----------------------------------------

    public async Task<string> CreateVerificationTokenAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var token = Tokens.NewToken();
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO email_verification_tokens (id, customer_id, token_hash, expires_at, consumed_at, created_at)
            VALUES (@id, @customer_id, @token_hash, @expires_at, NULL, NOW())
            """;
        AddTokenParameters(command, customerId, token, VerificationTokenTtl);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return token;
    }

    /// <summary>Consumes every open verification token, then issues exactly one new token.</summary>
    public async Task<string> RotateVerificationTokenAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var consume = connection.CreateCommand())
        {
            consume.Transaction = transaction;
            consume.CommandText = """
                UPDATE email_verification_tokens SET consumed_at = NOW()
                WHERE customer_id = @customer_id AND consumed_at IS NULL
                """;
            consume.Parameters.AddWithValue("customer_id", customerId);
            await consume.ExecuteNonQueryAsync(cancellationToken);
        }

        var token = Tokens.NewToken();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO email_verification_tokens (id, customer_id, token_hash, expires_at, consumed_at, created_at)
                VALUES (@id, @customer_id, @token_hash, @expires_at, NULL, NOW())
                """;
            AddTokenParameters(insert, customerId, token, VerificationTokenTtl);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return token;
    }

    /// <summary>
    /// Consumes one verification token and activates the customer. Admin and
    /// lifetime accounts cannot be activated by token, matching the frozen
    /// behaviour; the transaction rolls back so the token stays unconsumed.
    /// </summary>
    public async Task<Guid?> ConsumeVerificationAndActivateAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        Guid customerId;
        await using (var consume = connection.CreateCommand())
        {
            consume.Transaction = transaction;
            consume.CommandText = """
                UPDATE email_verification_tokens SET consumed_at = NOW()
                WHERE token_hash = @token_hash AND consumed_at IS NULL AND expires_at > NOW()
                RETURNING customer_id
                """;
            consume.Parameters.AddWithValue("token_hash", tokenHash);
            var result = await consume.ExecuteScalarAsync(cancellationToken);
            if (result is not Guid found)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
            customerId = found;
        }

        await using (var activate = connection.CreateCommand())
        {
            activate.Transaction = transaction;
            activate.CommandText = """
                UPDATE customers
                SET email_verified_at = COALESCE(email_verified_at, NOW()),
                    subscriber_activated_at = COALESCE(subscriber_activated_at, NOW()),
                    updated_at = NOW()
                WHERE id = @id AND is_admin = FALSE AND is_lifetime = FALSE
                """;
            activate.Parameters.AddWithValue("id", customerId);
            if (await activate.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = """
                UPDATE email_verification_tokens SET consumed_at = NOW()
                WHERE customer_id = @id AND consumed_at IS NULL
                """;
            clear.Parameters.AddWithValue("id", customerId);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return customerId;
    }

    // --- Password reset tokens ---------------------------------------------

    public async Task<ResetRequestOutcome> RequestPasswordResetAsync(
        string canonicalEmail,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        Guid customerId;
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = """
                SELECT id FROM customers
                WHERE canonical_email = @email AND email_verified_at IS NOT NULL
                """;
            lookup.Parameters.AddWithValue("email", canonicalEmail);
            var result = await lookup.ExecuteScalarAsync(cancellationToken);
            if (result is not Guid found)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ResetRequestOutcome.Shade();
            }
            customerId = found;
        }

        await using (var consume = connection.CreateCommand())
        {
            consume.Transaction = transaction;
            consume.CommandText = """
                UPDATE password_reset_tokens SET consumed_at = NOW()
                WHERE customer_id = @customer_id AND consumed_at IS NULL
                """;
            consume.Parameters.AddWithValue("customer_id", customerId);
            await consume.ExecuteNonQueryAsync(cancellationToken);
        }

        var token = Tokens.NewToken();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO password_reset_tokens (id, customer_id, token_hash, expires_at, consumed_at, created_at)
                VALUES (@id, @customer_id, @token_hash, @expires_at, NULL, NOW())
                """;
            AddTokenParameters(insert, customerId, token, ResetTokenTtl);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ResetRequestOutcome.Issued(customerId, canonicalEmail, token);
    }

    /// <summary>
    /// Consumes one reset token, replaces the password hash, and revokes every
    /// live session in the same transaction.
    /// </summary>
    public async Task<Guid?> ConsumePasswordResetAsync(
        string tokenHash,
        string newPasswordHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        Guid customerId;
        await using (var consume = connection.CreateCommand())
        {
            consume.Transaction = transaction;
            consume.CommandText = """
                UPDATE password_reset_tokens SET consumed_at = NOW()
                WHERE token_hash = @token_hash AND consumed_at IS NULL AND expires_at > NOW()
                RETURNING customer_id
                """;
            consume.Parameters.AddWithValue("token_hash", tokenHash);
            var result = await consume.ExecuteScalarAsync(cancellationToken);
            if (result is not Guid found)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
            customerId = found;
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE customers SET password_hash = @password_hash, updated_at = NOW()
                WHERE id = @id
                """;
            update.Parameters.AddWithValue("password_hash", newPasswordHash);
            update.Parameters.AddWithValue("id", customerId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var revoke = connection.CreateCommand())
        {
            revoke.Transaction = transaction;
            revoke.CommandText = """
                UPDATE sessions SET revoked_at = NOW()
                WHERE customer_id = @id AND revoked_at IS NULL
                """;
            revoke.Parameters.AddWithValue("id", customerId);
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return customerId;
    }

    // --- Billing entitlement events ----------------------------------------

    /// <summary>
    /// Applies a provider-agnostic billing event. Unknown customers are a no-op
    /// success: webhook deliveries are retried, and existence must not leak.
    /// </summary>
    public async Task ApplyBillingEventAsync(
        string eventType,
        Guid customerId,
        DateTime? nextBillingAt,
        CancellationToken cancellationToken = default)
    {
        string? sql = eventType switch
        {
            "subscription.activated" => """
                UPDATE customers
                SET subscriber_activated_at = COALESCE(subscriber_activated_at, NOW()),
                    next_billing_at = @next_billing_at
                WHERE id = @id
                """,
            "subscription.cancelled" => """
                UPDATE customers
                SET subscriber_activated_at = NULL,
                    next_billing_at = NULL
                WHERE id = @id
                """,
            // payment.failed is advisory: no entitlement change in this kit.
            _ => null,
        };
        if (sql is null)
        {
            return;
        }

        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", customerId);
        if (eventType == "subscription.activated")
        {
            command.Parameters.AddWithValue("next_billing_at", (object?)nextBillingAt ?? DBNull.Value);
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddTokenParameters(
        NpgsqlCommand command,
        Guid customerId,
        string token,
        TimeSpan ttl)
    {
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("token_hash", Tokens.HashToken(token));
        command.Parameters.AddWithValue("expires_at", DateTime.UtcNow.Add(ttl));
    }
}
