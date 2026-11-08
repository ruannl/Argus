using System.Text;
using argus.Server.Billing;
using argus.Server.Configuration;
using argus.Server.Data;
using argus.Server.Mail;
using argus.Server.Security;

namespace argus.Server.Http;

/// <summary>
/// The frozen account HTTP surface. Health, me, login, logout, register,
/// verify, resend, password-reset request and consume. Every account handler
/// requires the trusted-frontend proof first and then independently validates
/// the acting user's opaque session.
/// </summary>
public sealed class AccountApi(AppSettings settings,
                               Store store,
                               Policy policy,
                               RateLimiter rateLimiter,
                               IPwnedScreener pwned,
                               IMailer mailer,
                               ILoggerFactory loggerFactory)
{
    private static readonly Lazy<string> _dummyPasswordHash = new(() => PasswordHasher.Hash(Tokens.NewToken()));

    private readonly ILogger _audit = loggerFactory.CreateLogger("audit");
    private readonly ILogger _logger = loggerFactory.CreateLogger("account");

    // Test-only ordering seam. It is never set from a request or environment
    // and cannot grant authority; the production path does not wait here.
    private Func<Guid, CancellationToken, Task>? _beforeSessionIssuanceForTests = null;

    public void Map(WebApplication app)
    {
        app.MapGet("/health", Health);
        app.MapGet("/v1/me", MeAsync);
        app.MapPost("/v1/login", LoginAsync);
        app.MapPost("/v1/logout", LogoutAsync);
        app.MapPost("/v1/register", RegisterAsync);
        app.MapPost("/v1/verify", VerifyAsync);
        app.MapPost("/v1/verification/resend", ResendVerificationAsync);
        app.MapPost("/v1/password/reset/request", RequestPasswordResetAsync);
        app.MapPost("/v1/password/reset", ResetPasswordAsync);
        app.MapPost("/v1/billing/webhook", BillingWebhookAsync);
    }

    private IResult? Guard(HttpContext context)
    {
        if (!Trust.OriginValid(context, settings))
        {
            return Errors.OriginRejected();
        }
        return Trust.ApplicationSecretValid(context, settings) ? null : Errors.Unauthorized();
    }

    private IResult Health(HttpContext context) =>
        Trust.OriginValid(context, settings)
            ? Results.Json(new { status = "ok" })
            : Errors.OriginRejected();

    private async Task<IResult> MeAsync(HttpContext context,
                                        CancellationToken cancellationToken)
    {
        if (Guard(context) is { } failure)
        {
            return failure;
        }

        var rawToken = Trust.SessionToken(context);
        if (rawToken is null)
        {
            return Errors.Unauthorized();
        }

        var customerId = await store.ValidateAndTouchSessionAsync(Tokens.HashToken(rawToken), cancellationToken);
        if (customerId is null)
        {
            return Errors.Unauthorized();
        }

        var customer = await store.FindCustomerByIdAsync(customerId.Value, cancellationToken);
        return customer is null ? Errors.Unauthorized() : Results.Json(AccountResponse.From(customer));
    }

    private async Task<IResult> LoginAsync(HttpContext context,
                                           CancellationToken cancellationToken)
    {
        if (Guard(context) is { } failure)
        {
            return failure;
        }

        var parsed = await JsonBody.ReadAsync<LoginRequest>(context.Request, cancellationToken);
        if (!parsed.Parsed)
        {
            return Errors.InvalidRequest();
        }
        var request = parsed.Value!;

        var peerIp = context.Connection.RemoteIpAddress?.ToString();
        if (peerIp is not null && !rateLimiter.CheckIp(peerIp))
        {
            return Errors.RateLimited();
        }

        var canonicalEmail = policy.CanonicalizeEmail(request.Email);
        var loginKey = canonicalEmail ?? StableKey(request.Email ?? string.Empty);
        if (!rateLimiter.CheckLogin(loginKey))
        {
            return Errors.RateLimited();
        }

        CustomerRecord? customer = null;
        Exception? lookupError = null;
        try
        {
            if (canonicalEmail is not null)
            {
                customer = await store.FindCustomerByEmailAsync(canonicalEmail, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            lookupError = exception;
        }

        // Oversized passwords are replaced before hashing; the verify still runs
        // so an unknown email and a real one cost the same.
        var passwordForVerify =
            request.Password is not null && Encoding.UTF8.GetByteCount(request.Password) > 1024
                ? "\0"
                : request.Password ?? string.Empty;
        var hashToVerify = customer?.PasswordHash ?? _dummyPasswordHash.Value;
        var passwordMatches = PasswordHasher.Verify(passwordForVerify, hashToVerify);

        if (lookupError is not null)
        {
            _logger.LogError(lookupError, "login lookup failed");
            return Errors.Internal();
        }

        if (customer is null)
        {
            _audit.LogInformation("audit.login_failure reason={Reason}", "unknown_email");
            return Errors.InvalidCredentials();
        }

        if (customer.LockedUntil is { } lockedUntil && lockedUntil > DateTime.UtcNow)
        {
            _audit.LogInformation(
                "audit.login_failure customer_id={CustomerId} reason={Reason}",
                customer.Id,
                "locked");
            return Errors.InvalidCredentials();
        }

        if (!passwordMatches || customer.EmailVerifiedAt is null || customer.SubscriberActivatedAt is null)
        {
            var reason = !passwordMatches ? "invalid_credentials" : "not_verified_or_active";
            _audit.LogInformation(
                "audit.login_failure customer_id={CustomerId} reason={Reason}",
                customer.Id,
                reason);

            // Only accounts that can actually sign in accumulate lockout
            // failures; counting onboarding failures would let an attacker
            // pre-lock a victim.
            var loginable = customer.EmailVerifiedAt is not null && customer.SubscriberActivatedAt is not null;
            if (loginable && await store.RecordFailedLoginAsync(customer.Id, cancellationToken))
            {
                _audit.LogWarning("audit.account_locked customer_id={CustomerId}", customer.Id);
                try
                {
                    await mailer.SendSuspiciousLoginAsync(customer.CanonicalEmail, cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "suspicious-login mailer step failed");
                }
            }
            return Errors.InvalidCredentials();
        }

        if (_beforeSessionIssuanceForTests is { } beforeIssuance)
        {
            await beforeIssuance(customer.Id, cancellationToken);
        }

        var issued = await store.CreateSessionForVerifiedHashAsync(
            customer.Id, hashToVerify, cancellationToken);
        if (issued is null)
        {
            _audit.LogInformation(
                "audit.login_failure customer_id={CustomerId} reason={Reason}",
                customer.Id,
                "invalid_credentials");
            return Errors.InvalidCredentials();
        }
        var (token, expiresAt) = issued.Value;
        _audit.LogInformation("audit.login_success customer_id={CustomerId}", customer.Id);
        return Results.Json(new LoginResponse(token, Timestamps.Format(expiresAt)), statusCode: 200);
    }

    private async Task<IResult> LogoutAsync(HttpContext context,
                                            CancellationToken cancellationToken)
    {
        if (Guard(context) is { } failure)
        {
            return failure;
        }

        var rawToken = Trust.SessionToken(context);
        if (rawToken is null)
        {
            return Errors.Unauthorized();
        }

        var customerId = await store.RevokeSessionAsync(Tokens.HashToken(rawToken), cancellationToken);
        if (customerId is { } id)
        {
            _audit.LogInformation("audit.logout customer_id={CustomerId}", id);
        }
        return Results.StatusCode(204);
    }

    private async Task<IResult> RegisterAsync(HttpContext context,
                                              CancellationToken cancellationToken)
    {
        if (Guard(context) is { } failure)
        {
            return failure;
        }

        var parsed = await JsonBody.ReadAsync<RegisterRequest>(context.Request, cancellationToken);
        if (!parsed.Parsed)
        {
            return Errors.InvalidRequest();
        }
        var request = parsed.Value!;

        var peerIp = context.Connection.RemoteIpAddress?.ToString();
        if (peerIp is not null && !rateLimiter.CheckIp(peerIp))
        {
            return Errors.RateLimited();
        }

        var canonicalEmail = policy.CanonicalizeEmail(request.Email);
        if (canonicalEmail is null)
        {
            return Errors.InvalidRequest();
        }
        if (!rateLimiter.CheckEmail(canonicalEmail))
        {
            return Errors.RateLimited();
        }
        if (!policy.ValidatePassword(request.Password))
        {
            return Errors.InvalidRequest();
        }

        var pwnedResult = await ScreenPasswordAsync(request.Password!, cancellationToken);
        if (pwnedResult is true)
        {
            _audit.LogWarning("audit.password_pwned");
            return Errors.InvalidRequest();
        }
        if (pwnedResult is null)
        {
            _audit.LogWarning("audit.pwned_check_unavailable");
        }

        // Exactly one hash before the existence lookup: new accounts persist it,
        // existing accounts discard it, and timing does not expose which happened.
        var passwordHash = PasswordHasher.Hash(request.Password!);
        var (customer, isNew) = await store.InsertCustomerAsync(canonicalEmail, passwordHash, cancellationToken);

        if (customer.EmailVerifiedAt is not null)
        {
            await SafeMail(() => mailer.SendAlreadyRegisteredAsync(customer.CanonicalEmail, settings.FrontendUrl, cancellationToken));
            _audit.LogInformation(
                "audit.register customer_id={CustomerId} outcome={Outcome}",
                customer.Id,
                "already_verified");
            return Results.Json(new { status = "accepted" }, statusCode: 202);
        }

        var rawToken = isNew
            ? await store.CreateVerificationTokenAsync(customer.Id, cancellationToken)
            : await store.RotateVerificationTokenAsync(customer.Id, cancellationToken);
        await SafeMail(() => mailer.SendVerificationAsync(customer.CanonicalEmail, rawToken, settings.FrontendUrl, cancellationToken));
        _audit.LogInformation(
            "audit.register customer_id={CustomerId} outcome={Outcome}",
            customer.Id,
            isNew ? "created" : "token_rotated");
        return Results.Json(new { status = "accepted" }, statusCode: 202);
    }

    private async Task<IResult> ResendVerificationAsync(HttpContext context,
                                                        CancellationToken cancellationToken)
    {
        if (Guard(context) is { } failure)
        {
            return failure;
        }

        var parsed = await JsonBody.ReadAsync<ResendRequest>(context.Request, cancellationToken);
        if (!parsed.Parsed)
        {
            return Errors.InvalidRequest();
        }

        var peerIp = context.Connection.RemoteIpAddress?.ToString();
        if (peerIp is not null && !rateLimiter.CheckIp(peerIp))
        {
            return Errors.RateLimited();
        }

        var canonicalEmail = policy.CanonicalizeEmail(parsed.Value!.Email);
        if (canonicalEmail is null)
        {
            return Errors.InvalidRequest();
        }
        if (!rateLimiter.CheckEmail(canonicalEmail))
        {
            return Errors.RateLimited();
        }

        var customer = await store.FindCustomerByEmailAsync(canonicalEmail, cancellationToken);
        if (customer is { EmailVerifiedAt: not null })
        {
            await SafeMail(() => mailer.SendAlreadyRegisteredAsync(customer.CanonicalEmail, settings.FrontendUrl, cancellationToken));
        }
        else if (customer is not null)
        {
            var rawToken = await store.RotateVerificationTokenAsync(customer.Id, cancellationToken);
            await SafeMail(() => mailer.SendVerificationAsync(customer.CanonicalEmail, rawToken, settings.FrontendUrl, cancellationToken));
        }
        // Unknown email: no egress, identical accepted response.

        return Results.Json(new { status = "accepted" }, statusCode: 202);
    }

    private async Task<IResult> RequestPasswordResetAsync(HttpContext context,
                                                          CancellationToken cancellationToken)
    {
        if (Guard(context) is { } failure)
        {
            return failure;
        }

        var parsed = await JsonBody.ReadAsync<PasswordResetRequest>(context.Request, cancellationToken);
        if (!parsed.Parsed)
        {
            return Errors.InvalidRequest();
        }

        var peerIp = context.Connection.RemoteIpAddress?.ToString();
        if (peerIp is not null && !rateLimiter.CheckIp(peerIp))
        {
            return Errors.RateLimited();
        }

        var canonicalEmail = policy.CanonicalizeEmail(parsed.Value!.Email);
        var rateLimitKey = canonicalEmail ?? StableKey(parsed.Value.Email ?? string.Empty);
        if (!rateLimiter.CheckResetRequest(rateLimitKey))
        {
            return Errors.RateLimited();
        }

        if (canonicalEmail is not null)
        {
            var outcome = await store.RequestPasswordResetAsync(canonicalEmail, cancellationToken);
            if (outcome is ResetRequestOutcome.Issued issued)
            {
                _audit.LogInformation("audit.password_reset_requested customer_id={CustomerId}", issued.CustomerId);
                await SafeMail(() => mailer.SendPasswordResetAsync(issued.Recipient, issued.RawToken, settings.FrontendUrl, cancellationToken));
            }
            // Shade: unknown or unverified email gets no reset link.
        }

        return Results.Json(new { status = "accepted" }, statusCode: 202);
    }

    private async Task<IResult> ResetPasswordAsync(HttpContext context,
                                                   CancellationToken cancellationToken)
    {
        if (Guard(context) is { } failure)
        {
            return failure;
        }

        var parsed = await JsonBody.ReadAsync<PasswordResetConsumeRequest>(context.Request, cancellationToken);
        if (!parsed.Parsed)
        {
            return parsed.UnknownField ? Errors.InvalidRequest() : Errors.InvalidToken();
        }
        var request = parsed.Value!;

        if (!Tokens.HasValidShape(request.Token))
        {
            return Errors.InvalidToken();
        }

        var peerIp = context.Connection.RemoteIpAddress?.ToString();
        if (peerIp is not null && !rateLimiter.CheckIp(peerIp))
        {
            return Errors.RateLimited();
        }

        var tokenHash = Tokens.HashToken(request.Token!);
        if (!rateLimiter.CheckResetConsume(tokenHash))
        {
            return Errors.RateLimited();
        }
        if (!policy.ValidatePassword(request.Password))
        {
            return Errors.InvalidRequest();
        }

        var pwnedResult = await ScreenPasswordAsync(request.Password!, cancellationToken);
        if (pwnedResult is true)
        {
            _audit.LogWarning("audit.password_pwned");
            return Errors.InvalidRequest();
        }
        if (pwnedResult is null)
        {
            _audit.LogWarning("audit.pwned_check_unavailable");
        }

        var passwordHash = PasswordHasher.Hash(request.Password!);
        var customerId = await store.ConsumePasswordResetAsync(tokenHash, passwordHash, cancellationToken);
        if (customerId is null)
        {
            return Errors.InvalidToken();
        }

        _audit.LogInformation("audit.password_reset_consumed customer_id={CustomerId}", customerId.Value);
        return Results.Json(new { status = "reset" });
    }

    private async Task<IResult> VerifyAsync(HttpContext context,
                                            CancellationToken cancellationToken)
    {
        if (Guard(context) is { } failure)
        {
            return failure;
        }

        var parsed = await JsonBody.ReadAsync<VerifyRequest>(context.Request, cancellationToken);
        if (!parsed.Parsed)
        {
            return parsed.UnknownField ? Errors.InvalidRequest() : Errors.InvalidToken();
        }
        var request = parsed.Value!;

        if (!Tokens.HasValidShape(request.Token))
        {
            return Errors.InvalidToken();
        }

        var peerIp = context.Connection.RemoteIpAddress?.ToString();
        if (peerIp is not null && !rateLimiter.CheckIp(peerIp))
        {
            return Errors.RateLimited();
        }

        var tokenHash = Tokens.HashToken(request.Token!);
        if (!rateLimiter.CheckVerifyToken(tokenHash))
        {
            return Errors.RateLimited();
        }

        var customerId = await store.ConsumeVerificationAndActivateAsync(tokenHash, cancellationToken);
        if (customerId is null)
        {
            return Errors.InvalidToken();
        }

        _audit.LogInformation("audit.verify customer_id={CustomerId}", customerId.Value);
        return Results.Json(new { status = "verified" });
    }

    /// <summary>
    /// Provider webhook. Authentication is the provider's own HMAC signature,
    /// not the application secret: providers call this URL directly, not
    /// through the frontend. Signature failure 401, malformed 400, apply
    /// failure 500, otherwise 200 with an empty body.
    /// </summary>
    private async Task<IResult> BillingWebhookAsync(HttpContext context,
                                                    CancellationToken cancellationToken)
    {
        string body;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        var signature = context.Request.Headers[BillingEvents.WebhookSignatureHeader].ToString();
        if (!BillingEvents.VerifyWebhook(body, signature, settings.BillingWebhookSecret))
        {
            return Results.StatusCode(401);
        }
        if (!BillingEvents.TryParseEvent(body, out var eventType, out var customerId, out var nextBillingAt))
        {
            return Results.StatusCode(400);
        }

        try
        {
            await store.ApplyBillingEventAsync(eventType, customerId, nextBillingAt, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "billing event apply failed");
            return Results.StatusCode(500);
        }

        _audit.LogInformation(
            "audit.billing_event customer_id={CustomerId} event_kind={EventKind}",
            customerId,
            eventType);
        return Results.Ok();
    }

    private async Task<bool?> ScreenPasswordAsync(string password,
                                                  CancellationToken cancellationToken)
    {
        try
        {
            return await pwned.IsPwnedAsync(password, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "pwned screening unavailable");
            return null;
        }
    }

    private async Task SafeMail(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "mailer step failed");
        }
    }

    /// <summary>
    /// Rate-limit keys use the canonical email when valid, else a bounded
    /// stable digest so oversized or malformed input cannot grow the buckets.
    /// </summary>
    private static string StableKey(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return Encoding.UTF8.GetByteCount(normalized) > 320 ? Tokens.HashToken(normalized) : normalized;
    }
}

public sealed record AccountResponse(Guid Id,
                                     string Email,
                                     bool EmailVerified,
                                     bool SubscriberActive,
                                     string? SubscriptionStartedAt,
                                     string? NextBillingAt,
                                     bool Lifetime,
                                     bool Admin)
{
    public static AccountResponse From(CustomerRecord customer) => new(
        customer.Id,
        customer.CanonicalEmail,
        customer.EmailVerifiedAt is not null,
        customer.SubscriberActivatedAt is not null,
        customer.SubscriberActivatedAt is { } started ? Timestamps.Format(started) : null,
        customer.NextBillingAt is { } next ? Timestamps.Format(next) : null,
        customer.IsLifetime,
        customer.IsAdmin);
}
