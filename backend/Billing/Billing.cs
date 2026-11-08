using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace argus.Server.Billing;

/// <summary>
/// The provider-agnostic billing seam. The mock provider signs webhook
/// payloads with HMAC-SHA256; entitlement changes are the only customer
/// effect. This is deliberately not a payment integration.
/// </summary>
public static class BillingEvents
{
    public const string WebhookSignatureHeader = "X-Billing-Webhook-Signature";

    public static bool VerifyWebhook(string payload, string? signature, string secret)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return false;
        }

        var expected = Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
        var provided = signature.Trim().ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided));
    }

    /// <summary>
    /// Parses the mock provider payload. Unknown extra fields are tolerated;
    /// an unknown event type, missing fields, or an unparseable activated
    /// billing date are malformed.
    /// </summary>
    public static bool TryParseEvent(string payload,
                                     out string eventType,
                                     out Guid customerId,
                                     out DateTime? nextBillingAt)
    {
        eventType = string.Empty;
        customerId = Guid.Empty;
        nextBillingAt = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            if (!root.TryGetProperty("customer_id", out var customerElement)
                || customerElement.ValueKind != JsonValueKind.String
                || !Guid.TryParse(customerElement.GetString(), out var customer))
            {
                return false;
            }

            DateTime? next = null;
            if (root.TryGetProperty("next_billing_at", out var nextElement)
                && nextElement.ValueKind != JsonValueKind.Null)
            {
                if (nextElement.ValueKind != JsonValueKind.String
                    || !DateTime.TryParse(
                        nextElement.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out var parsed))
                {
                    return false;
                }
                next = parsed;
            }

            var type = typeElement.GetString()!;
            switch (type)
            {
                case "subscription.activated" when next is not null:
                case "subscription.cancelled":
                case "payment.failed":
                    eventType = type;
                    customerId = customer;
                    nextBillingAt = next;
                    return true;
                default:
                    return false;
            }
        }
    }
}
