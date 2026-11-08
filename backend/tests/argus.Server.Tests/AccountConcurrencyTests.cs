using System.Net;
using System.Reflection;
using argus.Server.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace argus.Server.Tests;

public sealed class AccountConcurrencyTests(KitFactory factory) : ApiTestBase(factory)
{
    private const string NewPassword = "kit-reset-passphrase-9Xw4!cobalt-lantern";

    [Fact]
    public async Task ResetCommittedAfterOldHashReadRejectsStaleLoginWithoutSuccessEffects()
    {
        var email = UniqueEmail();
        var originalSession = await RegisterVerifyLoginAsync(email);
        var unrelatedEmail = UniqueEmail();
        var unrelatedSession = await RegisterVerifyLoginAsync(unrelatedEmail);
        var customerId = (await BodyAsync(await SendAsync(Get("/v1/me", session: originalSession)))).GetProperty("id").GetGuid();
        var requested = await SendAsync(Post("/v1/password/reset/request", Json(new { email })));
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var resetToken = Factory.Mailer.LastToken("reset", email);
        Assert.NotNull(resetToken);

        await ExecuteSqlAsync($"UPDATE customers SET failed_login_count = 2 WHERE id = '{customerId}'");
        Factory.LogCapture.Clear();

        TaskCompletionSource entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = Factory.Services.GetRequiredService<AccountApi>();
        var field = typeof(AccountApi).GetField("_beforeSessionIssuanceForTests", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Null(field.GetValue(api));
        field.SetValue(api, (Func<Guid, CancellationToken, Task>)(async (id, cancellationToken) =>
        {
            if (id == customerId)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        }));

        Task<HttpResponseMessage>? loginTask = null;
        try
        {
            loginTask = SendAsync(Post("/v1/login", Json(new { email, password = TestEnvironment.GoodPassword, })));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var reset = await SendAsync(Post("/v1/password/reset", Json(new { token = resetToken, password = NewPassword, })));
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Get("/v1/me", session: originalSession))).StatusCode);

