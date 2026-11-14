using System.Diagnostics;
using System.Text.Json;
using argus.Server;
using argus.Server.Configuration;
using argus.Server.Data;
using argus.Server.Http;
using argus.Server.Mail;
using argus.Server.Security;
using argus.Server.Transactions;
using Microsoft.EntityFrameworkCore;

// The PDF parser child must run before configuration, migrations, logging, or
// web services are loaded. It reads only its bounded stdin frame and writes
// bounded sanitized JSON to stdout.
if (args.Contains("--parse-statement-pdf", StringComparer.Ordinal))
{
    // The runner terminates this child by process group when a descendant
    // outlives a timed-out or abnormal root and keeps an inherited pipe open.
    argus.Server.Imports.ParserProcessIsolation.JoinOwnProcessGroup();
    return await argus.Server.Imports.PdfParserChild.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.OpenStandardError());
}

// Maintenance commands never load the normal runtime configuration, build the
// web host or start routes/cleanup services.
if (args.Contains("migrate", StringComparer.Ordinal) || args.Contains("seed", StringComparer.Ordinal))
{
    return await MaintenanceProgram.RunAsync(args);
}

var builder = WebApplication.CreateBuilder(args);

AppSettings settings;
try
{
    settings = AppSettings.Load();
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

builder.Logging.ClearProviders();
if (settings.LogFormat == "json")
{
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    });
}
else
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
}
builder.Logging.SetMinimumLevel(settings.LogLevel switch
{
    "trace" => LogLevel.Trace,
    "debug" => LogLevel.Debug,
    "warn" => LogLevel.Warning,
    "error" => LogLevel.Error,
    _ => LogLevel.Information,
});

// Framework database errors can carry provider primary text in several
// categories, including Update and Query. Suppress the entire EF logging
// namespace at the host boundary; application and audit logs remain.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);

builder.WebHost.UseUrls($"http://{settings.Host}:{settings.Port}");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(settings.FrontendUrl)
    .WithMethods("GET", "POST", "PATCH", "DELETE")
    .WithHeaders(Brand.ApplicationSecretHeader, Brand.SessionHeader, Brand.FrontendOriginHeader)));

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<IInitializationFaultInjector>(NoopInitializationFaultInjector.Instance);
builder.Services.AddPooledDbContextFactory<ArgusDbContext>(options => PersistenceOptions.Configure(options, PersistenceOptions.BuildConnectionString(settings.DatabaseUrl)));
builder.Services.AddSingleton<Store>();
builder.Services.AddSingleton<FinanceStore>();
builder.Services.AddSingleton<Policy>();
builder.Services.AddSingleton<RateLimiter>();
builder.Services.AddSingleton<FinanceGuard>();
builder.Services.AddSingleton<IPwnedScreener, HibpScreener>();
builder.Services.AddSingleton<IMailer, ResendMailer>();
builder.Services.AddSingleton<AccountApi>();
builder.Services.AddSingleton<FinanceApi>();
builder.Services.AddSingleton<argus.Server.Imports.ParserAdmission>();
builder.Services.AddSingleton<argus.Server.Imports.PdfParserRunner>();
builder.Services.AddSingleton<argus.Server.Imports.IImportFaultInjector>(argus.Server.Imports.NoopImportFaultInjector.Instance);
builder.Services.AddSingleton<StatementImportStore>();
builder.Services.AddSingleton<StatementImportApi>();
builder.Services.AddSingleton<TransactionStore>();
builder.Services.AddSingleton<TransactionApi>();
builder.Services.AddSingleton<ITransactionFaultInjector>(NoopTransactionFaultInjector.Instance);
builder.Services.AddHostedService<TransactionCleanupService>();
builder.Services.AddHostedService<ImportCleanupService>();

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    context.Response.StatusCode = 500;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync("{\"error\":\"internal_server_error\"}");
}));

app.UseCors();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/v1/finance/imports") || context.Request.Path.StartsWithSegments("/v1/finance/transactions"))
    {
        context.Response.Headers.CacheControl = "no-store";
    }
    await next();
});

app.Use(async (context, next) =>
{
    string requestId = context.Request.Headers["X-Request-Id"].ToString();
    if (string.IsNullOrEmpty(requestId))
    {
        requestId = Guid.NewGuid().ToString("N");
    }
    context.Response.Headers["X-Request-Id"] = requestId;

    var stopwatch = Stopwatch.StartNew();
    await next();
    app.Logger.LogInformation("http_request method={Method} path={Path} status={Status} elapsed_ms={ElapsedMs}", context.Request.Method, context.Request.Path.Value, context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
});

await RuntimeDatabaseValidator.ValidateAsync(settings.DatabaseUrl);

app.Services.GetRequiredService<AccountApi>().Map(app);
app.Services.GetRequiredService<FinanceApi>().Map(app);
app.Services.GetRequiredService<StatementImportApi>().Map(app);
app.Services.GetRequiredService<TransactionApi>().Map(app);

app.Run();
return 0;

public partial class Program { }
