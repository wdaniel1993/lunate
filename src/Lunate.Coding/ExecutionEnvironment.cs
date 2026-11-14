using System.IO;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// Where a tool command's process runs. The seam maps the four values a container or local host
/// needs — executable, argument prefix, working directory and environment — owns batch and
/// interactive execution, and carries the cancel hook for what it started. Local execution is the
/// only implementation today; the extension/MCP path consumes it through <see cref="ProcessRunner"/>.
/// </summary>
internal interface IExecutionEnvironment
{
    /// <summary>
    /// One short line for a header or <c>/context</c>: <c>local</c> here, an environment name later.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Maps a tool command to the executable, arguments (prefix included), working directory and
    /// environment that actually run. The bind-mounted workspace keeps one absolute path on both
    /// sides, so there is no path-translation table.
    /// </summary>
    ExecutionCommand Map(ExecutionCommand command);

    /// <summary>
    /// True when the command runs somewhere else and this environment starts a host client for it
    /// (the container engine) instead of the tool's own command. The bash tool then hands over the
    /// plain command and leaves the host-side guard wrapping to the environment, which wraps the
    /// client it actually starts.
    /// </summary>
    bool StartsHostClient => false;

    /// <summary>Runs one batch tool command to completion with this environment's cancel semantics.</summary>
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Starts one interactive tool command with this environment's cancel semantics; see
    /// <see cref="IProcessSession"/>.
    /// </summary>
    Task<IProcessSession> StartSessionAsync(
        ProcessSessionRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Stops a process this environment started. Local execution keeps the children-first process
    /// tree kill; a container implementation may kill the command's process group instead.
    /// </summary>
    ValueTask CancelAsync(IExecutionProcess process);
}

/// <summary>
/// A selected environment could not start a command at all (a container engine that is absent or
/// not running, a container that could not be created). The message is model-facing: it says what
/// failed and what to do next, and the command never falls back to the host.
/// </summary>
internal sealed class ExecutionEnvironmentUnavailableException(string message) : Exception(message);

/// <summary>
/// One logical tool command, before the environment maps it: the four values the seam carries.
/// <see cref="Environment"/> is the caller's replacement (null inherits) and
/// <see cref="ExtraEnvironment"/> is applied on top, exactly as <see cref="ProcessRunRequest"/>
/// defines them.
/// </summary>
internal sealed record ExecutionCommand(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment,
    IReadOnlyDictionary<string, string>? ExtraEnvironment = null
);

/// <summary>
/// A process started by an execution environment. The same handle shape serves the local batch
/// spawn; a future environment can wrap its own client while keeping the cancel hook explicit.
/// </summary>
internal interface IExecutionProcess : IDisposable
{
    int Id { get; }

    StreamReader StandardOutput { get; }

    StreamReader StandardError { get; }

    StreamWriter? StandardInput { get; }

    Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>Ends this process with the environment's cancel semantics and reaps it.</summary>
    ValueTask CancelAsync();
}
