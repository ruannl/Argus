using System.Text;

namespace argus.Server.Data;

/// <summary>
/// The forward customer-isolation schema: alias ownership foreign keys,
/// transaction/session authority helper, owner-only cleanup gateways, RLS on
/// all protected relations and the runtime grant allowlist. The SQL is kept
/// out of the EF migration body so tests and validators can compare the same
/// contract text.
/// </summary>
public static class CustomerIsolationSql
{
    /// <summary>
    /// Expected definition for <c>argus_current_customer_id</c>. The startup validator compares
    /// the installed prosrc against this text after whitespace normalization.
    /// </summary>
    public const string AuthorityFunctionBody = """
                DECLARE
                    customer_text text := current_setting('argus.customer_id', true);
                    session_text text := current_setting('argus.session_hash', true);
                    transaction_text text := current_setting('argus.transaction_id', true);
                    result_customer uuid;
                BEGIN
                    IF customer_text IS NULL OR session_text IS NULL OR transaction_text IS NULL THEN
                        RETURN NULL;
                    END IF;
                    IF customer_text = '' OR session_text = '' OR transaction_text = '' THEN
                        RETURN NULL;
                    END IF;
                    IF customer_text !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN
                        RETURN NULL;
                    END IF;
                    IF customer_text = '00000000-0000-0000-0000-000000000000' THEN
                        RETURN NULL;
                    END IF;
                    IF session_text !~ '^[0-9a-f]{64}$' THEN
                        RETURN NULL;
                    END IF;
                    IF transaction_text !~ '^[0-9]+$' THEN
                        RETURN NULL;
                    END IF;
                    IF transaction_text IS DISTINCT FROM pg_current_xact_id_if_assigned()::text THEN
                        RETURN NULL;
                    END IF;

                    SELECT c.id INTO result_customer
                    FROM customers c
                    JOIN sessions s ON s.customer_id = c.id
                    WHERE c.id = customer_text::uuid
                      AND s.token_hash = session_text
                      AND s.revoked_at IS NULL
                      AND s.expires_at > clock_timestamp()
                      AND s.last_seen_at > clock_timestamp() - INTERVAL '30 minutes'
                      AND c.email_verified_at IS NOT NULL
                      AND c.subscriber_activated_at IS NOT NULL;

                    RETURN result_customer;
                END

        """;

    /// <summary>
    /// Expected definition for <c>argus_cleanup_import_reviews</c>. The startup validator compares
    /// the installed prosrc against this text after whitespace normalization.
    /// </summary>
    public const string ImportCleanupBody = """
                DECLARE
                    expired_ids uuid[];
                    expired_count integer := 0;
                    pruned_count integer := 0;
                BEGIN
                    SELECT array_agg(id) INTO expired_ids
                    FROM (
                        SELECT id
                        FROM import_reviews
                        WHERE status = 'Prepared' AND expires_at <= clock_timestamp()
                        ORDER BY expires_at
                        LIMIT 1000
                        FOR UPDATE
                    ) expired;

                    IF expired_ids IS NOT NULL THEN
                        DELETE FROM import_review_rows WHERE review_id = ANY(expired_ids);
                        UPDATE import_reviews
                        SET status = 'Expired',
                            parser_config = '{}'::jsonb,
                            start_date = NULL,
                            end_date = NULL,
                            statement_fingerprint = '',
                            summary_hash = '',
                            total_rows = 0,
                            imported_count = 0,
                            suppressed_count = 0,
                            skipped_count = 0,
                            possible_duplicate_count = 0,
                            exact_override_count = 0,
                            statement_override = FALSE,
                            statement_override_count = 0,
                            payload_bytes = 0,
                            version = version + 1,
                            updated_at = clock_timestamp()
                        WHERE id = ANY(expired_ids);
                        GET DIAGNOSTICS expired_count = ROW_COUNT;
                    END IF;

                    DELETE FROM import_reviews
                    WHERE status IN ('Abandoned', 'Expired')
                      AND updated_at < clock_timestamp() - INTERVAL '30 days';
                    GET DIAGNOSTICS pruned_count = ROW_COUNT;

                    RETURN jsonb_build_object('expired', expired_count, 'pruned', pruned_count);
                END

        """;

