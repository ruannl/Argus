using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace argus.Server.Tests;

public sealed class BillingContractTests(KitFactory factory) : ApiTestBase(factory)
{
    private static string Sign(string payload) =>
        Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(TestEnvironment.BillingSecret),
                    Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();

    private static HttpRequestMessage Webhook(string payload, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        if (signature is not null)
        {
            request.Headers.Add("X-Billing-Webhook-Signature", signature);
        }
        return request;
    }

    private async Task<Guid> CreateVerifiedCustomerAsync(string email)
    {
        await RegisterVerifyLoginAsync(email);
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM customers WHERE canonical_email = @email";
        command.Parameters.AddWithValue("email", email);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Webhook_RejectsBadSignature_Malformed_AndUnknownCustomers()
    {
        var email = UniqueEmail();
        var customerId = await CreateVerifiedCustomerAsync(email);
        var future = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        var badSignaturePayload = $"{{\"type\":\"subscription.activated\",\"customer_id\":\"{customerId}\",\"next_billing_at\":\"{future}\"}}";
        var badSignature = await SendAsync(Webhook(badSignaturePayload, "deadbeef"));
        Assert.Equal(HttpStatusCode.Unauthorized, badSignature.StatusCode);
        Assert.Equal(string.Empty, await badSignature.Content.ReadAsStringAsync());

        var malformedPayload = "{\"type\":\"subscription.activated\"}";
        var malformed = await SendAsync(Webhook(malformedPayload, Sign(malformedPayload)));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(string.Empty, await malformed.Content.ReadAsStringAsync());

        var unknownPayload = $"{{\"type\":\"subscription.activated\",\"customer_id\":\"{Guid.NewGuid()}\",\"next_billing_at\":\"{future}\"}}";
        var unknown = await SendAsync(Webhook(unknownPayload, Sign(unknownPayload)));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(string.Empty, await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Webhook_Activation_Cancellation_And_PaymentFailed_UpdateEntitlement()
    {
        var email = UniqueEmail();
        var customerId = await CreateVerifiedCustomerAsync(email);
        var session = await LoginAsync(email);
        var future = DateTime.UtcNow.AddDays(30);

        var activatedPayload =
            $"{{\"type\":\"subscription.activated\",\"customer_id\":\"{customerId}\",\"next_billing_at\":\"{future:yyyy-MM-dd'T'HH:mm:ss'Z'}\"}}";
        var activated = await SendAsync(Webhook(activatedPayload, Sign(activatedPayload)));
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);

        var meAfterActivation = await BodyAsync(await SendAsync(Get("/v1/me", session: session)));
        Assert.True(meAfterActivation.GetProperty("subscriber_active").GetBoolean());
        Assert.Equal(JsonValueKind.String, meAfterActivation.GetProperty("next_billing_at").ValueKind);

        var failedPayload = $"{{\"type\":\"payment.failed\",\"customer_id\":\"{customerId}\"}}";
        var failed = await SendAsync(Webhook(failedPayload, Sign(failedPayload)));
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        var meAfterFailure = await BodyAsync(await SendAsync(Get("/v1/me", session: session)));
        Assert.True(meAfterFailure.GetProperty("subscriber_active").GetBoolean());

        var cancelledPayload = $"{{\"type\":\"subscription.cancelled\",\"customer_id\":\"{customerId}\"}}";
        var cancelled = await SendAsync(Webhook(cancelledPayload, Sign(cancelledPayload)));
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var meAfterCancellation = await BodyAsync(await SendAsync(Get("/v1/me", session: session)));
        Assert.False(meAfterCancellation.GetProperty("subscriber_active").GetBoolean());
        Assert.Equal(JsonValueKind.Null, meAfterCancellation.GetProperty("next_billing_at").ValueKind);
    }

    private async Task<string> LoginAsync(string email)
    {
        var login = await SendAsync(
            Post("/v1/login", Json(new { email, password = TestEnvironment.GoodPassword })));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await BodyAsync(login)).GetProperty("token").GetString()!;
    }
}
