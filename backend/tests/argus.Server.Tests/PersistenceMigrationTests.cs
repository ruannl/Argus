using argus.Server.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace argus.Server.Tests;

public sealed class PersistenceMigrationTests
{
    [Fact]
    public async Task FreshSchemaCreatesEfHistoryAndEmptyLegacyLedger()
    {
        var schema = "fresh_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM schema_migrations")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
            Assert.Equal(20L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task PopulatedLegacyPrefixAdoptsInPlaceAndPreservesLedgerTimestamps()
    {
        var schema = "legacy_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.ApplyLegacyAsync(schemaUrl, 8, canonicalMarkerPresent: true);
            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);

            Guid customerId = Guid.NewGuid();
            Guid accountId = Guid.NewGuid();
            Guid categoryId = Guid.NewGuid();
            Guid statementId = Guid.NewGuid();
            Guid transactionId = Guid.NewGuid();
            Guid tagId = Guid.NewGuid();
            await PersistenceTestSupport.ExecuteAsync(connection, $"""
                INSERT INTO customers (id, canonical_email, password_hash, email_verified_at,
                                       subscriber_activated_at, created_at, updated_at)
                VALUES ('{customerId}', 'adopted@example.test', 'hash', NOW(), NOW(), NOW(), NOW());
                INSERT INTO financial_accounts (id, customer_id, display_name, institution, currency, created_at, updated_at)
                VALUES ('{accountId}', '{customerId}', 'Adopted cheque', 'FNB', 'ZAR', NOW(), NOW());
                INSERT INTO categories (id, customer_id, display_name, normalized_name, kind, is_system, is_default, created_at, updated_at)
                VALUES ('{categoryId}', '{customerId}', 'Groceries', 'groceries', 'Expense', FALSE, FALSE, NOW(), NOW());
                INSERT INTO merchant_tags (id, customer_id, display_name, normalized_name, created_at, updated_at)
                VALUES ('{tagId}', '{customerId}', 'Adopted tag', 'adopted tag', NOW(), NOW());
                INSERT INTO statement_imports (
                    id, customer_id, financial_account_id, source_type, statement_label,
                    statement_fingerprint, start_date, end_date, created_at, updated_at)
                VALUES (
                    '{statementId}', '{customerId}', '{accountId}', 'fnb_csv', 'Adopted statement',
                    'legacy-fingerprint', DATE '2025-01-01', DATE '2025-01-31', NOW(), NOW());
                INSERT INTO transactions (
                    id, customer_id, financial_account_id, statement_import_id, transaction_date,
                    description, normalized_description, amount, category_id, source_type,
                    source_row_number, identity_key, created_at, updated_at)
                VALUES (
                    '{transactionId}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-05',
                    'Adopted row', 'adopted row', -12.34, '{categoryId}', 'fnb_csv',
                    1, 'legacy-key', NOW(), NOW());
                INSERT INTO transaction_merchant_tags (transaction_id, merchant_tag_id, customer_id)
                VALUES ('{transactionId}', '{tagId}', '{customerId}');
                """);

            var beforeLedger = await ReadLedgerAsync(connection);
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            var afterLedger = await ReadLedgerAsync(connection);

            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
            Assert.Equal(8, afterLedger.Count);
            Assert.Equal(beforeLedger, afterLedger);
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM transactions")));
            Assert.Equal("legacy-key", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT identity_key FROM transactions"))!);
            Assert.Equal("legacy-fingerprint", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT statement_fingerprint FROM statement_imports"))!);
            Assert.True((bool)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT category_confirmed FROM transactions"))!);
            Assert.Equal("adopted row", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT merchant_key FROM transactions"))!);
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM import_review_rows")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task LegacyPrefixWithoutDataLedgerGetsTheLedgerAndMarkerFromTheCoordinator()
    {
        var schema = "nodata_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.ApplyLegacyAsync(schemaUrl, 5);
            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            await PersistenceTestSupport.ExecuteAsync(connection, "DROP TABLE data_migrations");

            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);

            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
            Assert.Equal(5L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM schema_migrations")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task CanonicalBackfillIsAtomicAndMarkerPresentReentryDoesNotRewrite()
    {
        var schema = "backfill_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.ApplyLegacyAsync(schemaUrl, 6);
            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);

            Guid customerId = Guid.NewGuid();
            Guid accountId = Guid.NewGuid();
            Guid categoryId = Guid.NewGuid();
            Guid statementId = Guid.NewGuid();
            Guid transactionId = Guid.NewGuid();
            await PersistenceTestSupport.ExecuteAsync(connection, $"""
                INSERT INTO customers (id, canonical_email, password_hash, created_at, updated_at)
                VALUES ('{customerId}', 'backfill@example.test', 'hash', NOW(), NOW());
                INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
                VALUES ('{accountId}', '{customerId}', 'Cheque', 'ZAR', NOW(), NOW());
                INSERT INTO categories (id, customer_id, display_name, normalized_name, kind, is_system, is_default, created_at, updated_at)
                VALUES ('{categoryId}', '{customerId}', 'Groceries', 'groceries', 'Expense', FALSE, FALSE, NOW(), NOW());
                INSERT INTO statement_imports (
                    id, customer_id, financial_account_id, source_type, statement_label,
                    statement_fingerprint, start_date, end_date, created_at, updated_at)
                VALUES (
                    '{statementId}', '{customerId}', '{accountId}', 'fnb_csv', 'Backfill statement',
                    'legacy-fingerprint', DATE '2025-01-01', DATE '2025-01-31', NOW(), NOW());
                INSERT INTO transactions (
                    id, customer_id, financial_account_id, statement_import_id, transaction_date,
                    description, normalized_description, amount, category_id, source_type,
                    source_row_number, identity_key, created_at, updated_at)
                VALUES (
                    '{transactionId}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-05',
                    'Backfill row', 'backfill row', -12.34, '{categoryId}', 'fnb_csv',
                    1, 'legacy-key', NOW(), NOW());
                """);

            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);

            string canonicalKey = (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT identity_key FROM transactions"))!;
            string canonicalFingerprint = (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT statement_fingerprint FROM statement_imports"))!;
            DateTime markerAt = (DateTime)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT applied_at FROM data_migrations WHERE name = '0006_canonical_identity_backfill'"))!;
            Assert.NotEqual("legacy-key", canonicalKey);
            Assert.NotEqual("legacy-fingerprint", canonicalFingerprint);

            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);

            Assert.Equal(canonicalKey, (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT identity_key FROM transactions"))!);
            Assert.Equal(canonicalFingerprint, (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT statement_fingerprint FROM statement_imports"))!);
            Assert.Equal(markerAt, (DateTime)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT applied_at FROM data_migrations WHERE name = '0006_canonical_identity_backfill'"))!);
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task InvalidHistoryAndSchemaDriftRejectWithoutEffects()
    {
        var schema = "invalid_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.ApplyLegacyAsync(schemaUrl, 1);
            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            await PersistenceTestSupport.ExecuteAsync(connection, "INSERT INTO schema_migrations (name) VALUES ('9999_unknown')");

            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistenceTestSupport.RunCoordinatorAsync(schemaUrl));
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = '__EFMigrationsHistory'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task UnknownAndNonPrefixEfHistoryRejectWithoutDataEffects()
    {
        var schema = "efhistory_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            await PersistenceTestSupport.SeedRepresentativeRowsAsync(schemaUrl);

            await using (var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                await PersistenceTestSupport.ExecuteAsync(connection, "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('9999_unknown', '10.0.4')");
            }
            IReadOnlyList<string> unknownBefore = await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl);
            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistenceTestSupport.RunCoordinatorAsync(schemaUrl));
            Assert.Equal(unknownBefore, await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl));

            await using (var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                await PersistenceTestSupport.ExecuteAsync(connection, "DELETE FROM \"__EFMigrationsHistory\"");
                await PersistenceTestSupport.ExecuteAsync(connection, "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20261110000002_0002_email_verification_tokens', '10.0.4')");
            }
            IReadOnlyList<string> nonPrefixBefore = await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl);
            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistenceTestSupport.RunCoordinatorAsync(schemaUrl));
            Assert.Equal(nonPrefixBefore, await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task StructuralDriftRejectsWithoutDataEffects()
    {
        var schema = "drift_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            await PersistenceTestSupport.SeedRepresentativeRowsAsync(schemaUrl);
            await using (var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                await PersistenceTestSupport.ExecuteAsync(connection, "DROP INDEX transactions_customer_unconfirmed_idx");
            }
            IReadOnlyList<string> before = await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl);
            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistenceTestSupport.RunCoordinatorAsync(schemaUrl));
            Assert.Equal(before, await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task PartialEfProgressResumesFromCommittedHistory()
    {
        var schema = "partial_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.ApplyLegacyAsync(schemaUrl, 5, canonicalMarkerPresent: true);
            await PersistenceTestSupport.RunEfToAsync(schemaUrl, LegacyMigrations.All[5].EfId);

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal(6L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));

            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);

            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
            Assert.Equal(5L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM schema_migrations")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task ConcurrentInitializersSerializeOnTheSessionAdvisoryLock()
    {
        var schema = "concurrent_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            var barrier = new BarrierInitializationFaultInjector(BarrierInitializationFaultInjector.BarrierPoint.AfterMigrationCommit);
            Task first = PersistenceTestSupport.RunCoordinatorAsync(schemaUrl, barrier);
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));

            Task second = PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            await Task.Delay(500);
            Assert.False(second.IsCompleted, "second initializer must wait on the session advisory lock");

            barrier.Release.TrySetResult();
            await Task.WhenAll(first, second);

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task SyncAndMixedInitializersSerializeOnTheSessionAdvisoryLock()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string syncSchema = "sync_" + Guid.NewGuid().ToString("N");
        string mixedSchema = "mixed_" + Guid.NewGuid().ToString("N");
        string syncUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, syncSchema);
        string mixedUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, mixedSchema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, syncSchema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, mixedSchema);
        try
        {
            string syncOwnerUrl = DatabaseRoleFixture.AppendRole(syncUrl, DatabaseIsolationContract.OwnerRole);
            var syncBarrier = new BarrierInitializationFaultInjector(BarrierInitializationFaultInjector.BarrierPoint.AfterMigrationCommit);
            Task firstSync = Task.Run(() => new InitializationCoordinator(syncOwnerUrl, syncBarrier).Initialize());
            await syncBarrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Task secondSync = Task.Run(() => new InitializationCoordinator(syncOwnerUrl, NoopInitializationFaultInjector.Instance).Initialize());
            await Task.Delay(500);
            Assert.False(secondSync.IsCompleted, "second synchronous initializer must wait on the session advisory lock");
            syncBarrier.Release.TrySetResult();
            await Task.WhenAll(firstSync, secondSync);

            string mixedOwnerUrl = DatabaseRoleFixture.AppendRole(mixedUrl, DatabaseIsolationContract.OwnerRole);
            var mixedBarrier = new BarrierInitializationFaultInjector(BarrierInitializationFaultInjector.BarrierPoint.AfterMigrationCommit);
            Task firstMixedSync = Task.Run(() => new InitializationCoordinator(mixedOwnerUrl, mixedBarrier).Initialize());
            await mixedBarrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Task secondMixedAsync = PersistenceTestSupport.RunCoordinatorAsync(mixedUrl);
            await Task.Delay(500);
            Assert.False(secondMixedAsync.IsCompleted, "mixed async entrant must wait on the synchronous initializer");
            mixedBarrier.Release.TrySetResult();
            await Task.WhenAll(firstMixedSync, secondMixedAsync);

            await using var syncConnection = await PersistenceTestSupport.OpenSchemaConnectionAsync(syncUrl);
            await using var mixedConnection = await PersistenceTestSupport.OpenSchemaConnectionAsync(mixedUrl);
            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(syncConnection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(mixedConnection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, syncSchema);
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, mixedSchema);
        }
    }

    [Fact]
    public async Task ContenderWaitsAtBeforeBackfillGap()
    {
        var schema = "gap_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.SeedLegacyBackfillFixtureAsync(schemaUrl);
            await AssertContenderWaitsAtAsync(schemaUrl, BarrierInitializationFaultInjector.BarrierPoint.BeforeBackfill);

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task ContenderWaitsDuringBackfillStaging()
    {
        var schema = "gapstaging_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.SeedLegacyBackfillFixtureAsync(schemaUrl);
            await AssertContenderWaitsAtAsync(schemaUrl, BarrierInitializationFaultInjector.BarrierPoint.AfterBackfillStaging);

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task ContenderWaitsBeforeBackfillMarker()
    {
        var schema = "gapmarker_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.SeedLegacyBackfillFixtureAsync(schemaUrl);
            await AssertContenderWaitsAtAsync(schemaUrl, BarrierInitializationFaultInjector.BarrierPoint.BeforeBackfillMarker);

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task MarkerPresentNoPendingConcurrencyUsesTheSameLock()
    {
        var schema = "nopending_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            await AssertContenderWaitsAtAsync(schemaUrl, BarrierInitializationFaultInjector.BarrierPoint.AfterLockAcquireAttempt);

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task SameSessionHoldsLockAcrossMigrationCommit()
    {
        var schema = "samepid_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            var barrier = new BarrierInitializationFaultInjector(BarrierInitializationFaultInjector.BarrierPoint.AfterMigrationCommit);
            Task coordinator = PersistenceTestSupport.RunCoordinatorAsync(schemaUrl, barrier);
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            int backendPid = barrier.BackendPid ?? throw new InvalidOperationException("initializer backend id was not captured");

            await using (var observer = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                int observedPid = Convert.ToInt32(await PersistenceTestSupport.ScalarAsync(observer,
                    $"SELECT l.pid FROM pg_locks l JOIN pg_database d ON d.oid = l.database WHERE l.locktype = 'advisory' AND l.classid = {InitializationCoordinator.LockKey1}::oid AND l.objid = {InitializationCoordinator.LockKey2}::oid AND l.objsubid = 2 AND d.datname = current_database()"));
                Assert.Equal(backendPid, observedPid);
            }

            barrier.Release.TrySetResult();
            await coordinator;
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task CancellationWhileWaitingReleasesTheAttemptCleanly()
    {
        var schema = "cancel_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            var barrier = new BarrierInitializationFaultInjector(BarrierInitializationFaultInjector.BarrierPoint.AfterMigrationCommit);
            Task first = PersistenceTestSupport.RunCoordinatorAsync(schemaUrl, barrier);
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            Task second = PersistenceTestSupport.RunCoordinatorAsync(schemaUrl, cancellationToken: cancellation.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

            barrier.Release.TrySetResult();
            await first;

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection,
                $"SELECT COUNT(*) FROM pg_locks l JOIN pg_database d ON d.oid = l.database WHERE l.locktype = 'advisory' AND l.classid = {InitializationCoordinator.LockKey1}::oid AND l.objid = {InitializationCoordinator.LockKey2}::oid AND l.objsubid = 2 AND d.datname = current_database()")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task SiblingDatabaseAdvisoryLockIsNotSelectedByTheTestDatabaseScope()
    {
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string sibling = "sibling_" + Guid.NewGuid().ToString("N");
        var baseBuilder = new NpgsqlConnectionStringBuilder(PersistenceOptions.BuildConnectionString(databaseUrl));
        var maintenanceBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString) { Database = "postgres" };
        var siblingBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString) { Database = sibling };
        await using var maintenance = new NpgsqlConnection(maintenanceBuilder.ConnectionString);
        await maintenance.OpenAsync();
        try
        {
            await PersistenceTestSupport.ExecuteAsync(maintenance, $"CREATE DATABASE {sibling}");
            await using (var siblingConnection = new NpgsqlConnection(siblingBuilder.ConnectionString))
            {
                await siblingConnection.OpenAsync();
                await PersistenceTestSupport.ScalarAsync(siblingConnection, $"SELECT pg_try_advisory_lock({InitializationCoordinator.LockKey1}, {InitializationCoordinator.LockKey2})");

                await using var testConnection = new NpgsqlConnection(PersistenceOptions.BuildConnectionString(databaseUrl));
                await testConnection.OpenAsync();
                int selectedInTestDatabase = Convert.ToInt32(await PersistenceTestSupport.ScalarAsync(testConnection,
                    $"SELECT COUNT(*) FROM pg_locks l JOIN pg_database d ON d.oid = l.database WHERE l.locktype = 'advisory' AND l.classid = {InitializationCoordinator.LockKey1}::oid AND l.objid = {InitializationCoordinator.LockKey2}::oid AND l.objsubid = 2 AND d.datname = current_database()"));
                Assert.Equal(0, selectedInTestDatabase);

                int selectedInSibling = Convert.ToInt32(await PersistenceTestSupport.ScalarAsync(siblingConnection,
                    $"SELECT COUNT(*) FROM pg_locks l JOIN pg_database d ON d.oid = l.database WHERE l.locktype = 'advisory' AND l.classid = {InitializationCoordinator.LockKey1}::oid AND l.objid = {InitializationCoordinator.LockKey2}::oid AND l.objsubid = 2 AND d.datname = current_database()"));
                Assert.Equal(1, selectedInSibling);
            }
        }
        finally
        {
            await PersistenceTestSupport.ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS {sibling} WITH (FORCE)");
        }
    }

    [Fact]
    public async Task ForcedSessionLossAbortsBackfillWithoutMarker()
    {
        var schema = "sessionloss_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            var barrier = new BarrierInitializationFaultInjector(BarrierInitializationFaultInjector.BarrierPoint.BeforeBackfill);
            Task first = PersistenceTestSupport.RunCoordinatorAsync(schemaUrl, barrier);
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));

            int backendPid = barrier.BackendPid ?? throw new InvalidOperationException("initializer backend id was not captured");
            Assert.True(backendPid > 0);
            await using (var observer = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                int observedPid = Convert.ToInt32(await PersistenceTestSupport.ScalarAsync(observer,
                    $"SELECT l.pid FROM pg_locks l JOIN pg_database d ON d.oid = l.database WHERE l.locktype = 'advisory' AND l.classid = {InitializationCoordinator.LockKey1}::oid AND l.objid = {InitializationCoordinator.LockKey2}::oid AND l.objsubid = 2 AND d.datname = current_database()"));
                Assert.Equal(backendPid, observedPid);
                await PersistenceTestSupport.ScalarAsync(observer, $"SELECT pg_terminate_backend({backendPid})");
            }

            barrier.Release.TrySetResult();
            await Assert.ThrowsAnyAsync<Exception>(() => first);

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));

            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task AmbiguousLockAcquisitionAbortsBeforeSchemaEffects()
    {
        var schema = "ambiguous_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            var fault = new FaultingInitializationFaultInjector(FaultingInitializationFaultInjector.FaultPoint.AfterLockAcquireAttempt);
            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistenceTestSupport.RunCoordinatorAsync(schemaUrl, fault));

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'")));
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection,
                $"SELECT COUNT(*) FROM pg_locks l JOIN pg_database d ON d.oid = l.database WHERE l.locktype = 'advisory' AND l.classid = {InitializationCoordinator.LockKey1}::oid AND l.objid = {InitializationCoordinator.LockKey2}::oid AND l.objsubid = 2 AND d.datname = current_database()")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task FailedUnlockReportsFailureAndLeavesARecoverableInitializedState()
    {
        var schema = "unlock_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            var fault = new FaultingInitializationFaultInjector(FaultingInitializationFaultInjector.FaultPoint.BeforeUnlock);
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => PersistenceTestSupport.RunCoordinatorAsync(schemaUrl, fault));
            Assert.Contains("cleanup failed", exception.Message, StringComparison.OrdinalIgnoreCase);

            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection,
                $"SELECT COUNT(*) FROM pg_locks l JOIN pg_database d ON d.oid = l.database WHERE l.locktype = 'advisory' AND l.classid = {InitializationCoordinator.LockKey1}::oid AND l.objid = {InitializationCoordinator.LockKey2}::oid AND l.objsubid = 2 AND d.datname = current_database()")));

            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task CancellationDuringBackfillStagingRollsBackTheAtomicTransformation()
    {
        var schema = "staging_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.ApplyLegacyAsync(schemaUrl, 6);
            await using var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);

            Guid customerId = Guid.NewGuid();
            Guid accountId = Guid.NewGuid();
            Guid categoryId = Guid.NewGuid();
            Guid statementId = Guid.NewGuid();
            Guid transactionId = Guid.NewGuid();
            await PersistenceTestSupport.ExecuteAsync(connection, $"""
                INSERT INTO customers (id, canonical_email, password_hash, created_at, updated_at)
                VALUES ('{customerId}', 'staging@example.test', 'hash', NOW(), NOW());
                INSERT INTO financial_accounts (id, customer_id, display_name, currency, created_at, updated_at)
                VALUES ('{accountId}', '{customerId}', 'Cheque', 'ZAR', NOW(), NOW());
                INSERT INTO categories (id, customer_id, display_name, normalized_name, kind, is_system, is_default, created_at, updated_at)
                VALUES ('{categoryId}', '{customerId}', 'Groceries', 'groceries', 'Expense', FALSE, FALSE, NOW(), NOW());
                INSERT INTO statement_imports (
                    id, customer_id, financial_account_id, source_type, statement_label,
                    statement_fingerprint, start_date, end_date, created_at, updated_at)
                VALUES (
                    '{statementId}', '{customerId}', '{accountId}', 'fnb_csv', 'Staging statement',
                    'legacy-fingerprint', DATE '2025-01-01', DATE '2025-01-31', NOW(), NOW());
                INSERT INTO transactions (
                    id, customer_id, financial_account_id, statement_import_id, transaction_date,
                    description, normalized_description, amount, category_id, source_type,
                    source_row_number, identity_key, created_at, updated_at)
                VALUES (
                    '{transactionId}', '{customerId}', '{accountId}', '{statementId}', DATE '2025-01-05',
                    'Staging row', 'staging row', -12.34, '{categoryId}', 'fnb_csv',
                    1, 'legacy-key', NOW(), NOW());
                """);

            var barrier = new BarrierInitializationFaultInjector(BarrierInitializationFaultInjector.BarrierPoint.AfterBackfillStaging);
            using var cancellation = new CancellationTokenSource();
            Task coordinator = PersistenceTestSupport.RunCoordinatorAsync(schemaUrl, barrier, cancellation.Token);
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator);

