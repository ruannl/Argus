using Npgsql;

namespace argus.Server.Data;

public sealed record CustomerRecord(
    Guid Id,
    string CanonicalEmail,
    string PasswordHash,
    DateTime? EmailVerifiedAt,
    DateTime? SubscriberActivatedAt,
    DateTime? NextBillingAt,
    bool IsLifetime,
    bool IsAdmin,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int FailedLoginCount,
    DateTime? LockedUntil);

public abstract record ResetRequestOutcome
{
    public sealed record Issued(Guid CustomerId, string Recipient, string RawToken) : ResetRequestOutcome;

    public sealed record Shade : ResetRequestOutcome;
}

/// <summary>
/// PostgreSQL connection factory and startup migration runner. Migrations are
/// embedded, applied in filename order, and recorded so a restart is a no-op.
/// </summary>
public sealed class Db
{
    private readonly string _connectionString;
    private readonly string _databaseUrl;

    public Db(string databaseUrl)
    {
        _databaseUrl = databaseUrl;
        _connectionString = BuildConnectionString(databaseUrl);
    }

    public NpgsqlConnection Connect() => new(_connectionString);

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = Connect();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);

        await using (var create = connection.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE IF NOT EXISTS schema_migrations (" +
                "name TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW())";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (name, sql) in Migrations.All)
        {
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "SELECT 1 FROM schema_migrations WHERE name = @name";
                check.Parameters.AddWithValue("name", name);
                if (await check.ExecuteScalarAsync(cancellationToken) is not null)
                {
                    continue;
                }
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var apply = connection.CreateCommand())
            {
                apply.Transaction = transaction;
                apply.CommandText = sql;
                await apply.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO schema_migrations (name) VALUES (@name)";
                record.Parameters.AddWithValue("name", name);
                await record.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public static string BuildConnectionString(string databaseUrl)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("database URL is not a valid URI");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
        };

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length == 2)
            {
                builder.Password = Uri.UnescapeDataString(parts[1]);
            }
        }

        var sslmode = QueryValue(uri.Query, "sslmode");
        if (sslmode is not null)
        {
            builder.SslMode = sslmode switch
            {
                "disable" => SslMode.Disable,
                "prefer" => SslMode.Prefer,
                "require" => SslMode.Require,
                "verify-ca" => SslMode.VerifyCA,
                "verify-full" => SslMode.VerifyFull,
                _ => throw new InvalidOperationException($"unsupported sslmode '{sslmode}'"),
            };
        }

        return builder.ConnectionString;
    }

    private static string? QueryValue(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            if (pair[..separator].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..]).ToLowerInvariant();
            }
        }
        return null;
    }
}

