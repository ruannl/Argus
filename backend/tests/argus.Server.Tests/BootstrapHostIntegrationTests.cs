using System.Diagnostics;
using argus.Server.Data;
using Npgsql;

namespace argus.Server.Tests;

/// <summary>
/// Process-level evidence: the actual bootstrap runs twice on a synthetic
/// installation, then the ordinary restricted-role host starts with cleanup
/// enabled. This is deliberately not a fake-seam or KitFactory test.
/// </summary>
public sealed class BootstrapHostIntegrationTests
{
    private const string DevDatabase = "argus_bootstrap_t10";
    private const int HostPort = 3299;

    [Fact]
    public async Task BootstrapTwiceThenRestrictedHostProvesPreparationAndInitialCleanup()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ARGUS_BOOTSTRAP_HOST_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            Console.WriteLine(
                "NOT RUN: set ARGUS_BOOTSTRAP_HOST_INTEGRATION=1 to execute against the separately authorized argus_pg16_t10 target.");
            return;
        }

        string testAdminUrl = TestEnvironment.RequireTestDatabaseUrl();
        await DatabaseRoleFixture.EnsureAsync(testAdminUrl);
        string devAdminUrl = ReplaceDatabase(testAdminUrl, DevDatabase);
        string install = CreateInstallation();
        string? hostFailure = null;
        try
        {
            var bootstrapEnvironment = BaseEnvironment();
            bootstrapEnvironment["ARGUS_DATABASE_URL"] = devAdminUrl;
            bootstrapEnvironment["ARGUS_TEST_DATABASE_URL"] = testAdminUrl;
            string container = Environment.GetEnvironmentVariable("ARGUS_BOOTSTRAP_DOCKER_CONTAINER") ?? "none";
            bootstrapEnvironment["ARGUS_DOCKER_CONTAINER"] = string.IsNullOrEmpty(container) ? "none" : container;
            bootstrapEnvironment["ARGUS_RUNTIME_PASSWORD"] = DatabaseRoleFixture.RuntimePassword;
            bootstrapEnvironment["ARGUS_MIGRATOR_PASSWORD"] = DatabaseRoleFixture.MigratorPassword;
            bootstrapEnvironment["ARGUS_SEED_DEV_PASSWORD"] = "bootstrap-dev-pin";
            bootstrapEnvironment["ARGUS_SEED_USER_PASSWORD"] = "bootstrap-user-pin";

            ProcessResult first = await RunAsync("node", ["scripts/bootstrap-dev.mjs"], install, bootstrapEnvironment, TimeSpan.FromMinutes(3), redact: true);
            Assert.True(first.ExitCode == 0, "first bootstrap failed: " + Redact(first.Combined));

            string backendEnv = File.ReadAllText(Path.Combine(install, "backend", ".env"));
            Assert.Contains("ARGUS_DATABASE_URL=postgres://argus_runtime:", backendEnv, StringComparison.Ordinal);
            Assert.DoesNotContain("ARGUS_TEST_DATABASE_URL", backendEnv, StringComparison.Ordinal);
            Assert.DoesNotContain("ARGUS_MAINTENANCE_DATABASE_URL", backendEnv, StringComparison.Ordinal);
            Assert.DoesNotContain("ARGUS_ADMIN_DATABASE_URL", backendEnv, StringComparison.Ordinal);
            string testEnv = File.ReadAllText(Path.Combine(install, "backend", ".env.test"));
            Assert.Contains("ARGUS_TEST_DATABASE_URL=postgres://", testEnv, StringComparison.Ordinal);
            string maintenanceEnv = File.ReadAllText(Path.Combine(install, "backend", ".env.maintenance"));
            Assert.Contains("ARGUS_DATABASE_URL=postgres://argus_migrator:", maintenanceEnv, StringComparison.Ordinal);

            string devPasswordHash = await ScalarAsync(devAdminUrl, "SELECT password_hash FROM customers WHERE canonical_email = 'dev@example.test'");
            Assert.Equal(2L, await CountAsync(devAdminUrl, "SELECT COUNT(*) FROM customers WHERE canonical_email IN ('dev@example.test', 'user@example.test')"));
            Assert.Equal((long)MigrationCatalog.EfIds.Count, await CountAsync(devAdminUrl, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\""));
            Assert.Equal(2L, await CountAsync(devAdminUrl, "SELECT COUNT(*) FROM pg_policies WHERE schemaname = 'public' AND tablename = 'financial_accounts'"));

            ProcessResult second = await RunAsync("node", ["scripts/bootstrap-dev.mjs"], install, bootstrapEnvironment, TimeSpan.FromMinutes(3), redact: true);
            Assert.True(second.ExitCode == 0, "second bootstrap failed: " + Redact(second.Combined));
            Assert.Equal(devPasswordHash, await ScalarAsync(devAdminUrl, "SELECT password_hash FROM customers WHERE canonical_email = 'dev@example.test'"));
            Assert.Equal(2L, await CountAsync(devAdminUrl, "SELECT COUNT(*) FROM customers WHERE canonical_email IN ('dev@example.test', 'user@example.test')"));

            Guid customerId = Guid.Parse(await ScalarAsync(devAdminUrl, "SELECT id FROM customers WHERE canonical_email = 'dev@example.test'"));
            Guid accountId = Guid.NewGuid();
            Guid reviewId = Guid.NewGuid();
            Guid operationId = Guid.NewGuid();
            await ExecuteAsync(devAdminUrl, $"""
                INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
                VALUES ('{accountId}', '{customerId}', 'Host cleanup account', 'ZAR', NOW(), NOW());
                INSERT INTO import_reviews (id, customer_id, financial_account_id, source_kind, parser_config, currency, start_date, end_date, version, status, expires_at, account_import_version, duplicate_policy_version, statement_fingerprint, summary_hash, total_rows, imported_count, suppressed_count, skipped_count, possible_duplicate_count, exact_override_count, statement_override, statement_override_count, payload_bytes, created_at, updated_at)
                VALUES ('{reviewId}', '{customerId}', '{accountId}', 'generic_csv', jsonb_build_object(), 'ZAR', DATE '2025-01-01', DATE '2025-01-31', 1, 'Prepared', NOW() - INTERVAL '1 hour', 0, 1, 'host-fp', 'host-hash', 0, 0, 0, 0, 0, 0, FALSE, 0, 0, NOW(), NOW());
                INSERT INTO transaction_operations (customer_id, operation_id, status, selection_mode, request_digest, confirmation_hash, action, target_stamps, selected_count, changed_count, unchanged_count, captured_at, expires_at, created_at, updated_at)
                VALUES ('{customerId}', '{operationId}', 'Prepared', 'explicit', 'host-request', 'host-confirmation', jsonb_build_object('kind', 'probe'), jsonb_build_object(), 0, 0, 0, NOW() - INTERVAL '2 hours', NOW() - INTERVAL '1 hour', NOW(), NOW());
                """);

            var hostEnvironment = BaseEnvironment();
            hostEnvironment["ARGUS_PORT"] = HostPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            hostEnvironment["ARGUS_HOST"] = "127.0.0.1";
            hostEnvironment["ARGUS_LOG_LEVEL"] = "warn";
            using var host = StartHost(install, hostEnvironment);
            try
            {
                await WaitForHostAsync(TimeSpan.FromSeconds(60));
                await WaitUntilAsync(
                    () => ScalarAsync(devAdminUrl, $"SELECT status FROM import_reviews WHERE id = '{reviewId}'"),
                    value => string.Equals(value, "Expired", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(30));
                await WaitUntilAsync(
                    () => ScalarAsync(devAdminUrl, $"SELECT status FROM transaction_operations WHERE customer_id = '{customerId}' AND operation_id = '{operationId}'"),
                    value => string.Equals(value, "Expired", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(30));
            }
            catch (Exception exception)
            {
                hostFailure = exception.GetType().Name;
                throw;
            }
            finally
            {
                Kill(host);
            }

            await ExecuteAsync(devAdminUrl, "ALTER TABLE import_reviews DISABLE ROW LEVEL SECURITY");
            Guid blockedReviewId = Guid.NewGuid();
            await ExecuteAsync(devAdminUrl, $"""
                INSERT INTO import_reviews (id, customer_id, financial_account_id, source_kind, parser_config, currency, start_date, end_date, version, status, expires_at, account_import_version, duplicate_policy_version, statement_fingerprint, summary_hash, total_rows, imported_count, suppressed_count, skipped_count, possible_duplicate_count, exact_override_count, statement_override, statement_override_count, payload_bytes, created_at, updated_at)
                VALUES ('{blockedReviewId}', '{customerId}', '{accountId}', 'generic_csv', jsonb_build_object(), 'ZAR', DATE '2025-01-01', DATE '2025-01-31', 1, 'Prepared', NOW() - INTERVAL '1 hour', 0, 1, 'blocked-fp', 'blocked-hash', 0, 0, 0, 0, 0, 0, FALSE, 0, 0, NOW(), NOW());
                """);

            ProcessResult blockedStart = await RunAsync(
                "dotnet", ["run", "--no-build"], Path.Combine(install, "backend"), hostEnvironment, TimeSpan.FromSeconds(60), redact: true);
            Assert.NotEqual(0, blockedStart.ExitCode);
            Assert.Equal("Prepared", await ScalarAsync(devAdminUrl, $"SELECT status FROM import_reviews WHERE id = '{blockedReviewId}'"));

            await ExecuteAsync(devAdminUrl, "ALTER TABLE import_reviews ENABLE ROW LEVEL SECURITY; ALTER TABLE import_reviews FORCE ROW LEVEL SECURITY");
            await ExecuteAsync(devAdminUrl, $"DELETE FROM import_reviews WHERE id IN ('{reviewId}', '{blockedReviewId}')");
            await ExecuteAsync(devAdminUrl, $"DELETE FROM transaction_operations WHERE customer_id = '{customerId}' AND operation_id = '{operationId}'");
            await ExecuteAsync(devAdminUrl, $"DELETE FROM financial_accounts WHERE id = '{accountId}'");
        }
        finally
        {
            if (Directory.Exists(install))
            {
                Directory.Delete(install, recursive: true);
            }
        }

        Assert.Null(hostFailure);
    }

    private static string CreateInstallation()
    {
        string baseDirectory = Environment.GetEnvironmentVariable("ARGUS_TEST_INSTALL_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "argus-t10");
        string install = Path.Combine(baseDirectory, "install-" + Guid.NewGuid().ToString("N"));
        string repository = RepositoryRoot();
        CopyDirectory(Path.Combine(repository, "backend"), Path.Combine(install, "backend"));
        CopyDirectory(Path.Combine(repository, "scripts"), Path.Combine(install, "scripts"));
        Directory.CreateDirectory(Path.Combine(install, "frontend"));
        return install;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "argus.Server.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("repository root could not be located");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            string name = Path.GetFileName(file);
            if (name.Equals(".env", StringComparison.Ordinal) || name.StartsWith(".env.", StringComparison.Ordinal))
            {
                continue;
            }
            File.Copy(file, Path.Combine(destination, name), overwrite: true);
        }
        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            string name = Path.GetFileName(directory);
            if (name is "bin" or "obj" or "tests" or "TestResults")
            {
                continue;
            }
            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }

    private static Dictionary<string, string?> BaseEnvironment()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string key in new[]
                 {
                     "PATH", "HOME", "USER", "TMPDIR", "TEMP", "TMP", "LANG", "LC_ALL",
                     "DOTNET_ROOT", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "DOTNET_NOLOGO",
                     "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_USE_POLLING_FILE_WATCHER",
                 })
        {
            environment[key] = Environment.GetEnvironmentVariable(key);
        }

        return environment;
    }

    private static Process StartHost(string install, Dictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.Combine(install, "backend"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment.Clear();
        foreach ((string key, string? value) in environment)
        {
            if (value is not null)
            {
                startInfo.Environment[key] = value;
            }
        }
        startInfo.ArgumentList.Add("run");

        Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("host process did not start");
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();

        return process;
    }

    private static async Task WaitForHostAsync(TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var cancellation = new CancellationTokenSource(timeout);
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{HostPort}/v1/me", cancellation.Token);
                return;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(250, cancellation.Token);
            }
            catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(250, cancellation.Token);
            }
        }

        throw new TimeoutException("restricted host did not start listening");
    }

    private static async Task WaitUntilAsync(Func<Task<string>> read, Func<string, bool> predicate, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!cancellation.IsCancellationRequested)
        {
            if (predicate(await read()))
            {
                return;
            }

            await Task.Delay(250, cancellation.Token);
        }

        throw new TimeoutException("expected database state was not reached");
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<ProcessResult> RunAsync(string fileName,
                                                      string[] arguments,
                                                      string workingDirectory,
                                                      Dictionary<string, string?> environment,
                                                      TimeSpan timeout,
                                                      bool redact)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment.Clear();
        foreach ((string key, string? value) in environment)
        {
            if (value is not null)
            {
                startInfo.Environment[key] = value;
            }
        }
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{fileName} did not start");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw new TimeoutException($"{fileName} exceeded its timeout");
        }

        string combined = output.Result + error.Result;
        return new ProcessResult(process.ExitCode, redact ? Redact(combined) : combined);
    }

    private static string Redact(string value)
    {
        return value
            .Replace(DatabaseRoleFixture.RuntimePassword, "[runtime-password]", StringComparison.Ordinal)
            .Replace(DatabaseRoleFixture.MigratorPassword, "[migrator-password]", StringComparison.Ordinal)
            .Replace("bootstrap-dev-pin", "[dev-pin]", StringComparison.Ordinal)
            .Replace("bootstrap-user-pin", "[user-pin]", StringComparison.Ordinal);
    }

    private static string ReplaceDatabase(string databaseUrl, string database)
    {
        var builder = new UriBuilder(databaseUrl)
        {
            Path = "/" + database,
        };

        return builder.Uri.ToString();
    }

    private static async Task<long> CountAsync(string databaseUrl, string sql)
    {
        await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(databaseUrl));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ScalarAsync(string databaseUrl, string sql)
    {
        await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(databaseUrl));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private static async Task ExecuteAsync(string databaseUrl, string sql)
    {
        await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(databaseUrl));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record ProcessResult(int ExitCode, string Combined);
}
