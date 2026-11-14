using System.Globalization;
using System.Text;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// The host half a container command starts: one engine-client process, with the extra cancel step
/// that runs the container-side kill after the host tree kill. Local execution is the production
/// implementation; tests substitute it to observe the argv and the cancel without an engine.
/// </summary>
internal interface IHostCommandRunner
{
    Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken,
        Func<Task>? afterCancel
    );

    Task<IProcessSession> StartSessionAsync(
        ProcessSessionRequest request,
        CancellationToken cancellationToken
    );
}

/// <summary>The production host half: the local environment's batch runner with the cancel hook.</summary>
internal sealed class LocalHostCommandRunner : IHostCommandRunner
{
    internal static LocalHostCommandRunner Instance { get; } = new();

    private LocalHostCommandRunner() { }

    public Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken,
        Func<Task>? afterCancel
    ) => LocalExecutionEnvironment.Instance.RunAsync(request, cancellationToken, afterCancel);

    public Task<IProcessSession> StartSessionAsync(
        ProcessSessionRequest request,
        CancellationToken cancellationToken
    ) => LocalExecutionEnvironment.Instance.StartSessionAsync(request, cancellationToken);
}

/// <summary>
/// Container execution: every process-backed tool command runs through one long-lived
/// <c>docker</c> or <c>podman</c> container, created before the first command and kept for the
/// session because a stop plus start costs about 10.3 s. The workspace is bind-mounted at its host
/// absolute path, so the file tools, verified edits and the post-call change scan keep working
/// untouched; the state root, the install prefix, <c>auth.json</c> and the engine socket are never
/// mounted. The host half (the engine client) still goes through the bash guard; no guard runs
/// inside the container.
/// </summary>
internal sealed class ContainerExecutionEnvironment : IExecutionEnvironment, IAsyncDisposable
{
    /// <summary>The container-side shell: the image supplies it, and it records the process group.</summary>
    internal const string ContainerShell = "/bin/sh";

    /// <summary>The wrapper's own <c>$0</c>; it is never executed, it only names the script.</summary>
    internal const string WrapperName = "lunate-exec";

    private static readonly TimeSpan CreationTimeout = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(30);

    private readonly ExecutionEnvironmentSelection _selection;
    private readonly string _workspace;
    private readonly IHostCommandRunner _host;
    private readonly bool _guardEnabled;
    private readonly string _container;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Lock _gate = new();
    private BashGuard? _guard;
    private Task<string?>? _creation;
    private ExecutionEnvironmentStatus _status;
    private int _call;
    private bool _disposed;