public static class Migrations
{
    public static readonly (string Name, string Sql)[] All =
    [
        ("0001_customer_accounts", """
            CREATE TABLE customers (
                id UUID PRIMARY KEY,
                canonical_email TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                email_verified_at TIMESTAMPTZ NULL,
                subscriber_activated_at TIMESTAMPTZ NULL,
                next_billing_at TIMESTAMPTZ NULL,
                is_lifetime BOOLEAN NOT NULL DEFAULT FALSE,
                is_admin BOOLEAN NOT NULL DEFAULT FALSE,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE sessions (
                id UUID PRIMARY KEY,
                customer_id UUID NOT NULL REFERENCES customers(id),
                token_hash TEXT NOT NULL UNIQUE,
                expires_at TIMESTAMPTZ NOT NULL,
                revoked_at TIMESTAMPTZ NULL,
                last_seen_at TIMESTAMPTZ NOT NULL,
                created_at TIMESTAMPTZ NOT NULL
            );

            CREATE INDEX sessions_customer_id_idx ON sessions(customer_id);
            """),
        ("0002_email_verification_tokens", """
            CREATE TABLE email_verification_tokens (
                id UUID PRIMARY KEY,
                customer_id UUID NOT NULL REFERENCES customers(id),
                token_hash TEXT NOT NULL UNIQUE,
                expires_at TIMESTAMPTZ NOT NULL,
                consumed_at TIMESTAMPTZ NULL,
                created_at TIMESTAMPTZ NOT NULL
            );

            CREATE INDEX email_verification_tokens_customer_id_idx
                ON email_verification_tokens(customer_id);
            """),
        ("0003_password_reset_tokens", """
            CREATE TABLE password_reset_tokens (
                id UUID PRIMARY KEY,
                customer_id UUID NOT NULL REFERENCES customers(id),
                token_hash TEXT NOT NULL UNIQUE,
                expires_at TIMESTAMPTZ NOT NULL,
                consumed_at TIMESTAMPTZ NULL,
                created_at TIMESTAMPTZ NOT NULL
            );

            CREATE INDEX password_reset_tokens_customer_id_idx
                ON password_reset_tokens(customer_id);
            """),
        ("0004_failed_login_lockout", """
            ALTER TABLE customers
                ADD COLUMN failed_login_count INT NOT NULL DEFAULT 0,
                ADD COLUMN locked_until TIMESTAMPTZ NULL;
            """),
        ("0005_finance_foundation", """
            ALTER TABLE customers
                ADD COLUMN categories_seeded_at TIMESTAMPTZ NULL;

            CREATE TABLE financial_accounts (
                id UUID PRIMARY KEY,
                customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
                display_name TEXT NOT NULL,
                institution TEXT NULL,
                currency TEXT NOT NULL DEFAULT 'ZAR' CHECK (currency = 'ZAR'),
                is_archived BOOLEAN NOT NULL DEFAULT FALSE,
                archived_at TIMESTAMPTZ NULL,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                UNIQUE (customer_id, id)
            );

            CREATE INDEX financial_accounts_customer_id_idx ON financial_accounts(customer_id);

            CREATE TABLE categories (
                id UUID PRIMARY KEY,
                customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
                display_name TEXT NOT NULL,
                normalized_name TEXT NOT NULL,
                kind TEXT NOT NULL,
                is_system BOOLEAN NOT NULL DEFAULT FALSE,
                is_default BOOLEAN NOT NULL DEFAULT FALSE,
                is_archived BOOLEAN NOT NULL DEFAULT FALSE,
                archived_at TIMESTAMPTZ NULL,
                sort_order INT NOT NULL DEFAULT 100,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                UNIQUE (customer_id, id),
                UNIQUE (customer_id, normalized_name),
                CONSTRAINT categories_kind_check CHECK (
                    (is_system = TRUE AND kind = 'Uncategorised' AND is_archived = FALSE)
                    OR (is_system = FALSE AND kind IN ('Income', 'Expense', 'Transfer', 'Excluded'))
                )
            );

            CREATE UNIQUE INDEX categories_customer_system_uidx
                ON categories(customer_id) WHERE is_system = TRUE;
            CREATE INDEX categories_customer_id_idx ON categories(customer_id);

            CREATE TABLE merchant_tags (
                id UUID PRIMARY KEY,
                customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
                display_name TEXT NOT NULL,
                normalized_name TEXT NOT NULL,
                is_archived BOOLEAN NOT NULL DEFAULT FALSE,
                archived_at TIMESTAMPTZ NULL,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                UNIQUE (customer_id, id),
                UNIQUE (customer_id, normalized_name)
            );

            CREATE INDEX merchant_tags_customer_id_idx ON merchant_tags(customer_id);

            CREATE TABLE statement_imports (
                id UUID PRIMARY KEY,
                customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
                financial_account_id UUID NOT NULL,
                source_type TEXT NOT NULL,
                statement_label TEXT NOT NULL,
                statement_fingerprint TEXT NOT NULL,
                start_date DATE NOT NULL,
                end_date DATE NOT NULL,
                total_rows INT NOT NULL DEFAULT 0,
                imported_count INT NOT NULL DEFAULT 0,
                suppressed_count INT NOT NULL DEFAULT 0,
                skipped_count INT NOT NULL DEFAULT 0,
                is_hidden BOOLEAN NOT NULL DEFAULT FALSE,
                hidden_at TIMESTAMPTZ NULL,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                UNIQUE (customer_id, id),
                FOREIGN KEY (customer_id, financial_account_id)
                    REFERENCES financial_accounts(customer_id, id) ON DELETE RESTRICT
            );

            CREATE TABLE transactions (
                id UUID PRIMARY KEY,
                customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
                financial_account_id UUID NOT NULL,
                statement_import_id UUID NOT NULL,
                transaction_date DATE NOT NULL,
                description TEXT NOT NULL,
                normalized_description TEXT NOT NULL,
                amount NUMERIC(18, 2) NOT NULL,
                category_id UUID NOT NULL,
                source_type TEXT NOT NULL,
                source_page INT NULL,
                source_row_number INT NOT NULL DEFAULT 1,
                identity_key TEXT NOT NULL,
                occurrence_index INT NOT NULL DEFAULT 1,
                is_hidden_by_user BOOLEAN NOT NULL DEFAULT FALSE,
                version INT NOT NULL DEFAULT 1,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                FOREIGN KEY (customer_id, financial_account_id)
                    REFERENCES financial_accounts(customer_id, id) ON DELETE RESTRICT,
                FOREIGN KEY (customer_id, statement_import_id)
                    REFERENCES statement_imports(customer_id, id) ON DELETE CASCADE,
                FOREIGN KEY (customer_id, category_id)
                    REFERENCES categories(customer_id, id) ON DELETE RESTRICT
            );

            CREATE INDEX transactions_customer_category_idx
                ON transactions(customer_id, category_id);
            CREATE INDEX transactions_customer_account_idx
                ON transactions(customer_id, financial_account_id);

            CREATE TABLE transaction_merchant_tags (
                transaction_id UUID NOT NULL REFERENCES transactions(id) ON DELETE CASCADE,
                merchant_tag_id UUID NOT NULL REFERENCES merchant_tags(id) ON DELETE CASCADE,
                assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (transaction_id, merchant_tag_id)
            );

            CREATE INDEX transaction_merchant_tags_tag_idx
                ON transaction_merchant_tags(merchant_tag_id);
            """),
    ];
}
