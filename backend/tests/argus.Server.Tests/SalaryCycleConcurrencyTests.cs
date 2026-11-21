using System.Net;
using System.Text.Json;
using Npgsql;

namespace argus.Server.Tests;

public sealed class SalaryCycleConcurrencyTests(KitFactory factory) : TransactionTestBase(factory)
{
    private const string SalaryPath = "/v1/finance/salary-cycles";

    [Fact]
    public async Task ParallelSameDateConfirmationsCreateOneBoundaryAndOneRevision()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        Guid first = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 5, 25), "Receipt one", 10_000m, 1);
        Guid second = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 5, 25), "Receipt two", 10_000m, 2);

        Task<HttpResponseMessage> firstTask = SendAsync(ConfirmRequest(session, first, 0));
        Task<HttpResponseMessage> secondTask = SendAsync(ConfirmRequest(session, second, 0));
        HttpResponseMessage[] responses = await Task.WhenAll(firstTask, secondTask);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        JsonElement[] bodies = await Task.WhenAll(responses.Select(async response => await BodyAsync(response)));
        string[] statuses = bodies.Select(body => body.GetProperty("status").GetString()!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "already_confirmed", "created" }, statuses);
        Assert.All(bodies, body => Assert.Equal("1", body.GetProperty("revision").GetString()));
        Assert.Single(bodies.Select(body => body.GetProperty("id").GetGuid()).Distinct());

        JsonElement list = await ListAsync(session);
        Assert.Single(list.GetProperty("starts").EnumerateArray());
        Assert.Equal("1", list.GetProperty("revision").GetString());
    }

    [Fact]
    public async Task ConcurrentDifferentDatesSerializeAndRejectTheStaleSequence()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        Guid first = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 6, 1), "June receipt", 10_000m, 1);
        Guid second = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 6, 2), "Second receipt", 10_000m, 2);

        HttpResponseMessage[] responses = await Task.WhenAll(
            SendAsync(ConfirmRequest(session, first, 0)),
            SendAsync(ConfirmRequest(session, second, 0)));

        int created = responses.Count(response => response.StatusCode == HttpStatusCode.OK);
        HttpResponseMessage rejected = responses.Single(response => response.StatusCode != HttpStatusCode.OK);
        Assert.Equal(1, created);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("cycle_sequence_changed", await ErrorKindAsync(rejected));

        JsonElement list = await ListAsync(session);
        Assert.Single(list.GetProperty("starts").EnumerateArray());
        Assert.Equal("1", list.GetProperty("revision").GetString());
    }

    [Fact]
    public async Task ConfirmationReReadsSourceAfterAConcurrentIndividualHide()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        Guid transactionId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 7, 1), "Receipt", 10_000m, 1);

        await using NpgsqlConnection blocker = await Factory.CreateDb().OpenAsync();
        await using NpgsqlTransaction lockTransaction = await blocker.BeginTransactionAsync();
        await using (NpgsqlCommand lockCommand = blocker.CreateCommand())
        {
            lockCommand.Transaction = lockTransaction;
            lockCommand.CommandText = "SELECT id FROM transactions WHERE id = @id FOR UPDATE";
            lockCommand.Parameters.AddWithValue("id", transactionId);
            await lockCommand.ExecuteScalarAsync();
        }

        Task<HttpResponseMessage> confirmTask = SendAsync(ConfirmRequest(session, transactionId, 0));
        await Task.Delay(150);
        await using (NpgsqlCommand hideCommand = blocker.CreateCommand())
        {
            hideCommand.Transaction = lockTransaction;
            hideCommand.CommandText = "UPDATE transactions SET is_hidden_by_user = TRUE WHERE id = @id";
            hideCommand.Parameters.AddWithValue("id", transactionId);
            await hideCommand.ExecuteNonQueryAsync();
        }
        await lockTransaction.CommitAsync();

        HttpResponseMessage response = await confirmTask;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("receipt_ineligible", await ErrorKindAsync(response));

        JsonElement list = await ListAsync(session);
        Assert.Empty(list.GetProperty("starts").EnumerateArray());
        Assert.Equal("0", list.GetProperty("revision").GetString());
    }

    [Fact]
    public async Task ConfirmationSeesAConcurrentSourceDeletionAsNotFound()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        Guid transactionId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 8, 1), "Receipt", 10_000m, 1);

        await using NpgsqlConnection blocker = await Factory.CreateDb().OpenAsync();
        await using NpgsqlTransaction lockTransaction = await blocker.BeginTransactionAsync();
        await using (NpgsqlCommand lockCommand = blocker.CreateCommand())
        {
            lockCommand.Transaction = lockTransaction;
            lockCommand.CommandText = "SELECT id FROM transactions WHERE id = @id FOR UPDATE";
            lockCommand.Parameters.AddWithValue("id", transactionId);
            await lockCommand.ExecuteScalarAsync();
        }

        Task<HttpResponseMessage> confirmTask = SendAsync(ConfirmRequest(session, transactionId, 0));
        await Task.Delay(150);
        await using (NpgsqlCommand deleteCommand = blocker.CreateCommand())
        {
            deleteCommand.Transaction = lockTransaction;
            deleteCommand.CommandText = """
                DELETE FROM transaction_merchant_tags WHERE transaction_id = @id;
                DELETE FROM transactions WHERE id = @id;
                DELETE FROM statement_imports WHERE id = @statement;
                """;
            deleteCommand.Parameters.AddWithValue("id", transactionId);
            deleteCommand.Parameters.AddWithValue("statement", statementId);
            await deleteCommand.ExecuteNonQueryAsync();
        }
        await lockTransaction.CommitAsync();

        HttpResponseMessage response = await confirmTask;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await ErrorKindAsync(response));

        JsonElement list = await ListAsync(session);
        Assert.Empty(list.GetProperty("starts").EnumerateArray());
    }

    [Fact]
    public async Task RemoveThenReAddHasANewIdentityAndAnOldRemovalCannotDeleteIt()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        Guid transactionId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 9, 1), "Receipt", 10_000m, 1);

        JsonElement created = await ConfirmAsync(session, transactionId, 0);
        Guid originalId = created.GetProperty("id").GetGuid();
        JsonElement removed = await DeleteAsync(session, originalId, 1);
        Assert.Equal("2", removed.GetProperty("revision").GetString());

        JsonElement readded = await ConfirmAsync(session, transactionId, 2);
        Guid newId = readded.GetProperty("id").GetGuid();
        Assert.NotEqual(originalId, newId);
        Assert.Equal("3", readded.GetProperty("revision").GetString());

        HttpResponseMessage staleRevisionRemoval = await SendAsync(DeleteJson(
            SalaryPath + "/" + originalId,
            Json(new { expected_revision = "1", confirm = true }),
            session: session));
        Assert.Equal(HttpStatusCode.Conflict, staleRevisionRemoval.StatusCode);
        Assert.Equal("cycle_sequence_changed", await ErrorKindAsync(staleRevisionRemoval));

        HttpResponseMessage staleRemoval = await SendAsync(DeleteJson(
            SalaryPath + "/" + originalId,
            Json(new { expected_revision = "3", confirm = true }),
            session: session));
        Assert.Equal(HttpStatusCode.NotFound, staleRemoval.StatusCode);

        JsonElement list = await ListAsync(session);
        Assert.Equal(newId, list.GetProperty("starts").EnumerateArray().Single().GetProperty("id").GetGuid());
        Assert.Equal("3", list.GetProperty("revision").GetString());
    }

    [Fact]
    public async Task AnUncertainSuccessIsResolvedByReadingTheSequence()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var (_, expenseId) = await DefaultCategoriesAsync(session);
        var statementId = await SeedStatementAsync(customerId, accountId);
        Guid transactionId = await SeedTransactionAsync(customerId, accountId, statementId, expenseId, new DateOnly(2025, 10, 1), "Receipt", 10_000m, 1);

        JsonElement created = await ConfirmAsync(session, transactionId, 0);
        Assert.Equal("created", created.GetProperty("status").GetString());

        // The caller lost the response. It must read the sequence rather than
        // blindly repeating a removal or inventing a success.
        JsonElement observed = await ListAsync(session);
        Assert.Equal("1", observed.GetProperty("revision").GetString());
        Assert.Equal("2025-10-01", observed.GetProperty("starts").EnumerateArray().Single().GetProperty("start_date").GetString());

        JsonElement repeated = await ConfirmAsync(session, transactionId, 0);
        Assert.Equal("already_confirmed", repeated.GetProperty("status").GetString());
        Assert.Equal(created.GetProperty("id").GetGuid(), repeated.GetProperty("id").GetGuid());
        Assert.Equal("1", repeated.GetProperty("revision").GetString());
    }

    private HttpRequestMessage ConfirmRequest(string session, Guid transactionId, long revision) =>
        Post(
            SalaryPath,
            Json(new
            {
                transaction_id = transactionId,
                expected_transaction_version = 1,
                expected_revision = revision.ToString(),
                confirm = true,
            }),
            session: session);

    private async Task<JsonElement> ConfirmAsync(string session, Guid transactionId, long revision)
    {
        HttpResponseMessage response = await SendAsync(ConfirmRequest(session, transactionId, revision));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private async Task<JsonElement> DeleteAsync(string session, Guid id, long revision)
    {
        HttpResponseMessage response = await SendAsync(DeleteJson(
            SalaryPath + "/" + id,
            Json(new { expected_revision = revision.ToString(), confirm = true }),
            session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private async Task<JsonElement> ListAsync(string session)
    {
        HttpResponseMessage response = await SendAsync(Get(SalaryPath, session: session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }
}
