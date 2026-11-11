using System.Net;
using argus.Server.Data;
using argus.Server.Imports;
using Microsoft.Extensions.DependencyInjection;

namespace argus.Server.Tests;

/// <summary>
/// Phase 4 cleanup evidence. Expiring a prepared review must retain only its
/// terminal header, delete every payload row, and prune terminal headers after
/// the thirty-day retention window without touching a live review.
/// </summary>
public sealed class StatementImportCleanupTests(KitFactory factory) : ApiTestBase(factory)
{
    private async Task<(Guid CustomerId, Guid AccountId)> NewCustomerAndAccountAsync()
    {
        var session = await RegisterVerifyLoginAsync(UniqueEmail());
        var me = await SendAsync(Get("/v1/me", session: session));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Guid customerId = (await BodyAsync(me)).GetProperty("id").GetGuid();
        var account = await SendAsync(Post("/v1/finance/accounts", Json(new { display_name = "Cheque" }), session: session));
        Assert.Equal(HttpStatusCode.Created, account.StatusCode);

        return (customerId, (await BodyAsync(account)).GetProperty("id").GetGuid());
    }

    private static ParsedStatement Statement(string description) => new(
        StatementFormat.GenericCsv,
        [Row(description)],
        new DateOnly(2025, 1, 1),
        new DateOnly(2025, 1, 31),
        new Dictionary<string, string>(),
        [],
        []);

    private static ParsedStatementRow Row(string description) => new(
        Guid.NewGuid(),
        7,
        null,
        1,
        new Dictionary<string, string?>
        {
            ["date"] = "2025-01-02",
            ["description"] = description,
            ["amount"] = "-10.00",
        },
        new DateOnly(2025, 1, 2),
        description,
        description.ToLowerInvariant(),
        -1000,
        [],
        [],
        null,
        null);

    [Fact]
    public async Task ExpiredReviewRetainsTerminalHeaderAndOldTerminalHeadersArePruned()
    {
        var (customerId, accountId) = await NewCustomerAndAccountAsync();
        var store = Factory.Services.GetRequiredService<StatementImportStore>();
        var config = new ParserConfig { Format = "generic" };

        var expired = await store.CreateReviewAsync(customerId, accountId, StatementSourceKind.GenericCsv, config, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), Statement("Expired coffee"));
        var live = await store.CreateReviewAsync(customerId, accountId, StatementSourceKind.GenericCsv, config, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), Statement("Live coffee"));
        var oldAbandoned = await store.CreateReviewAsync(customerId, accountId, StatementSourceKind.GenericCsv, config, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), Statement("Old abandoned coffee"));
        var recentAbandoned = await store.CreateReviewAsync(customerId, accountId, StatementSourceKind.GenericCsv, config, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), Statement("Recent abandoned coffee"));

        await ExecuteSqlAsync($"UPDATE import_reviews SET expires_at = NOW() - INTERVAL '1 hour' WHERE id = '{expired.Summary.Id}'");
        await ExecuteSqlAsync($"UPDATE import_reviews SET status = 'Abandoned', updated_at = NOW() - INTERVAL '40 days' WHERE id = '{oldAbandoned.Summary.Id}'");
        await ExecuteSqlAsync($"UPDATE import_reviews SET status = 'Abandoned' WHERE id = '{recentAbandoned.Summary.Id}'");

        await store.CleanupExpiredAsync();

        Assert.Equal(1, await CountWhereAsync("import_reviews", $"id = '{expired.Summary.Id}' AND status = 'Expired'"));
        Assert.Equal(1, await CountWhereAsync(
            "import_reviews",
            $"id = '{expired.Summary.Id}' AND payload_bytes = 0 AND total_rows = 0 AND possible_duplicate_count = 0 AND version = 2 AND parser_config = '{{}}'::jsonb"));
        Assert.Equal(0, await CountWhereAsync("import_review_rows", $"review_id = '{expired.Summary.Id}'"));

        Assert.Equal(1, await CountWhereAsync("import_reviews", $"id = '{live.Summary.Id}' AND status = 'Prepared' AND total_rows = 1 AND version = 1"));
        Assert.True(await ScalarAsync($"SELECT payload_bytes FROM import_reviews WHERE id = '{live.Summary.Id}'") > 0);
        Assert.Equal(1, await CountWhereAsync("import_review_rows", $"review_id = '{live.Summary.Id}'"));

        Assert.Equal(0, await CountWhereAsync("import_reviews", $"id = '{oldAbandoned.Summary.Id}'"));
        Assert.Equal(1, await CountWhereAsync("import_reviews", $"id = '{recentAbandoned.Summary.Id}' AND status = 'Abandoned'"));
    }
}
