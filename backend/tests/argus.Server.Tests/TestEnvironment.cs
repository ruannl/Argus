using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using argus.Server.Data;
using argus.Server.Mail;

namespace argus.Server.Tests;

public static class TestEnvironment
{
    public const string ApiSecret = "test-api-secret-0123456789abcdef";
    public const string FrontendUrl = "http://127.0.0.1:3000";
    public const string BillingSecret = "test-billing-secret-0123456789abcdef";
    public const string GoodPassword = "kit-isolation-passphrase-7Qv2!mango-cobalt";

    /// <summary>
    /// Isolation tests need a real PostgreSQL database. A missing URL is a hard
    /// failure, never a skip.
    /// </summary>
    public static string RequireTestDatabaseUrl()
    {
        var url = Environment.GetEnvironmentVariable("ARGUS_TEST_DATABASE_URL");
        if (string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException(
                "ARGUS_TEST_DATABASE_URL is required for isolation tests; refusing to skip.");
        }
        return url;
    }

    public static void Configure()
    {
        var url = RequireTestDatabaseUrl();
        Set("DATABASE_URL", url);
        Set("API_SECRET", ApiSecret);
        Set("FRONTEND_URL", FrontendUrl);
        Set("RESEND_API_KEY", "test-placeholder-key-not-for-production");
        Set("RESEND_FROM", "local@example.test");
        Set("BILLING_WEBHOOK_SECRET", BillingSecret);
        Set("LOG_LEVEL", "error");
        Set("LOG_FORMAT", "json");
    }

    private static void Set(string name, string value) =>
        Environment.SetEnvironmentVariable("ARGUS_" + name, value);
}

/// <summary>Captures outbound mail so tests can consume one-time tokens.</summary>
public sealed class CapturingMailer : IMailer
{
    private readonly object _gate = new();
    private readonly List<(string Kind, string Recipient, string Token)> _sent = [];

    public Task SendVerificationAsync(
        string recipient,
        string rawToken,
        string frontendUrl,
        CancellationToken cancellationToken = default)
    {
        Record("verification", recipient, rawToken);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(
        string recipient,
        string rawToken,
        string frontendUrl,
        CancellationToken cancellationToken = default)
    {
        Record("reset", recipient, rawToken);
        return Task.CompletedTask;
    }

    public Task SendAlreadyRegisteredAsync(
        string recipient,
        string frontendUrl,
        CancellationToken cancellationToken = default)
    {
        Record("already-registered", recipient, string.Empty);
        return Task.CompletedTask;
    }

    public Task SendSuspiciousLoginAsync(string recipient, CancellationToken cancellationToken = default)
    {
        Record("suspicious-login", recipient, string.Empty);
        return Task.CompletedTask;
    }

    public string? LastToken(string kind, string recipient)
    {
        lock (_gate)
        {
            foreach (var item in Enumerable.Reverse(_sent))
            {
                if (item.Kind == kind && item.Recipient == recipient)
                {
                    return item.Token;
                }
            }
            return null;
        }
    }

    public int Count(string kind, string recipient)
    {
        lock (_gate)
        {
            return _sent.Count(item => item.Kind == kind && item.Recipient == recipient);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _sent.Clear();
        }
    }

    private void Record(string kind, string recipient, string token)
    {
        lock (_gate)
        {
            _sent.Add((kind, recipient, token));
        }
    }
}

public sealed class KitFactory : WebApplicationFactory<Program>
{
    public CapturingMailer Mailer { get; } = new();

    public KitFactory()
    {
        TestEnvironment.Configure();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMailer>();
            services.AddSingleton<IMailer>(Mailer);
        });
    }

    public Db CreateDb() => new(TestEnvironment.RequireTestDatabaseUrl());
}
