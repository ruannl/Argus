using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace argus.Server.Tests;

public abstract class ApiTestBase(KitFactory factory) : IClassFixture<KitFactory>, IAsyncLifetime
{
    protected KitFactory Factory { get; } = factory;

    protected HttpClient Client { get; } = factory.CreateClient();

    public async Task InitializeAsync() => await ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    protected async Task ResetDatabaseAsync()
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "TRUNCATE import_review_rows, import_reviews, import_commit_receipts, import_quota_locks, " +
            "transaction_operation_items, transaction_operations, " +
            "transaction_merchant_tags, transactions, statement_imports, merchant_tags, " +
            "categories, financial_accounts, customers, sessions, email_verification_tokens, " +
            "password_reset_tokens CASCADE";
        await command.ExecuteNonQueryAsync();
        Factory.Mailer.Clear();
        Factory.LogCapture.Clear();
    }

    protected static string Json(object value) => JsonSerializer.Serialize(value);

    protected static HttpRequestMessage Get(
        string path,
        string? secret = TestEnvironment.ApiSecret,
        string? session = null,
        string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddHeaders(request, secret, session, origin);
        return request;
    }

    protected static HttpRequestMessage Post(
        string path,
        string json,
        string? secret = TestEnvironment.ApiSecret,
        string? session = null,
        string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        AddHeaders(request, secret, session, origin);
        return request;
    }

    protected static HttpRequestMessage Patch(
        string path,
        string json,
        string? secret = TestEnvironment.ApiSecret,
        string? session = null,
        string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        AddHeaders(request, secret, session, origin);
        return request;
    }

    protected static HttpRequestMessage Delete(
        string path,
        string? secret = TestEnvironment.ApiSecret,
        string? session = null,
        string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        AddHeaders(request, secret, session, origin);
        return request;
    }

    protected static HttpRequestMessage DeleteJson(
        string path,
        string json,
        string? secret = TestEnvironment.ApiSecret,
        string? session = null,
        string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        AddHeaders(request, secret, session, origin);
        return request;
    }

    protected static HttpRequestMessage MultipartPost(
        string path,
        byte[] file,
        string optionsJson,
        string? secret = TestEnvironment.ApiSecret,
        string? session = null,
        string? origin = null,
        string filename = "statement.bin")
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(optionsJson, Encoding.UTF8, "application/json"), "options");
        content.Add(new ByteArrayContent(file), "file", filename);
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        AddHeaders(request, secret, session, origin);
        return request;
    }

    private static void AddHeaders(
        HttpRequestMessage request,
        string? secret,
        string? session,
        string? origin)
    {
        if (secret is not null)
        {
            request.Headers.Add("X-81e1a205-ec1c-4e03-b785-9748db1e7677-Application-Secret", secret);
        }
        if (session is not null)
        {
            request.Headers.Add("X-81e1a205-ec1c-4e03-b785-9748db1e7677-Session", session);
        }
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }
    }

    protected async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) =>
        await Client.SendAsync(request);

    protected static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    protected static async Task<string> ErrorKindAsync(HttpResponseMessage response) =>
        (await BodyAsync(response)).GetProperty("error").GetString()!;

    protected static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.test";

    protected async Task<string> RegisterAsync(string email, string? password = null)
    {
        password ??= TestEnvironment.GoodPassword;
        var response = await SendAsync(Post("/v1/register", Json(new { email, password })));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var token = Factory.Mailer.LastToken("verification", email);
        Assert.False(string.IsNullOrEmpty(token), "verification token was not captured");
        return token!;
    }

    protected async Task VerifyAsync(string token)
    {
        var response = await SendAsync(Post("/v1/verify", Json(new { token })));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    protected async Task<string> RegisterVerifyLoginAsync(string email, string? password = null)
    {
        password ??= TestEnvironment.GoodPassword;
        var verificationToken = await RegisterAsync(email, password);
        await VerifyAsync(verificationToken);

        var login = await SendAsync(Post("/v1/login", Json(new { email, password })));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await BodyAsync(login)).GetProperty("token").GetString()!;
    }

    protected async Task<long> CountWhereAsync(string table, string predicate)
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {predicate}";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    protected async Task<long> CountRowsAsync(string table)
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    protected async Task<long> ScalarAsync(string sql)
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    protected async Task<string?> StringScalarAsync(string sql)
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync() as string;
    }

    protected async Task ExecuteSqlAsync(string sql)
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>Database-clock and lock observations for bounded import-expiry tests.</summary>
internal static class ImportExpiryTestHelpers
{
    internal static async Task<DateTime> SetDeadlineAsync(KitFactory factory, Guid reviewId, int seconds = 7)
    {
        await using var connection = await factory.CreateDb().OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE import_reviews SET expires_at = clock_timestamp() + @seconds * INTERVAL '1 second'
            WHERE id = @id RETURNING expires_at
            """;
        command.Parameters.AddWithValue("seconds", seconds);
        command.Parameters.AddWithValue("id", reviewId);
        return (DateTime)(await command.ExecuteScalarAsync())!;
    }

    internal static async Task WaitForTagWaitAsync(NpgsqlConnection observer, int blockerPid)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await using var command = observer.CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM pg_stat_activity
                    WHERE @blocker = ANY(pg_blocking_pids(pid)) AND state = 'active'
                      AND query LIKE '%FROM merchant_tags%' AND query LIKE '%FOR UPDATE%'
                )
                """;
            command.Parameters.AddWithValue("blocker", blockerPid);
            if ((bool)(await command.ExecuteScalarAsync())!) return;
            await Task.Delay(25);
        }
        Assert.Fail("The import commit did not reach the selected tag lock wait.");
    }

    internal static async Task WaitForDeadlineAsync(NpgsqlConnection observer, DateTime deadline)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            await using var command = observer.CreateCommand();
            command.CommandText = "SELECT clock_timestamp() >= @deadline";
            command.Parameters.AddWithValue("deadline", deadline);
            if ((bool)(await command.ExecuteScalarAsync())!) return;
            await Task.Delay(25);
        }
        Assert.Fail("The persisted review deadline did not pass during the bounded wait.");
    }
}
