using Lunate.Agent;
using Lunate.Ai;
using Lunate.Coding;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility.Testing;

/// <summary>
/// Hosts one extension for tests: it installs the extension under the caller's temp directory,
/// discovers and loads it through the real <see cref="ExtensionLoader"/> (real manifest, real load
/// context, real trust flow with a <see cref="ScriptedTrustPrompt"/>), starts the session and its
/// background services, runs the real harness against a caller-provided <see cref="IChatClient"/>,
/// and stops idempotently. Disposal unloads the extension. All state stays under the caller's temp
/// directory; the real <c>~/.lunate</c> is never touched.
/// </summary>
public sealed class ExtensionTestHost : IAsyncDisposable
{
    private readonly ExtensionTestHostOptions _options;
    private readonly FileChangeBus _fileChanges;
    private LoadedExtension? _loaded;
    private Session? _session;
    private ExtensionMcpServers? _mcpServers;
    private bool _stopped;
    private bool _disposed;

    public ExtensionTestHost(ExtensionTestHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TempDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ExtensionId);
        if (options.ExtensionDirectory is null && options.SourceFiles is null)
        {
            throw new ArgumentException(
                "ExtensionTestHostOptions needs an ExtensionDirectory or SourceFiles to install.",
                nameof(options)
            );
        }

        _options = options;
        TempDirectory = Path.GetFullPath(options.TempDirectory);
        Directory.CreateDirectory(TempDirectory);
        WorkingDirectory = Path.Combine(TempDirectory, "worktree");
        Directory.CreateDirectory(WorkingDirectory);
        StorePath = Path.Combine(TempDirectory, "store");
        Directory.CreateDirectory(StorePath);
        SessionPath = Path.Combine(TempDirectory, "sessions", "session.jsonl");
        ExtensionDirectory = Path.Combine(
            WorkingDirectory,
            ".lunate",
            "extensions",
            options.ExtensionId
        );
        Log = new RecordingExtensionLog();
        Trust = new ScriptedTrustPrompt(options.TrustDecisions?.ToArray() ?? [true]);
        Events = new EventRecorder();
        Tools = new ToolRegistry();
        Runner = new ExtensionLoader(
            new ExtensionHostOptions
            {
                StorePath = StorePath,
                Log = Log,
                ClientFactory =
                    options.ClientFactory
                    ?? new ChatClientFactory(
                        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance
                    ),
                ReservedCommands = Commands.BuiltinNames,
                BuiltIns = options.BuiltIns ?? [],
            },
            options.HookRunnerOptions is { } hookOptions ? new HookRunner(hookOptions, Log) : null
        );
        _fileChanges = new FileChangeBus(WorkingDirectory, Log);
        if (options.DecisionService is { } decisionService)
        {
            Runner.Services.RegisterCore("core/decision", decisionService);
        }

