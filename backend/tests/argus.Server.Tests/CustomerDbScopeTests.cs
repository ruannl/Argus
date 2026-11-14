using argus.Server.Data;
using argus.Server.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace argus.Server.Tests;

public sealed class CustomerDbScopeTests
{
    [Fact]
    public async Task ScopeAdmitsOwnAuthorityAndHidesOtherCustomersRows()
    {
        await using ScopeFixture fixture = await ScopeFixture.CreateAsync();
        await using var scope = await CustomerDbScope.OpenSharedAsync(
            fixture.ContextFactory,
            new FinanceAuthority(fixture.CustomerA, fixture.SessionAHash));

        List<Guid> accounts = await scope.Context.FinancialAccounts
            .Select(account => account.Id)
            .ToListAsync();

        Assert.Equal([fixture.AccountA], accounts);
        await scope.CommitAsync();
    }

    [Fact]
    public async Task MissingMismatchedRevokedAndExpiredAuthorityFailsClosed()
    {
        await using ScopeFixture fixture = await ScopeFixture.CreateAsync();
        var factory = fixture.ContextFactory;

        await Assert.ThrowsAsync<FinanceAuthorityException>(() =>
            CustomerDbScope.OpenSharedAsync(factory, new FinanceAuthority(Guid.NewGuid(), fixture.SessionAHash)));
        await Assert.ThrowsAsync<FinanceAuthorityException>(() =>
            CustomerDbScope.OpenSharedAsync(factory, new FinanceAuthority(fixture.CustomerA, new string('a', 64))));

        await fixture.ExecuteAdminAsync($"UPDATE sessions SET revoked_at = NOW() WHERE token_hash = '{fixture.SessionAHash}'");
        await Assert.ThrowsAsync<FinanceAuthorityException>(() =>
            CustomerDbScope.OpenSharedAsync(factory, new FinanceAuthority(fixture.CustomerA, fixture.SessionAHash)));

        await fixture.ExecuteAdminAsync($"UPDATE sessions SET revoked_at = NULL, expires_at = NOW() - INTERVAL '1 minute' WHERE token_hash = '{fixture.SessionAHash}'");
        await Assert.ThrowsAsync<FinanceAuthorityException>(() =>
            CustomerDbScope.OpenSharedAsync(factory, new FinanceAuthority(fixture.CustomerA, fixture.SessionAHash)));
    }

