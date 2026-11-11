using argus.Server.Data.Entities;
using argus.Server.Security;
using Microsoft.EntityFrameworkCore;

namespace argus.Server.Data;

/// <summary>
/// All account-authority persistence. One truth lives in PostgreSQL; every
/// statement here is the only writer for its fact.
/// </summary>
public sealed record CustomerRecord(Guid Id,
                                    string CanonicalEmail,
                                    string PasswordHash,
                                    DateTime? EmailVerifiedAt,
                                    DateTime? SubscriberActivatedAt,
                                    DateTime? NextBillingAt,
                                    bool IsLifetime,
                                    bool IsAdmin,
                                    DateTime CreatedAt,
                                    DateTime UpdatedAt,
                                    int FailedLoginCount,
                                    DateTime? LockedUntil);

public abstract record ResetRequestOutcome
{
    public sealed record Issued(Guid CustomerId, string Recipient, string RawToken) : ResetRequestOutcome;

    public sealed record Shade : ResetRequestOutcome;
}

public sealed class Store(IDbContextFactory<ArgusDbContext> contextFactory)
{
    public static readonly TimeSpan SessionAbsoluteTtl = TimeSpan.FromHours(12);
    public static readonly TimeSpan SessionIdleWindow = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan VerificationTokenTtl = TimeSpan.FromHours(24);
    public static readonly TimeSpan ResetTokenTtl = TimeSpan.FromHours(1);

    private static CustomerRecord MapCustomer(Customer customer) => new(
        customer.Id,
        customer.CanonicalEmail,
        customer.PasswordHash,
        customer.EmailVerifiedAt,
        customer.SubscriberActivatedAt,
        customer.NextBillingAt,
        customer.IsLifetime,
        customer.IsAdmin,
        customer.CreatedAt,
        customer.UpdatedAt,
        customer.FailedLoginCount,
        customer.LockedUntil);

    // --- Customers ----------------------------------------------------------

    public async Task<CustomerRecord?> FindCustomerByEmailAsync(string canonicalEmail, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        Customer? customer = await context.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CanonicalEmail == canonicalEmail, cancellationToken);