            Assert.Equal("legacy-key", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT identity_key FROM transactions"))!);
            Assert.Equal("legacy-fingerprint", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT statement_fingerprint FROM statement_imports"))!);
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));

            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            Assert.NotEqual("legacy-key", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT identity_key FROM transactions"))!);
            Assert.NotEqual("legacy-fingerprint", (string)(await PersistenceTestSupport.ScalarAsync(connection, "SELECT statement_fingerprint FROM statement_imports"))!);
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(connection, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    private static async Task AssertContenderWaitsAtAsync(string schemaUrl,
                                                           BarrierInitializationFaultInjector.BarrierPoint point)
    {
        var barrier = new BarrierInitializationFaultInjector(point);
        Task first = PersistenceTestSupport.RunCoordinatorAsync(schemaUrl, barrier);
        await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var counting = new CountingInitializationFaultInjector();
        Task second = PersistenceTestSupport.RunCoordinatorAsync(schemaUrl, counting);
        await counting.FirstFailedAttempt.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(second.IsCompleted, "contender must still be waiting after a failed advisory-lock attempt");

        barrier.Release.TrySetResult();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task SemanticDriftRejectsChangedDefinitionsWithoutDataEffects()
    {
        await AssertDriftRejectedAsync("DROP INDEX transactions_customer_unconfirmed_idx; CREATE INDEX transactions_customer_unconfirmed_idx ON transactions(customer_id) WHERE category_confirmed = TRUE;");
        await AssertDriftRejectedAsync("ALTER TABLE transactions ALTER COLUMN category_confirmed SET DEFAULT FALSE;");
        await AssertDriftRejectedAsync("ALTER TABLE financial_accounts ALTER COLUMN currency SET DEFAULT 'zar';");
        await AssertDriftRejectedAsync("CREATE OR REPLACE FUNCTION argus_merchant_key(description text) RETURNS text LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$ SELECT 'changed' $$;");
        await AssertDriftRejectedAsync("""
            CREATE OR REPLACE FUNCTION argus_merchant_key(description text) RETURNS text
            LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
                SELECT string_agg(token, ' ' ORDER BY ordinality)
                FROM (
                    SELECT token, ordinality
                    FROM unnest(regexp_split_to_array(lower(coalesce(description, '')), '[^A-Z0-9]+'))
                         WITH ORDINALITY AS parts(token, ordinality)
                    WHERE token <> ''
                      AND token !~ '^[0-9]+$'
                      AND token !~ '^(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)$'
                      AND length(token) > 1
                    ORDER BY ordinality
                    LIMIT 2
                ) AS significant
            $$;
            """);
        await AssertDriftRejectedAsync("""
            CREATE OR REPLACE FUNCTION argus_merchant_key(description text) RETURNS text
            LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
                SELECT string_agg(token, ' ' ORDER BY ordinality)
                FROM (
                    SELECT token, ordinality
                    FROM unnest(regexp_split_to_array(lower(coalesce(description, '')), '[^a-z0-9]+::textCREATE OR REPLACE '))
                         WITH ORDINALITY AS parts(token, ordinality)
                    WHERE token <> ''
                      AND token !~ '^[0-9]+$'
                      AND token !~ '^(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)$'
                      AND length(token) > 1
                    ORDER BY ordinality
                    LIMIT 2
                ) AS significant
            $$;
            """);
        await AssertDriftRejectedAsync(MerchantKeyFunctionSql("[^a-z0-9]+::text"));
        await AssertDriftRejectedAsync("ALTER TABLE sessions DROP CONSTRAINT sessions_customer_id_fkey; ALTER TABLE sessions ADD CONSTRAINT sessions_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE;");
        await AssertDriftRejectedAsync("ALTER TABLE categories DROP CONSTRAINT categories_kind_check; ALTER TABLE categories ADD CONSTRAINT categories_kind_check CHECK (is_system = TRUE OR is_system = FALSE);");
        await AssertDriftRejectedAsync("ALTER TABLE categories DROP CONSTRAINT categories_kind_check; ALTER TABLE categories ADD CONSTRAINT categories_kind_check CHECK ((is_system = TRUE AND kind = 'uncategorised' AND is_archived = FALSE) OR (is_system = FALSE AND kind IN ('Income', 'Expense', 'Transfer', 'Excluded')));");
        await AssertDriftRejectedAsync("ALTER TABLE categories DROP CONSTRAINT categories_kind_check; ALTER TABLE categories ADD CONSTRAINT categories_kind_check CHECK (is_system = TRUE AND (kind = 'Uncategorised' OR is_archived = FALSE) OR (is_system = FALSE AND kind IN ('Income', 'Expense', 'Transfer', 'Excluded')));");
        await AssertDriftRejectedAsync("ALTER TABLE transactions DROP CONSTRAINT transactions_source_id_pair_check; ALTER TABLE transactions ADD CONSTRAINT transactions_source_id_pair_check CHECK ((source_id_namespace IS NULL) = (source_id_value IS NULL)) NOT VALID;");
        await AssertDriftRejectedAsync("ALTER TABLE categories DROP CONSTRAINT categories_customer_id_normalized_name_key; CREATE INDEX categories_customer_id_normalized_name_key ON categories(customer_id, normalized_name);");
    }

    [Fact]
    public async Task QuotedSqlTextInsideLiteralsIsNotNormalizedAway()
    {
        var schema = "quotedliteral_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            await using (var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                await PersistenceTestSupport.ExecuteAsync(connection, """
                    ALTER TABLE categories DROP CONSTRAINT categories_kind_check;
                    ALTER TABLE categories ADD CONSTRAINT categories_kind_check CHECK (
                        (is_system = TRUE AND kind = 'uncategorised' AND is_archived = FALSE) OR
                        (is_system = FALSE AND kind IN ('Income', 'Expense::text', 'Transfer', 'Excluded')));
                    """);
            }

            IReadOnlyList<string> before = await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl);
            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistenceTestSupport.RunCoordinatorAsync(schemaUrl));
            Assert.Equal(before, await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task ChangedMerchantKeyCastChainRejectsWithoutDataEffects()
    {
        await AssertDriftRejectedAsync(MerchantKeyFunctionSql("[^a-z0-9]+", descriptionExpression: "description::jsonb::text"));
    }

    [Fact]
    public async Task MerchantKeyPlaceholderCollisionRejectsWithoutDataEffects()
    {
        await AssertDriftRejectedAsync($"SET check_function_bodies = off; {MerchantKeyFunctionSql("[^a-z0-9]+", predicate: "token <> __argus_quoted_1__")}");
    }

    [Fact]
    public async Task MerchantKeyLiteralContainingPlaceholderTextRejectsWithoutDataEffects()
    {
        await AssertDriftRejectedAsync(MerchantKeyFunctionSql("[^a-z0-9]+__argus_quoted_1__"));
    }

    [Fact]
    public async Task OlderPrefixDriftRejectsBeforeMigrationEffects()
    {
        var schema = "prefixdrift_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.ApplyLegacyAsync(schemaUrl, 5, canonicalMarkerPresent: true);
            await PersistenceTestSupport.SeedLegacy0005RowsAsync(schemaUrl);
            await using (var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                await PersistenceTestSupport.ExecuteAsync(connection, "DROP INDEX transactions_customer_account_idx");
            }

            IReadOnlyList<string> before = await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl);
            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistenceTestSupport.RunCoordinatorAsync(schemaUrl));
            Assert.Equal(before, await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl));

            await using var check = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(check, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = '__EFMigrationsHistory'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    private static string MerchantKeyFunctionSql(string regexLiteral,
                                                 string predicate = "token <> ''",
                                                 string descriptionExpression = "description")
    {
        return $"""
            CREATE OR REPLACE FUNCTION argus_merchant_key(description text) RETURNS text
            LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
                SELECT string_agg(token, ' ' ORDER BY ordinality)
                FROM (
                    SELECT token, ordinality
                    FROM unnest(regexp_split_to_array(lower(coalesce({descriptionExpression}, '')), '{regexLiteral}'))
                         WITH ORDINALITY AS parts(token, ordinality)
                    WHERE {predicate}
                      AND token !~ '^[0-9]+$'
                      AND token !~ '^(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)$'
                      AND length(token) > 1
                    ORDER BY ordinality
                    LIMIT 2
                ) AS significant
            $$;
            """;
    }

    private static async Task AssertDriftRejectedAsync(string driftSql)
    {
        var schema = "semantic_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            await PersistenceTestSupport.SeedRepresentativeRowsAsync(schemaUrl);
            await using (var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                await PersistenceTestSupport.ExecuteAsync(connection, driftSql);
            }
            IReadOnlyList<string> before = await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl);

            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistenceTestSupport.RunCoordinatorAsync(schemaUrl));
            Assert.Equal(before, await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task ForeignSchemaForeignKeyTargetRejectsWithoutDataEffects()
    {
        var schema = "foreignfk_" + Guid.NewGuid().ToString("N");
        var foreignSchema = "other_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);
            await PersistenceTestSupport.SeedRepresentativeRowsAsync(schemaUrl);
            await using (var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                await PersistenceTestSupport.ExecuteAsync(connection, $"""
                    CREATE SCHEMA {foreignSchema};
                    CREATE TABLE {foreignSchema}.customers (id uuid PRIMARY KEY);
                    ALTER TABLE sessions DROP CONSTRAINT sessions_customer_id_fkey;
                    ALTER TABLE sessions ADD CONSTRAINT sessions_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES {foreignSchema}.customers(id);
                    """);
            }

            IReadOnlyList<string> before = await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl);
            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistenceTestSupport.RunCoordinatorAsync(schemaUrl));
            Assert.Equal(before, await PersistenceTestSupport.ReadDataDigestsAsync(schemaUrl));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, foreignSchema);
        }
    }

    [Fact]
    public async Task EmptyRetainedLegacyLedgerRecoversFromZero()
    {
        var schema = "emptyledger_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await using (var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                await PersistenceTestSupport.ExecuteAsync(connection, """
                    CREATE TABLE schema_migrations (
                        name TEXT PRIMARY KEY,
                        applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                    );
                    CREATE TABLE data_migrations (
                        name TEXT PRIMARY KEY,
                        applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                    );
                    """);
            }

            await PersistenceTestSupport.RunCoordinatorAsync(schemaUrl);

            await using var check = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal((long)MigrationCatalog.EfIds.Count, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(check, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"")));
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(check, "SELECT COUNT(*) FROM schema_migrations")));
            Assert.Equal(1L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(check, "SELECT COUNT(*) FROM data_migrations WHERE name = '0006_canonical_identity_backfill'")));
            Assert.Equal(20L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(check, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public async Task CanonicalMarkerWithoutSchemaProgressRejects()
    {
        var schema = "markerzero_" + Guid.NewGuid().ToString("N");
        string databaseUrl = TestEnvironment.RequireTestDatabaseUrl();
        string schemaUrl = PersistenceTestSupport.SchemaUrl(databaseUrl, schema);
        await PersistenceTestSupport.CreateSchemaAsync(databaseUrl, schema);
        try
        {
            await using (var connection = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl))
            {
                await PersistenceTestSupport.ExecuteAsync(connection, """
                    CREATE TABLE schema_migrations (
                        name TEXT PRIMARY KEY,
                        applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                    );
                    CREATE TABLE data_migrations (
                        name TEXT PRIMARY KEY,
                        applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                    );
                    INSERT INTO data_migrations (name) VALUES ('0006_canonical_identity_backfill');
                    """);
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => PersistenceTestSupport.RunCoordinatorAsync(schemaUrl));
            await using var check = await PersistenceTestSupport.OpenSchemaConnectionAsync(schemaUrl);
            Assert.Equal(0L, Convert.ToInt64(await PersistenceTestSupport.ScalarAsync(check, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = '__EFMigrationsHistory'")));
        }
        finally
        {
            await PersistenceTestSupport.DropSchemaAsync(databaseUrl, schema);
        }
    }

    [Fact]
    public void DesignTimeDatabaseApplyIsRejectedBeforeOpeningAConnection()
    {
        var factory = new DesignTimeDbContextFactory();
        using var context = factory.CreateDbContext([]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => context.Database.Migrate());
        Assert.Contains("design-time database access", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<string>> ReadLedgerAsync(NpgsqlConnection connection)
    {
        var entries = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name || '|' || applied_at::text FROM schema_migrations ORDER BY applied_at, name";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(reader.GetString(0));
        }

        return entries;
    }
}
