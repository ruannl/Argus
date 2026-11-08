using System.Globalization;
using argus.Server.Imports;
using Npgsql;

namespace argus.Server.Data;

public sealed record CustomerRecord(Guid Id,
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

    public Db(string databaseUrl)
    {
        _connectionString = BuildConnectionString(databaseUrl);
    }

    public NpgsqlConnection Connect() => new(_connectionString);

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = Connect();
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await OpenAsync(cancellationToken);

        await using (NpgsqlCommand create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE IF NOT EXISTS schema_migrations (" + "name TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW())";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach ((string? name, string? sql) in Migrations.All)
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

        await BackfillCanonicalIdentitiesAsync(connection, cancellationToken);
    }

    /// <summary>
    /// One-time data backfill after schema migration 0006. It derives canonical
    /// transaction identities and statement fingerprints from stored rows and
    /// allocates deterministic occurrence slots without dropping data. The
    /// marker makes the operation idempotent across restarts.
    /// </summary>
    public static async Task BackfillCanonicalIdentitiesAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS data_migrations (
                    name TEXT PRIMARY KEY,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                )
                """;
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT 1 FROM data_migrations WHERE name = @name";
            check.Parameters.AddWithValue("name", "0006_canonical_identity_backfill");
            if (await check.ExecuteScalarAsync(cancellationToken) is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }
        }

        var transactions = new List<BackfillTransaction>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT id, customer_id, financial_account_id, transaction_date,
                       (amount * 100)::bigint, normalized_description, created_at
                FROM transactions
                ORDER BY customer_id, financial_account_id, created_at, id
                """;
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var date = reader.GetFieldValue<DateOnly>(3);
                long cents = reader.GetInt64(4);
                string normalized = reader.GetString(5);
                transactions.Add(new BackfillTransaction(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), date, cents, normalized, StatementIdentity.ComputeIdentityKey(date, cents, normalized), reader.GetFieldValue<DateTime>(6)));
            }
        }

        // Stage every row on a unique temporary key before writing canonical
        // keys. This avoids colliding with a row that already owns the target
        // (identity_key, occurrence_index) while the unique constraint is live.
        await using (var stage = connection.CreateCommand())
        {
            stage.Transaction = transaction;
            stage.CommandText = """
                UPDATE transactions
                SET identity_key = 'tmp:' || id::text,
                    occurrence_index = 1,
                    identity_version = 1
                """;
            await stage.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var group in transactions.GroupBy(
                     row => (row.CustomerId, row.AccountId, row.IdentityKey)))
        {
            int occurrence = 1;
            foreach (var row in group.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id))
            {
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE transactions
                    SET identity_key = @identity_key,
                        identity_version = 1,
                        occurrence_index = @occurrence_index
                    WHERE id = @id
                    """;
                update.Parameters.AddWithValue("identity_key", row.IdentityKey);
                update.Parameters.AddWithValue("occurrence_index", occurrence);
                update.Parameters.AddWithValue("id", row.Id);
                await update.ExecuteNonQueryAsync(cancellationToken);
                occurrence++;
            }
        }

        var statements = new List<BackfillStatement>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT id, customer_id, financial_account_id, start_date, end_date, created_at
                FROM statement_imports
                ORDER BY customer_id, financial_account_id, created_at, id
                """;
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                statements.Add(new BackfillStatement(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetFieldValue<DateOnly>(3), reader.GetFieldValue<DateOnly>(4), reader.GetFieldValue<DateTime>(5), []));
            }
        }

        var statementsById = statements.ToDictionary(statement => statement.Id);
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT statement_import_id, id, transaction_date, description,
                       normalized_description, (amount * 100)::bigint, source_page, source_row_number
                FROM transactions
                WHERE statement_import_id IS NOT NULL
                ORDER BY statement_import_id, source_row_number, id
                """;
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!statementsById.TryGetValue(reader.GetGuid(0), out var statement))
                {
                    continue;
                }

                var date = reader.GetFieldValue<DateOnly>(2);
                long cents = reader.GetInt64(5);
                string description = reader.GetString(3);
                string normalized = reader.GetString(4);
                statement.Rows.Add(new ParsedStatementRow(
                    reader.GetGuid(1),
                    reader.IsDBNull(7) ? 1 : reader.GetInt32(7),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? 1 : reader.GetInt32(7),
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["description"] = description,
                        ["amount"] = StatementText.FormatCents(cents),
                    },
                    date,
                    description,
                    normalized,
                    cents,
                    [],
                    [],
                    null,
                    null));
            }
        }

        foreach (var statement in statements)
        {
            statement.Fingerprint = StatementIdentity.ComputeStatementFingerprint(statement.Rows, statement.StartDate, statement.EndDate);
        }

        await using (var stage = connection.CreateCommand())
        {
            stage.Transaction = transaction;
            stage.CommandText = """
                UPDATE statement_imports
                SET statement_fingerprint = 'tmp:' || id::text,
                    fingerprint_occurrence = 1
                """;
            await stage.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var group in statements.GroupBy(
                     statement => (statement.CustomerId, statement.AccountId, statement.Fingerprint)))
        {
            int occurrence = 1;
            foreach (var statement in group.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id))
            {
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE statement_imports
                    SET statement_fingerprint = @fingerprint,
                        fingerprint_occurrence = @occurrence
                    WHERE id = @id
                    """;
                update.Parameters.AddWithValue("fingerprint", statement.Fingerprint);
                update.Parameters.AddWithValue("occurrence", occurrence);
                update.Parameters.AddWithValue("id", statement.Id);
                await update.ExecuteNonQueryAsync(cancellationToken);
                occurrence++;
            }
        }

        await using (var marker = connection.CreateCommand())
        {
            marker.Transaction = transaction;
            marker.CommandText = "INSERT INTO data_migrations (name) VALUES ('0006_canonical_identity_backfill')";
            await marker.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private sealed class BackfillTransaction(Guid id,
                                             Guid customerId,
                                             Guid accountId,
                                             DateOnly date,
                                             long cents,
                                             string normalized,
                                             string identityKey,
                                             DateTime createdAt)
    {
        public Guid Id { get; } = id;
        public Guid CustomerId { get; } = customerId;
        public Guid AccountId { get; } = accountId;
        public DateOnly Date { get; } = date;
        public long Cents { get; } = cents;
        public string Normalized { get; } = normalized;
        public string IdentityKey { get; } = identityKey;
        public DateTime CreatedAt { get; } = createdAt;
    }

    private sealed class BackfillStatement(Guid id,
                                           Guid customerId,
                                           Guid accountId,
                                           DateOnly startDate,
                                           DateOnly endDate,
                                           DateTime createdAt,
                                           List<ParsedStatementRow> rows)
    {
        public Guid Id { get; } = id;
        public Guid CustomerId { get; } = customerId;
        public Guid AccountId { get; } = accountId;
        public DateOnly StartDate { get; } = startDate;
        public DateOnly EndDate { get; } = endDate;
        public DateTime CreatedAt { get; } = createdAt;
        public List<ParsedStatementRow> Rows { get; } = rows;
        public string Fingerprint { get; set; } = string.Empty;
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
            string[] parts = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length == 2)
            {
                builder.Password = Uri.UnescapeDataString(parts[1]);
            }
        }

        string? sslmode = QueryValue(uri.Query, "sslmode");
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
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
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
        ("0006_statement_import", """
            ALTER TABLE financial_accounts
                ADD COLUMN import_version BIGINT NOT NULL DEFAULT 0;

            ALTER TABLE statement_imports
                ADD COLUMN version INT NOT NULL DEFAULT 1,
                ADD COLUMN fingerprint_occurrence INT NOT NULL DEFAULT 1,
                ADD COLUMN duplicate_policy_version INT NOT NULL DEFAULT 1,
                ADD COLUMN possible_duplicate_count INT NOT NULL DEFAULT 0,
                ADD COLUMN exact_override_count INT NOT NULL DEFAULT 0,
                ADD COLUMN statement_override_count INT NOT NULL DEFAULT 0,
                ADD COLUMN override_summary JSONB NOT NULL DEFAULT '{}'::jsonb;

            WITH ranked AS (
                SELECT id,
                       ROW_NUMBER() OVER (
                           PARTITION BY customer_id, financial_account_id, statement_fingerprint
                           ORDER BY created_at, id) AS occurrence
                FROM statement_imports
            )
            UPDATE statement_imports s
            SET fingerprint_occurrence = ranked.occurrence
            FROM ranked
            WHERE s.id = ranked.id;

            ALTER TABLE statement_imports
                ADD CONSTRAINT statement_imports_customer_account_id_key
                    UNIQUE (customer_id, financial_account_id, id),
                ADD CONSTRAINT statement_imports_version_check CHECK (version > 0),
                ADD CONSTRAINT statement_imports_fingerprint_occurrence_check CHECK (fingerprint_occurrence > 0),
                ADD CONSTRAINT statement_imports_dates_check CHECK (start_date <= end_date),
                ADD CONSTRAINT statement_imports_counts_check CHECK (
                    total_rows >= 0 AND imported_count >= 0 AND suppressed_count >= 0 AND
                    skipped_count >= 0 AND possible_duplicate_count >= 0 AND
                    exact_override_count >= 0 AND statement_override_count >= 0 AND
                    total_rows = imported_count + suppressed_count + skipped_count
                );

            CREATE UNIQUE INDEX statement_imports_fingerprint_slot_uidx
                ON statement_imports(customer_id, financial_account_id, statement_fingerprint, fingerprint_occurrence);
            CREATE INDEX statement_imports_history_idx
                ON statement_imports(customer_id, created_at DESC, id DESC);

            ALTER TABLE transactions
                ADD COLUMN identity_version INT NOT NULL DEFAULT 1,
                ADD COLUMN source_id_namespace TEXT NULL,
                ADD COLUMN source_id_value TEXT NULL,
                ADD CONSTRAINT transactions_source_id_pair_check CHECK (
                    (source_id_namespace IS NULL) = (source_id_value IS NULL)
                ),
                ADD CONSTRAINT transactions_identity_version_check CHECK (identity_version > 0),
                ADD CONSTRAINT transactions_occurrence_index_check CHECK (occurrence_index > 0);

            WITH ranked AS (
                SELECT id,
                       ROW_NUMBER() OVER (
                           PARTITION BY customer_id, financial_account_id, identity_key
                           ORDER BY created_at, id) AS occurrence
                FROM transactions
            )
            UPDATE transactions t
            SET occurrence_index = ranked.occurrence
            FROM ranked
            WHERE t.id = ranked.id;

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM transactions t
                    JOIN statement_imports s ON s.id = t.statement_import_id
                    WHERE s.customer_id <> t.customer_id
                       OR s.financial_account_id <> t.financial_account_id
                ) THEN
                    RAISE EXCEPTION 'transaction statement ownership mismatch';
                END IF;
            END $$;

            ALTER TABLE transactions
                ADD CONSTRAINT transactions_customer_id_key UNIQUE (customer_id, id),
                ADD CONSTRAINT transactions_identity_occurrence_key
                    UNIQUE (customer_id, financial_account_id, identity_key, occurrence_index),
                ADD CONSTRAINT transactions_statement_row_key
                    UNIQUE (customer_id, statement_import_id, source_row_number),
                ADD CONSTRAINT transactions_statement_account_fk
                    FOREIGN KEY (customer_id, financial_account_id, statement_import_id)
                    REFERENCES statement_imports(customer_id, financial_account_id, id) ON DELETE CASCADE;

            CREATE INDEX transactions_statement_children_idx
                ON transactions(customer_id, statement_import_id, source_row_number);
            CREATE INDEX transactions_near_match_idx
                ON transactions(customer_id, financial_account_id, amount, transaction_date);

            ALTER TABLE transaction_merchant_tags ADD COLUMN customer_id UUID NULL;

            UPDATE transaction_merchant_tags j
            SET customer_id = t.customer_id
            FROM transactions t
            WHERE t.id = j.transaction_id;

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM transaction_merchant_tags j
                    JOIN transactions t ON t.id = j.transaction_id
                    JOIN merchant_tags m ON m.id = j.merchant_tag_id
                    WHERE t.customer_id <> m.customer_id
                ) THEN
                    RAISE EXCEPTION 'transaction merchant tag ownership mismatch';
                END IF;
            END $$;

            ALTER TABLE transaction_merchant_tags
                ALTER COLUMN customer_id SET NOT NULL,
                ADD CONSTRAINT transaction_merchant_tags_transaction_fk
                    FOREIGN KEY (customer_id, transaction_id)
                    REFERENCES transactions(customer_id, id) ON DELETE CASCADE,
                ADD CONSTRAINT transaction_merchant_tags_tag_fk
                    FOREIGN KEY (customer_id, merchant_tag_id)
                    REFERENCES merchant_tags(customer_id, id) ON DELETE CASCADE;

            CREATE INDEX transaction_merchant_tags_customer_tag_idx
                ON transaction_merchant_tags(customer_id, merchant_tag_id);

            CREATE TABLE import_quota_locks (
                customer_id UUID PRIMARY KEY REFERENCES customers(id) ON DELETE CASCADE,
                locked_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE import_reviews (
                id UUID PRIMARY KEY,
                customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
                financial_account_id UUID NOT NULL,
                source_kind TEXT NOT NULL,
                parser_config JSONB NOT NULL,
                currency TEXT NOT NULL DEFAULT 'ZAR' CHECK (currency = 'ZAR'),
                start_date DATE NULL,
                end_date DATE NULL,
                version INT NOT NULL DEFAULT 1,
                status TEXT NOT NULL DEFAULT 'Prepared'
                    CHECK (status IN ('Prepared', 'Abandoned', 'Expired')),
                expires_at TIMESTAMPTZ NOT NULL,
                account_import_version BIGINT NOT NULL,
                duplicate_policy_version INT NOT NULL,
                statement_fingerprint TEXT NOT NULL,
                summary_hash TEXT NOT NULL,
                total_rows INT NOT NULL,
                imported_count INT NOT NULL,
                suppressed_count INT NOT NULL,
                skipped_count INT NOT NULL,
                possible_duplicate_count INT NOT NULL,
                exact_override_count INT NOT NULL,
                statement_override BOOLEAN NOT NULL DEFAULT FALSE,
                statement_override_count INT NOT NULL DEFAULT 0,
                payload_bytes BIGINT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                UNIQUE (customer_id, id),
                FOREIGN KEY (customer_id, financial_account_id)
                    REFERENCES financial_accounts(customer_id, id) ON DELETE RESTRICT,
                CONSTRAINT import_reviews_version_check CHECK (version > 0),
                CONSTRAINT import_reviews_dates_check CHECK (
                    (start_date IS NULL AND end_date IS NULL) OR start_date <= end_date
                ),
                CONSTRAINT import_reviews_counts_check CHECK (
                    total_rows >= 0 AND imported_count >= 0 AND suppressed_count >= 0 AND
                    skipped_count >= 0 AND possible_duplicate_count >= 0 AND
                    exact_override_count >= 0 AND statement_override_count >= 0 AND
                    total_rows = imported_count + suppressed_count + skipped_count
                )
            );

            CREATE INDEX import_reviews_customer_status_idx
                ON import_reviews(customer_id, status, expires_at);
            CREATE INDEX import_reviews_account_idx
                ON import_reviews(customer_id, financial_account_id);

            CREATE TABLE import_review_rows (
                row_id UUID PRIMARY KEY,
                review_id UUID NOT NULL,
                customer_id UUID NOT NULL,
                source_line INT NOT NULL,
                source_page INT NULL,
                source_row_number INT NOT NULL,
                raw_values JSONB NOT NULL,
                transaction_date DATE NULL,
                description TEXT NOT NULL,
                normalized_description TEXT NOT NULL,
                amount_cents BIGINT NULL,
                source_id_namespace TEXT NULL,
                source_id_value TEXT NULL,
                validation_codes JSONB NOT NULL,
                warnings JSONB NOT NULL,
                duplicate_state TEXT NOT NULL DEFAULT 'none'
                    CHECK (duplicate_state IN ('none', 'suppressed', 'override', 'within_file', 'suppressed_within_file')),
                duplicate_reasons JSONB NOT NULL DEFAULT '[]'::jsonb,
                matching_transaction_ids JSONB NOT NULL DEFAULT '[]'::jsonb,
                matching_import_ids JSONB NOT NULL DEFAULT '[]'::jsonb,
                near_candidates JSONB NOT NULL DEFAULT '[]'::jsonb,
                near_candidate_total INT NOT NULL DEFAULT 0,
                within_file_group TEXT NULL,
                within_file_state TEXT NOT NULL DEFAULT 'none'
                    CHECK (within_file_state IN ('none', 'pending', 'keep', 'skip')),
                skip BOOLEAN NOT NULL DEFAULT FALSE,
                override BOOLEAN NOT NULL DEFAULT FALSE,
                suggested_tag_ids JSONB NOT NULL DEFAULT '[]'::jsonb,
                selected_tag_ids JSONB NOT NULL DEFAULT '[]'::jsonb,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                FOREIGN KEY (customer_id, review_id)
                    REFERENCES import_reviews(customer_id, id) ON DELETE CASCADE,
                CONSTRAINT import_review_rows_source_id_pair_check CHECK (
                    (source_id_namespace IS NULL) = (source_id_value IS NULL)
                ),
                UNIQUE (review_id, row_id)
            );

            CREATE INDEX import_review_rows_review_idx
                ON import_review_rows(review_id, source_row_number);

            CREATE TABLE import_commit_receipts (
                id UUID PRIMARY KEY,
                customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
                operation_id UUID NOT NULL,
                financial_account_id UUID NOT NULL,
                request_digest TEXT NOT NULL,
                state TEXT NOT NULL CHECK (state IN ('Committed', 'Deleted')),
                statement_import_id UUID NULL,
                result JSONB NOT NULL,
                deletion_digest TEXT NULL,
                deletion_expected_count BIGINT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                UNIQUE (customer_id, operation_id),
                FOREIGN KEY (customer_id, financial_account_id)
                    REFERENCES financial_accounts(customer_id, id) ON DELETE RESTRICT
            );

            CREATE INDEX import_commit_receipts_customer_idx
                ON import_commit_receipts(customer_id, operation_id);
            """)
        ,
        ("0007_transaction_workspace", """
            CREATE TABLE transaction_operations (
                customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
                operation_id UUID NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('Prepared', 'Applied', 'Abandoned', 'Expired')),
                selection_mode TEXT NOT NULL CHECK (selection_mode IN ('explicit', 'all_matching')),
                request_digest TEXT NOT NULL,
                filter_digest TEXT NULL,
                confirmation_hash TEXT NULL,
                action JSONB NOT NULL DEFAULT '{}'::jsonb,
                target_stamps JSONB NOT NULL DEFAULT '{}'::jsonb,
                selected_count INT NOT NULL CHECK (selected_count >= 0),
                changed_count INT NOT NULL CHECK (changed_count >= 0),
                unchanged_count INT NOT NULL CHECK (unchanged_count >= 0),
                captured_at TIMESTAMPTZ NOT NULL,
                expires_at TIMESTAMPTZ NOT NULL,
                applied_at TIMESTAMPTZ NULL,
                terminal_at TIMESTAMPTZ NULL,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                PRIMARY KEY (customer_id, operation_id),
                CONSTRAINT transaction_operations_counts_check CHECK (
                    selected_count = changed_count + unchanged_count
                ),
                CONSTRAINT transaction_operations_expiry_check CHECK (expires_at > captured_at),
                CONSTRAINT transaction_operations_applied_check CHECK (
                    (status = 'Applied') = (applied_at IS NOT NULL)
                ),
                CONSTRAINT transaction_operations_prepared_payload_check CHECK (
                    status <> 'Prepared' OR (
                        confirmation_hash IS NOT NULL AND action <> '{}'::jsonb
                    )
                )
            );

            CREATE INDEX transaction_operations_customer_status_idx
                ON transaction_operations(customer_id, status, expires_at);
            CREATE INDEX transaction_operations_terminal_idx
                ON transaction_operations(customer_id, terminal_at DESC)
                WHERE status <> 'Prepared';

            CREATE TABLE transaction_operation_items (
                customer_id UUID NOT NULL,
                operation_id UUID NOT NULL,
                transaction_id UUID NOT NULL,
                transaction_version INT NOT NULL CHECK (transaction_version > 0),
                financial_account_id UUID NOT NULL,
                statement_import_id UUID NOT NULL,
                ordinal INT NOT NULL CHECK (ordinal >= 0),
                PRIMARY KEY (customer_id, operation_id, transaction_id),
                FOREIGN KEY (customer_id, operation_id)
                    REFERENCES transaction_operations(customer_id, operation_id) ON DELETE CASCADE
            );

            CREATE INDEX transaction_operation_items_header_idx
                ON transaction_operation_items(customer_id, operation_id, ordinal);

            CREATE INDEX transactions_customer_date_idx
                ON transactions(customer_id, transaction_date, id);
            CREATE INDEX transactions_customer_amount_idx
                ON transactions(customer_id, amount, id);
            """)
    ];
}