        return customer is null ? null : MapCustomer(customer);
    }

    public async Task<CustomerRecord?> FindCustomerByIdAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        Customer? customer = await context.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == customerId, cancellationToken);

        return customer is null ? null : MapCustomer(customer);
    }

    public async Task<(CustomerRecord Customer, bool Created)> InsertCustomerAsync(string canonicalEmail, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        List<Customer> inserted = await context.Customers
            .FromSqlInterpolated($"""
                WITH inserted AS (
                    INSERT INTO customers (id, canonical_email, password_hash, created_at, updated_at)
                    VALUES ({Guid.NewGuid()}, {canonicalEmail}, {passwordHash}, NOW(), NOW())
                    ON CONFLICT (canonical_email) DO NOTHING
                    RETURNING *
                )
                SELECT * FROM inserted
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);
        if (inserted.Count > 0)
        {
            return (MapCustomer(inserted[0]), true);
        }

        var existing = await FindCustomerByEmailAsync(canonicalEmail, cancellationToken);

        return existing is null ? throw new InvalidOperationException("customer insert conflicted but no row exists") : (existing, false);
    }

    /// <summary>
    /// Adds one failed login. Returns true exactly when this failure crossed the
    /// threshold and locked the account for 900 seconds.
    /// </summary>
    public async Task<bool> RecordFailedLoginAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        List<Customer> updated = await context.Customers
            .FromSqlInterpolated($"""
                UPDATE customers
                SET failed_login_count = failed_login_count + 1,
                    locked_until = CASE
                        WHEN failed_login_count + 1 >= 5 THEN NOW() + INTERVAL '900 seconds'
                        ELSE locked_until
                    END,
                    updated_at = NOW()
                WHERE id = {customerId}
                RETURNING *
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);

        return updated.Count > 0 && updated[0].FailedLoginCount >= 5;
    }

    // --- Sessions -----------------------------------------------------------

    /// <summary>
    /// Issues only against the exact encoded hash that the login handler
    /// verified. Holding the customer row through commit serializes issuance
    /// with password reset's hash replacement and session revocation.
    /// </summary>
    public async Task<(string Token, DateTime ExpiresAt)?> CreateSessionForVerifiedHashAsync(Guid customerId, string verifiedPasswordHash, CancellationToken cancellationToken = default)
    {
        string token = Tokens.NewToken();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        List<CredentialRow> locked = await context.Database.SqlQuery<CredentialRow>($"""
            SELECT password_hash AS "PasswordHash",
                   email_verified_at AS "EmailVerifiedAt",
                   subscriber_activated_at AS "SubscriberActivatedAt",
                   locked_until AS "LockedUntil"
            FROM customers
            WHERE id = {customerId}
            FOR UPDATE
            """).ToListAsync(cancellationToken);
        CredentialRow? customer = locked.SingleOrDefault();
        var now = DateTime.UtcNow;
        if (customer is null ||
            !string.Equals(customer.PasswordHash, verifiedPasswordHash, StringComparison.Ordinal) ||
            customer.EmailVerifiedAt is null ||
            customer.SubscriberActivatedAt is null ||
            (customer.LockedUntil is not null && customer.LockedUntil > now))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await context.Customers
            .Where(item => item.Id == customerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.FailedLoginCount, 0)
                .SetProperty(item => item.LockedUntil, (DateTime?)null)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        context.Sessions.Add(new Session
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            TokenHash = Tokens.HashToken(token),
            ExpiresAt = now.Add(SessionAbsoluteTtl),
            RevokedAt = null,
            LastSeenAt = now,
            CreatedAt = now,
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (token, now.Add(SessionAbsoluteTtl));
    }

    /// <summary>
    /// Returns the owning customer when the session is not revoked, not past its
    /// absolute expiry, and not idle beyond the idle window; touching the row
    /// and enforcing the idle check happen in one conditional UPDATE.
    /// </summary>
    public async Task<Guid?> ValidateAndTouchSessionAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        List<Session> updated = await context.Sessions
            .FromSqlInterpolated($"""
                UPDATE sessions
                SET last_seen_at = NOW()
                WHERE token_hash = {tokenHash}
                  AND revoked_at IS NULL
                  AND expires_at > NOW()
                  AND last_seen_at > NOW() - INTERVAL '30 minutes'
                RETURNING *
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);

        return updated.Count > 0 ? updated[0].CustomerId : null;
    }

    /// <summary>Revokes one session; returns the owner when it revoked a live row.</summary>
    public async Task<Guid?> RevokeSessionAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        List<Session> updated = await context.Sessions
            .FromSqlInterpolated($"""
                UPDATE sessions
                SET revoked_at = NOW()
                WHERE token_hash = {tokenHash} AND revoked_at IS NULL
                RETURNING *
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);

        return updated.Count > 0 ? updated[0].CustomerId : null;
    }

    // --- Email verification tokens -----------------------------------------

    public async Task<string> CreateVerificationTokenAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        string token = Tokens.NewToken();
        var now = DateTime.UtcNow;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            TokenHash = Tokens.HashToken(token),
            ExpiresAt = now.Add(VerificationTokenTtl),
            ConsumedAt = null,
            CreatedAt = now,
        });
        await context.SaveChangesAsync(cancellationToken);

        return token;
    }

    /// <summary>Consumes every open verification token, then issues exactly one new token.</summary>
    public async Task<string> RotateVerificationTokenAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        string token = Tokens.NewToken();
        var now = DateTime.UtcNow;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.EmailVerificationTokens
            .Where(item => item.CustomerId == customerId && item.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConsumedAt, now), cancellationToken);
        context.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            TokenHash = Tokens.HashToken(token),
            ExpiresAt = now.Add(VerificationTokenTtl),
            ConsumedAt = null,
            CreatedAt = now,
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return token;
    }

    /// <summary>
    /// Consumes one verification token and activates the customer. Admin and
    /// lifetime accounts cannot be activated by token, matching the frozen
    /// behaviour; the transaction rolls back so the token stays unconsumed.
    /// </summary>
    public async Task<Guid?> ConsumeVerificationAndActivateAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        List<EmailVerificationToken> consumed = await context.EmailVerificationTokens
            .FromSqlInterpolated($"""
                UPDATE email_verification_tokens
                SET consumed_at = NOW()
                WHERE token_hash = {tokenHash} AND consumed_at IS NULL AND expires_at > NOW()
                RETURNING *
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);
        if (consumed.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        Guid customerId = consumed[0].CustomerId;
        var now = DateTime.UtcNow;
        int activated = await context.Customers
            .Where(customer => customer.Id == customerId && !customer.IsAdmin && !customer.IsLifetime)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(customer => customer.EmailVerifiedAt, customer => customer.EmailVerifiedAt ?? now)
                .SetProperty(customer => customer.SubscriberActivatedAt, customer => customer.SubscriberActivatedAt ?? now)
                .SetProperty(customer => customer.UpdatedAt, now), cancellationToken);
        if (activated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await context.EmailVerificationTokens
            .Where(item => item.CustomerId == customerId && item.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConsumedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return customerId;
    }

    // --- Password reset tokens ---------------------------------------------

    public async Task<ResetRequestOutcome> RequestPasswordResetAsync(string canonicalEmail, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        Guid? found = await context.Customers
            .Where(customer => customer.CanonicalEmail == canonicalEmail && customer.EmailVerifiedAt != null)
            .Select(customer => (Guid?)customer.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ResetRequestOutcome.Shade();
        }

        Guid customerId = found.Value;
        var now = DateTime.UtcNow;
        await context.PasswordResetTokens
            .Where(item => item.CustomerId == customerId && item.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConsumedAt, now), cancellationToken);
        string token = Tokens.NewToken();
        context.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            TokenHash = Tokens.HashToken(token),
            ExpiresAt = now.Add(ResetTokenTtl),
            ConsumedAt = null,
            CreatedAt = now,
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ResetRequestOutcome.Issued(customerId, canonicalEmail, token);
    }

    /// <summary>
    /// Consumes one reset token, replaces the password hash, and revokes every
    /// live session in the same transaction.
    /// </summary>
    public async Task<Guid?> ConsumePasswordResetAsync(string tokenHash, string newPasswordHash, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        List<PasswordResetToken> consumed = await context.PasswordResetTokens
            .FromSqlInterpolated($"""
                UPDATE password_reset_tokens
                SET consumed_at = NOW()
                WHERE token_hash = {tokenHash} AND consumed_at IS NULL AND expires_at > NOW()
                RETURNING *
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);
        if (consumed.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        Guid customerId = consumed[0].CustomerId;
        var now = DateTime.UtcNow;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE customers SET password_hash = {newPasswordHash}, updated_at = NOW()
            WHERE id = {customerId}
            """, cancellationToken);
        await context.Sessions
            .Where(session => session.CustomerId == customerId && session.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return customerId;
    }

    // --- Billing entitlement events ----------------------------------------

    /// <summary>
    /// Applies a provider-agnostic billing event. Unknown customers are a no-op
    /// success: webhook deliveries are retried, and existence must not leak.
    /// </summary>
    public async Task ApplyBillingEventAsync(string eventType,
                                             Guid customerId,
                                             DateTime? nextBillingAt,
                                             CancellationToken cancellationToken = default)
    {
        if (eventType is not "subscription.activated" and not "subscription.cancelled")
        {
            return;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (eventType == "subscription.activated")
        {
            var now = DateTime.UtcNow;
            await context.Customers
                .Where(customer => customer.Id == customerId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(customer => customer.SubscriberActivatedAt, customer => customer.SubscriberActivatedAt ?? now)
                    .SetProperty(customer => customer.NextBillingAt, nextBillingAt), cancellationToken);
            return;
        }

        await context.Customers
            .Where(customer => customer.Id == customerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(customer => customer.SubscriberActivatedAt, (DateTime?)null)
                .SetProperty(customer => customer.NextBillingAt, (DateTime?)null), cancellationToken);
    }

    private sealed class CredentialRow
    {
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime? EmailVerifiedAt { get; set; }
        public DateTime? SubscriberActivatedAt { get; set; }
        public DateTime? LockedUntil { get; set; }
    }
}
