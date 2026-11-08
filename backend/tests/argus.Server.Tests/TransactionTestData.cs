using System.Net;
using System.Text.Json;
using Npgsql;

namespace argus.Server.Tests;

/// <summary>
/// Shared synthetic workspace fixture for the transaction workspace tests.
/// Public flows use the real HTTP handlers; bulk rows are seeded directly into
/// the real PostgreSQL schema so query and concurrency claims stay real.
/// </summary>
public abstract class TransactionTestBase(KitFactory factory) : ApiTestBase(factory)
{
    protected async Task<(string Session, Guid CustomerId)> NewCustomerAsync()
    {
        var email = UniqueEmail();
        var session = await RegisterVerifyLoginAsync(email);
        var me = await SendAsync(Get("/v1/me", session: session));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        return (session, (await BodyAsync(me)).GetProperty("id").GetGuid());
    }

    protected async Task<Guid> CreateAccountAsync(string session, string name = "Cheque")
    {
        var response = await SendAsync(Post(
            "/v1/finance/accounts", Json(new { display_name = name }), session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    protected async Task<JsonElement[]> ListCategoriesAsync(string session)
    {
        var response = await SendAsync(Get("/v1/finance/categories", session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response))
            .GetProperty("categories")
            .EnumerateArray()
            .ToArray();
    }

    protected async Task<(Guid SystemCategoryId, Guid ExpenseCategoryId)> DefaultCategoriesAsync(
        string session)
    {
        var categories = await ListCategoriesAsync(session);
        var system = categories.Single(category => category.GetProperty("is_system").GetBoolean());
        var expense = categories.First(
            category => category.GetProperty("kind").GetString() == "Expense");
        return (system.GetProperty("id").GetGuid(), expense.GetProperty("id").GetGuid());
    }

    protected async Task<Guid> CreateTagAsync(string session, string name)
    {
        var response = await SendAsync(Post(
            "/v1/finance/merchant-tags", Json(new { display_name = name }), session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    protected async Task<Guid> SeedTagAsync(Guid customerId, string name)
    {
        var id = Guid.NewGuid();
        await ExecuteSqlParamsAsync(
            """
            INSERT INTO merchant_tags (id, customer_id, display_name, normalized_name, created_at, updated_at)
            VALUES (@id, @customer_id, @name, @normalized, NOW(), NOW())
            """,
            ("id", id),
            ("customer_id", customerId),
            ("name", name),
            ("normalized", name.ToLowerInvariant()));
        return id;
    }

    protected async Task<Guid> SeedStatementAsync(
        Guid customerId,
        Guid accountId,
        bool hidden = false,
        string label = "Synthetic statement")
    {
        var id = Guid.NewGuid();
        await ExecuteSqlParamsAsync(
            """
            INSERT INTO statement_imports (
                id, customer_id, financial_account_id, source_type, statement_label,
                statement_fingerprint, fingerprint_occurrence, start_date, end_date,
                total_rows, imported_count, suppressed_count, skipped_count,
                is_hidden, hidden_at, created_at, updated_at)
            VALUES (
                @id, @customer_id, @account_id, 'fnb_csv', @label,
                @fingerprint, 1, DATE '2024-01-01', DATE '2024-12-31',
                0, 0, 0, 0, @hidden, CASE WHEN @hidden THEN NOW() ELSE NULL END,
                NOW(), NOW())
            """,
            ("id", id),
            ("customer_id", customerId),
            ("account_id", accountId),
            ("label", label),
            ("fingerprint", "fp-" + id.ToString("N")),
            ("hidden", hidden));
        return id;
    }

    protected async Task<Guid> SeedTransactionAsync(
        Guid customerId,
        Guid accountId,
        Guid statementId,
        Guid categoryId,
        DateOnly date,
        string description,
        decimal amount,
        int sourceRowNumber = 1,
        Guid[]? tagIds = null,
        bool hidden = false,
        int version = 1,
        int? sourcePage = null)
    {
        var id = Guid.NewGuid();
        await ExecuteSqlParamsAsync(
            """
            INSERT INTO transactions (
                id, customer_id, financial_account_id, statement_import_id,
                transaction_date, description, normalized_description, amount,
                category_id, source_type, source_page, source_row_number,
                identity_key, occurrence_index, is_hidden_by_user, version,
                created_at, updated_at)
            VALUES (
                @id, @customer_id, @account_id, @statement_id,
                @date, @description, @normalized, @amount,
                @category_id, 'fnb_csv', @source_page, @source_row,
                @identity_key, 1, @hidden, @version, NOW(), NOW())
            """,
            ("id", id),
            ("customer_id", customerId),
            ("account_id", accountId),
            ("statement_id", statementId),
            ("date", date),
            ("description", description),
            ("normalized", description.ToLowerInvariant()),
            ("amount", amount),
            ("category_id", categoryId),
            ("source_page", sourcePage),
            ("source_row", sourceRowNumber),
            ("identity_key", "seed-" + id.ToString("N")),
            ("hidden", hidden),
            ("version", version));

        if (tagIds is not null)
        {
            foreach (var tagId in tagIds)
            {
                await ExecuteSqlParamsAsync(
                    """
                    INSERT INTO transaction_merchant_tags (transaction_id, merchant_tag_id, customer_id)
                    VALUES (@transaction_id, @tag_id, @customer_id)
                    """,
                    ("transaction_id", id),
                    ("tag_id", tagId),
                    ("customer_id", customerId));
            }
        }

        return id;
    }

    protected async Task SetStatementHiddenAsync(Guid statementId, bool hidden)
    {
        await ExecuteSqlParamsAsync(
            """
            UPDATE statement_imports
            SET is_hidden = @hidden,
                hidden_at = CASE WHEN @hidden THEN NOW() ELSE NULL END,
                version = version + 1,
                updated_at = NOW()
            WHERE id = @id
            """,
            ("hidden", hidden),
            ("id", statementId));
    }

    protected async Task ExecuteSqlParamsAsync(
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        var db = Factory.CreateDb();
        await using var connection = await db.OpenAsync();
        await using var command = connection.CreateCommand();
        // Bulk synthetic fixtures insert tens of thousands of rows with foreign
        // key and index maintenance. CI runners vary widely in disk and CPU
        // throughput, so the production 30-second statement bound is not a
        // fixture timeout; keep a generous bound so slow runners do not fail a
        // correct fixture.
        command.CommandTimeout = 600;
        // Fixture rows are read back after this call returns, so commit
        // visibility is all the test needs. Skipping the local WAL flush keeps
        // a slow or contended CI disk from stalling bulk setup; durability of
        // synthetic fixture data is not under test.
        command.CommandText = "SET synchronous_commit = off; " + sql;
        foreach (var (name, value) in parameters)
        {
            if (value is Guid[] guids)
            {
                command.Parameters.AddWithValue(
                    name, NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid, guids);
            }
            else
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
        }
        await command.ExecuteNonQueryAsync();
    }

    protected static async Task<JsonElement[]> ListResponseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        return body.GetProperty("transactions").EnumerateArray().ToArray();
    }
}
