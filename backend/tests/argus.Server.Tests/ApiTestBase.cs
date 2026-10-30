using System.Net;
using System.Text;
using System.Text.Json;

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
            "TRUNCATE customers, sessions, email_verification_tokens, password_reset_tokens CASCADE";
        await command.ExecuteNonQueryAsync();
        Factory.Mailer.Clear();
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

    protected async Task<long> CountRowsAsync(string table)
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync())!;
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