    /// <summary>
    /// Expected definition for <c>argus_cleanup_transaction_operations</c>. The startup validator compares
    /// the installed prosrc against this text after whitespace normalization.
    /// </summary>
    public const string TransactionCleanupBody = """
                DECLARE
                    customer_ids uuid[];
                    operation_ids uuid[];
                    expired_count integer := 0;
                BEGIN
                    SELECT array_agg(customer_id), array_agg(operation_id)
                    INTO customer_ids, operation_ids
                    FROM (
                        SELECT customer_id, operation_id
                        FROM transaction_operations
                        WHERE status = 'Prepared' AND expires_at <= clock_timestamp()
                        ORDER BY expires_at
                        LIMIT 1000
                        FOR UPDATE
                    ) expired;

                    IF customer_ids IS NOT NULL THEN
                        DELETE FROM transaction_operation_items i
                        USING unnest(customer_ids, operation_ids) AS expired(customer_id, operation_id)
                        WHERE i.customer_id = expired.customer_id
                          AND i.operation_id = expired.operation_id;

                        UPDATE transaction_operations o
                        SET status = 'Expired',
                            terminal_at = clock_timestamp(),
                            action = '{}'::jsonb,
                            target_stamps = '{}'::jsonb,
                            updated_at = clock_timestamp()
                        FROM unnest(customer_ids, operation_ids) AS expired(customer_id, operation_id)
                        WHERE o.customer_id = expired.customer_id
                          AND o.operation_id = expired.operation_id
                          AND o.status = 'Prepared'
                          AND o.expires_at <= clock_timestamp();
                        GET DIAGNOSTICS expired_count = ROW_COUNT;
                    END IF;

                    RETURN jsonb_build_object('expired', expired_count);
                END

        """;

    public static string BuildUpSql()
    {
        var builder = new StringBuilder();
        builder.AppendLine("-- Anchor every unqualified reference on the controlled application schema.");
        builder.AppendLine("SELECT set_config('search_path', format('%I, pg_catalog, pg_temp', current_schema()), true);");
        builder.AppendLine();
        AppendAliasPreflight(builder);
        AppendAliasForeignKeys(builder);
        AppendFunctions(builder);
        AppendPolicies(builder);
        AppendGrants(builder);

        return builder.ToString();
    }

    private static void AppendAliasPreflight(StringBuilder builder)
    {
        builder.AppendLine("""
            DO $preflight$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM merchant_aliases a
                    JOIN merchant_tags t ON t.id = a.merchant_tag_id
                    WHERE t.customer_id <> a.customer_id
                ) THEN
                    RAISE EXCEPTION 'customer isolation preflight failed: merchant alias tag ownership mismatch';
                END IF;
                IF EXISTS (
                    SELECT 1
                    FROM merchant_aliases a
                    JOIN categories c ON c.id = a.category_id
                    WHERE a.category_id IS NOT NULL AND c.customer_id <> a.customer_id
                ) THEN
                    RAISE EXCEPTION 'customer isolation preflight failed: merchant alias category ownership mismatch';
                END IF;
            END
            $preflight$;
            """);
        builder.AppendLine();
    }

    private static void AppendAliasForeignKeys(StringBuilder builder)
    {
        builder.AppendLine("""
            DO $fk$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'merchant_aliases_customer_tag_fk'
                      AND conrelid = 'merchant_aliases'::regclass
                ) THEN
                    ALTER TABLE merchant_aliases
                        ADD CONSTRAINT merchant_aliases_customer_tag_fk
                        FOREIGN KEY (customer_id, merchant_tag_id)
                        REFERENCES merchant_tags (customer_id, id)
                        ON DELETE CASCADE;
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'merchant_aliases_customer_category_fk'
                      AND conrelid = 'merchant_aliases'::regclass
                ) THEN
                    ALTER TABLE merchant_aliases
                        ADD CONSTRAINT merchant_aliases_customer_category_fk
                        FOREIGN KEY (customer_id, category_id)
                        REFERENCES categories (customer_id, id)
                        ON DELETE NO ACTION;
                END IF;
            END
            $fk$;
            """);
        builder.AppendLine();
    }

