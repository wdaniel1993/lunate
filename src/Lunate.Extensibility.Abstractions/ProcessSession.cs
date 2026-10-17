namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// One interactive child process: line-oriented stdin/stdout/stderr exchange while the process
/// runs, plus a tree kill. <see cref="IProcessRunner.StartSessionAsync"/> starts it; the caller
/// writes protocol lines with <see cref="WriteInputLineAsync"/> and reads answers with
/// <see cref="ReadOutputLineAsync"/> until both return <c>null</c> (the child's stdout closed).
/// </summary>
/// <remarks>
/// This is the long-lived counterpart of <see cref="IProcessRunner.RunAsync"/>: no run timeout and
/// no output cap — the caller owns the framing, its own line bound (<see
/// cref="ProcessSessionRequest.MaxLineBytes"/>) and the shutdown grace. <see cref="KillAsync"/>
/// reuses the same children-first tree kill as <see cref="IProcessRunner.RunAsync"/>.
/// </remarks>
public interface IProcessSession : IAsyncDisposable
{
    /// <summary>
    /// Reads one stdout line: the bytes up to the first <c>\n</c>, decoded as UTF-8 with a
    /// trailing <c>\r</c> stripped. Returns null at end of stream. A line longer than
    /// <see cref="ProcessSessionRequest.MaxLineBytes"/> makes the reader drain it and throw
    /// <see cref="System.IO.InvalidDataException"/> — never an unbounded allocation.
    /// </summary>
    ValueTask<string?> ReadOutputLineAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads one stderr line with the same framing and bound as
    /// <see cref="ReadOutputLineAsync"/>; null at end of stream.
    /// </summary>
    ValueTask<string?> ReadErrorLineAsync(CancellationToken cancellationToken);

    /// <summary>Writes one stdin line: the text, then <c>\n</c>, flushed as one write.</summary>
    ValueTask WriteInputLineAsync(string line, CancellationToken cancellationToken);

    /// <summary>True once the child has exited (killed or self-terminated).</summary>
    bool HasExited { get; }

    /// <summary>The exit code once the child has exited; null before that.</summary>
    int? ExitCode { get; }

    /// <summary>Completes with the exit code when the child exits; cancellation abandons the wait.</summary>
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>Kills the process tree children-first and waits, bounded, for the root to die; idempotent.</summary>
    ValueTask KillAsync();
}

/// <summary>
/// One interactive child to start. <see cref="Environment"/> is exactly what the child receives —
/// there is no inherit mode — so Lunate's own environment (provider keys included) can never leak
/// by omission.
/// </summary>
/// <param name="FileName">The executable to start.</param>
/// <param name="Arguments">The argument list, passed verbatim (no shell interpolation).</param>
/// <param name="WorkingDirectory">The child's working directory.</param>
/// <param name="Environment">The child's whole environment; never null.</param>
/// <param name="MaxLineBytes">
/// The greatest line the stdout/stderr readers accept, in bytes; null reads unbounded lines. An
/// oversized line is drained, then <see cref="System.IO.InvalidDataException"/> is thrown.
/// </param>
public sealed record ProcessSessionRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    int? MaxLineBytes = null
);
