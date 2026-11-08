using System.Net;
using argus.Server.Data;
using argus.Server.Imports;
using Microsoft.Extensions.DependencyInjection;

namespace argus.Server.Tests;

public sealed class StatementImportMatchingTests(KitFactory factory) : ApiTestBase(factory)
{
    private async Task<(string Session, Guid CustomerId)> NewCustomerAsync()
    {
        var session = await RegisterVerifyLoginAsync(UniqueEmail());
        var me = await SendAsync(Get("/v1/me", session: session));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        return (session, (await BodyAsync(me)).GetProperty("id").GetGuid());
    }

    private async Task<Guid> CreateAccountAsync(string session)
    {
        var response = await SendAsync(Post(
            "/v1/finance/accounts", Json(new { display_name = "Cheque" }), session: session));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    private static ParsedStatementRow Row(
        int index,
        string? sourceNamespace,
        string? sourceId)
    {
        var date = new DateOnly(2025, 1, 2);
        const string description = "Coffee";
        const long cents = -1000;
        var rowId = Guid.NewGuid();
        return new ParsedStatementRow(
            rowId,
            index + 7,
            null,
            index + 1,
            new Dictionary<string, string?>
            {
                ["date"] = "2025-01-02",
                ["description"] = description,
                ["amount"] = "-10.00",
                ["source_id"] = sourceId,
            },
            date,
            description,
            "coffee",
            cents,
            [],
            [],
            sourceNamespace,
            sourceId);
    }

    private static ParsedStatement Statement(params ParsedStatementRow[] rows) => new(
        StatementFormat.GenericCsv,
        rows,
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 1, 31),
        new Dictionary<string, string>(),
        [],
        []);

    [Fact]
    public async Task MixedStableAndAnonymousOccurrencesUseMaximumCompatiblePairing()
    {
        var (session, customerId) = await NewCustomerAsync();
        var accountId = await CreateAccountAsync(session);
        var store = Factory.Services.GetRequiredService<StatementImportStore>();
        var config = new ParserConfig { Format = "generic" };

        var firstRows = new[]
        {
            Row(0, "A", "x"),
            Row(1, null, null),
        };
        var first = await store.CreateReviewAsync(
            customerId,
            accountId,
            StatementSourceKind.GenericCsv,
            config,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            Statement(firstRows));
        var identityKey = StatementIdentity.ComputeIdentityKey(
            new DateOnly(2025, 1, 2), -1000, "coffee");
        var patched = await store.PatchReviewAsync(
            customerId,
            first.Summary.Id,
            first.Summary.Version,
            [new ImportDecision(firstRows[0].RowId, null, null, null, identityKey, "keep_all", null)],
            null);
        var committed = await store.CommitReviewAsync(
            customerId, first.Summary.Id, patched.Version, patched.SummaryHash);
        Assert.Equal(2, committed.Summary.ImportedCount);

        var second = await store.CreateReviewAsync(
            customerId,
            accountId,
            StatementSourceKind.GenericCsv,
            config,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            Statement(Row(0, "B", "p"), Row(1, "A", "y")));

        Assert.Equal(0, second.Summary.ImportedCount);
        Assert.Equal(2, second.Summary.SuppressedCount);
        Assert.All(second.Page.Rows, row =>
            Assert.True(row.DuplicateState is "suppressed" or "suppressed_within_file"));
        var matchedIds = second.Page.Rows
            .SelectMany(row => row.MatchingTransactionIds)
            .Distinct()
            .ToArray();
        Assert.Equal(2, matchedIds.Length);
        Assert.All(second.Page.Rows, row =>
            Assert.Contains(committed.Summary.Id, row.MatchingImportIds));
    }
}
