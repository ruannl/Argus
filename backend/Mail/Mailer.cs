using System.Net;
using System.Net.Http.Headers;
using System.Text;
using argus.Server.Configuration;

namespace argus.Server.Mail;

public interface IMailer
{
    Task SendVerificationAsync(string recipient,
                               string rawToken,
                               string frontendUrl,
                               CancellationToken cancellationToken = default);

    Task SendPasswordResetAsync(string recipient,
                                string rawToken,
                                string frontendUrl,
                                CancellationToken cancellationToken = default);

    Task SendAlreadyRegisteredAsync(
        string recipient,
        string frontendUrl,
        CancellationToken cancellationToken = default);

    Task SendSuspiciousLoginAsync(string recipient, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resend delivery over HTTPS. Every step pads to a fixed floor so mail
/// latency does not become an account-existence oracle, and a short network
/// timeout keeps a slow provider from holding a request open. Delivery
/// failures are logged, never surfaced to the caller: the account flows stay
/// non-enumerating.
/// </summary>
public sealed class ResendMailer(AppSettings settings, ILogger<ResendMailer> logger) : IMailer
{
    private const string Endpoint = "https://api.resend.com/emails";
    private const int AntiEnumerationPadMs = 750;
    private const int NetworkTimeoutMs = 2000;

    private static readonly HttpClient _client = new() { Timeout = Timeout.InfiniteTimeSpan };

    public Task SendVerificationAsync(string recipient,
                                      string rawToken,
                                      string frontendUrl,
                                      CancellationToken cancellationToken = default)
    {
        var link = $"{frontendUrl.TrimEnd('/')}/verify?token={Uri.EscapeDataString(rawToken)}";
        return SendAsync(
            recipient,
            "Verify your email address",
            $"Confirm your email address to finish setting up your account:\n\n{link}\n\nIf you did not create an account, you can ignore this message.",
            $"<p>Confirm your email address to finish setting up your account:</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Verify email</a></p><p>If you did not create an account, you can ignore this message.</p>",
            cancellationToken);
    }

    public Task SendPasswordResetAsync(string recipient,
                                       string rawToken,
                                       string frontendUrl,
                                       CancellationToken cancellationToken = default)
    {
        var link = $"{frontendUrl.TrimEnd('/')}/reset?token={Uri.EscapeDataString(rawToken)}";
        return SendAsync(
            recipient,
            "Reset your password",
            $"Use this link to choose a new password. It expires in one hour:\n\n{link}\n\nIf you did not request a reset, you can ignore this message.",
            $"<p>Use this link to choose a new password. It expires in one hour:</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Reset password</a></p><p>If you did not request a reset, you can ignore this message.</p>",
            cancellationToken);
    }

    public Task SendAlreadyRegisteredAsync(
        string recipient,
        string frontendUrl,
        CancellationToken cancellationToken = default)
    {
        var link = $"{frontendUrl.TrimEnd('/')}/login";
        return SendAsync(
            recipient,
            "Your account is already set up",
            $"An account already exists for this address. Sign in here:\n\n{link}",
            $"<p>An account already exists for this address. <a href=\"{WebUtility.HtmlEncode(link)}\">Sign in</a>.</p>",
            cancellationToken);
    }

    public Task SendSuspiciousLoginAsync(string recipient, CancellationToken cancellationToken = default)
    {
        return SendAsync(
            recipient,
            "Suspicious sign-in attempts on your account",
            "We locked your account for a short time after several failed sign-in attempts. If this was not you, reset your password.",
            "<p>We locked your account for a short time after several failed sign-in attempts.</p><p>If this was not you, reset your password.</p>",
            cancellationToken);
    }

    private async Task SendAsync(string recipient,
                                 string subject,
                                 string text,
                                 string html,
                                 CancellationToken cancellationToken)
    {
        var pad = Task.Delay(AntiEnumerationPadMs, CancellationToken.None);
        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                from = settings.ResendFrom,
                to = new[] { recipient },
                subject,
                text,
                html,
            });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(NetworkTimeoutMs);

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ResendApiKey);

            using var response = await _client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "mailer.resend_rejected status={Status} recipient={Recipient}",
                    (int)response.StatusCode,
                    recipient);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "mailer.resend_unavailable recipient={Recipient}", recipient);
        }
        finally
        {
            await pad;
        }
    }
}
