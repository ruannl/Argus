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

    public static IResult InvalidCredentials() =>
        Results.Json(new { error = "invalid_credentials" }, statusCode: 401);

    public static IResult InvalidRequest() =>
        Results.Json(new { error = "invalid_request" }, statusCode: 400);

    public static IResult InvalidToken() =>
        Results.Json(new { error = "invalid_token" }, statusCode: 400);

    public static IResult RateLimited() =>
        Results.Json(new { error = "rate_limited" }, statusCode: 429);

    public static IResult Internal() =>
        Results.Json(new { error = "internal_server_error" }, statusCode: 500);

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
        foreach (var name in new[] { "Origin", Brand.FrontendOriginHeader })
        {
            var value = context.Request.Headers[name].ToString();
            if (!string.IsNullOrEmpty(value) && value != settings.FrontendUrl)
            {
                return false;
            }
        }
        return true;
    }

    public static bool ApplicationSecretValid(HttpContext context, AppSettings settings)
    {
        var provided = context.Request.Headers[Brand.ApplicationSecretHeader].ToString();
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        // Hash both sides before the constant-time compare so length differences
        // are not observable and no padding decision is required.
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(settings.ApiSecret));
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }

    /// <summary>Returns the raw session token, or null when absent or oversized.</summary>
    public static string? SessionToken(HttpContext context)
    {
        var value = context.Request.Headers[Brand.SessionHeader].ToString();
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

public static class JsonBody
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public readonly record struct ParseResult<T>(T? Value, bool Parsed, bool UnknownField);

    public static async Task<ParseResult<T>> ReadAsync<T>(
        HttpRequest request,
        CancellationToken cancellationToken = default)
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
            var value = JsonSerializer.Deserialize<T>(body, Options);
            return value is null
                ? new ParseResult<T>(default, false, false)
                : new ParseResult<T>(value, true, false);
        }
        catch (JsonException exception)
        {
            var unknown = exception.Message.Contains("could not be mapped", StringComparison.OrdinalIgnoreCase);
            return new ParseResult<T>(default, false, unknown);
        }
    }
}

public static class Timestamps
{
    /// <summary>RFC3339-style UTC timestamp used for every API timestamp field.</summary>
    public static string Format(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'");
}
