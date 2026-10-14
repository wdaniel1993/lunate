using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The <c>core/process</c> implementation: runs one child process with a timeout, an output cap, a
/// tree kill and an optional stdin line pump. Absorbs the former <c>PosixProcessTree</c> and the
/// bash tool's kill/cap/drain logic.
/// </summary>
internal sealed class ProcessRunner : IProcessRunner
{
    /// <summary>The default per-stream output cap when the request does not set one.</summary>
    internal const int DefaultOutputCapBytes = 1_048_576;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        int cap = request.OutputCapBytes is { } requestedCap and > 0
            ? requestedCap
            : DefaultOutputCapBytes;
        Process process =
            Process.Start(BuildStartInfo(request))
            ?? throw new InvalidOperationException("the process did not start");
        using (process)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            if (request.Timeout is { } timeout)
            {
                timeoutCts.CancelAfter(timeout);
            }

            if (request.LineHandler is null)
            {
                process.StandardInput.Close();
            }

            Task<(string Text, bool Capped)> stdoutTask = request.LineHandler is { } handler
                ? PumpLinesAsync(
                    process.StandardOutput,
                    process.StandardInput,
                    handler,
                    cap,
                    timeoutCts.Token
                )
                : ReadBoundedAsync(process.StandardOutput.BaseStream, cap, CancellationToken.None);
            Task<(string Text, bool Capped)> stderrTask = ReadBoundedAsync(
                process.StandardError.BaseStream,
                cap,
                CancellationToken.None
            );

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await KillTreeAsync(process).ConfigureAwait(false);
                (string stdout, string stderr, bool stdoutCapped, bool stderrCapped) =
                    await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
                return new ProcessRunResult(null, stdout, stderr, TimedOut: true)
                {
                    StdoutCapped = stdoutCapped,
                    StderrCapped = stderrCapped,
                };
            }
            catch (OperationCanceledException)
            {
                await KillTreeAsync(process).ConfigureAwait(false);
                await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
                throw;
            }

            (string outText, bool outCapped) = await stdoutTask.ConfigureAwait(false);
            (string errText, bool errCapped) = await stderrTask.ConfigureAwait(false);
            return new ProcessRunResult(process.ExitCode, outText, errText, TimedOut: false)
            {
                StdoutCapped = outCapped,
                StderrCapped = errCapped,
            };
        }
    }

    private static ProcessStartInfo BuildStartInfo(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is { } environment)
        {
            startInfo.Environment.Clear();
            foreach ((string name, string value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        return startInfo;
    }

    /// <summary>
    /// Serves every stdout line to the handler in order and writes each non-null answer back to
    /// the child's stdin as one flushed line. Delivery is never capped; only the accumulated text
    /// is. A closed pipe on either side ends the pump.
    /// </summary>
    private static async Task<(string Text, bool Capped)> PumpLinesAsync(
        StreamReader reader,
        StreamWriter stdin,
        Func<string, CancellationToken, Task<string?>> handler,
        int cap,
        CancellationToken ct
    )
    {
        var builder = new StringBuilder();
        var bytes = 0;
        var capped = false;
        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception) when (IsPipeClosed(exception))
            {
                break;
            }

            if (line is null)
            {
                break;
            }

            if (!capped)
            {
                int lineBytes = Utf8.GetByteCount(line) + 1;
                if (bytes + lineBytes <= cap)
                {
                    bytes += lineBytes;
                    builder.Append(line).Append('\n');
                }
                else
                {
                    capped = true;
                }
            }

            string? response;
            try
            {
                response = await handler(line, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (IsPipeClosed(exception))
            {
                break;
            }

            if (response is null)
            {
                continue;
            }

            try
            {
                await stdin.WriteAsync(response.AsMemory()).ConfigureAwait(false);
                await stdin.WriteAsync("\n".AsMemory()).ConfigureAwait(false);
                await stdin.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsPipeClosed(exception))
            {
                break;
            }
        }

        return (builder.ToString(), capped);
    }

    /// <summary>
    /// Reads one stream into bounded UTF-8 text. Bytes beyond the cap are drained and discarded,
    /// so the child never blocks on a full pipe; the decoded text stops at the cap.
    /// </summary>
    private static async Task<(string Text, bool Capped)> ReadBoundedAsync(
        Stream stream,
        int cap,
        CancellationToken ct
    )
    {
        var builder = new StringBuilder();
        Decoder decoder = Utf8.GetDecoder();
        var buffer = new byte[8192];
        var chars = new char[8192];
        var bytesRead = 0;
        var capped = false;
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsPipeClosed(exception))
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            int remaining = cap - bytesRead;
            int use = Math.Min(read, Math.Max(remaining, 0));
            if (use < read)
            {
                capped = true;
            }

            if (use > 0)
            {
                bytesRead += use;
                int count = decoder.GetChars(buffer, 0, use, chars, 0, flush: false);
                builder.Append(chars, 0, count);
            }
        }

        int tail = decoder.GetChars([], 0, 0, chars, 0, flush: true);
        builder.Append(chars, 0, tail);
        return (builder.ToString(), capped);
    }

    private static bool IsPipeClosed(Exception exception) =>
        exception is IOException or ObjectDisposedException or InvalidOperationException;

    private static async Task<(
        string Stdout,
        string Stderr,
        bool StdoutCapped,
        bool StderrCapped
    )> DrainAsync(
        Task<(string Text, bool Capped)> stdoutTask,
        Task<(string Text, bool Capped)> stderrTask
    )
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask)
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
        }
        catch (TimeoutException) { }

        bool stdoutDone = stdoutTask.Status == TaskStatus.RanToCompletion;
        bool stderrDone = stderrTask.Status == TaskStatus.RanToCompletion;
        return (
            stdoutDone ? stdoutTask.Result.Text : string.Empty,
            stderrDone ? stderrTask.Result.Text : string.Empty,
            stdoutDone && stdoutTask.Result.Capped,
            stderrDone && stderrTask.Result.Capped
        );
    }

    private static async Task KillTreeAsync(Process process)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception)
                when (exception
                        is InvalidOperationException
                            or Win32Exception
                            or NotSupportedException
                ) { }
        }
        else
        {
            // System.Diagnostics' entireProcessTree path SIGSTOPs the whole tree first and
            // wedges macOS CI VMs (runner loses communication; repro on the T-15 PR).
            // Enumerate with ps and SIGKILL children first instead — see KillPosixTree.
            KillPosixTree(process.Id);
        }

        try
        {
            await process
                .WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch (TimeoutException) { }
    }

    /// <summary>
    /// Kills a POSIX process tree by enumerating descendants with <c>ps</c> and sending
    /// SIGKILL to each child before its parent.
    /// </summary>
    /// <remarks>
    /// Deliberately avoids <see cref="Process.Kill(bool)"/> with <c>entireProcessTree: true</c>:
    /// its Unix implementation SIGSTOPs the whole tree before killing it, which was observed to
    /// wedge macOS CI VMs — the runner agent stops responding, no logs or step timeouts fire,
    /// and the job dies with "the hosted runner lost communication with the server". A minimal
    /// repro (spawn <c>sh -c 'exec sleep 300' &amp; sleep 300</c>; call the framework tree kill)
    /// wedges the VM, while this enumeration path completes in milliseconds. The Windows
    /// implementation (job objects) is unaffected and stays on the framework path.
    /// </remarks>
    private static void KillPosixTree(int rootPid)
    {
        IReadOnlyList<int> targets;
        try
        {
            targets = EnumerateDescendants(ReadProcessTable(), rootPid);
        }
        catch (Exception exception)
            when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            // Enumeration failed (e.g. ps unavailable); degrade to killing the root only.
            targets = [rootPid];
        }

        foreach (int pid in targets)
        {
            try
            {
                Process.GetProcessById(pid).Kill();
            }
            catch (Exception exception)
                when (exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Already exited or not ours to kill; best effort by design.
            }
        }
    }

    /// <summary>Collects the root's descendants (children before parents), root last.</summary>
    internal static IReadOnlyList<int> EnumerateDescendants(string processTable, int rootPid)
    {
        var parentOf = new Dictionary<int, int>();
        foreach (string line in processTable.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (
                parts.Length == 2
                && int.TryParse(
                    parts[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int pid
                )
                && int.TryParse(
                    parts[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parent
                )
                && pid > 0
                && pid != parent
            )
            {
                parentOf[pid] = parent;
            }
        }

        var ordered = new List<int>();
        var visited = new HashSet<int>();

        void Collect(int pid)
        {
            if (!visited.Add(pid))
            {
                return;
            }

            foreach (
                int child in parentOf
                    .Where(entry => entry.Value == pid)
                    .Select(entry => entry.Key)
                    .Order()
            )
            {
                Collect(child);
            }

            ordered.Add(pid);
        }

        Collect(rootPid);
        return ordered;
    }

    private static string ReadProcessTable()
    {
        using var ps = Process.Start(
            new ProcessStartInfo("/bin/ps")
            {
                RedirectStandardOutput = true,
                ArgumentList = { "-axo", "pid=,ppid=" },
            }
        )!;
        string output = ps.StandardOutput.ReadToEnd();
        ps.WaitForExit();
        return output;
    }
}
