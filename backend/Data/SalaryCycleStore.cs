using System.Data;
using System.Globalization;
using argus.Server.Data.Entities;
using argus.Server.Http;
using argus.Server.Reporting;
using argus.Server.Transactions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace argus.Server.Data;

/// <summary>
/// Owned salary-cycle reporting dates. It stores only a confirmed calendar
/// date and its boundary identity; no source transaction payload, amount,
/// description, account label or source foreign key is retained. Every read
/// and write is scoped by the authenticated customer and revalidated before
/// commit.
/// </summary>
public sealed class SalaryCycleStore(IDbContextFactory<ArgusDbContext> contextFactory, TimeProvider timeProvider)
{
    private readonly IDbContextFactory<ArgusDbContext> _contextFactory = contextFactory;
    private readonly TimeProvider _timeProvider = timeProvider;

    private const string LockAccountSql = """
        SELECT * FROM financial_accounts
        WHERE customer_id = {0} AND id = {1}
        FOR SHARE
        """;

    private const string LockTransactionSql = """
        SELECT * FROM transactions
        WHERE customer_id = {0} AND id = {1}
        FOR UPDATE
        """;

    public async Task<SalaryCycleListResponse> ListAsync(FinanceAuthority authority,
                                                          DateOnly? beforeDate,
                                                          CancellationToken cancellationToken = default)
    {
        await using var scope = await CustomerDbScope.OpenAsync(
            _contextFactory, authority, IsolationLevel.RepeatableRead, exclusiveCustomerLock: false, workspaceTimeouts: true, cancellationToken);
        var context = scope.Context;

        long revision = await ReadRevisionAsync(context, authority.CustomerId, cancellationToken);
        List<SalaryCycleStart> starts = await context.SalaryCycleStarts
            .AsNoTracking()
            .Where(start => start.CustomerId == authority.CustomerId &&
                            (beforeDate == null || start.StartDate < beforeDate.Value))
            .OrderByDescending(start => start.StartDate)
            .ThenByDescending(start => start.Id)
            .Take(ReportingLimits.MaxSalaryStartPageSize + 1)
            .ToListAsync(cancellationToken);

        string? nextBeforeDate = null;
        if (starts.Count > ReportingLimits.MaxSalaryStartPageSize)
        {
            starts.RemoveRange(ReportingLimits.MaxSalaryStartPageSize, starts.Count - ReportingLimits.MaxSalaryStartPageSize);
            // Exclusive cursor: the next page contains every older date.
            nextBeforeDate = starts[^1].StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        await scope.CommitAsync(cancellationToken);

        return new SalaryCycleListResponse(
            revision.ToString(CultureInfo.InvariantCulture),
            starts.Select(start => new SalaryCycleBoundaryResponse(
                start.Id,
                FormatDate(start.StartDate),
                Timestamps.Format(start.CreatedAt))).ToArray(),
            nextBeforeDate);
    }

    public async Task<SalaryCyclePreviewResponse> PreviewAsync(FinanceAuthority authority,
                                                               SalaryCyclePreviewRequest request,
                                                               CancellationToken cancellationToken = default)
    {
        await using var scope = await CustomerDbScope.OpenAsync(
            _contextFactory, authority, IsolationLevel.RepeatableRead, exclusiveCustomerLock: false, workspaceTimeouts: true, cancellationToken);
        var context = scope.Context;
        Guid customerId = authority.CustomerId;
        DateOnly today = ReportingClock.Today(_timeProvider);

        long revision = await ReadRevisionAsync(context, customerId, cancellationToken);
        List<SalaryCycleStart> starts = await LoadStartsAsync(context, customerId, cancellationToken);

        SalaryCyclePreviewResponse response;
        if (string.Equals(request.Action, "confirm", StringComparison.Ordinal))
        {
            if (request.TransactionId is null || request.TransactionId == Guid.Empty ||
                request.ExpectedTransactionVersion is null || request.ExpectedTransactionVersion < 1 ||
                request.StartId is not null)
            {
                throw new SalaryCycleStoreException(ReportErrorCodes.InvalidRequest);
            }

            SourceRow source = await LoadSourceAsync(context, customerId, request.TransactionId.Value, cancellationToken)
                ?? throw new SalaryCycleStoreException(ReportErrorCodes.NotFound);
            if (source.Version != request.ExpectedTransactionVersion.Value)
            {
                throw new SalaryCycleStoreException(ReportErrorCodes.VersionConflict);
            }
            if (!IsEligible(source, today))
            {
                throw new SalaryCycleStoreException(ReportErrorCodes.ReceiptIneligible);
            }

            response = BuildConfirmPreview(revision, starts, source, today);
        }
        else if (string.Equals(request.Action, "remove", StringComparison.Ordinal))
        {
            if (request.StartId is null || request.StartId == Guid.Empty ||
                request.TransactionId is not null || request.ExpectedTransactionVersion is not null)
            {
                throw new SalaryCycleStoreException(ReportErrorCodes.InvalidRequest);
            }

            SalaryCycleStart start = starts.SingleOrDefault(item => item.Id == request.StartId.Value)
                ?? throw new SalaryCycleStoreException(ReportErrorCodes.NotFound);
            response = BuildRemovePreview(revision, starts, start, today);
        }
        else
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.InvalidRequest);
        }

