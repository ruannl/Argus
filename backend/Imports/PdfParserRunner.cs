using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace argus.Server.Imports;

/// <summary>
/// Parent-side runner for the PDF parser child. The runner owns the concurrency
/// slot, the hard timeout, the resident-memory bound, stdin framing, stdout
/// bounding, and tree cleanup. It never logs child diagnostics.
/// </summary>
public sealed class PdfParserRunner : IDisposable
{
    private readonly ParserAdmission _admission;
    private readonly bool _ownsAdmission;
    private readonly Func<ProcessStartInfo> _startInfoFactory;
    private readonly TimeSpan _timeout;

    public PdfParserRunner(
        Func<ProcessStartInfo>? startInfoFactory = null,
        TimeSpan? timeout = null,
        ParserAdmission? admission = null)
    {
        _startInfoFactory = startInfoFactory ?? DefaultStartInfo;
        _timeout = timeout ?? TimeSpan.FromSeconds(ImportLimits.ParserDeadlineSeconds);
        _admission = admission ?? new ParserAdmission();
        _ownsAdmission = admission is null;
    }

    public async Task<ParsedStatement> ParseAsync(
        byte[] bytes,
        PdfChildRequestOptions options,
        CancellationToken cancellationToken = default)
    {
        await _admission.EnterAsync(cancellationToken);
        try
        {
            return await ParseCoreAsync(bytes, options, cancellationToken);
        }
        finally
        {
            _admission.Exit();
        }
    }

