namespace argus.Server.Imports;

/// <summary>
/// Process-wide parser admission and deadline. CSV and PDF parsers share the
/// same slots so the service-wide concurrent-parser bound is truthful. Excess
/// work is rejected immediately; callers never wait in an unbounded queue.
/// A slot is returned only when the underlying action task is terminal, not
/// when a caller stops waiting because of a deadline or its own cancellation.
/// </summary>
public sealed class ParserAdmission : IDisposable
{
    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _deadline;

    public ParserAdmission(int capacity = ImportLimits.MaxConcurrentParsers, TimeSpan? deadline = null)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        _slots = new SemaphoreSlim(capacity, capacity);
        _deadline = deadline ?? TimeSpan.FromSeconds(ImportLimits.ParserDeadlineSeconds);
    }

    /// <summary>
    /// Runs one parser action inside a shared slot and a hard response deadline.
    /// The action receives the linked token and must observe it during CPU-bound
    /// work. Caller cancellation is propagated; an expired parser deadline maps
    /// to the fixed processing-limit rejection. The slot stays owned by the
    /// worker until the action task finishes, even after the caller stops
    /// waiting.
    /// </summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_deadline);
        var deadlineToken = deadline.Token;
        Task<T> actionTask;
        try
        {
            actionTask = action(deadlineToken);
        }
        catch
        {
            deadline.Dispose();
            Exit();
            throw;
        }

        // Register the caller's cancellable wait before the observer can
        // dispose the deadline source on a synchronously completed action.
        var waitTask = actionTask.WaitAsync(deadlineToken);

        // The observer owns the permit from here on. It also observes any
        // late fault after an early deadline/cancellation return.
        _ = ObserveAndReleaseAsync(actionTask, deadline);

        try
        {
            return await waitTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new StatementRejectedException(ParserReasons.ProcessingLimit);
        }
    }

    /// <summary>
    /// Waits until a parser slot is free without consuming it. Intended for
    /// deterministic lifecycle tests; it never bypasses admission.
    /// </summary>
    public async Task WaitForCapacityAsync(CancellationToken cancellationToken = default)
    {
        await _slots.WaitAsync(cancellationToken);
        _slots.Release();
    }

    public async Task EnterAsync(CancellationToken cancellationToken)
    {
        if (!await _slots.WaitAsync(0, cancellationToken))
        {
            throw new StatementRejectedException(ParserReasons.ImportBusy);
        }
    }

    public void Exit() => _slots.Release();

    public void Dispose() => _slots.Dispose();

    private async Task ObserveAndReleaseAsync<T>(Task<T> actionTask, CancellationTokenSource deadline)
    {
        try
        {
            await actionTask.ConfigureAwait(false);
        }
        catch
        {
            // The caller may already have received a deadline/cancellation
            // response; observe the worker's terminal fault here.
        }
        finally
        {
            deadline.Dispose();
            Exit();
        }
    }
}