    private static void AppendFunctions(StringBuilder builder)
    {
        builder.AppendLine("""
            CREATE OR REPLACE FUNCTION argus_current_customer_id() RETURNS uuid
            LANGUAGE plpgsql
            VOLATILE
            PARALLEL UNSAFE
            SECURITY INVOKER
            SET search_path FROM CURRENT
            AS $fn$
            DECLARE
                customer_text text := current_setting('argus.customer_id', true);
                session_text text := current_setting('argus.session_hash', true);
                transaction_text text := current_setting('argus.transaction_id', true);
                result_customer uuid;
            BEGIN
                IF customer_text IS NULL OR session_text IS NULL OR transaction_text IS NULL THEN
                    RETURN NULL;
                END IF;
                IF customer_text = '' OR session_text = '' OR transaction_text = '' THEN
                    RETURN NULL;
                END IF;
                IF customer_text !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN
                    RETURN NULL;
                END IF;
                IF customer_text = '00000000-0000-0000-0000-000000000000' THEN
                    RETURN NULL;
                END IF;
                IF session_text !~ '^[0-9a-f]{64}$' THEN
                    RETURN NULL;
                END IF;
                IF transaction_text !~ '^[0-9]+$' THEN
                    RETURN NULL;
                END IF;
                IF transaction_text IS DISTINCT FROM pg_current_xact_id_if_assigned()::text THEN
                    RETURN NULL;
                END IF;

                SELECT c.id INTO result_customer
                FROM customers c
                JOIN sessions s ON s.customer_id = c.id
                WHERE c.id = customer_text::uuid
                  AND s.token_hash = session_text
                  AND s.revoked_at IS NULL
                  AND s.expires_at > clock_timestamp()
                  AND s.last_seen_at > clock_timestamp() - INTERVAL '30 minutes'
                  AND c.email_verified_at IS NOT NULL
                  AND c.subscriber_activated_at IS NOT NULL;

                RETURN result_customer;
            END
            $fn$;
            """);
        builder.AppendLine();
        builder.AppendLine("""
            CREATE OR REPLACE FUNCTION argus_cleanup_import_reviews() RETURNS jsonb
            LANGUAGE plpgsql
            VOLATILE
            PARALLEL UNSAFE
            SECURITY DEFINER
            SET search_path FROM CURRENT
            AS $fn$
            DECLARE
                expired_ids uuid[];
                expired_count integer := 0;
                pruned_count integer := 0;
            BEGIN
                SELECT array_agg(id) INTO expired_ids
                FROM (
                    SELECT id
                    FROM import_reviews
                    WHERE status = 'Prepared' AND expires_at <= clock_timestamp()
                    ORDER BY expires_at
                    LIMIT 1000
                    FOR UPDATE
                ) expired;

                IF expired_ids IS NOT NULL THEN
                    DELETE FROM import_review_rows WHERE review_id = ANY(expired_ids);
                    UPDATE import_reviews
                    SET status = 'Expired',
                        parser_config = '{}'::jsonb,
                        start_date = NULL,
                        end_date = NULL,
                        statement_fingerprint = '',
                        summary_hash = '',
                        total_rows = 0,
                        imported_count = 0,
                        suppressed_count = 0,
                        skipped_count = 0,
                        possible_duplicate_count = 0,
                        exact_override_count = 0,
                        statement_override = FALSE,
                        statement_override_count = 0,
                        payload_bytes = 0,
                        version = version + 1,
                        updated_at = clock_timestamp()
                    WHERE id = ANY(expired_ids);
                    GET DIAGNOSTICS expired_count = ROW_COUNT;
                END IF;

                DELETE FROM import_reviews
                WHERE status IN ('Abandoned', 'Expired')
                  AND updated_at < clock_timestamp() - INTERVAL '30 days';
                GET DIAGNOSTICS pruned_count = ROW_COUNT;

                RETURN jsonb_build_object('expired', expired_count, 'pruned', pruned_count);
            END
            $fn$;
            """);
        builder.AppendLine();
        builder.AppendLine("""
            CREATE OR REPLACE FUNCTION argus_cleanup_transaction_operations() RETURNS jsonb
            LANGUAGE plpgsql
            VOLATILE
            PARALLEL UNSAFE
            SECURITY DEFINER
            SET search_path FROM CURRENT
            AS $fn$
            DECLARE
                customer_ids uuid[];
                operation_ids uuid[];
                expired_count integer := 0;
            BEGIN
                SELECT array_agg(customer_id), array_agg(operation_id)
                INTO customer_ids, operation_ids
                FROM (
                    SELECT customer_id, operation_id
                    FROM transaction_operations
                    WHERE status = 'Prepared' AND expires_at <= clock_timestamp()
                    ORDER BY expires_at
                    LIMIT 1000
                    FOR UPDATE
                ) expired;

                IF customer_ids IS NOT NULL THEN
                    DELETE FROM transaction_operation_items i
                    USING unnest(customer_ids, operation_ids) AS expired(customer_id, operation_id)
                    WHERE i.customer_id = expired.customer_id
                      AND i.operation_id = expired.operation_id;

                    UPDATE transaction_operations o
                    SET status = 'Expired',
                        terminal_at = clock_timestamp(),
                        action = '{}'::jsonb,
                        target_stamps = '{}'::jsonb,
                        updated_at = clock_timestamp()
                    FROM unnest(customer_ids, operation_ids) AS expired(customer_id, operation_id)
                    WHERE o.customer_id = expired.customer_id
                      AND o.operation_id = expired.operation_id
                      AND o.status = 'Prepared'
                      AND o.expires_at <= clock_timestamp();
                    GET DIAGNOSTICS expired_count = ROW_COUNT;
                END IF;

                RETURN jsonb_build_object('expired', expired_count);
            END
            $fn$;
            """);
        builder.AppendLine();
        builder.AppendLine($"""
            REVOKE ALL ON FUNCTION {DatabaseIsolationContract.MerchantKeyFunction}(text) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {DatabaseIsolationContract.MerchantKeyFunction}(text)
                TO {DatabaseIsolationContract.OwnerRole}, {DatabaseIsolationContract.RuntimeRole};
            REVOKE ALL ON FUNCTION {DatabaseIsolationContract.AuthorityFunction}() FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {DatabaseIsolationContract.AuthorityFunction}()
                TO {DatabaseIsolationContract.OwnerRole}, {DatabaseIsolationContract.RuntimeRole};
            REVOKE ALL ON FUNCTION {DatabaseIsolationContract.ImportCleanupFunction}() FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {DatabaseIsolationContract.ImportCleanupFunction}()
                TO {DatabaseIsolationContract.OwnerRole}, {DatabaseIsolationContract.RuntimeRole};
            REVOKE ALL ON FUNCTION {DatabaseIsolationContract.TransactionCleanupFunction}() FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {DatabaseIsolationContract.TransactionCleanupFunction}()
                TO {DatabaseIsolationContract.OwnerRole}, {DatabaseIsolationContract.RuntimeRole};
            """);
        builder.AppendLine();
    }

