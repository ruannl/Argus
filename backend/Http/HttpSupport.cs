using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using argus.Server.Configuration;

namespace argus.Server.Http;

/// <summary>Exact frozen error bodies. Origin rejection is deliberately bodyless.</summary>
public static class Errors
{
    public static IResult Unauthorized() => Results.Json(new { error = "unauthorized" }, statusCode: 401);

    public static IResult InvalidCredentials() => Results.Json(new { error = "invalid_credentials" }, statusCode: 401);

    public static IResult InvalidRequest() => Results.Json(new { error = "invalid_request" }, statusCode: 400);

    public static IResult InvalidToken() => Results.Json(new { error = "invalid_token" }, statusCode: 400);

    public static IResult RateLimited() => Results.Json(new { error = "rate_limited" }, statusCode: 429);

    public static IResult Internal() => Results.Json(new { error = "internal_server_error" }, statusCode: 500);

    public static IResult NotFound() => Results.Json(new { error = "not_found" }, statusCode: 404);

    public static IResult DuplicateName() => Results.Json(new { error = "duplicate_name" }, statusCode: 409);

    public static IResult SystemCategoryImmutable() => Results.Json(new { error = "system_category_immutable" }, statusCode: 400);

    public static IResult KindChangeConfirmationRequired() => Results.Json(new { error = "kind_change_confirmation_required" }, statusCode: 400);

    public static IResult OriginRejected() => Results.StatusCode(403);
}

/// <summary>
/// The two independent caller proofs plus the acting-user session. CORS and
/// origin headers are not authentication.
/// </summary>
public static class Trust
{
    public static bool OriginValid(HttpContext context, AppSettings settings)
    {
        foreach (string? name in new[] { "Origin", Brand.FrontendOriginHeader })
        {
            string value = context.Request.Headers[name].ToString();
            if (!string.IsNullOrEmpty(value) && value != settings.FrontendUrl)
            {
                return false;
            }
        }

        return true;
    }

    public static bool ApplicationSecretValid(HttpContext context, AppSettings settings)
    {
        string provided = context.Request.Headers[Brand.ApplicationSecretHeader].ToString();
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        // Hash both sides before the constant-time compare so length differences
        // are not observable and no padding decision is required.
        byte[] providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(settings.ApiSecret));

        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }

    /// <summary>Returns the raw session token, or null when absent or oversized.</summary>
    public static string? SessionToken(HttpContext context)
    {
        string value = context.Request.Headers[Brand.SessionHeader].ToString();

        return string.IsNullOrEmpty(value) || value.Length > 1024 ? null : value;
    }
}

public sealed record LoginRequest(string? Email, string? Password);

public sealed record RegisterRequest(string? Email, string? Password);

public sealed record VerifyRequest(string? Token);

public sealed record ResendRequest(string? Email);

public sealed record PasswordResetRequest(string? Email);

public sealed record PasswordResetConsumeRequest(string? Token, string? Password);

public sealed record LoginResponse(string Token, string ExpiresAt);

public sealed record CreateAccountRequest(string? DisplayName, string? Institution);

public sealed record CreateCategoryRequest(string? DisplayName, string? Kind);

public sealed record UpdateCategoryRequest(string? DisplayName, string? Kind, bool? ConfirmKindChange);

public sealed record CreateMerchantTagRequest(string? DisplayName);

public sealed record UpdateMerchantTagRequest(string? DisplayName);

public static class JsonBody
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public readonly record struct ParseResult<T>(T? Value, bool Parsed, bool UnknownField);

    /// <summary>
    /// Account PATCH needs to tell "institution omitted" from "institution
    /// explicitly null", which a nullable DTO cannot express. Only
    /// <c>display_name</c> (required string) and <c>institution</c> (optional
    /// string or null) are accepted; any other property is unknown.
    /// </summary>
    public readonly record struct AccountPatchParseResult(bool Parsed,
                                                          bool UnknownField,
                                                          string? DisplayName,
                                                          bool InstitutionProvided,
                                                          string? Institution);

    public static async Task<AccountPatchParseResult> ReadAccountPatchAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        string body;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new AccountPatchParseResult(false, false, null, false, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new AccountPatchParseResult(false, false, null, false, null);
            }

            string? displayName = null;
            bool institutionProvided = false;
            string? institution = null;

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "display_name":
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            return new AccountPatchParseResult(false, false, null, false, null);
                        }
                        displayName = property.Value.GetString();
                        break;
                    case "institution":
                        institutionProvided = true;
                        if (property.Value.ValueKind == JsonValueKind.Null)
                        {
                            institution = null;
                        }
                        else if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            institution = property.Value.GetString();
                        }
                        else
                        {
                            return new AccountPatchParseResult(false, false, null, false, null);
                        }
                        break;
                    default:
                        return new AccountPatchParseResult(false, true, null, false, null);
                }
            }

            if (displayName is null)
            {
                return new AccountPatchParseResult(false, false, null, false, null);
            }

            return new AccountPatchParseResult(true, false, displayName, institutionProvided, institution);
        }
        catch (JsonException)
        {
            return new AccountPatchParseResult(false, false, null, false, null);
        }
    }

    public static ParseResult<T> ParseBytes<T>(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return new ParseResult<T>(default, false, false);
        }

        try
        {
            T? value = JsonSerializer.Deserialize<T>(body, _options);
            return value is null ? new ParseResult<T>(default, false, false) : new ParseResult<T>(value, true, false);
        }
        catch (JsonException exception)
        {
            bool unknown = exception.Message.Contains("could not be mapped", StringComparison.OrdinalIgnoreCase);
            return new ParseResult<T>(default, false, unknown);
        }
    }

    public static async Task<ParseResult<T>> ReadAsync<T>(HttpRequest request, CancellationToken cancellationToken = default)
    {
        string body;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new ParseResult<T>(default, false, false);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ParseResult<T>(default, false, false);
            }

            T? value = JsonSerializer.Deserialize<T>(body, _options);
            return value is null ? new ParseResult<T>(default, false, false) : new ParseResult<T>(value, true, false);
        }
        catch (JsonException exception)
        {
            bool unknown = exception.Message.Contains("could not be mapped", StringComparison.OrdinalIgnoreCase);
            return new ParseResult<T>(default, false, unknown);
        }
    }
}

public static class Timestamps
{
    /// <summary>RFC3339-style UTC timestamp used for every API timestamp field.</summary>
    public static string Format(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'");
}
