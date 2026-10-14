namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// A session-scoped background service: an LSP server, watcher or connection. The host starts it
/// with <c>SessionStarted</c> semantics and stops it idempotently on <c>SessionEnding</c>. A
/// failure is reported and contained: it never crashes the run.
/// </summary>
public interface IBackgroundService
{
    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The core process capability (<c>core/process</c>): runs one child process to completion with a
/// timeout, an output cap and a tree kill. On timeout or cancellation the process tree is killed
/// children-first: on POSIX descendants are enumerated with <c>ps</c> and SIGKILLed before their
/// parents (the framework's tree kill SIGSTOPs the tree first and has wedged macOS CI); Windows
/// keeps the framework's job-object path as the fallback. A process that cannot start throws the
/// platform's start exception (<see cref="System.ComponentModel.Win32Exception"/>,
/// <see cref="System.IO.IOException"/> and friends); the caller renders the teaching error.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs the request to completion. The result carries exit code, output and flags.</summary>
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// One process to run. <see cref="Environment"/> is exactly what the child receives: null inherits
/// the runner's own environment; a non-null dictionary replaces it entirely (extensions should pass
/// an explicit allow-list so Lunate's environment — provider keys included — cannot leak).
/// </summary>
/// <param name="FileName">The executable to start.</param>
/// <param name="Arguments">The argument list, passed verbatim (no shell interpolation).</param>
/// <param name="WorkingDirectory">The child's working directory.</param>
/// <param name="Environment">The child's environment; null inherits, non-null replaces.</param>
/// <param name="Timeout">The run's deadline; null runs without one.</param>
/// <param name="OutputCapBytes">The per-stream output cap; null uses the runner's default.</param>
public sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment = null,
    TimeSpan? Timeout = null,
    int? OutputCapBytes = null
)
{
    /// <summary>
    /// Serves interactive children: when set, every stdout line is handed to it, in order, while
    /// the process runs, and a non-null return is written back to the child's stdin as one flushed
    /// line. Delivery is never capped; <see cref="ProcessRunResult.OutputCapped"/> only reports the
    /// accumulated text. Null (the default) closes the child's stdin — the batch mode the bash tool
    /// uses.
    /// </summary>
    public Func<string, CancellationToken, Task<string?>>? LineHandler { get; init; }
}

/// <summary>
/// The outcome of one process run. Output is decoded as UTF-8 and capped per stream: reading
/// continues past the cap so the child never blocks, but the text stops there and
/// <see cref="StdoutCapped"/>/<see cref="StderrCapped"/> report it — the caller renders any marker.
/// </summary>
/// <param name="ExitCode">The exit code; null when the run timed out and the tree was killed.</param>
/// <param name="Stdout">The captured standard output (capped).</param>
/// <param name="Stderr">The captured standard error (capped).</param>
/// <param name="TimedOut">True when the deadline expired and the tree was killed.</param>
public sealed record ProcessRunResult(int? ExitCode, string Stdout, string Stderr, bool TimedOut)
{
    /// <summary>True when standard output hit the cap and its text was truncated.</summary>
    public bool StdoutCapped { get; init; }

    /// <summary>True when standard error hit the cap and its text was truncated.</summary>
    public bool StderrCapped { get; init; }

    /// <summary>True when either stream hit the cap and its text was truncated.</summary>
    public bool OutputCapped => StdoutCapped || StderrCapped;
}

/// <summary>
/// Read-only handle to the core file change bus. A subscription may carry a pattern: <c>*</c>
/// matches any characters and <c>?</c> exactly one; patterns match the whole canonical absolute
/// path and compare case-sensitively on Linux, case-insensitively elsewhere. A null or empty
/// pattern matches every change.
/// </summary>
public interface IFileChangeBus
{
    /// <summary>Subscribes for the extension's lifetime; disposal ends the subscription.</summary>
    IDisposable Subscribe(IFileChangedHandler handler, string? pathPattern = null);
}

/// <summary>
/// Read-only handle to the core mutation queue: schedules a read-modify-write on a canonical
/// absolute path so concurrent mutations of one file apply one after another.
/// </summary>
public interface IMutationQueue
{
    Task<T> RunAsync<T>(
        string path,
        Func<CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken
    );
}

/// <summary>Core workspace identity exposed to extensions.</summary>
public sealed record WorkspaceInfo(string WorktreeRoot, string? RepoRoot, string? GitCommonDir);