        await scope.CommitAsync(cancellationToken);

        return response;
    }

    public async Task<SalaryCycleConfirmResponse> ConfirmAsync(FinanceAuthority authority,
                                                               SalaryCycleConfirmRequest request,
                                                               CancellationToken cancellationToken = default)
    {
        if (request.TransactionId is null || request.TransactionId == Guid.Empty ||
            request.ExpectedTransactionVersion is null || request.ExpectedTransactionVersion < 1 ||
            !TryParseRevision(request.ExpectedRevision, out long expectedRevision) ||
            request.Confirm != true)
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.InvalidRequest);
        }

        DateOnly today = ReportingClock.Today(_timeProvider);

        try
        {
            return await ConfirmOnceAsync(authority, request, expectedRevision, today, cancellationToken);
        }
        catch (Exception exception) when (
            PersistenceErrors.FindPostgresException(exception) is { } postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
            string.Equals(postgres.ConstraintName, "salary_cycle_starts_customer_date_key", StringComparison.Ordinal))
        {
            // A parallel confirmation of another eligible receipt on the same
            // date won. The loser wrote nothing; report the retained date as
            // already confirmed after validating its own source.
            SalaryCycleConfirmResponse? raced = await ReadAlreadyConfirmedAsync(authority, request, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw new SalaryCycleStoreException(ReportErrorCodes.ReportBusy);
        }
    }

    private async Task<SalaryCycleConfirmResponse> ConfirmOnceAsync(FinanceAuthority authority,
                                                                    SalaryCycleConfirmRequest request,
                                                                    long expectedRevision,
                                                                    DateOnly today,
                                                                    CancellationToken cancellationToken)
    {
        Guid customerId = authority.CustomerId;
        await using var scope = await CustomerDbScope.OpenAsync(
            _contextFactory, authority, IsolationLevel.ReadCommitted, exclusiveCustomerLock: true, workspaceTimeouts: true, cancellationToken);
        var context = scope.Context;

        Guid transactionId = request.TransactionId!.Value;
        Guid? accountId = await context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.CustomerId == customerId && transaction.Id == transactionId)
            .Select(transaction => (Guid?)transaction.FinancialAccountId)
            .SingleOrDefaultAsync(cancellationToken);
        if (accountId is null)
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.NotFound);
        }

        List<FinancialAccount> accounts = await context.FinancialAccounts
            .FromSqlRaw(LockAccountSql, customerId, accountId.Value)
            .AsTracking()
            .ToListAsync(cancellationToken);
        if (accounts.Count != 1)
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.NotFound);
        }

        List<Transaction> locked = await context.Transactions
            .FromSqlRaw(LockTransactionSql, customerId, transactionId)
            .AsTracking()
            .ToListAsync(cancellationToken);
        if (locked.Count != 1)
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.NotFound);
        }

        // Re-read under the locks that serialize statement deletion and
        // individual hide/restore with this eligibility decision.
        SourceRow source = await LoadSourceAsync(context, customerId, transactionId, cancellationToken)
            ?? throw new SalaryCycleStoreException(ReportErrorCodes.NotFound);
        if (source.Version != request.ExpectedTransactionVersion!.Value)
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.VersionConflict);
        }
        if (!IsEligible(source, today))
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.ReceiptIneligible);
        }

        SalaryCycleState? state = await context.SalaryCycleStates
            .FromSqlRaw("SELECT * FROM salary_cycle_state WHERE customer_id = {0} FOR UPDATE", customerId)
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
        long revision = state?.Revision ?? 0;

        SalaryCycleStart? existing = await context.SalaryCycleStarts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                start => start.CustomerId == customerId && start.StartDate == source.TransactionDate,
                cancellationToken);
        if (existing is not null)
        {
            // Same-date receipts share one boundary. This no-op is reported as
            // already confirmed even when the caller's expected revision is
            // stale; the source was still fully validated above.
            await scope.CommitAsync(cancellationToken);

            return new SalaryCycleConfirmResponse(
                existing.Id,
                FormatDate(existing.StartDate),
                revision.ToString(CultureInfo.InvariantCulture),
                "already_confirmed");
        }

        if (revision != expectedRevision)
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.CycleSequenceChanged);
        }

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        var start = new SalaryCycleStart
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            StartDate = source.TransactionDate,
            CreatedAt = now,
        };
        context.SalaryCycleStarts.Add(start);

        if (state is null)
        {
            state = new SalaryCycleState { CustomerId = customerId, Revision = 1, UpdatedAt = now };
            context.SalaryCycleStates.Add(state);
        }
        else
        {
            state.Revision += 1;
            state.UpdatedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return new SalaryCycleConfirmResponse(
            start.Id,
            FormatDate(start.StartDate),
            state.Revision.ToString(CultureInfo.InvariantCulture),
            "created");
    }

    private async Task<SalaryCycleConfirmResponse?> ReadAlreadyConfirmedAsync(FinanceAuthority authority,
                                                                              SalaryCycleConfirmRequest request,
                                                                              CancellationToken cancellationToken)
    {
        await using var scope = await CustomerDbScope.OpenAsync(
            _contextFactory, authority, IsolationLevel.RepeatableRead, exclusiveCustomerLock: false, workspaceTimeouts: true, cancellationToken);
        var context = scope.Context;
        Guid customerId = authority.CustomerId;
        DateOnly today = ReportingClock.Today(_timeProvider);

        SourceRow source = await LoadSourceAsync(context, customerId, request.TransactionId!.Value, cancellationToken)
            ?? throw new SalaryCycleStoreException(ReportErrorCodes.NotFound);
        if (source.Version != request.ExpectedTransactionVersion!.Value)
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.VersionConflict);
        }
        if (!IsEligible(source, today))
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.ReceiptIneligible);
        }

        SalaryCycleStart? existing = await context.SalaryCycleStarts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                start => start.CustomerId == customerId && start.StartDate == source.TransactionDate,
                cancellationToken);
        if (existing is null)
        {
            return null;
        }

        long revision = await ReadRevisionAsync(context, customerId, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return new SalaryCycleConfirmResponse(
            existing.Id,
            FormatDate(existing.StartDate),
            revision.ToString(CultureInfo.InvariantCulture),
            "already_confirmed");
    }

    public async Task<SalaryCycleRemoveResponse> RemoveAsync(FinanceAuthority authority,
                                                             Guid startId,
                                                             SalaryCycleRemoveRequest request,
                                                             CancellationToken cancellationToken = default)
    {
        if (startId == Guid.Empty || !TryParseRevision(request.ExpectedRevision, out long expectedRevision) || request.Confirm != true)
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.InvalidRequest);
        }

        Guid customerId = authority.CustomerId;
        await using var scope = await CustomerDbScope.OpenAsync(
            _contextFactory, authority, IsolationLevel.ReadCommitted, exclusiveCustomerLock: true, workspaceTimeouts: true, cancellationToken);
        var context = scope.Context;

        SalaryCycleState? state = await context.SalaryCycleStates
            .FromSqlRaw("SELECT * FROM salary_cycle_state WHERE customer_id = {0} FOR UPDATE", customerId)
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
        long revision = state?.Revision ?? 0;
        if (revision != expectedRevision)
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.CycleSequenceChanged);
        }

        SalaryCycleStart? start = await context.SalaryCycleStarts
            .SingleOrDefaultAsync(item => item.CustomerId == customerId && item.Id == startId, cancellationToken);
        if (start is null)
        {
            throw new SalaryCycleStoreException(ReportErrorCodes.NotFound);
        }

        context.SalaryCycleStarts.Remove(start);
        if (state is not null)
        {
            state.Revision += 1;
            state.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        }

        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);

        long newRevision = state?.Revision ?? revision + 1;

        return new SalaryCycleRemoveResponse(start.Id, FormatDate(start.StartDate), newRevision.ToString(CultureInfo.InvariantCulture));
    }

    private static SalaryCyclePreviewResponse BuildConfirmPreview(long revision,
                                                                  List<SalaryCycleStart> starts,
                                                                  SourceRow source,
                                                                  DateOnly today)
    {
        SalaryCycleStart? existing = starts.SingleOrDefault(start => start.StartDate == source.TransactionDate);
        if (existing is not null)
        {
            return new SalaryCyclePreviewResponse(
                "confirm",
                FormatRevision(revision),
                FormatDate(source.TransactionDate),
                ToSource(source),
                null,
                [],
                [
                    "This date is already confirmed. Selecting another eligible receipt on the same date creates no boundary, replaces no source and changes no revision or cycle.",
                    "The confirmed date remains reporting metadata if its source is later hidden or deleted.",
                ],
                RetentionDisclosure: true,
                AlreadyConfirmed: true);
        }

        DateOnly end = TodayEnd(starts, source.TransactionDate, today);
        SalaryCycleStart? predecessor = starts.LastOrDefault(start => start.StartDate < source.TransactionDate);
        SalaryCycleStart? successor = starts.FirstOrDefault(start => start.StartDate > source.TransactionDate);

        var affected = new List<SalaryCycleIntervalResponse>
        {
            new(FormatDate(source.TransactionDate), FormatDate(end), "New cycle"),
        };
        var warnings = new List<string>
        {
            "The confirmed date remains reporting metadata if its source is later hidden or deleted.",
            "Adding a date changes the selected cycle and every later comparison; this is a reporting change, not a source edit.",
        };

        if (predecessor is not null)
        {
            if (!ReportingPeriods.TryAddDays(source.TransactionDate, -1, out DateOnly predecessorEnd) || predecessorEnd < predecessor.StartDate)
            {
                throw new SalaryCycleStoreException(ReportErrorCodes.InvalidRequest);
            }

            affected.Add(new SalaryCycleIntervalResponse(
                FormatDate(predecessor.StartDate),
                FormatDate(predecessorEnd),
                "Adjusted preceding cycle"));
            warnings.Add("This date splits the cycle that starts on the preceding confirmed date.");
        }
        else
        {
            warnings.Add("This is the earliest confirmed date. Transactions before it are not available in salary-cycle mode; Calendar and Custom remain available.");
        }

        if (successor is not null)
        {
            warnings.Add("The following cycle's comparison to its previous cycle changes because its previous boundary is now this date.");
        }

        return new SalaryCyclePreviewResponse(
            "confirm",
            FormatRevision(revision),
            FormatDate(source.TransactionDate),
            ToSource(source),
            new SalaryCycleIntervalResponse(FormatDate(source.TransactionDate), FormatDate(end), "New cycle"),
            affected.ToArray(),
            warnings.ToArray(),
            RetentionDisclosure: true,
            AlreadyConfirmed: false);
    }

    private static SalaryCyclePreviewResponse BuildRemovePreview(long revision,
                                                                 List<SalaryCycleStart> starts,
                                                                 SalaryCycleStart start,
                                                                 DateOnly today)
    {
        DateOnly end = TodayEnd(starts, start.StartDate, today);
        SalaryCycleStart? predecessor = starts.LastOrDefault(item => item.StartDate < start.StartDate);

        var affected = new List<SalaryCycleIntervalResponse>
        {
            new(FormatDate(start.StartDate), FormatDate(end), "Cycle being removed"),
        };
        var warnings = new List<string>
        {
            "Removing this date changes the affected cycle and every later comparison; the source transaction is unchanged.",
            "The source transaction and its category and tags are not changed by this correction.",
        };

        if (predecessor is not null)
        {
            affected.Add(new SalaryCycleIntervalResponse(
                FormatDate(predecessor.StartDate),
                FormatDate(end),
                "Adjusted preceding cycle"));
            warnings.Add("The preceding cycle extends to the end of the removed cycle.");
        }
        else
        {
            warnings.Add("Removing the earliest confirmed date makes earlier transactions unavailable in salary-cycle mode. Calendar and Custom remain available.");
            warnings.Add("With every date removed, Overview returns to salary-cycle setup.");
        }

        return new SalaryCyclePreviewResponse(
            "remove",
            FormatRevision(revision),
            FormatDate(start.StartDate),
            null,
            null,
            affected.ToArray(),
            warnings.ToArray(),
            RetentionDisclosure: true,
            AlreadyConfirmed: false);
    }

    private static DateOnly TodayEnd(List<SalaryCycleStart> starts, DateOnly startDate, DateOnly today)
    {
        SalaryCycleStart? successor = starts
            .Where(start => start.StartDate > startDate)
            .OrderBy(start => start.StartDate)
            .FirstOrDefault();
        if (successor is null)
        {
            return today;
        }

        return ReportingPeriods.TryAddDays(successor.StartDate, -1, out DateOnly end) ? end : today;
    }

    private static bool IsEligible(SourceRow source, DateOnly today) =>
        source.Amount > 0m &&
        source.TransactionDate <= today &&
        !source.StatementHidden &&
        !source.HiddenByUser;

    private static async Task<long> ReadRevisionAsync(ArgusDbContext context,
                                                      Guid customerId,
                                                      CancellationToken cancellationToken) =>
        await context.SalaryCycleStates
            .AsNoTracking()
            .Where(state => state.CustomerId == customerId)
            .Select(state => state.Revision)
            .SingleOrDefaultAsync(cancellationToken);

    private static async Task<List<SalaryCycleStart>> LoadStartsAsync(ArgusDbContext context,
                                                                       Guid customerId,
                                                                       CancellationToken cancellationToken) =>
        await context.SalaryCycleStarts
            .AsNoTracking()
            .Where(start => start.CustomerId == customerId)
            .OrderBy(start => start.StartDate)
            .ThenBy(start => start.Id)
            .ToListAsync(cancellationToken);

    private static async Task<SourceRow?> LoadSourceAsync(ArgusDbContext context,
                                                          Guid customerId,
                                                          Guid transactionId,
                                                          CancellationToken cancellationToken) =>
        await (
            from transaction in context.Transactions.AsNoTracking()
            join account in context.FinancialAccounts
                on new { transaction.CustomerId, Id = transaction.FinancialAccountId } equals new { account.CustomerId, Id = account.Id }
            join category in context.Categories
                on new { transaction.CustomerId, Id = transaction.CategoryId } equals new { category.CustomerId, Id = category.Id }
            join statement in context.StatementImports
                on new { transaction.CustomerId, Id = transaction.StatementImportId } equals new { statement.CustomerId, Id = statement.Id }
            where transaction.CustomerId == customerId && transaction.Id == transactionId
            select new SourceRow(
                transaction.Id,
                transaction.Version,
                transaction.TransactionDate,
                transaction.Description,
                transaction.Amount,
                account.Id,
                account.DisplayName,
                account.Institution,
                account.IsArchived,
                category.Id,
                category.DisplayName,
                category.Kind,
                category.IsSystem,
                category.IsArchived,
                transaction.CategoryConfirmed,
                statement.IsHidden,
                transaction.IsHiddenByUser))
            .SingleOrDefaultAsync(cancellationToken);

    private static SalaryCycleSourceResponse ToSource(SourceRow source) =>
        new(
            source.Id,
            source.Version,
            FormatDate(source.TransactionDate),
            source.Description,
            TransactionRequestParser.FormatAmount(source.Amount),
            "ZAR",
            new TransactionAccountRef(source.AccountId, source.AccountName, source.AccountInstitution, source.AccountArchived),
            new TransactionCategoryRef(
                source.CategoryId,
                source.CategoryName,
                source.CategoryKind,
                source.CategorySystem,
                source.CategoryArchived),
            source.CategoryConfirmed);

    private static string FormatDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatRevision(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool TryParseRevision(string? value, out long revision)
    {
        revision = 0;

        return value is not null &&
               value.Length is > 0 and <= 19 &&
               value.All(character => character is >= '0' and <= '9') &&
               long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out revision) &&
               revision >= 0;
    }

    private sealed record SourceRow(Guid Id,
                                    int Version,
                                    DateOnly TransactionDate,
                                    string Description,
                                    decimal Amount,
                                    Guid AccountId,
                                    string AccountName,
                                    string? AccountInstitution,
                                    bool AccountArchived,
                                    Guid CategoryId,
                                    string CategoryName,
                                    string CategoryKind,
                                    bool CategorySystem,
                                    bool CategoryArchived,
                                    bool CategoryConfirmed,
                                    bool StatementHidden,
                                    bool HiddenByUser);
}

public sealed class SalaryCycleStoreException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