    private static void AppendPolicies(StringBuilder builder)
    {
        foreach (string table in DatabaseIsolationContract.ProtectedTables)
        {
            builder.AppendLine($"""
                ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS {DatabaseIsolationContract.RuntimePolicy} ON {table};
                CREATE POLICY {DatabaseIsolationContract.RuntimePolicy} ON {table}
                    AS PERMISSIVE FOR ALL TO {DatabaseIsolationContract.RuntimeRole}
                    USING (customer_id = (SELECT {DatabaseIsolationContract.AuthorityFunction}()))
                    WITH CHECK (COALESCE(customer_id = (SELECT {DatabaseIsolationContract.AuthorityFunction}()), false));
                DROP POLICY IF EXISTS {DatabaseIsolationContract.OwnerPolicy} ON {table};
                CREATE POLICY {DatabaseIsolationContract.OwnerPolicy} ON {table}
                    AS PERMISSIVE FOR ALL TO {DatabaseIsolationContract.OwnerRole}
                    USING (true)
                    WITH CHECK (true);
                """);
        }
        builder.AppendLine();
    }

    private static void AppendGrants(StringBuilder builder)
    {
        builder.AppendLine($"""
            GRANT SELECT ON TABLE customers TO {DatabaseIsolationContract.RuntimeRole};
            GRANT INSERT (id, canonical_email, password_hash, created_at, updated_at)
                ON TABLE customers TO {DatabaseIsolationContract.RuntimeRole};
            GRANT UPDATE (password_hash, email_verified_at, subscriber_activated_at, next_billing_at,
                          failed_login_count, locked_until, updated_at, categories_seeded_at)
                ON TABLE customers TO {DatabaseIsolationContract.RuntimeRole};
            GRANT SELECT, INSERT ON TABLE sessions TO {DatabaseIsolationContract.RuntimeRole};
            GRANT UPDATE (last_seen_at, revoked_at) ON TABLE sessions TO {DatabaseIsolationContract.RuntimeRole};
            GRANT SELECT, INSERT ON TABLE email_verification_tokens TO {DatabaseIsolationContract.RuntimeRole};
            GRANT UPDATE (consumed_at) ON TABLE email_verification_tokens TO {DatabaseIsolationContract.RuntimeRole};
            GRANT SELECT, INSERT ON TABLE password_reset_tokens TO {DatabaseIsolationContract.RuntimeRole};
            GRANT UPDATE (consumed_at) ON TABLE password_reset_tokens TO {DatabaseIsolationContract.RuntimeRole};
            GRANT SELECT ON TABLE schema_migrations TO {DatabaseIsolationContract.RuntimeRole};
            GRANT SELECT ON TABLE data_migrations TO {DatabaseIsolationContract.RuntimeRole};
            GRANT SELECT ON TABLE "__EFMigrationsHistory" TO {DatabaseIsolationContract.RuntimeRole};
            """);
        builder.AppendLine();

        foreach (string table in DatabaseIsolationContract.ProtectedTables)
        {
            builder.Append($"GRANT SELECT, INSERT ON TABLE {table} TO {DatabaseIsolationContract.RuntimeRole};");
            if (DatabaseIsolationContract.RuntimeDeleteTables.Contains(table, StringComparer.Ordinal))
            {
                builder.Append($" GRANT DELETE ON TABLE {table} TO {DatabaseIsolationContract.RuntimeRole};");
            }
            builder.AppendLine();

            if (DatabaseIsolationContract.RuntimeUpdateColumns.TryGetValue(table, out string[]? columns))
            {
                builder.AppendLine($"GRANT UPDATE ({string.Join(", ", columns)}) ON TABLE {table} TO {DatabaseIsolationContract.RuntimeRole};");
            }
        }
        builder.AppendLine();
    }
}
