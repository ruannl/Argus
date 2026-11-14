using argus.Server.Data;
using argus.Server.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace argus.Server.Tests;

/// <summary>
/// Database-boundary isolation evidence. These tests connect through the same
/// restricted login as the normal host and deliberately omit application
/// ownership predicates. They are not HTTP tests.
/// </summary>
public sealed class DatabaseCustomerIsolationTests
{
    private static readonly string[] _protectedTables =
    [
        "financial_accounts",
        "categories",
        "merchant_tags",
        "merchant_aliases",
        "statement_imports",
        "transactions",
        "transaction_merchant_tags",
        "import_quota_locks",
        "import_reviews",
        "import_review_rows",
        "import_commit_receipts",
        "transaction_operations",
        "transaction_operation_items",
    ];

    [Fact]
    public async Task UnfilteredSelectsSeeOnlyTheAuthenticatedCustomer()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        foreach (string table in _protectedTables)
        {
            Assert.Equal(1, await fixture.ScopeCountAsync(fixture.AuthorityA, table));
            Assert.Equal(1, await fixture.ScopeCountAsync(fixture.AuthorityB, table));
        }
    }

    [Fact]
    public async Task MissingInvalidAndForeignAuthoritySeeNoProtectedRows()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        foreach (string table in _protectedTables)
        {
            Assert.Equal(0, await fixture.RawCountAsync(null, table));
            Assert.Equal(0, await fixture.RawCountAsync(new FinanceAuthority(fixture.CustomerC, Tokens.HashToken(Tokens.NewToken())), table));
            Assert.Equal(0, await fixture.RawCountAsync(new FinanceAuthority(Guid.NewGuid(), fixture.SessionAHash), table));
        }
    }

    [Fact]
    public async Task OmittedFilterUpdatesAndDeletesAffectOnlyOwnRows()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        var mutations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["financial_accounts"] = "UPDATE financial_accounts SET display_name = 'isolation-probe'",
            ["categories"] = "UPDATE categories SET display_name = 'isolation-probe'",
            ["merchant_tags"] = "UPDATE merchant_tags SET display_name = 'isolation-probe'",
            ["merchant_aliases"] = "UPDATE merchant_aliases SET display_alias = 'isolation-probe'",
            ["statement_imports"] = "UPDATE statement_imports SET is_hidden = NOT is_hidden",
            ["transactions"] = "UPDATE transactions SET is_hidden_by_user = NOT is_hidden_by_user",
            ["transaction_merchant_tags"] = "DELETE FROM transaction_merchant_tags",
            ["import_quota_locks"] = "UPDATE import_quota_locks SET locked_at = clock_timestamp()",
            ["import_reviews"] = "UPDATE import_reviews SET status = 'Abandoned'",
            ["import_review_rows"] = "UPDATE import_review_rows SET skip = NOT skip",
            ["import_commit_receipts"] = "UPDATE import_commit_receipts SET updated_at = clock_timestamp()",
            ["transaction_operations"] = "UPDATE transaction_operations SET updated_at = clock_timestamp()",
            ["transaction_operation_items"] = "DELETE FROM transaction_operation_items",
        };

        var beforeA = new Dictionary<string, string>(StringComparer.Ordinal);
        var beforeB = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string table in mutations.Keys)
        {
            beforeA[table] = await AdminDigestAsync(fixture, table, fixture.CustomerA);
            beforeB[table] = await AdminDigestAsync(fixture, table, fixture.CustomerB);
        }

        foreach ((string table, string sql) in mutations)
        {
            int affected = await fixture.RawMutationAsync(fixture.AuthorityA, sql);
            Assert.Equal(1, affected);
            int expectedOwnRows = sql.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            Assert.Equal(expectedOwnRows, await fixture.AdminCountAsync(table, fixture.CustomerA));
            Assert.Equal(1, await fixture.AdminCountAsync(table, fixture.CustomerB));
            string afterDigest = await AdminDigestAsync(fixture, table, fixture.CustomerA);
            Assert.True(beforeA[table] != afterDigest, $"A-side digest did not change for {table}");
            Assert.Equal(beforeB[table], await AdminDigestAsync(fixture, table, fixture.CustomerB));
        }

        var afterA = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string table in mutations.Keys)
        {
            afterA[table] = await AdminDigestAsync(fixture, table, fixture.CustomerA);
        }

        foreach ((string table, string sql) in mutations)
        {
            int affected = await fixture.RawMutationAsync(fixture.AuthorityB, sql);
            Assert.Equal(1, affected);
            Assert.Equal(afterA[table], await AdminDigestAsync(fixture, table, fixture.CustomerA));
            string afterBDigest = await AdminDigestAsync(fixture, table, fixture.CustomerB);
            Assert.True(beforeB[table] != afterBDigest, $"B-side digest did not change for {table}");
        }
    }

    [Fact]
    public async Task CrossCustomerInsertAndOwnershipChangesAreRejected()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        string accountInsert = $"""
            INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
            VALUES ('{Guid.NewGuid()}', '{fixture.CustomerB}', 'forged', 'ZAR', NOW(), NOW())
            """;
        await AssertRejectedAsync(fixture, fixture.AuthorityA, accountInsert, "42501");

        string categoryUpdate = $"UPDATE categories SET customer_id = '{fixture.CustomerB}'";
        await AssertRejectedAsync(fixture, fixture.AuthorityA, categoryUpdate, "42501");

        string aliasCrossTag = $"""
            INSERT INTO merchant_aliases (id, customer_id, display_alias, normalized_alias, merchant_tag_id, category_id, created_at, updated_at)
            VALUES ('{Guid.NewGuid()}', '{fixture.CustomerA}', 'cross', 'cross', '{fixture.TagB}', '{fixture.CategoryA}', NOW(), NOW())
            """;
        await AssertRejectedAsync(fixture, fixture.AuthorityA, aliasCrossTag, "23503");
    }

    [Fact]
    public async Task UnverifiedSubscriberAndAdminFlagDoNotBypassAuthority()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        var unverified = new FinanceAuthority(fixture.CustomerC, fixture.SessionCHash);
        await Assert.ThrowsAsync<FinanceAuthorityException>(() =>
            fixture.ScopeCountAsync(unverified, "financial_accounts"));

        await fixture.ExecuteAdminAsync($"UPDATE customers SET is_admin = TRUE WHERE id = '{fixture.CustomerB}'");
        Assert.Equal(1, await fixture.ScopeCountAsync(fixture.AuthorityB, "financial_accounts"));
        Assert.Equal(0, await fixture.RawCountAsync(fixture.AuthorityA, "financial_accounts", customerPredicate: fixture.CustomerB));
    }

    [Fact]
    public async Task LegacyNonOwnerSchemaLeaksWithoutRlsAndFailsClosedAfterMigration()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schema = "legacy_rls_" + Guid.NewGuid().ToString("N");
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.ApplyLegacyAsync(schemaUrl, 10, canonicalMarkerPresent: true);
            Guid customerA = Guid.NewGuid();
            Guid customerB = Guid.NewGuid();
            Guid accountA = Guid.NewGuid();
            Guid accountB = Guid.NewGuid();
            string sessionHash = Tokens.HashToken(Tokens.NewToken());
            await using (var admin = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                await PersistenceTestSupport.ExecuteAsync(admin, $"""
                    INSERT INTO customers (id, canonical_email, password_hash, email_verified_at, subscriber_activated_at, created_at, updated_at)
                    VALUES
                        ('{customerA}', 'legacy-a-{Guid.NewGuid():N}@example.test', 'hash', NOW(), NOW(), NOW(), NOW()),
                        ('{customerB}', 'legacy-b-{Guid.NewGuid():N}@example.test', 'hash', NOW(), NOW(), NOW(), NOW());
                    INSERT INTO sessions (id, customer_id, token_hash, expires_at, last_seen_at, created_at)
                    VALUES ('{Guid.NewGuid()}', '{customerA}', '{sessionHash}', NOW() + INTERVAL '1 hour', NOW(), NOW());
                    INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
                    VALUES
                        ('{accountA}', '{customerA}', 'Legacy A', 'ZAR', NOW(), NOW()),
                        ('{accountB}', '{customerB}', 'Legacy B', 'ZAR', NOW(), NOW());
                    GRANT USAGE ON SCHEMA {schema} TO {DatabaseIsolationContract.RuntimeRole};
                    GRANT SELECT ON TABLE financial_accounts TO {DatabaseIsolationContract.RuntimeRole};
                    """);
            }

            string runtimeUrl = DatabaseRoleFixture.RuntimeSchemaUrl(databaseUrl, schema);
            Assert.Equal(2L, await RuntimeAccountCountAsync(runtimeUrl, null, null));

            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);

            Assert.Equal(0L, await RuntimeAccountCountAsync(runtimeUrl, null, null));
            Assert.Equal(1L, await RuntimeAccountCountAsync(runtimeUrl, customerA, sessionHash));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    private static async Task<long> RuntimeAccountCountAsync(string runtimeUrl, Guid? customerId, string? sessionHash)
    {
        await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(runtimeUrl));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        if (customerId is not null && sessionHash is not null)
        {
            await using var set = connection.CreateCommand();
            set.Transaction = transaction;
            set.CommandText = """
                SELECT set_config('argus.customer_id', @customer, true),
                       set_config('argus.session_hash', @hash, true),
                       set_config('argus.transaction_id', pg_current_xact_id()::text, true)
                """;
            set.Parameters.AddWithValue("customer", customerId.Value.ToString("D"));
            set.Parameters.AddWithValue("hash", sessionHash);
            await set.ExecuteNonQueryAsync();
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*)::bigint FROM financial_accounts";
        long count = Convert.ToInt64(await command.ExecuteScalarAsync());
        await transaction.RollbackAsync();
        return count;
    }

    [Fact]
    public async Task StaleTransactionIdentifierCannotAuthorizeAProtectedRead()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        string staleTransactionId = await fixture.BeginAndCaptureTransactionIdAsync(fixture.AuthorityA);
        Assert.Equal(0, await fixture.RawCountWithTransactionIdAsync(fixture.AuthorityA, staleTransactionId, "financial_accounts"));
        Assert.Equal(0, await fixture.RawCountWithTransactionIdAsync(fixture.AuthorityA, "1", "financial_accounts"));
    }

    [Fact]
    public async Task NormalStartupRejectsPrivilegedLoginAssumingRuntimeRole()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        string url = PersistenceTestSupport.SchemaUrl(
            DatabaseRoleFixture.AppendRole(fixture.DatabaseUrl, DatabaseIsolationContract.RuntimeRole),
            fixture.Schema);
        var options = PersistenceOptions.Build(url);
        await using var context = new ArgusDbContext(options);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IsolationSchemaValidator.ValidateAsync(context, requireRuntimeLogin: true, CancellationToken.None));
        Assert.Contains("must authenticate as", exception.Message);
    }

    [Fact]
    public async Task StartupValidationRejectsBroadenedRuntimePolicy()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        await MutateAdminAsync(fixture, $"""
            ALTER POLICY {DatabaseIsolationContract.RuntimePolicy} ON {fixture.Schema}.financial_accounts
                USING (true) WITH CHECK (true);
            """);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ValidateOwnerAsync(fixture));
        Assert.Contains("does not enforce customer equality", exception.Message);
    }

    [Fact]
    public async Task StartupValidationRejectsChangedAuthorityFunctionBody()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        await MutateAdminAsync(fixture, $"""
            CREATE OR REPLACE FUNCTION {fixture.Schema}.{DatabaseIsolationContract.AuthorityFunction}() RETURNS uuid
            LANGUAGE plpgsql VOLATILE PARALLEL UNSAFE SECURITY INVOKER
            SET search_path TO {fixture.Schema}, pg_catalog, pg_temp
            AS $fn$ BEGIN RETURN NULL; END $fn$;
            """);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ValidateOwnerAsync(fixture));
        Assert.Contains("body differs", exception.Message);
    }

    [Fact]
    public async Task StartupValidationRejectsCaseChangedFunctionBody()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        string changedBody = CustomerIsolationSql.AuthorityFunctionBody.Replace(
            "INTERVAL '30 minutes'", "INTERVAL '30 MINUTES'", StringComparison.Ordinal);
        Assert.NotEqual(CustomerIsolationSql.AuthorityFunctionBody, changedBody);
        await MutateAdminAsync(fixture, $"""
            CREATE OR REPLACE FUNCTION {fixture.Schema}.{DatabaseIsolationContract.AuthorityFunction}() RETURNS uuid
            LANGUAGE plpgsql VOLATILE PARALLEL UNSAFE SECURITY INVOKER
            SET search_path TO {fixture.Schema}, pg_catalog, pg_temp
            AS $fn$
            {changedBody}
            $fn$;
            """);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ValidateOwnerAsync(fixture));
        Assert.Contains("body differs", exception.Message);
    }

    [Fact]
    public async Task StartupValidationRejectsFunctionGrantOption()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        await MutateAdminAsync(fixture, $"""
            GRANT EXECUTE ON FUNCTION {fixture.Schema}.{DatabaseIsolationContract.AuthorityFunction}()
                TO {DatabaseIsolationContract.RuntimeRole} WITH GRANT OPTION;
            """);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ValidateOwnerAsync(fixture));
        Assert.Contains("grants EXECUTE beyond owner and runtime", exception.Message);
    }

    [Fact]
    public async Task StartupValidationRejectsBroadGrantOnLedgerTable()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        await MutateAdminAsync(fixture, $"GRANT TRUNCATE ON {fixture.Schema}.data_migrations TO {DatabaseIsolationContract.RuntimeRole};");
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ValidateOwnerAsync(fixture));
        Assert.Contains("broad grant", exception.Message);
    }

    [Fact]
    public async Task NormalStartupRejectsMissingCanonicalBackfillMarker()
    {
        await using IsolationFixture fixture = await IsolationFixture.CreateAsync();
        await MutateAdminAsync(fixture, $"DELETE FROM {fixture.Schema}.data_migrations;");
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RuntimeDatabaseValidator.ValidateAsync(fixture.RuntimeSchemaUrl));
        Assert.Contains("canonical backfill is not complete", exception.Message);
    }

    private static async Task MutateAdminAsync(IsolationFixture fixture, string sql)
    {
        await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(fixture.DatabaseUrl));
        await connection.OpenAsync();
        await PersistenceTestSupport.ExecuteAsync(connection, sql);
    }

    private static async Task ValidateOwnerAsync(IsolationFixture fixture)
    {
        var options = PersistenceOptions.Build(DatabaseRoleFixture.MigrationSchemaUrl(fixture.DatabaseUrl, fixture.Schema));
        await using var context = new ArgusDbContext(options);
        await IsolationSchemaValidator.ValidateAsync(context, requireRuntimeLogin: false, CancellationToken.None);
    }

    private static async Task<string> AdminDigestAsync(IsolationFixture fixture, string table, Guid customerId)
    {
        await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(
            PersistenceTestSupport.SchemaUrl(fixture.DatabaseUrl, fixture.Schema));
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COALESCE(md5(string_agg(t::text, '|' ORDER BY t::text)), 'empty') FROM {table} t WHERE customer_id = '{customerId}'";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertRejectedAsync(IsolationFixture fixture,
                                                  FinanceAuthority authority,
                                                  string sql,
                                                  string expectedSqlState)
    {
        PostgresException exception = await fixture.RawExecuteAsync(authority, sql);
        Assert.Equal(expectedSqlState, exception.SqlState);
    }

    private sealed class IsolationFixture : IAsyncDisposable
    {
        private IsolationFixture(string databaseUrl,
                                 string schema,
                                 string runtimeSchemaUrl,
                                 Guid customerA,
                                 Guid customerB,
                                 Guid customerC,
                                 Guid accountA,
                                 Guid accountB,
                                 Guid categoryA,
                                 Guid categoryB,
                                 Guid tagA,
                                 Guid tagB,
                                 Guid statementA,
                                 Guid statementB,
                                 Guid transactionA,
                                 Guid transactionB,
                                 Guid reviewA,
                                 Guid reviewB,
                                 Guid operationA,
                                 Guid operationB,
                                 string sessionAHash,
                                 string sessionBHash,
                                 string sessionCHash,
                                 IDbContextFactory<ArgusDbContext> contextFactory)
        {
            DatabaseUrl = databaseUrl;
            Schema = schema;
            RuntimeSchemaUrl = runtimeSchemaUrl;
            CustomerA = customerA;
            CustomerB = customerB;
            CustomerC = customerC;
            AccountA = accountA;
            AccountB = accountB;
            CategoryA = categoryA;
            CategoryB = categoryB;
            TagA = tagA;
            TagB = tagB;
            StatementA = statementA;
            StatementB = statementB;
            TransactionA = transactionA;
            TransactionB = transactionB;
            ReviewA = reviewA;
            ReviewB = reviewB;
            OperationA = operationA;
            OperationB = operationB;
            SessionAHash = sessionAHash;
            SessionBHash = sessionBHash;
            SessionCHash = sessionCHash;
            ContextFactory = contextFactory;
            AuthorityA = new FinanceAuthority(customerA, sessionAHash);
            AuthorityB = new FinanceAuthority(customerB, sessionBHash);
        }

        public string DatabaseUrl { get; }
        public string Schema { get; }
        public string RuntimeSchemaUrl { get; }
        public Guid CustomerA { get; }
        public Guid CustomerB { get; }
        public Guid CustomerC { get; }
        public Guid AccountA { get; }
        public Guid AccountB { get; }
        public Guid CategoryA { get; }
        public Guid CategoryB { get; }
        public Guid TagA { get; }
        public Guid TagB { get; }
        public Guid StatementA { get; }
        public Guid StatementB { get; }
        public Guid TransactionA { get; }
        public Guid TransactionB { get; }
        public Guid ReviewA { get; }
        public Guid ReviewB { get; }
        public Guid OperationA { get; }
        public Guid OperationB { get; }
        public string SessionAHash { get; }
        public string SessionBHash { get; }
        public string SessionCHash { get; }
        public FinanceAuthority AuthorityA { get; }
        public FinanceAuthority AuthorityB { get; }
        public IDbContextFactory<ArgusDbContext> ContextFactory { get; }

        public static async Task<IsolationFixture> CreateAsync()
        {
            string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
            string schema = "isolation_" + Guid.NewGuid().ToString("N");
            string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
            await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);

            Guid customerA = Guid.NewGuid();
            Guid customerB = Guid.NewGuid();
            Guid customerC = Guid.NewGuid();
            Guid accountA = Guid.NewGuid();
            Guid accountB = Guid.NewGuid();
            Guid categoryA = Guid.NewGuid();
            Guid categoryB = Guid.NewGuid();
            Guid tagA = Guid.NewGuid();
            Guid tagB = Guid.NewGuid();
            Guid statementA = Guid.NewGuid();
            Guid statementB = Guid.NewGuid();
            Guid transactionA = Guid.NewGuid();
            Guid transactionB = Guid.NewGuid();
            Guid reviewA = Guid.NewGuid();
            Guid reviewB = Guid.NewGuid();
            Guid operationA = Guid.NewGuid();
            Guid operationB = Guid.NewGuid();
            string sessionAHash = Tokens.HashToken(Tokens.NewToken());
            string sessionBHash = Tokens.HashToken(Tokens.NewToken());
            string sessionCHash = Tokens.HashToken(Tokens.NewToken());

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            await PersistenceTestSupport.ExecuteAsync(connection, $"""
                INSERT INTO customers (id, canonical_email, password_hash, email_verified_at, subscriber_activated_at, created_at, updated_at)
                VALUES
                    ('{customerA}', 'a-{Guid.NewGuid():N}@example.test', 'hash', NOW(), NOW(), NOW(), NOW()),
                    ('{customerB}', 'b-{Guid.NewGuid():N}@example.test', 'hash', NOW(), NOW(), NOW(), NOW()),
                    ('{customerC}', 'c-{Guid.NewGuid():N}@example.test', 'hash', NULL, NULL, NOW(), NOW());

                INSERT INTO sessions (id, customer_id, token_hash, expires_at, last_seen_at, created_at)
                VALUES
                    ('{Guid.NewGuid()}', '{customerA}', '{sessionAHash}', NOW() + INTERVAL '1 hour', NOW(), NOW()),
                    ('{Guid.NewGuid()}', '{customerB}', '{sessionBHash}', NOW() + INTERVAL '1 hour', NOW(), NOW()),
                    ('{Guid.NewGuid()}', '{customerC}', '{sessionCHash}', NOW() + INTERVAL '1 hour', NOW(), NOW());

                INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
                VALUES
                    ('{accountA}', '{customerA}', 'A account', 'ZAR', NOW(), NOW()),
                    ('{accountB}', '{customerB}', 'B account', 'ZAR', NOW(), NOW());

                INSERT INTO categories (id, customer_id, display_name, normalized_name, kind, is_system, is_default, is_archived, sort_order, created_at, updated_at)
                VALUES
                    ('{categoryA}', '{customerA}', 'A category', 'a category', 'Expense', FALSE, FALSE, FALSE, 100, NOW(), NOW()),
                    ('{categoryB}', '{customerB}', 'B category', 'b category', 'Expense', FALSE, FALSE, FALSE, 100, NOW(), NOW());

                INSERT INTO merchant_tags (id, customer_id, display_name, normalized_name, created_at, updated_at)
                VALUES
                    ('{tagA}', '{customerA}', 'A tag', 'a tag', NOW(), NOW()),
                    ('{tagB}', '{customerB}', 'B tag', 'b tag', NOW(), NOW());

                INSERT INTO merchant_aliases (id, customer_id, display_alias, normalized_alias, merchant_tag_id, category_id, created_at, updated_at)
                VALUES
                    ('{Guid.NewGuid()}', '{customerA}', 'A alias', 'a alias', '{tagA}', '{categoryA}', NOW(), NOW()),
                    ('{Guid.NewGuid()}', '{customerB}', 'B alias', 'b alias', '{tagB}', '{categoryB}', NOW(), NOW());

                INSERT INTO statement_imports (id, customer_id, financial_account_id, source_type, statement_label, statement_fingerprint, start_date, end_date, created_at, updated_at)
                VALUES
                    ('{statementA}', '{customerA}', '{accountA}', 'generic_csv', 'A statement', 'fp-a', DATE '2025-01-01', DATE '2025-01-31', NOW(), NOW()),
                    ('{statementB}', '{customerB}', '{accountB}', 'generic_csv', 'B statement', 'fp-b', DATE '2025-01-01', DATE '2025-01-31', NOW(), NOW());

                INSERT INTO transactions (id, customer_id, financial_account_id, statement_import_id, transaction_date, description, normalized_description, amount, category_id, source_type, source_row_number, identity_key, created_at, updated_at)
                VALUES
                    ('{transactionA}', '{customerA}', '{accountA}', '{statementA}', DATE '2025-01-05', 'A row', 'a row', -10.00, '{categoryA}', 'generic_csv', 1, 'key-a', NOW(), NOW()),
                    ('{transactionB}', '{customerB}', '{accountB}', '{statementB}', DATE '2025-01-05', 'B row', 'b row', -20.00, '{categoryB}', 'generic_csv', 1, 'key-b', NOW(), NOW());

                INSERT INTO transaction_merchant_tags (transaction_id, merchant_tag_id, customer_id)
                VALUES
                    ('{transactionA}', '{tagA}', '{customerA}'),
                    ('{transactionB}', '{tagB}', '{customerB}');

                INSERT INTO import_quota_locks (customer_id) VALUES ('{customerA}'), ('{customerB}');

                INSERT INTO import_reviews (id, customer_id, financial_account_id, source_kind, parser_config, currency, start_date, end_date, version, status, expires_at, account_import_version, duplicate_policy_version, statement_fingerprint, summary_hash, total_rows, imported_count, suppressed_count, skipped_count, possible_duplicate_count, exact_override_count, statement_override, statement_override_count, payload_bytes, created_at, updated_at)
                VALUES
                    ('{reviewA}', '{customerA}', '{accountA}', 'generic_csv', jsonb_build_object(), 'ZAR', DATE '2025-01-01', DATE '2025-01-31', 1, 'Prepared', NOW() + INTERVAL '1 hour', 0, 1, 'fp-a', 'hash-a', 0, 0, 0, 0, 0, 0, FALSE, 0, 0, NOW(), NOW()),
                    ('{reviewB}', '{customerB}', '{accountB}', 'generic_csv', jsonb_build_object(), 'ZAR', DATE '2025-01-01', DATE '2025-01-31', 1, 'Prepared', NOW() + INTERVAL '1 hour', 0, 1, 'fp-b', 'hash-b', 0, 0, 0, 0, 0, 0, FALSE, 0, 0, NOW(), NOW());

                INSERT INTO import_review_rows (row_id, review_id, customer_id, source_line, source_row_number, raw_values, description, normalized_description, amount_cents, validation_codes, warnings, suggested_category_id, selected_category_id, created_at, updated_at)
                VALUES
                    ('{Guid.NewGuid()}', '{reviewA}', '{customerA}', 1, 1, jsonb_build_object(), 'A row', 'a row', -1000, '[]'::jsonb, '[]'::jsonb, '{categoryA}', '{categoryA}', NOW(), NOW()),
                    ('{Guid.NewGuid()}', '{reviewB}', '{customerB}', 1, 1, jsonb_build_object(), 'B row', 'b row', -2000, '[]'::jsonb, '[]'::jsonb, '{categoryB}', '{categoryB}', NOW(), NOW());

                INSERT INTO import_commit_receipts (id, customer_id, operation_id, financial_account_id, request_digest, state, statement_import_id, result, created_at, updated_at)
                VALUES
                    ('{Guid.NewGuid()}', '{customerA}', '{Guid.NewGuid()}', '{accountA}', 'digest-a', 'Committed', '{statementA}', jsonb_build_object(), NOW(), NOW()),
                    ('{Guid.NewGuid()}', '{customerB}', '{Guid.NewGuid()}', '{accountB}', 'digest-b', 'Committed', '{statementB}', jsonb_build_object(), NOW(), NOW());

                INSERT INTO transaction_operations (customer_id, operation_id, status, selection_mode, request_digest, action, target_stamps, selected_count, changed_count, unchanged_count, captured_at, expires_at, applied_at, terminal_at, created_at, updated_at)
                VALUES
                    ('{customerA}', '{operationA}', 'Applied', 'explicit', 'request-a', jsonb_build_object(), jsonb_build_object(), 0, 0, 0, NOW() - INTERVAL '2 minutes', NOW() + INTERVAL '1 hour', NOW(), NOW(), NOW(), NOW()),
                    ('{customerB}', '{operationB}', 'Applied', 'explicit', 'request-b', jsonb_build_object(), jsonb_build_object(), 0, 0, 0, NOW() - INTERVAL '2 minutes', NOW() + INTERVAL '1 hour', NOW(), NOW(), NOW(), NOW());

                INSERT INTO transaction_operation_items (customer_id, operation_id, transaction_id, transaction_version, financial_account_id, statement_import_id, ordinal)
                VALUES
                    ('{customerA}', '{operationA}', '{transactionA}', 1, '{accountA}', '{statementA}', 0),
                    ('{customerB}', '{operationB}', '{transactionB}', 1, '{accountB}', '{statementB}', 0);
                """);

            string runtimeUrl = DatabaseRoleFixture.RuntimeSchemaUrl(databaseUrl, schema);
            var options = PersistenceOptions.Build(runtimeUrl);
            var factory = new TestContextFactory(options);

            return new IsolationFixture(databaseUrl, schema, runtimeUrl, customerA, customerB, customerC,
                accountA, accountB, categoryA, categoryB, tagA, tagB, statementA, statementB,
                transactionA, transactionB, reviewA, reviewB, operationA, operationB,
                sessionAHash, sessionBHash, sessionCHash, factory);
        }

        public async Task<int> ScopeCountAsync(FinanceAuthority authority, string table)
        {
            await using var scope = await CustomerDbScope.OpenSharedAsync(ContextFactory, authority);
            string sql = string.Concat("SELECT COUNT(*)::int AS \"Value\" FROM ", table);
            int count = await scope.Context.Database
                .SqlQueryRaw<int>(sql)
                .SingleAsync();
            await scope.CommitAsync();

            return count;
        }

        public async Task<int> RawCountAsync(FinanceAuthority? authority, string table, Guid? customerPredicate = null)
        {
            await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(RuntimeSchemaUrl));
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            if (authority is not null)
            {
                await SetAuthorityAsync(connection, transaction, authority);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = customerPredicate is null
                ? $"SELECT COUNT(*)::int FROM {table}"
                : $"SELECT COUNT(*)::int FROM {table} WHERE customer_id = '{customerPredicate}'";
            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
            await transaction.RollbackAsync();

            return count;
        }

        public async Task<int> RawMutationAsync(FinanceAuthority authority, string sql)
        {
            await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(RuntimeSchemaUrl));
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await SetAuthorityAsync(connection, transaction, authority);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            int affected = await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();

            return affected;
        }

        public async Task<PostgresException> RawExecuteAsync(FinanceAuthority authority, string sql)
        {
            await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(RuntimeSchemaUrl));
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await SetAuthorityAsync(connection, transaction, authority);
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
                await transaction.RollbackAsync();
                throw new InvalidOperationException("expected the statement to be rejected");
            }
            catch (PostgresException exception)
            {
                await transaction.RollbackAsync();
                return exception;
            }
        }

        public async Task<int> AdminCountAsync(string table, Guid customerId)
        {
            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(
                PersistenceTestSupport.SchemaUrl(DatabaseUrl, Schema));
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*)::int FROM {table} WHERE customer_id = '{customerId}'";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task<string> BeginAndCaptureTransactionIdAsync(FinanceAuthority authority)
        {
            await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(RuntimeSchemaUrl));
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await SetAuthorityAsync(connection, transaction, authority);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT current_setting('argus.transaction_id', true)";
            string id = (string)(await command.ExecuteScalarAsync())!;
            await transaction.RollbackAsync();
            return id;
        }

        public async Task<int> RawCountWithTransactionIdAsync(FinanceAuthority authority, string transactionId, string table)
        {
            await using var connection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(RuntimeSchemaUrl));
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var set = connection.CreateCommand())
            {
                set.Transaction = transaction;
                set.CommandText = """
                    SELECT set_config('argus.customer_id', @customer, true),
                           set_config('argus.session_hash', @hash, true),
                           set_config('argus.transaction_id', @transaction, true)
                    """;
                set.Parameters.AddWithValue("customer", authority.CustomerId.ToString("D"));
                set.Parameters.AddWithValue("hash", authority.SessionHash);
                set.Parameters.AddWithValue("transaction", transactionId);
                await set.ExecuteNonQueryAsync();
            }
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT COUNT(*)::int FROM {table}";
            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
            await transaction.RollbackAsync();
            return count;
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

        private static async Task SetAuthorityAsync(NpgsqlConnection connection,
                                                    NpgsqlTransaction transaction,
                                                    FinanceAuthority authority)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT set_config('argus.customer_id', @customer, true),
                       set_config('argus.session_hash', @hash, true),
                       set_config('argus.transaction_id', pg_current_xact_id()::text, true)
                """;
            command.Parameters.AddWithValue("customer", authority.CustomerId.ToString("D"));
            command.Parameters.AddWithValue("hash", authority.SessionHash);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class TestContextFactory(DbContextOptions<ArgusDbContext> options) : IDbContextFactory<ArgusDbContext>
    {
        public ArgusDbContext CreateDbContext() => new(options);

        public Task<ArgusDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