    /// <summary>
    /// One environment for one session and workspace. <paramref name="host"/> is the local batch
    /// runner that starts the engine client, <paramref name="guard"/> the host guard whose wrap
    /// covers that client (null resolves the live one), and <paramref name="containerName"/> exists
    /// so a test can name and inspect the container it drives.
    /// </summary>
    internal ContainerExecutionEnvironment(
        ExecutionEnvironmentSelection selection,
        string workspace,
        IHostCommandRunner? host = null,
        BashGuard? guard = null,
        bool guardEnabled = true,
        string? containerName = null
    )
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        _selection = selection;
        _workspace = workspace;
        _host = host ?? LocalHostCommandRunner.Instance;
        _guard = guard;
        _guardEnabled = guardEnabled;
        _container =
            containerName
            ?? string.Create(
                CultureInfo.InvariantCulture,
                $"lunate-{Guid.NewGuid().ToString("N")[..12]}"
            );
        _status = ExecutionEnvironmentStatus.StartingFor(Description);
    }

    public string Description => _selection.Description;

    /// <summary>A container command starts a host client, so the tool hands over the plain command.</summary>
    public bool StartsHostClient => true;

    /// <summary>The container's name, for the argv and for the tests that drive a real engine.</summary>
    internal string ContainerName => _container;

    /// <summary>The selected environment and its readiness; read on every <c>/context</c>.</summary>
    internal ExecutionEnvironmentStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>The host-side mapping of one command, with this environment's next process-group file.</summary>
    public ExecutionCommand Map(ExecutionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return MapCommand(command, PgidPath());
    }

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

        string? failure = await EnsureContainerAsync().ConfigureAwait(false);
        if (failure is not null)
        {
            throw new ExecutionEnvironmentUnavailableException(Unavailable(failure));
        }

        string pgidPath = PgidPath();
        ExecutionCommand mapped = MapCommand(
            new ExecutionCommand(
                request.FileName,
                request.Arguments,
                request.WorkingDirectory,
                request.Environment,
                request.ExtraEnvironment
            ),
            pgidPath
        );
        ProcessRunRequest hostRequest = request with
        {
            FileName = mapped.Executable,
            Arguments = mapped.Arguments,
            WorkingDirectory = mapped.WorkingDirectory,
            Environment = mapped.Environment,
            ExtraEnvironment = mapped.ExtraEnvironment,
        };
        string container = _container;
        return await _host
            .RunAsync(hostRequest, cancellationToken, () => KillGroupAsync(container, pgidPath))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The seam's interactive half. No tool reaches it in this change (extension, MCP and Roslyn
    /// sessions stay on local execution), but the mapped host half is the same one batch calls use,
    /// so the seam has no hole: it is the engine client that starts, not the command.
    /// </summary>
    public async Task<IProcessSession> StartSessionAsync(
        ProcessSessionRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        string? failure = await EnsureContainerAsync().ConfigureAwait(false);
        if (failure is not null)
        {
            throw new ExecutionEnvironmentUnavailableException(Unavailable(failure));
        }

        ExecutionCommand mapped = MapCommand(
            new ExecutionCommand(
                request.FileName,
                request.Arguments,
                request.WorkingDirectory,
                request.Environment
            ),
            PgidPath()
        );
        return await _host
            .StartSessionAsync(
                request with
                {
                    FileName = mapped.Executable,
                    Arguments = mapped.Arguments,
                    WorkingDirectory = mapped.WorkingDirectory,
                    Environment = mapped.Environment ?? request.Environment,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public ValueTask CancelAsync(IExecutionProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return process.CancelAsync();
    }

    /// <summary>Removes the session's container, best effort: every exit path owns it.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        try
        {
            await RemoveContainerAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Disposal is best effort on every exit path; the name is unique per session.
        }

        _lifecycle.Dispose();
    }

    /// <summary>
    /// The argv a container command maps to: the engine's <c>exec</c> in the workspace, the named
    /// container, and the container's shell recording the command's process group before it execs
    /// the command unchanged. The engine client inherits the host environment (it needs PATH and the
    /// engine's own variables); the container receives only the explicitly passed pairs, because
    /// <c>exec</c> inherits nothing.
    /// </summary>
    private ExecutionCommand MapCommand(ExecutionCommand command, string pgidPath)
    {
        List<string> arguments = ["exec", "-i", "-w", command.WorkingDirectory];
        foreach ((string name, string value) in ForwardedEnvironment(command))
        {
            arguments.Add("-e");
            arguments.Add(string.Create(CultureInfo.InvariantCulture, $"{name}={value}"));
        }

        arguments.Add(_container);
        arguments.Add(ContainerShell);
        arguments.Add("-c");
        arguments.Add(Wrapper(pgidPath));
        arguments.Add(WrapperName);
        arguments.Add(ContainerShell);
        arguments.AddRange(command.Arguments);

        var host = new ExecutionCommand(
            _selection.Executable,
            arguments,
            command.WorkingDirectory,
            Environment: null,
            ExtraEnvironment: null
        );
        return WrapHostClient(host, command.WorkingDirectory);
    }

    /// <summary>
    /// Wraps the engine client in the host guard: the same OS-level deny that covers the local
    /// shell, applied to the process Lunate actually starts. The command inside the container is
    /// not guarded — the mount is what limits its reach, and the guard's refusal still runs on the
    /// host before any engine starts.
    /// </summary>
    private ExecutionCommand WrapHostClient(ExecutionCommand host, string workingDirectory)
    {
        if (!_guardEnabled)
        {
            return host;
        }

        _guard ??= new BashGuard();
        GuardCommand wrapped = _guard.WrapHostClient(host.Executable, workingDirectory);
        return host with
        {
            Executable = wrapped.FileName,
            Arguments = [.. wrapped.ArgumentsPrefix, .. host.Arguments],
        };
    }

    /// <summary>
    /// The explicit pairs the request names, replacement first and extras last, both sorted so the
    /// argv is deterministic. Nothing else crosses: <c>exec</c> forwards no host environment.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> ForwardedEnvironment(
        ExecutionCommand command
    )
    {
        if (command.Environment is { Count: > 0 } replacement)
        {
            foreach (KeyValuePair<string, string> pair in Sorted(replacement))
            {
                yield return pair;
            }
        }

        if (command.ExtraEnvironment is { Count: > 0 } extras)
        {
            foreach (KeyValuePair<string, string> pair in Sorted(extras))
            {
                yield return pair;
            }
        }

        static IEnumerable<KeyValuePair<string, string>> Sorted(
            IReadOnlyDictionary<string, string> values
        ) => values.OrderBy(pair => pair.Key, StringComparer.Ordinal);
    }

    /// <summary>
    /// The container-side wrapper: records the command's process group, then execs the command, so
    /// the recorded id and the running command share one pid and one group. A cancel can then kill
    /// that group inside the container, which the host tree kill alone cannot reach.
    /// </summary>
    internal static string Wrapper(string pgidPath) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"""
            pgid=$(ps -o pgid= -p $$ 2>/dev/null | tr -d ' ')
            [ -n "$pgid" ] || pgid=$(cut -d' ' -f5 /proc/$$/stat 2>/dev/null)
            printf '%s' "$pgid" > {pgidPath}
            exec "$@"
            """
        );

    /// <summary>
    /// The container-side cancel: <c>TERM</c> the recorded group, wait briefly, then <c>KILL</c>.
    /// Exit 0 means the group is proven gone; 3 (nothing usable recorded) and 4 (still alive) mean
    /// it cannot be proven, and the caller removes and recreates the container instead. A recorded
    /// group of 0 or 1 is never killed: it is not this command's group.
    /// </summary>
    internal static string KillScript(string pgidPath) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"""
            p={pgidPath}
            [ -s "$p" ] || exit 3
            g=$(cat "$p")
            case "$g" in ''|*[!0-9]*) exit 3;; esac
            [ "$g" -gt 1 ] || exit 3
            kill -s TERM -- "-$g" 2>/dev/null || true
            n=0
            while kill -0 -- "-$g" 2>/dev/null; do
              n=$((n+1))
              [ "$n" -ge 20 ] && break
              sleep 0.05
            done
            kill -s KILL -- "-$g" 2>/dev/null || true
            kill -0 -- "-$g" 2>/dev/null && exit 4
            exit 0
            """
        );

    private string PgidPath() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"/tmp/{_container}-{Interlocked.Increment(ref _call)}.pgid"
        );

    /// <summary>
    /// One creation attempt per environment, awaited by the first command that needs it. A failed
    /// creation stays failed: a selected environment never retries itself into a host fallback, and
    /// a new session is the way to try again.
    /// </summary>
    private async Task<string?> EnsureContainerAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }

            if (_creation is null)
            {
                _creation = CreateContainerAsync();
            }

            return await _creation.ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task<string?> CreateContainerAsync()
    {
        ProcessRunResult result = await _host
            .RunAsync(
                new ProcessRunRequest(
                    _selection.Executable,
                    [
                        "run",
                        "-d",
                        "--name",
                        _container,
                        "-v",
                        Mount(),
                        "-w",
                        _workspace,
                        _selection.Image,
                    ],
                    _workspace,
                    Timeout: CreationTimeout
                ),
                CancellationToken.None,
                afterCancel: null
            )
            .ConfigureAwait(false);
        string? failure = result.ExitCode == 0 ? null : Reason(result);
        SetStatus(
            failure is null
                ? ExecutionEnvironmentStatus.ReadyFor(Description)
                : ExecutionEnvironmentStatus.FailedFor(Description, failure)
        );
        return failure;
    }

    /// <summary>
    /// The container-side kill after the host tree kill, and the S-7 lesson: the host client's
    /// death stops nothing inside. A group that cannot be proven gone means the container is
    /// removed and recreated before the next command, so no later command shares its state.
    /// </summary>
    private async Task KillGroupAsync(string container, string pgidPath)
    {
        ProcessRunResult result = await _host
            .RunAsync(
                new ProcessRunRequest(
                    _selection.Executable,
                    ["exec", container, ContainerShell, "-c", KillScript(pgidPath)],
                    _workspace,
                    Timeout: ControlTimeout
                ),
                CancellationToken.None,
                afterCancel: null
            )
            .ConfigureAwait(false);
        if (result.TimedOut || result.ExitCode != 0)
        {
            await ResetContainerAsync().ConfigureAwait(false);
        }
    }

    private async Task ResetContainerAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            _creation = null;
            SetStatus(ExecutionEnvironmentStatus.StartingFor(Description));
            await RemoveContainerAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task RemoveContainerAsync()
    {
        await _host
            .RunAsync(
                new ProcessRunRequest(
                    _selection.Executable,
                    ["rm", "-f", _container],
                    _workspace,
                    Timeout: ControlTimeout
                ),
                CancellationToken.None,
                afterCancel: null
            )
            .ConfigureAwait(false);
    }

    private string Mount() =>
        string.Create(CultureInfo.InvariantCulture, $"{_workspace}:{_workspace}");

    private void SetStatus(ExecutionEnvironmentStatus status)
    {
        lock (_gate)
        {
            _status = status;
        }
    }

    /// <summary>The creation failure on one line: the engine's first stderr line, or its exit code.</summary>
    private static string Reason(ProcessRunResult result)
    {
        string line =
            result
                .Stderr.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .FirstOrDefault()
            ?? string.Empty;
        if (line.Length == 0)
        {
            line = string.Create(
                CultureInfo.InvariantCulture,
                $"exit code {result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}"
            );
        }

        return line.Length <= 200 ? line : line[..200];
    }

    private string Unavailable(string reason) =>
        FormattableString.Invariant(
            $"the container environment {Description} could not create its container: {reason}; check that {_selection.Engine} is installed and running (on macOS: {_selection.Engine} machine start) and that image '{_selection.Image}' can be pulled. The command did not run on the host."
        );

    /// <summary>
    /// The session's environment for a selection, or null for the local default. The container is
    /// not created here: the first command creates it, and the header can already name the
    /// selection because the name comes from the selection, not the engine.
    /// </summary>
    internal static ContainerExecutionEnvironment? Create(
        ExecutionEnvironmentSelection? selection,
        string workspace,
        bool guardEnabled,
        BashGuard? guard = null
    ) =>
        selection is null
            ? null
            : new ContainerExecutionEnvironment(
                selection,
                workspace,
                guard: guard,
                guardEnabled: guardEnabled
            );
}
