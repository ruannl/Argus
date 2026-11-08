using argus.Server.Imports;

namespace argus.Server.Tests;

public sealed class ParserAdmissionTests
{
    private static TaskCompletionSource<bool> Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task AssertBusyAsync(ParserAdmission admission)
    {
        var exception = await Assert.ThrowsAsync<StatementRejectedException>(() => admission.RunAsync(_ => Task.FromResult(0), CancellationToken.None));
        Assert.Equal(ParserReasons.ImportBusy, exception.Reason);
    }

    private static async Task AssertAdmittedAsync(ParserAdmission admission, int expected)
    {
        await admission.WaitForCapacityAsync().WaitAsync(TimeSpan.FromSeconds(2));
        var result = await admission.RunAsync(_ => Task.FromResult(expected), CancellationToken.None);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task RejectsExcessParserWithoutWaiting()
    {
        using ParserAdmission admission = new ParserAdmission(capacity: 1, deadline: TimeSpan.FromSeconds(5));
        var entered = Signal();
        var release = Signal();
        var blocker = admission.RunAsync(
            async token =>
            {
                entered.SetResult(true);
                await release.Task.WaitAsync(token);
                return 1;
            },
            CancellationToken.None);
        await entered.Task;

        await AssertBusyAsync(admission);

        release.SetResult(true);
        Assert.Equal(1, await blocker);
    }

    [Fact]
    public async Task SuccessReleasesSlot()
    {
        using ParserAdmission admission = new ParserAdmission(capacity: 1);

        Assert.Equal(1, await admission.RunAsync(_ => Task.FromResult(1), CancellationToken.None));
        await AssertAdmittedAsync(admission, 2);
    }

    [Fact]
    public async Task ActionExceptionReleasesSlot()
    {
        using ParserAdmission admission = new ParserAdmission(capacity: 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => admission.RunAsync<int>(_ => throw new InvalidOperationException("parser boom"), CancellationToken.None));
        Assert.Equal("parser boom", exception.Message);
        await AssertAdmittedAsync(admission, 2);
    }

    [Fact]
    public async Task DeadlineKeepsSlotUntilCancelledWorkerStops()
    {
        using ParserAdmission admission = new ParserAdmission(capacity: 1, deadline: TimeSpan.FromMilliseconds(50));
        var started = Signal();
        var cleanup = Signal();
        var workerFinished = Signal();
        var blocked = admission.RunAsync(
            async token =>
            {
                started.SetResult(true);
                try
                {
                    await cleanup.Task.WaitAsync(token);
                }
                catch (OperationCanceledException)
                {
                    // Deliberately remain alive after cancellation until the
                    // cleanup gate opens, proving the slot is still owned.
                    await cleanup.Task;
                }
                finally
                {
                    workerFinished.SetResult(true);
                }
                return 1;
            },
            CancellationToken.None);
        await started.Task;

        var exception = await Assert.ThrowsAsync<StatementRejectedException>(() => blocked);
        Assert.Equal(ParserReasons.ProcessingLimit, exception.Reason);
        Assert.False(workerFinished.Task.IsCompleted);
        await AssertBusyAsync(admission);

        cleanup.SetResult(true);
        await workerFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await AssertAdmittedAsync(admission, 2);
    }

    [Fact]
    public async Task CallerCancellationKeepsSlotUntilCancelledWorkerStops()
    {
        using ParserAdmission admission = new ParserAdmission(capacity: 1, deadline: TimeSpan.FromSeconds(30));
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        var started = Signal();
        var cleanup = Signal();
        var workerFinished = Signal();
        var blocked = admission.RunAsync(
            async token =>
            {
                started.SetResult(true);
                try
                {
                    await cleanup.Task.WaitAsync(token);
                }
                catch (OperationCanceledException)
                {
                    await cleanup.Task;
                }
                finally
                {
                    workerFinished.SetResult(true);
                }
                return 1;
            },
            cancellation.Token);
        await started.Task;

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        Assert.False(workerFinished.Task.IsCompleted);
        await AssertBusyAsync(admission);

        cleanup.SetResult(true);
        await workerFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await AssertAdmittedAsync(admission, 2);
    }

    [Fact]
    public async Task LateFaultAfterDeadlineReleasesSlotWhenWorkerStops()
    {
        using ParserAdmission admission = new ParserAdmission(capacity: 1, deadline: TimeSpan.FromMilliseconds(50));
        var started = Signal();
        var cleanup = Signal();
        var workerFinished = Signal();
        var blocked = admission.RunAsync<int>(
            async token =>
            {
                started.SetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    await cleanup.Task;
                    throw new InvalidOperationException("late parser fault");
                }
                finally
                {
                    workerFinished.SetResult(true);
                }
                return 1;
            },
            CancellationToken.None);
        await started.Task;

        var exception = await Assert.ThrowsAsync<StatementRejectedException>(() => blocked);
        Assert.Equal(ParserReasons.ProcessingLimit, exception.Reason);
        await AssertBusyAsync(admission);

        cleanup.SetResult(true);
        await workerFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await AssertAdmittedAsync(admission, 2);
    }

    [Fact]
    public async Task CsvDispatchUsesTheSharedAdmission()
    {
        using ParserAdmission admission = new ParserAdmission(capacity: 1, deadline: TimeSpan.FromSeconds(5));
        var entered = Signal();
        var release = Signal();
        var blocker = admission.RunAsync(
            async token =>
            {
                entered.SetResult(true);
                await release.Task.WaitAsync(token);
                return 1;
            },
            CancellationToken.None);
        await entered.Task;

        using PdfParserRunner runner = new PdfParserRunner();
        var file = StatementFixtureBuilder.BuildFnbCsv(2, chequeAccount: false).Bytes;
        var exception = await Assert.ThrowsAsync<StatementRejectedException>(() => StatementParser.InspectAsync(file, StatementSourceKind.Fnb, null, null, null, runner, admission));
        Assert.Equal(ParserReasons.ImportBusy, exception.Reason);

        release.SetResult(true);
        await blocker;
    }
}