            release.TrySetResult();
            var staleLogin = await loginTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.Unauthorized, staleLogin.StatusCode);
            Assert.Equal("invalid_credentials", await ErrorKindAsync(staleLogin));
            Assert.Equal(0L, await CountWhereAsync("sessions", $"customer_id = '{customerId}' AND revoked_at IS NULL"));
            Assert.Equal(2L, await ScalarAsync($"SELECT failed_login_count FROM customers WHERE id = '{customerId}'"));
            Assert.False(Factory.LogCapture.Contains("audit.login_success"));
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(Get("/v1/me", session: unrelatedSession))).StatusCode);

            var oldPassword = await SendAsync(Post("/v1/login", Json(new { email, password = TestEnvironment.GoodPassword, })));
            Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
            var replay = await SendAsync(Post("/v1/password/reset", Json(new { token = resetToken, password = NewPassword, })));
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
            Assert.Equal("invalid_token", await ErrorKindAsync(replay));

            var newLogin = await SendAsync(Post("/v1/login", Json(new { email, password = NewPassword, })));
            Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
            var token = (await BodyAsync(newLogin)).GetProperty("token").GetString();
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(Get("/v1/me", session: token))).StatusCode);
        }
        finally
        {
            release.TrySetResult();
            field.SetValue(api, null);
            if (loginTask is not null)
            {
                try
                {
                    await loginTask.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch
                {
                    // Preserve the original assertion failure while ensuring the gate is released.
                }
            }
        }
    }

    [Fact]
    public async Task IssuanceCommittedFirstIsRevokedBeforeResetSucceeds()
    {
        var email = UniqueEmail();
        var originalSession = await RegisterVerifyLoginAsync(email);
        var customerId = (await BodyAsync(await SendAsync(Get("/v1/me", session: originalSession)))).GetProperty("id").GetGuid();
        var request = await SendAsync(Post("/v1/password/reset/request", Json(new { email })));
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var resetToken = Factory.Mailer.LastToken("reset", email);
        Assert.NotNull(resetToken);

        // This trigger is confined to the dedicated test database. It holds
        // the actual login INSERT at an advisory lock while the real reset
        // handler attempts its customer update. The customer row must stay
        // locked until issuance commits, so reset has to wait for that login.
        var suffix = Guid.NewGuid().ToString("N");
        var functionName = $"account_login_gate_{suffix}";
        var triggerName = functionName;
        var key = Random.Shared.NextInt64(1, long.MaxValue);
        await ExecuteSqlAsync($"""
            CREATE FUNCTION {functionName}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.customer_id = '{customerId}'::uuid THEN
                    PERFORM pg_advisory_xact_lock({key});
                END IF;
                RETURN NEW;
            END;
            $$
            """);
        await ExecuteSqlAsync($"""
            CREATE TRIGGER {triggerName} BEFORE INSERT ON sessions
            FOR EACH ROW EXECUTE FUNCTION {functionName}()
            """);

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var observer = await db.OpenAsync();
        Task<HttpResponseMessage>? loginTask = null;
        Task<HttpResponseMessage>? resetTask = null;
        var lockHeld = false;
        try
        {
            await using (var hold = blocker.CreateCommand())
            {
                hold.CommandText = "SELECT pg_advisory_lock(@key)";
                hold.Parameters.AddWithValue("key", key);
                await hold.ExecuteNonQueryAsync();
                lockHeld = true;
            }
            int blockerPid;
            await using (var pid = blocker.CreateCommand())
            {
                pid.CommandText = "SELECT pg_backend_pid()";
                blockerPid = (int)(await pid.ExecuteScalarAsync())!;
            }

            Factory.LogCapture.Clear();
            loginTask = SendAsync(Post("/v1/login", Json(new { email, password = TestEnvironment.GoodPassword, })));
            var issuingPid = await WaitForBlockedPidAsync(observer, blockerPid, "INSERT INTO sessions");
            Assert.False(loginTask.IsCompleted);

            resetTask = SendAsync(Post("/v1/password/reset", Json(new { token = resetToken, password = NewPassword, })));
            await WaitForBlockedPidAsync(observer, issuingPid, "UPDATE customers SET password_hash");
            Assert.False(resetTask.IsCompleted);

            await using (var release = blocker.CreateCommand())
            {
                release.CommandText = "SELECT pg_advisory_unlock(@key)";
                release.Parameters.AddWithValue("key", key);
                Assert.Equal(true, await release.ExecuteScalarAsync());
                lockHeld = false;
            }

            var login = await loginTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var token = (await BodyAsync(login)).GetProperty("token").GetString();
            var reset = await resetTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Get("/v1/me", session: token))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Get("/v1/me", session: originalSession))).StatusCode);
            Assert.Equal(0L, await CountWhereAsync("sessions", $"customer_id = '{customerId}' AND revoked_at IS NULL"));
            Assert.True(Factory.LogCapture.Contains("audit.login_success"));
        }
        finally
        {
            if (lockHeld)
            {
                await using var release = blocker.CreateCommand();
                release.CommandText = "SELECT pg_advisory_unlock(@key)";
                release.Parameters.AddWithValue("key", key);
                await release.ExecuteScalarAsync();
            }
            foreach (var pending in new[] { loginTask, resetTask })
            {
                if (pending is not null)
                {
                    try
                    {
                        await pending.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    catch
                    {
                        // A failed assertion must not leave the test trigger installed.
                    }
                }
            }
            await ExecuteSqlAsync($"DROP TRIGGER IF EXISTS {triggerName} ON sessions");
            await ExecuteSqlAsync($"DROP FUNCTION IF EXISTS {functionName}()");
        }
    }

    [Fact]
    public async Task CancelWhileWaitingForCredentialRowDoesNotIssueOrClearFailures()
    {
        var email = UniqueEmail();
        var originalSession = await RegisterVerifyLoginAsync(email);
        var customerId = (await BodyAsync(await SendAsync(Get("/v1/me", session: originalSession)))).GetProperty("id").GetGuid();
        await ExecuteSqlAsync($"UPDATE customers SET failed_login_count = 2 WHERE id = '{customerId}'");

        var db = Factory.CreateDb();
        await using var blocker = await db.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        int blockerPid;
        await using (var rowLock = blocker.CreateCommand())
        {
            rowLock.Transaction = blockerTransaction;
            rowLock.CommandText = "SELECT id FROM customers WHERE id = @id FOR UPDATE";
            rowLock.Parameters.AddWithValue("id", customerId);
            Assert.Equal(customerId, await rowLock.ExecuteScalarAsync());
        }
        await using (var pid = blocker.CreateCommand())
        {
            pid.Transaction = blockerTransaction;
            pid.CommandText = "SELECT pg_backend_pid()";
            blockerPid = (int)(await pid.ExecuteScalarAsync())!;
        }

        Factory.LogCapture.Clear();
        await using var observer = await db.OpenAsync();
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        var loginTask = Client.SendAsync(Post("/v1/login", Json(new { email, password = TestEnvironment.GoodPassword, })), cancellation.Token);
        try
        {
            await WaitForBlockedPidAsync(observer, blockerPid, "SELECT password_hash");
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await loginTask.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
            try
            {
                await loginTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (OperationCanceledException)
            {
                // The request was cancelled while still waiting for the row.
            }
        }

        Assert.Equal(1L, await CountWhereAsync("sessions", $"customer_id = '{customerId}' AND revoked_at IS NULL"));
        Assert.Equal(2L, await ScalarAsync($"SELECT failed_login_count FROM customers WHERE id = '{customerId}'"));
        Assert.False(Factory.LogCapture.Contains("audit.login_success"));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Get("/v1/me", session: originalSession))).StatusCode);
    }

    private static async Task<int> WaitForBlockedPidAsync(NpgsqlConnection observer, int blockerPid, string queryFragment)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var query = observer.CreateCommand();
            query.CommandText = """
                SELECT pid FROM pg_stat_activity
                WHERE @blocker = ANY(pg_blocking_pids(pid))
                  AND query ILIKE @fragment
                ORDER BY pid LIMIT 1
                """;
            query.Parameters.AddWithValue("blocker", blockerPid);
            query.Parameters.AddWithValue("fragment", $"%{queryFragment}%");
            if (await query.ExecuteScalarAsync() is int pid)
            {
                return pid;
            }

            await Task.Delay(25);
        }
        Assert.Fail($"A real {queryFragment} request did not reach the PostgreSQL lock wait.");

        return 0;
    }
}
