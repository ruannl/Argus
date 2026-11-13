using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace argus.Server.Tests;

public sealed class AccountContractTests(KitFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Health_ReturnsOk_And_RejectsWrongOrigin()
    {
        var ok = await SendAsync(Get("/health"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("ok", (await BodyAsync(ok)).GetProperty("status").GetString());

        var rejected = await SendAsync(Get("/health", origin: "http://evil.test"));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(string.Empty, await rejected.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AccountEndpoints_RequireTheApplicationSecret()
    {
        var missing = await SendAsync(Get("/v1/me", secret: null));
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("unauthorized", await ErrorKindAsync(missing));

        var wrong = await SendAsync(Get("/v1/me", secret: "wrong-secret"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var register = await SendAsync(Post("/v1/register", Json(new { email = UniqueEmail(), password = TestEnvironment.GoodPassword }), secret: null));
        Assert.Equal(HttpStatusCode.Unauthorized, register.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsExactSnakeCaseFields_AndRejectsRevokedSession()
    {
        var email = UniqueEmail();
        var session = await RegisterVerifyLoginAsync(email);

        var me = await SendAsync(Get("/v1/me", session: session));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await BodyAsync(me);

        var names = body.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();
        Assert.Equal(["admin", "email", "email_verified", "id", "lifetime", "next_billing_at", "subscriber_active", "subscription_started_at"], names);
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.True(body.GetProperty("email_verified").GetBoolean());
        Assert.True(body.GetProperty("subscriber_active").GetBoolean());
        Assert.False(body.GetProperty("admin").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("next_billing_at").ValueKind);

        var logout = await SendAsync(Post("/v1/logout", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(string.Empty, await logout.Content.ReadAsStringAsync());

        var afterLogout = await SendAsync(Get("/v1/me", session: session));
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);

        var logoutAgain = await SendAsync(Post("/v1/logout", string.Empty, session: session));
        Assert.Equal(HttpStatusCode.NoContent, logoutAgain.StatusCode);
    }

    [Fact]
    public async Task Register_IsNonEnumerating_Idempotent_AndRejectsUnknownFields()
    {
        var email = UniqueEmail();
        var first = await SendAsync(Post("/v1/register", Json(new { email, password = TestEnvironment.GoodPassword })));
        var second = await SendAsync(Post("/v1/register", Json(new { email, password = TestEnvironment.GoodPassword })));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal("accepted", (await BodyAsync(first)).GetProperty("status").GetString());
        Assert.Equal("accepted", (await BodyAsync(second)).GetProperty("status").GetString());
        Assert.Equal(1, await CountRowsAsync("customers"));

        var unknown = await SendAsync(Post("/v1/register", Json(new { email = UniqueEmail(), password = TestEnvironment.GoodPassword, extra = 1 })));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("invalid_request", await ErrorKindAsync(unknown));
    }

    [Fact]
    public async Task Register_RejectsWeakAndCommonPasswords_AndInvalidEmail()
    {
        var shortPassword = await SendAsync(Post("/v1/register", Json(new { email = UniqueEmail(), password = "short" })));
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
        Assert.Equal("invalid_request", await ErrorKindAsync(shortPassword));

        var commonPassword = await SendAsync(Post("/v1/register", Json(new { email = UniqueEmail(), password = "Password123" })));
        Assert.Equal(HttpStatusCode.BadRequest, commonPassword.StatusCode);

        var badEmail = await SendAsync(Post("/v1/register", Json(new { email = "not-an-email", password = TestEnvironment.GoodPassword })));
        Assert.Equal(HttpStatusCode.BadRequest, badEmail.StatusCode);
    }

    [Fact]
    public async Task Verify_Activates_RejectsReplay_AndParseErrors()
    {
        var email = UniqueEmail();
        var token = await RegisterAsync(email);

        var unknownField = await SendAsync(Post("/v1/verify", Json(new { token, extra = "x" })));
        Assert.Equal(HttpStatusCode.BadRequest, unknownField.StatusCode);
        Assert.Equal("invalid_request", await ErrorKindAsync(unknownField));

        var missing = await SendAsync(Post("/v1/verify", "{}"));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("invalid_token", await ErrorKindAsync(missing));

        var malformed = await SendAsync(Post("/v1/verify", Json(new { token = "abc" })));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("invalid_token", await ErrorKindAsync(malformed));

        var verified = await SendAsync(Post("/v1/verify", Json(new { token })));
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal("verified", (await BodyAsync(verified)).GetProperty("status").GetString());

        var replay = await SendAsync(Post("/v1/verify", Json(new { token })));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("invalid_token", await ErrorKindAsync(replay));
    }

    [Fact]
    public async Task Login_FailsBeforeVerification_SucceedsAfter()
    {
        var email = UniqueEmail();
        var verificationToken = await RegisterAsync(email);

        var before = await SendAsync(Post("/v1/login", Json(new { email, password = TestEnvironment.GoodPassword })));
        Assert.Equal(HttpStatusCode.Unauthorized, before.StatusCode);
        Assert.Equal("invalid_credentials", await ErrorKindAsync(before));

        await VerifyAsync(verificationToken);

        var after = await SendAsync(Post("/v1/login", Json(new { email, password = TestEnvironment.GoodPassword })));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var body = await BodyAsync(after);
        Assert.Equal(64, body.GetProperty("token").GetString()!.Length);
        Assert.True(DateTime.TryParse(body.GetProperty("expires_at").GetString(), out _));

        var wrongPassword = await SendAsync(Post("/v1/login", Json(new { email, password = "definitely-wrong-password" })));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal("invalid_credentials", await ErrorKindAsync(wrongPassword));

        var unknownEmail = await SendAsync(Post("/v1/login", Json(new { email = UniqueEmail(), password = TestEnvironment.GoodPassword })));
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal("invalid_credentials", await ErrorKindAsync(unknownEmail));

        var unknownField = await SendAsync(Post("/v1/login", Json(new { email, password = TestEnvironment.GoodPassword, extra = 1 })));
        Assert.Equal(HttpStatusCode.BadRequest, unknownField.StatusCode);
        Assert.Equal("invalid_request", await ErrorKindAsync(unknownField));
    }

    [Fact]
    public async Task Login_Lockout_AfterFiveFailures()
    {
        var email = UniqueEmail();
        await RegisterVerifyLoginAsync(email);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failure = await SendAsync(Post("/v1/login", Json(new { email, password = "definitely-wrong-password" })));
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        // The lock rejects the correct password before any session is minted.
        var locked = await SendAsync(
            Post("/v1/login", Json(new { email, password = TestEnvironment.GoodPassword })));
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal("invalid_credentials", await ErrorKindAsync(locked));

        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT locked_until FROM customers WHERE canonical_email = @email";
        command.Parameters.AddWithValue("email", email);
        var lockedUntil = await command.ExecuteScalarAsync();
        Assert.NotNull(lockedUntil);
        Assert.True((DateTime)lockedUntil! > DateTime.UtcNow);
    }

    [Fact]
    public async Task ExpiredAndIdleSessions_AreRejected()
    {
        var expiredEmail = UniqueEmail();
        var expiredSession = await RegisterVerifyLoginAsync(expiredEmail);
        await ExecuteSqlAsync("UPDATE sessions SET expires_at = NOW() - INTERVAL '1 minute'");
        var expired = await SendAsync(Get("/v1/me", session: expiredSession));
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);

        var idleEmail = UniqueEmail();
        var idleSession = await RegisterVerifyLoginAsync(idleEmail);
        await ExecuteSqlAsync("UPDATE sessions SET last_seen_at = NOW() - INTERVAL '31 minutes'");
        var idle = await SendAsync(Get("/v1/me", session: idleSession));
        Assert.Equal(HttpStatusCode.Unauthorized, idle.StatusCode);
    }

    [Fact]
    public async Task PasswordReset_Request_UsesTheSameResponseFloorForKnownAndUnknownEmails()
    {
        var email = UniqueEmail();
        await RegisterVerifyLoginAsync(email);
        var unknownEmail = UniqueEmail();

        var known = Stopwatch.StartNew();
        var knownResponse = await SendAsync(Post("/v1/password/reset/request", Json(new { email })));
        known.Stop();

        var unknown = Stopwatch.StartNew();
        var unknownResponse = await SendAsync(Post("/v1/password/reset/request", Json(new { email = unknownEmail })));
        unknown.Stop();

        Assert.Equal(HttpStatusCode.Accepted, knownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknownResponse.StatusCode);
        Assert.True(unknown.ElapsedMilliseconds >= 600,
                    $"unknown-email response returned in {unknown.ElapsedMilliseconds} ms, below the response floor");
        Assert.True(Math.Abs(known.ElapsedMilliseconds - unknown.ElapsedMilliseconds) <= 400,
                    $"known {known.ElapsedMilliseconds} ms vs unknown {unknown.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task PasswordReset_IsNonEnumerating_ConsumesToken_AndRevokesSessions()
    {
        var email = UniqueEmail();
        var oldSession = await RegisterVerifyLoginAsync(email);

        var unknownEmail = UniqueEmail();
        var shade = await SendAsync(Post("/v1/password/reset/request", Json(new { email = unknownEmail })));
        Assert.Equal(HttpStatusCode.Accepted, shade.StatusCode);
        Assert.Null(Factory.Mailer.LastToken("reset", unknownEmail));

        var request = await SendAsync(Post("/v1/password/reset/request", Json(new { email })));
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var resetToken = Factory.Mailer.LastToken("reset", email);
        Assert.False(string.IsNullOrEmpty(resetToken));

        var unknownField = await SendAsync(Post("/v1/password/reset", Json(new { token = resetToken, password = TestEnvironment.GoodPassword, extra = 1 })));
        Assert.Equal(HttpStatusCode.BadRequest, unknownField.StatusCode);
        Assert.Equal("invalid_request", await ErrorKindAsync(unknownField));

        var weak = await SendAsync(Post("/v1/password/reset", Json(new { token = resetToken, password = "short" })));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal("invalid_request", await ErrorKindAsync(weak));

        var newPassword = "kit-reset-passphrase-9Xw4!cobalt-lantern";
        var reset = await SendAsync(Post("/v1/password/reset", Json(new { token = resetToken, password = newPassword })));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal("reset", (await BodyAsync(reset)).GetProperty("status").GetString());

        var revoked = await SendAsync(Get("/v1/me", session: oldSession));
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

        var replay = await SendAsync(Post("/v1/password/reset", Json(new { token = resetToken, password = newPassword })));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("invalid_token", await ErrorKindAsync(replay));

        var login = await SendAsync(Post("/v1/login", Json(new { email, password = newPassword })));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRegister_CreatesExactlyOneCustomer()
    {
        var email = UniqueEmail();
        var responses = await Task.WhenAll(SendAsync(Post("/v1/register", Json(new { email, password = TestEnvironment.GoodPassword }))), SendAsync(Post("/v1/register", Json(new { email, password = TestEnvironment.GoodPassword }))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        Assert.Equal(1, await CountRowsAsync("customers"));
    }

    [Fact]
    public async Task RegisterPerEmailRateLimit_Returns429()
    {
        var email = UniqueEmail();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var accepted = await SendAsync(Post("/v1/register", Json(new { email, password = TestEnvironment.GoodPassword })));
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        var limited = await SendAsync(Post("/v1/register", Json(new { email, password = TestEnvironment.GoodPassword })));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", await ErrorKindAsync(limited));
    }
}
