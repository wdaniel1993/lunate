using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// Local execution: the only <see cref="IExecutionEnvironment"/> today. A command is mapped
/// unchanged, spawned through the existing <see cref="ChildProcess"/> path, and cancelled with the
/// existing children-first process-tree kill. Output caps, timeouts, the line pump, environment
/// policy and exit codes are deliberately unchanged.
/// </summary>
internal sealed partial class LocalExecutionEnvironment : IExecutionEnvironment
{
    /// <summary>The default per-stream output cap when the request does not set one.</summary>
    private const int DefaultOutputCapBytes = 1_048_576;

    internal static LocalExecutionEnvironment Instance { get; } = new();

    private LocalExecutionEnvironment() { }

    public string Description => "local";

    public ExecutionCommand Map(ExecutionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command;
    }

    public ValueTask CancelAsync(IExecutionProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return process.CancelAsync();
    }

    public async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken
    ) => await RunAsync(request, cancellationToken, afterCancel: null).ConfigureAwait(false);

    /// <summary>
    /// The batch run with an environment's extra cancel step: after the host-side children-first
    /// tree kill, <paramref name="afterCancel"/> runs. A container environment passes the
    /// container-side process-group kill there, so the one place a cancel happens stays in this
    /// runner instead of being duplicated (output caps, timeouts and the line pump included).
    /// </summary>
    internal async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken,
        Func<Task>? afterCancel
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
        ProcessRunRequest effective = MapRequest(request);
        using IExecutionProcess child = ChildProcess.Start(effective);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.Timeout is { } timeout)
        {
            timeoutCts.CancelAfter(timeout);
        }

        if (request.LineHandler is null)
        {
            child.StandardInput?.Close();
        }

        Task<(string Text, bool Capped)> stdoutTask = request.LineHandler is { } handler
            ? LocalProcessStreams.PumpLinesAsync(
                child.StandardOutput,
                child.StandardInput!,
                handler,
                cap,
                timeoutCts.Token
            )
            : LocalProcessStreams.ReadBoundedAsync(
                child.StandardOutput.BaseStream,
                cap,
                CancellationToken.None
            );
        Task<(string Text, bool Capped)> stderrTask = LocalProcessStreams.ReadBoundedAsync(
            child.StandardError.BaseStream,
            cap,
            CancellationToken.None
        );

        int exitCode;
        try
        {
            exitCode = await child.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await CancelAsync(child).ConfigureAwait(false);
            await AfterCancelAsync(afterCancel).ConfigureAwait(false);
            (string stdout, string stderr, bool stdoutCapped, bool stderrCapped) =
                await LocalProcessStreams.DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            return new ProcessRunResult(null, stdout, stderr, TimedOut: true)
            {
                StdoutCapped = stdoutCapped,
                StderrCapped = stderrCapped,
            };
        }
        catch (OperationCanceledException)
        {
            await CancelAsync(child).ConfigureAwait(false);
            await AfterCancelAsync(afterCancel).ConfigureAwait(false);
            await LocalProcessStreams.DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }

        (string outText, bool outCapped) = await stdoutTask.ConfigureAwait(false);
        (string errText, bool errCapped) = await stderrTask.ConfigureAwait(false);
        return new ProcessRunResult(exitCode, outText, errText, TimedOut: false)
        {
            StdoutCapped = outCapped,
            StderrCapped = errCapped,
        };
    }

    /// <summary>Runs the environment's extra cancel step; the host tree kill already happened.</summary>
    private static async Task AfterCancelAsync(Func<Task>? afterCancel)
    {
        if (afterCancel is not null)
        {
            await afterCancel().ConfigureAwait(false);
        }
    }

    private ProcessRunRequest MapRequest(ProcessRunRequest request)
    {
        ExecutionCommand mapped = Map(
            new ExecutionCommand(
                request.FileName,
                request.Arguments,
                request.WorkingDirectory,
                request.Environment,
                request.ExtraEnvironment
            )
        );
        return request with
        {
            FileName = mapped.Executable,
            Arguments = mapped.Arguments,
            WorkingDirectory = mapped.WorkingDirectory,
            Environment = mapped.Environment,
            ExtraEnvironment = mapped.ExtraEnvironment,
        };
    }
}
