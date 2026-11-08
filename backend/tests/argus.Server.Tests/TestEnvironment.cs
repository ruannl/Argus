using System.Net;
using argus.Server.Data;
using argus.Server.Imports;
using argus.Server.Mail;
using argus.Server.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
        Set("LOG_LEVEL", "info");
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

/// <summary>
/// Captures every formatted log line so tests can assert that user-supplied
/// entity names never reach application logs or audit events.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<string> _entries = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public bool Contains(string value)
    {
        lock (_gate)
        {
            return _entries.Any(entry => entry.Contains(value, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    internal void Record(
        string category,
        LogLevel level,
        string message,
        Exception? exception)
    {
        lock (_gate)
        {
            _entries.Add(exception is null
                ? $"{level} {category}: {message}"
                : $"{level} {category}: {message} [{exception.GetType().Name}]");
        }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            provider.Record(category, logLevel, formatter(state, exception), exception);
    }
}

/// <summary>
/// The in-memory test server has no peer address, so finance rate limiting
/// would never run. Give finance requests a loopback address (or the address
/// named by <c>X-Test-Remote-Ip</c>) without changing account-endpoint behaviour.
/// </summary>
public sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Path.StartsWithSegments("/v1/finance"))
            {
                var header = context.Request.Headers["X-Test-Remote-Ip"].ToString();
                context.Connection.RemoteIpAddress =
                    IPAddress.TryParse(header, out var address) ? address : IPAddress.Loopback;
            }
            await nextMiddleware();
        });
        next(app);
    };
}

public sealed class KitFactory : WebApplicationFactory<Program>
{
    public CapturingMailer Mailer { get; } = new();

    public CapturingLoggerProvider LogCapture { get; } = new();

    public KitFactory() : this(null, null)
    {
    }

    internal KitFactory(IImportFaultInjector? faultInjector) : this(faultInjector, null)
    {
    }

    internal KitFactory(
        IImportFaultInjector? faultInjector,
        ITransactionFaultInjector? transactionFaultInjector)
    {
        FaultInjector = faultInjector;
        TransactionFaultInjector = transactionFaultInjector;
        TestEnvironment.Configure();
    }

    public IImportFaultInjector? FaultInjector { get; }

    public ITransactionFaultInjector? TransactionFaultInjector { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMailer>();
            services.AddSingleton<IMailer>(Mailer);
            services.AddSingleton<ILoggerProvider>(LogCapture);
            services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
            services.AddSingleton<IImportFaultInjector>(
                FaultInjector ?? NoopImportFaultInjector.Instance);
            services.RemoveAll<ITransactionFaultInjector>();
            services.AddSingleton<ITransactionFaultInjector>(
                TransactionFaultInjector ?? NoopTransactionFaultInjector.Instance);
        });
    }

    public Db CreateDb() => new(TestEnvironment.RequireTestDatabaseUrl());
}