    [Fact]
    public async Task TransactionLocalSettingsDoNotSurviveCommit()
    {
        await using ScopeFixture fixture = await ScopeFixture.CreateAsync();
        await using (var scope = await CustomerDbScope.OpenSharedAsync(
                         fixture.ContextFactory,
                         new FinanceAuthority(fixture.CustomerA, fixture.SessionAHash)))
        {
            await scope.CommitAsync();
        }

        await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(fixture.RuntimeSchemaUrl));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_setting('argus.customer_id', true), current_setting('argus.session_hash', true), current_setting('argus.transaction_id', true)";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0) || string.IsNullOrEmpty(reader.GetString(0)));
        Assert.True(reader.IsDBNull(1) || string.IsNullOrEmpty(reader.GetString(1)));
        Assert.True(reader.IsDBNull(2) || string.IsNullOrEmpty(reader.GetString(2)));
    }

    [Fact]
    public async Task PooledConnectionReuseDoesNotLeakAuthorityToTheNextScope()
    {
        await using ScopeFixture fixture = await ScopeFixture.CreateAsync();
        var authorityA = new FinanceAuthority(fixture.CustomerA, fixture.SessionAHash);
        var authorityB = new FinanceAuthority(fixture.CustomerB, fixture.SessionBHash);

        await using (var first = await CustomerDbScope.OpenSharedAsync(fixture.ContextFactory, authorityA))
        {
            Assert.Equal([fixture.AccountA], await first.Context.FinancialAccounts.Select(account => account.Id).ToListAsync());
            await first.CommitAsync();
        }
        await using (var second = await CustomerDbScope.OpenSharedAsync(fixture.ContextFactory, authorityB))
        {
            Assert.Equal([fixture.AccountB], await second.Context.FinancialAccounts.Select(account => account.Id).ToListAsync());
            await second.CommitAsync();
        }
        await Assert.ThrowsAsync<FinanceAuthorityException>(() =>
            CustomerDbScope.OpenSharedAsync(fixture.ContextFactory, new FinanceAuthority(Guid.NewGuid(), fixture.SessionAHash)));
    }

    [Fact]
    public async Task SingleConnectionPoolReusesTheSameBackendForConsecutiveScopes()
    {
        await using ScopeFixture fixture = await ScopeFixture.CreateAsync();
        var factory = SingleConnectionFactory(fixture);
        long firstPid;
        long secondPid;

        await using (var first = await CustomerDbScope.OpenSharedAsync(
                         factory,
                         new FinanceAuthority(fixture.CustomerA, fixture.SessionAHash)))
        {
            firstPid = await BackendPidAsync(first.Context);
            Assert.Equal([fixture.AccountA], await first.Context.FinancialAccounts.Select(account => account.Id).ToListAsync());
            await first.CommitAsync();
        }
        await using (var second = await CustomerDbScope.OpenSharedAsync(
                         factory,
                         new FinanceAuthority(fixture.CustomerB, fixture.SessionBHash)))
        {
            secondPid = await BackendPidAsync(second.Context);
            Assert.Equal([fixture.AccountB], await second.Context.FinancialAccounts.Select(account => account.Id).ToListAsync());
            await second.CommitAsync();
        }

        Assert.Equal(firstPid, secondPid);
    }

    [Fact]
    public async Task FinalAdmissionRejectsExpiryAfterTentativeWriteAndRollsItBack()
    {
        await using ScopeFixture fixture = await ScopeFixture.CreateAsync();
        await fixture.ExecuteAdminAsync($"""
            UPDATE sessions SET expires_at = clock_timestamp() + INTERVAL '3 seconds', last_seen_at = clock_timestamp()
            WHERE token_hash = '{fixture.SessionAHash}';
            """);
        Guid accountId = Guid.NewGuid();
        await using var scope = await CustomerDbScope.OpenExclusiveAsync(
            fixture.ContextFactory,
            new FinanceAuthority(fixture.CustomerA, fixture.SessionAHash));
        Guid categoryId = Guid.NewGuid();
        await scope.Context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
            VALUES ({accountId}, {fixture.CustomerA}, 'expiry probe', 'ZAR', clock_timestamp(), clock_timestamp());
            INSERT INTO categories (id, customer_id, display_name, normalized_name, kind, is_system, is_default, is_archived, sort_order, created_at, updated_at)
            VALUES ({categoryId}, {fixture.CustomerA}, 'expiry category', 'expiry category', 'Expense', FALSE, FALSE, FALSE, 1, clock_timestamp(), clock_timestamp());
            UPDATE customers SET categories_seeded_at = clock_timestamp() WHERE id = {fixture.CustomerA};
            """);
        await Task.Delay(TimeSpan.FromMilliseconds(3500));

        await Assert.ThrowsAsync<FinanceAuthorityException>(() => scope.CommitAsync());

        await using var admin = await OpenAdminAsync(fixture);
        await using (var accountCount = admin.CreateCommand())
        {
            accountCount.CommandText = $"SELECT COUNT(*)::bigint FROM financial_accounts WHERE id = '{accountId}'";
            Assert.Equal(0L, Convert.ToInt64(await accountCount.ExecuteScalarAsync()));
        }
        await using (var categoryCount = admin.CreateCommand())
        {
            categoryCount.CommandText = $"SELECT COUNT(*)::bigint FROM categories WHERE id = '{categoryId}'";
            Assert.Equal(0L, Convert.ToInt64(await categoryCount.ExecuteScalarAsync()));
        }
        await using (var seeded = admin.CreateCommand())
        {
            seeded.CommandText = $"SELECT categories_seeded_at IS NULL FROM customers WHERE id = '{fixture.CustomerA}'";
            Assert.True(Convert.ToBoolean(await seeded.ExecuteScalarAsync()));
        }
    }

    [Fact]
    public async Task AdmissionFailsWhenRevocationLockIsHeldFirst()
    {
        await using ScopeFixture fixture = await ScopeFixture.CreateAsync();
        await using var admin = await OpenAdminAsync(fixture);
        await using var transaction = await admin.BeginTransactionAsync();
        await using (var lockSession = admin.CreateCommand())
        {
            lockSession.Transaction = transaction;
            lockSession.CommandText = $"SELECT 1 FROM sessions WHERE token_hash = '{fixture.SessionAHash}' FOR UPDATE";
            await lockSession.ExecuteNonQueryAsync();
        }

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CustomerDbScope.OpenSharedAsync(
                fixture.ContextFactory,
                new FinanceAuthority(fixture.CustomerA, fixture.SessionAHash)));
        PostgresException inner = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, inner.SqlState);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task SessionRevocationWaitsForAnAdmittedScope()
    {
        await using ScopeFixture fixture = await ScopeFixture.CreateAsync();
        await using var scope = await CustomerDbScope.OpenSharedAsync(
            fixture.ContextFactory,
            new FinanceAuthority(fixture.CustomerA, fixture.SessionAHash));
        await using var admin = await OpenAdminAsync(fixture);

        await using (var blocked = admin.CreateCommand())
        {
            blocked.CommandText = $"SET lock_timeout = '200ms'; UPDATE sessions SET revoked_at = clock_timestamp() WHERE token_hash = '{fixture.SessionAHash}'";
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => blocked.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
        }

        await scope.CommitAsync();
        await using var retry = admin.CreateCommand();
        retry.CommandText = $"UPDATE sessions SET revoked_at = clock_timestamp() WHERE token_hash = '{fixture.SessionAHash}'";
        Assert.Equal(1, await retry.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ActivationUpdateWaitsForAnAdmittedScope()
    {
        await using ScopeFixture fixture = await ScopeFixture.CreateAsync();
        await using var scope = await CustomerDbScope.OpenSharedAsync(
            fixture.ContextFactory,
            new FinanceAuthority(fixture.CustomerA, fixture.SessionAHash));
        await using var admin = await OpenAdminAsync(fixture);

        await using (var blocked = admin.CreateCommand())
        {
            blocked.CommandText = $"SET lock_timeout = '200ms'; UPDATE customers SET subscriber_activated_at = NULL WHERE id = '{fixture.CustomerA}'";
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => blocked.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
        }

        await scope.CommitAsync();
        await using var retry = admin.CreateCommand();
        retry.CommandText = $"UPDATE customers SET subscriber_activated_at = NULL WHERE id = '{fixture.CustomerA}'";
        Assert.Equal(1, await retry.ExecuteNonQueryAsync());
    }

    private static IDbContextFactory<ArgusDbContext> SingleConnectionFactory(ScopeFixture fixture)
    {
        var builder = new NpgsqlConnectionStringBuilder(PersistenceOptions.BuildConnectionString(fixture.RuntimeSchemaUrl))
        {
            MaxPoolSize = 1,
        };
        var options = new DbContextOptionsBuilder<ArgusDbContext>().UseNpgsql(builder.ConnectionString).Options;
        return new PooledDbContextFactory<ArgusDbContext>(options, poolSize: 1);
    }

    private static Task<long> BackendPidAsync(ArgusDbContext context) =>
        context.Database.SqlQuery<long>($"SELECT pg_backend_pid()::bigint AS \"Value\"").SingleAsync();

    private static async Task<NpgsqlConnection> OpenAdminAsync(ScopeFixture fixture)
    {
        var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(
            PersistenceTestSupport.SchemaUrl(fixture.DatabaseUrl, fixture.Schema)));
        await connection.OpenAsync();
        return connection;
    }

    private sealed class ScopeFixture : IAsyncDisposable
    {
        private ScopeFixture(string databaseUrl,
                             string schema,
                             string runtimeSchemaUrl,
                             Guid customerA,
                             Guid customerB,
                             Guid accountA,
                             Guid accountB,
                             string sessionAHash,
                             string sessionBHash,
                             IDbContextFactory<ArgusDbContext> contextFactory)
        {
            DatabaseUrl = databaseUrl;
            Schema = schema;
            RuntimeSchemaUrl = runtimeSchemaUrl;
            CustomerA = customerA;
            CustomerB = customerB;
            AccountA = accountA;
            AccountB = accountB;
            SessionAHash = sessionAHash;
            SessionBHash = sessionBHash;
            ContextFactory = contextFactory;
        }

        public string DatabaseUrl { get; }
        public string Schema { get; }
        public string RuntimeSchemaUrl { get; }
        public Guid CustomerA { get; }
        public Guid CustomerB { get; }
        public Guid AccountA { get; }
        public Guid AccountB { get; }
        public string SessionAHash { get; }
        public string SessionBHash { get; }
        public IDbContextFactory<ArgusDbContext> ContextFactory { get; }

        public static async Task<ScopeFixture> CreateAsync()
        {
            string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
            string schema = "scope_" + Guid.NewGuid().ToString("N");
            string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
            await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);

            Guid customerA = Guid.NewGuid();
            Guid customerB = Guid.NewGuid();
            Guid accountA = Guid.NewGuid();
            Guid accountB = Guid.NewGuid();
            string sessionAHash = Tokens.HashToken(Tokens.NewToken());
            string sessionBHash = Tokens.HashToken(Tokens.NewToken());

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            await PersistenceTestSupport.ExecuteAsync(connection, $"""
                INSERT INTO customers (id, canonical_email, password_hash, email_verified_at, subscriber_activated_at, created_at, updated_at)
                VALUES
                    ('{customerA}', 'a-{Guid.NewGuid():N}@example.test', 'hash', NOW(), NOW(), NOW(), NOW()),
                    ('{customerB}', 'b-{Guid.NewGuid():N}@example.test', 'hash', NOW(), NOW(), NOW(), NOW());
                INSERT INTO sessions (id, customer_id, token_hash, expires_at, last_seen_at, created_at)
                VALUES
                    ('{Guid.NewGuid()}', '{customerA}', '{sessionAHash}', NOW() + INTERVAL '1 hour', NOW(), NOW()),
                    ('{Guid.NewGuid()}', '{customerB}', '{sessionBHash}', NOW() + INTERVAL '1 hour', NOW(), NOW());
                INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
                VALUES
                    ('{accountA}', '{customerA}', 'A account', 'ZAR', NOW(), NOW()),
                    ('{accountB}', '{customerB}', 'B account', 'ZAR', NOW(), NOW());
                """);

            string runtimeUrl = DatabaseRoleFixture.RuntimeSchemaUrl(databaseUrl, schema);
            var options = PersistenceOptions.Build(runtimeUrl);
            var factory = new TestContextFactory(options);

            return new ScopeFixture(databaseUrl, schema, runtimeUrl, customerA, customerB, accountA, accountB, sessionAHash, sessionBHash, factory);
        }

        public async Task ExecuteAdminAsync(string sql)
        {
            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(
                PersistenceTestSupport.SchemaUrl(DatabaseUrl, Schema));
            await PersistenceTestSupport.ExecuteAsync(connection, sql);
        }

        public async ValueTask DisposeAsync()
        {
            await PersistenceTestSupport.DropSchemaAsync(DatabaseUrl, Schema);
        }
    }

    private sealed class TestContextFactory(DbContextOptions<ArgusDbContext> options) : IDbContextFactory<ArgusDbContext>
    {
        public ArgusDbContext CreateDbContext() => new(options);

        public Task<ArgusDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