        Runner.Services.RegisterCore("core/file-bus", _fileChanges);
        Runner.Services.RegisterCore(
            "core/mutation-queue",
            new MutationQueueHandle(FileMutationQueue.Shared)
        );
        Runner.Services.RegisterCore(
            "core/workspace",
            new WorkspaceInfo(Path.GetFullPath(WorkingDirectory), null, null)
        );
        Runner.Services.RegisterCore("core/process", new ProcessRunner());
        InstallExtension(options);
    }

    /// <summary>The caller-owned temp directory all host state lives under.</summary>
    public string TempDirectory { get; }

    /// <summary>The extension store (settings, secrets, trust) under the temp directory.</summary>
    public string StorePath { get; }

    /// <summary>The worktree the session runs in, under the temp directory.</summary>
    public string WorkingDirectory { get; }

    /// <summary>The installed project extension directory (<c>.lunate/extensions/&lt;id&gt;</c>).</summary>
    public string ExtensionDirectory { get; }

    /// <summary>The session file the runs append to, under the temp directory.</summary>
    public string SessionPath { get; }

    /// <summary>The real loader; its hook runner is wired into every run.</summary>
    public ExtensionLoader Runner { get; }

    /// <summary>Every line the loaded extension logged, in emission order.</summary>
    public RecordingExtensionLog Log { get; }

    /// <summary>The trust decisions and counts for the load.</summary>
    public ScriptedTrustPrompt Trust { get; }

    /// <summary>Every event of every run on this host, in emission order.</summary>
    public EventRecorder Events { get; }

    /// <summary>The tool registry runs execute against; add tools before running.</summary>
    public ToolRegistry Tools { get; }

    /// <summary>The loaded extension; throws before the first <see cref="StartAsync"/>.</summary>
    public LoadedExtension LoadedExtension =>
        _loaded
        ?? throw new InvalidOperationException(
            "The extension is not loaded yet; call StartAsync or RunAsync first."
        );

    /// <summary>
    /// Discovers and loads the extension and starts the session: the load starts the extension's
    /// background services, then the session-started event fires once over the loaded extensions
    /// (the production composition point). Later calls do nothing.
    /// </summary>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loaded is not null)
        {
            return;
        }

        Runner.Discover(WorkingDirectory, _options.RepositoryIdentity);
        _loaded = await Runner
            .Load(
                _options.ExtensionId,
                WorkingDirectory,
                _options.RepositoryIdentity,
                Trust,
                cancellationToken
            )
            .ConfigureAwait(false);
        await Runner
            .StartSessionAsync(WorkingDirectory, _options.RepositoryIdentity, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the extension's child process now, before the first hook is dispatched. A remote
    /// extension otherwise spawns and initializes inside its first handler, which spends the
    /// handler's budget on process start — on a loaded runner that can exceed it. Returns false
    /// when the extension is in-process (nothing to warm).
    /// </summary>
    public async ValueTask<bool> WarmUpAsync(
        string extensionId = "",
        CancellationToken cancellationToken = default
    )
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        return await Runner
            .StartRemoteAsync(
                string.IsNullOrWhiteSpace(extensionId) ? _options.ExtensionId : extensionId,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the session, runs one prompt through the real harness and flushes file changes.
    /// Runs may repeat on one host — each call is one prompt in the same session; the flush makes
    /// this run's deliveries observable without ending them, and the one-way drain happens at
    /// <see cref="StopAsync"/> (session end), mirroring the production teardown.
    /// </summary>
    public async Task<ExtensionRunResult> RunAsync(
        IChatClient client,
        string prompt,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        await StartAsync(cancellationToken).ConfigureAwait(false);

        Session session = EnsureSession();
        var harnessOptions = new AgentHarnessOptions
        {
            MaxSteps = _options.MaxSteps,
            SystemPrompt = _options.SystemPrompt,
            WorkingDirectory = WorkingDirectory,
            SubagentLimits = _options.SubagentLimits ?? new SubagentLimits(),
            Approver = _options.Approver,
            Hooks = new AgentHookAdapter(Runner.Hooks),
            Session = session,
            FileChanges = _fileChanges,
            ModelRegistry = Runner.ModelRegistry,
        };
        if (_options.ConfigureHarness is { } configure)
        {
            harnessOptions = configure(harnessOptions);
        }

        var harness = new AgentHarness(client, Tools, harnessOptions);

        List<AgentEvent> events = [];
        await foreach (
            AgentEvent agentEvent in harness
                .RunAsync(prompt, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            Events.Emit(agentEvent);
            events.Add(agentEvent);
        }

        await _fileChanges.FlushAsync(cancellationToken).ConfigureAwait(false);
        return new ExtensionRunResult(events, session.Path);
    }

    /// <summary>
    /// Composes the loaded extension's registered tools into <see cref="Tools"/> in registration
    /// order — the production order: extension tools before MCP tools, so call this before
    /// <see cref="ComposeMcpServersAsync"/>. Tools already present are left alone. Throws before
    /// the extension was started.
    /// </summary>
    public IReadOnlyList<ExtensionToolAdapter> ComposeTools()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loaded is null)
        {
            throw new InvalidOperationException(
                "The extension is not loaded yet; call StartAsync or RunAsync first."
            );
        }

        IReadOnlyList<ExtensionToolAdapter> registered = Runner.Tools.List();
        foreach (ExtensionToolAdapter tool in registered)
        {
            if (Tools.Find(tool.Name) is null)
            {
                Tools.Add(tool);
            }
        }

        return registered;
    }

    /// <summary>
    /// Unloads the extension through the real loader: its tools leave the loader's registry, its
    /// in-flight calls are cancelled and <c>ToolsChanged</c> fires; the composed <see cref="Tools"/>
    /// entries are removed too. Idempotent.
    /// </summary>
    public async ValueTask UnloadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loaded is null)
        {
            return;
        }

        IReadOnlyList<ExtensionToolAdapter> registered = Runner.Tools.List();
        await Runner.Unload(_loaded.Id).ConfigureAwait(false);
        foreach (ExtensionToolAdapter tool in registered)
        {
            Tools.Remove(tool.Name);
        }
    }

    /// <summary>
    /// Composes the extension MCP-server registrations through the production mapping
    /// (<see cref="ExtensionMcpServers"/>): the first call lists every server once (starting each
    /// exactly once), merges the listed tools into <see cref="Tools"/> and returns the public
    /// statuses; later calls re-observe the composed set with bounded liveness probes (no restart,
    /// no duplicate tools) and return fresh statuses. The host owns the composed set:
    /// <see cref="StopAsync"/> and <see cref="DisposeAsync"/> stop the servers.
    /// </summary>
    public async Task<IReadOnlyList<ExtensionMcpServerStatus>> ComposeMcpServersAsync(
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await StartAsync(cancellationToken).ConfigureAwait(false);
        if (_mcpServers is null)
        {
            _mcpServers = new ExtensionMcpServers(Log, Runner.McpServers.List());
            IReadOnlyList<ITool> tools = await _mcpServers
                .ListToolsAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (ITool tool in tools)
            {
                Tools.Add(tool);
            }
        }
        else
        {
            await _mcpServers.ObserveStatusesAsync(cancellationToken).ConfigureAwait(false);
        }

        return [.. _mcpServers.Statuses.Select(ToPublicStatus)];
    }

    /// <summary>
    /// Ends the session through the real lifecycle: file changes drain one-way (the session-end
    /// final flush, before the session ends), so later notifications are dropped and logged; later
    /// calls do nothing.
    /// </summary>
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        if (_mcpServers is { } servers)
        {
            _mcpServers = null;
            await servers.DisposeAsync().ConfigureAwait(false);
        }

        await _fileChanges.DrainAsync(cancellationToken).ConfigureAwait(false);
        await Runner.EndSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops the session, unloads the extension and releases the file change bus.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        if (_loaded is not null)
        {
            await Runner.Unload(_loaded.Id).ConfigureAwait(false);
        }

        _fileChanges.Dispose();
    }

    private Session EnsureSession()
    {
        if (_session is not null)
        {
            return _session;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
        _session = File.Exists(SessionPath)
            ? Session.Load(SessionPath)
            : Session.Create(
                SessionPath,
                WorkingDirectory,
                _options.RepositoryIdentity,
                WorkingDirectory
            );
        return _session;
    }

    private static ExtensionMcpServerStatus ToPublicStatus(McpServerStatus status) =>
        new(
            status.Name,
            status.State switch
            {
                McpServerState.NotStarted => ExtensionMcpServerState.NotStarted,
                McpServerState.Started => ExtensionMcpServerState.Started,
                _ => ExtensionMcpServerState.Failed,
            },
            status.ToolCount,
            status.Reason,
            status.Source
        );

    private void InstallExtension(ExtensionTestHostOptions options)
    {
        Directory.CreateDirectory(ExtensionDirectory);
        if (
            options.ExtensionDirectory is { } source
            && !PathsEqual(Path.GetFullPath(source), ExtensionDirectory)
        )
        {
            CopyDirectory(Path.GetFullPath(source), ExtensionDirectory);
        }

        if (options.SourceFiles is { } files)
        {
            foreach ((string relativePath, string contents) in files)
            {
                string target = Path.Combine(ExtensionDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, contents);
            }
        }

        if (!File.Exists(Path.Combine(ExtensionDirectory, "extension.json")))
        {
            throw new ArgumentException(
                $"The extension to test has no extension.json under '{ExtensionDirectory}'; provide one through ExtensionDirectory or SourceFiles.",
                nameof(options)
            );
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (
            string directory in Directory.EnumerateDirectories(
                source,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            Directory.CreateDirectory(
                Path.Combine(destination, Path.GetRelativePath(source, directory))
            );
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );
}