    private async Task<ParsedStatement> ParseCoreAsync(
        byte[] bytes,
        PdfChildRequestOptions options,
        CancellationToken cancellationToken)
    {
        var header = JsonSerializer.SerializeToUtf8Bytes(options, PdfChildProtocol.JsonOptions);
        if (header.Length > 64 * 1024)
        {
            throw new StatementRejectedException(ParserReasons.InvalidRequest);
        }

        using var process = new Process { StartInfo = _startInfoFactory() };
        try
        {
            if (!process.Start())
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
        }
        catch (StatementRejectedException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }

        using var killSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var deadlineSource = new CancellationTokenSource();
        var killedForBound = false;
        var terminateDescendants = false;
        Task? stderrTask = null;
        Task<byte[]>? stdoutTask = null;
        Task? inputTask = null;
        Task? memoryTask = null;
        var leaderPid = process.Id;
        try
        {
            stderrTask = DrainAsync(process.StandardError.BaseStream, killSource.Token);
            stdoutTask = ReadBoundedAsync(process.StandardOutput.BaseStream, killSource.Token);
            inputTask = WriteInputAsync(process.StandardInput.BaseStream, header, bytes, killSource.Token);
            memoryTask = MonitorMemoryAsync(process, () => killedForBound = true, killSource.Token);

            var exitTask = process.WaitForExitAsync(CancellationToken.None);
            var deadlineTask = Task.Delay(_timeout, deadlineSource.Token);
            var completed = await Task.WhenAny(exitTask, deadlineTask, Task.Delay(Timeout.Infinite, cancellationToken));
            if (completed != exitTask)
            {
                killedForBound = true;
                terminateDescendants = true;
                Kill(process, leaderPid);
            }

            var exited = await Task.WhenAny(
                exitTask, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
            if (exited != exitTask)
            {
                killedForBound = true;
                terminateDescendants = true;
                Kill(process, leaderPid);
                throw new StatementRejectedException(ParserReasons.ProcessingLimit);
            }
            await exitTask;
            deadlineSource.Cancel();

            byte[] output;
            try
            {
                output = await AwaitBounded(stdoutTask, TimeSpan.FromSeconds(2));
            }
            catch (StatementRejectedException)
            {
                terminateDescendants = true;
                Kill(process, leaderPid);
                throw;
            }

            killSource.Cancel();
            await AwaitQuietly(stderrTask, TimeSpan.FromSeconds(1));
            await AwaitQuietly(inputTask, TimeSpan.FromSeconds(1));
            await AwaitQuietly(memoryTask, TimeSpan.FromSeconds(1));

            if (killedForBound)
            {
                throw new StatementRejectedException(ParserReasons.ProcessingLimit);
            }
            if (process.ExitCode != 0)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }

            if (output.Length == 0)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }

            PdfChildResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<PdfChildResponse>(
                    output, PdfChildProtocol.JsonOptions);
            }
            catch (JsonException)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }

            if (response is null)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
            if (!response.Ok)
            {
                throw new StatementRejectedException(response.Reason ?? ParserReasons.UnsupportedLayout);
            }
            if (response.Statement is null)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
            return PdfChildProtocol.FromChild(response.Statement);
        }
        finally
        {
            killSource.Cancel();
            deadlineSource.Cancel();
            if (terminateDescendants || !process.HasExited)
            {
                Kill(process, leaderPid);
            }
            await AwaitQuietly(stderrTask, TimeSpan.FromSeconds(1));
            await AwaitQuietly(stdoutTask, TimeSpan.FromSeconds(1));
            await AwaitQuietly(inputTask, TimeSpan.FromSeconds(1));
            await AwaitQuietly(memoryTask, TimeSpan.FromSeconds(1));
        }
    }

    private static async Task<T> AwaitBounded<T>(Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout, CancellationToken.None));
        if (completed != task)
        {
            throw new StatementRejectedException(ParserReasons.ProcessingLimit);
        }
        return await task;
    }

    private static async Task AwaitQuietly(Task? task, TimeSpan timeout)
    {
        if (task is null)
        {
            return;
        }
        var completed = await Task.WhenAny(task, Task.Delay(timeout, CancellationToken.None));
        if (completed == task)
        {
            try
            {
                await task;
            }
            catch
            {
                // Child diagnostics are never surfaced or logged.
            }
            return;
        }

        // The bounded cleanup wait does not block on a pipe held by a stubborn
        // descendant. Attach an observation so a late fault is still consumed;
        // the caller never sees it.
        _ = task.ContinueWith(
            static finished => _ = finished.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static async Task WriteInputAsync(
        Stream stdin,
        byte[] header,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, header.Length);
        await stdin.WriteAsync(length, cancellationToken);
        await stdin.WriteAsync(header, cancellationToken);
        await stdin.WriteAsync(bytes, cancellationToken);
        await stdin.FlushAsync(cancellationToken);
        stdin.Close();
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stdout, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stdout.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > ImportLimits.MaxPdfChildOutputBytes)
            {
                throw new StatementRejectedException(ParserReasons.ProcessingLimit);
            }
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static async Task DrainAsync(Stream stderr, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (await stderr.ReadAsync(buffer, cancellationToken) > 0)
        {
            // Discard: child diagnostics never reach logs or responses.
        }
    }

    private static async Task MonitorMemoryAsync(
        Process process,
        Action onExceeded,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                if (process.WorkingSet64 > ImportLimits.MaxPdfChildResidentBytes)
                {
                    onExceeded();
                    Kill(process, process.Id);
                    return;
                }
                await Task.Delay(100, cancellationToken);
            }
        }
        catch
        {
            // The process exited or the monitor was cancelled.
        }
    }

    private static void Kill(Process process, int leaderPid)
    {
        // The child joins its own process group on Linux. Group termination is
        // what reaches a descendant that inherited a pipe after the root exited;
        // Process.Kill(entireProcessTree) silently does nothing once the root is
        // gone. Both are best effort and never surface to the caller.
        ParserProcessIsolation.KillProcessGroup(leaderPid);
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort; the OS reaps the tree.
        }
    }

    private static ProcessStartInfo DefaultStartInfo()
    {
        var serverAssembly = typeof(Program).Assembly.Location;
        var runtimeConfig = Path.ChangeExtension(serverAssembly, ".runtimeconfig.json");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        if (!File.Exists(runtimeConfig))
        {
            // Test host output: reuse the test runtime's deps/runtimeconfig so
            // the server assembly can be launched apphost-free.
            var entry = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(entry))
            {
                var deps = Path.ChangeExtension(entry, ".deps.json");
                var testRuntimeConfig = Path.ChangeExtension(entry, ".runtimeconfig.json");
                startInfo.ArgumentList.Add("exec");
                if (File.Exists(deps))
                {
                    startInfo.ArgumentList.Add("--depsfile");
                    startInfo.ArgumentList.Add(deps);
                }
                if (File.Exists(testRuntimeConfig))
                {
                    startInfo.ArgumentList.Add("--runtimeconfig");
                    startInfo.ArgumentList.Add(testRuntimeConfig);
                }
            }
        }

        startInfo.ArgumentList.Add(serverAssembly);
        startInfo.ArgumentList.Add("--parse-statement-pdf");
        startInfo.Environment["DOTNET_GCHeapHardLimit"] = "0x10000000";
        return startInfo;
    }

    public void Dispose()
    {
        if (_ownsAdmission)
        {
            _admission.Dispose();
        }
    }
}
