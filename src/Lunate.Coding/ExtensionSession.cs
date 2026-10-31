using System.Diagnostics;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Contracts;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn.BuiltIn;

namespace Lunate.Coding;

/// <summary>
/// The injectable seams of a session's extension host. Production leaves them null and takes the
/// defaults (the <c>~/.lunate</c> store, a mode-specific trust prompt, the mode's stderr log);
/// tests pass a temp store, a scripted prompt and a recording log.
/// </summary>
internal sealed record ExtensionSeams
{
    /// <summary>The extension store root; null uses <c>~/.lunate</c>.</summary>
    public string? StorePath { get; init; }

    /// <summary>The project-extension trust prompt; null picks the mode default.</summary>
    public IExtensionTrustPrompt? Prompt { get; init; }

    /// <summary>Where extension diagnostics are reported; null writes to the mode's stderr.</summary>
    public IExtensionLog? Log { get; init; }

    /// <summary>The one-line user notice sink (a remote extension crash); null logs only.</summary>
    public Action<string>? Notice { get; init; }

    /// <summary>
    /// The extensions the host composes out of its own assemblies (manifest plus factory), in
    /// composition order, before anything found in the store; empty by default. A
    /// <c>--no-extensions</c> session composes none of these.
    /// </summary>
    public IReadOnlyList<BuiltInExtension>? BuiltIns { get; init; }

    /// <summary>
    /// True composes the built-in set but nothing from the store (the <c>lunate mcp roslyn</c>
    /// composition): no store scan, no trust, no third-party load. Unlike
    /// <see cref="NoExtensions"/>, the built-ins still compose.
    /// </summary>
    public bool BuiltInsOnly { get; init; }

    /// <summary>The host's per-workspace service ceiling, which may lower built-in manifest caps.</summary>
    public int? WorkspaceServiceCapLimit { get; init; }

    /// <summary>The session catalog the model registry falls back to; null disables the fallback.</summary>
    public ModelCatalog? ModelCatalog { get; init; }

    /// <summary>The agent decision primitive exposed to extensions as <c>core/decision</c>.</summary>
    public IAgentDecisionService? AgentDecisionService { get; init; }

    /// <summary>The client factory the model registry uses; null leaves those paths teaching errors.</summary>
    public IChatClientFactory? ClientFactory { get; init; }

    /// <summary>
    /// True composes no extensions at all (the <c>--no-extensions</c> flag): built-ins included,
    /// plus no store scan, no trust and no third-party load. Configured <c>mcp.json</c> servers are
    /// unaffected. The flag says what it means, so nothing the host owns composes under it.
    /// </summary>
    public bool NoExtensions { get; init; }

    /// <summary>
    /// True composes in register-only inspect mode (the <c>lunate extensions</c> path): trusted
    /// extensions load and register, but nothing starts — no session-start hooks, no background
    /// services and no <c>ToolsChanged</c> dispatch (design D3). Never prompts beyond the seam's
    /// default.
    /// </summary>
    public bool Inspect { get; init; }

    /// <summary>
    /// The single-file restore engine factory; null uses the production NuGet-backed engine. It is
    /// a factory because the engine must not exist until a restore runs (no <c>NuGet.*</c> assembly
    /// in a session whose extensions are all built); tests pass a fake so no test needs a feed.
    /// </summary>
    public Func<ISingleFileRestoreEngine?>? RestoreEngine { get; init; }

    /// <summary>
    /// The single-file compile factory; null uses the Roslyn-backed compiler. A factory for the same
    /// reason: the compiler assemblies are loaded by the first build, never by composing a session.
    /// </summary>
    public Func<ISingleFileCompiler?>? Compiler { get; init; }

    /// <summary>
    /// Where locked packages and the reference pack are read from; null uses NuGet's own folder
    /// (<c>NUGET_PACKAGES</c>, else <c>~/.nuget/packages</c>). A build and the load of its result
    /// must agree on this folder, so composing a session hands one value to both; a test points it
    /// at a temporary folder so no run reads the user's cache.
    /// </summary>
    public string? GlobalPackagesFolder { get; init; }
}

/// <summary>A discovery or load failure the session reported and continued past.</summary>
internal sealed record ExtensionSessionError(string? ExtensionId, string Message);

/// <summary>The compose outcome of one discovered extension (design D1).</summary>
internal enum ExtensionLoadState
{
    Loaded,
    TrustDenied,
    Failed,
}

/// <summary>
/// One discovered extension's compose outcome: the state, how long the load path took and the
/// failure message when it was denied or failed (the <c>/extensions</c> view's data).
/// </summary>
internal sealed record ExtensionLoadResult(
    string Id,
    ExtensionLoadState State,
    TimeSpan Elapsed,
    string? Message
);

/// <summary>The four contribution counts of one extension plus the declared commands no source
/// provided and the command ids that shadowed its own (the <c>/extensions</c> view's count row).</summary>
internal readonly record struct ExtensionCounts(int Tools, int Commands, int Hooks, int Services)
{
    /// <summary>Manifest commands the extension declared but no source provided; not runnable.</summary>
    public IReadOnlyList<string> DeclaredNotProvidedCommands { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The ids that own a name this extension also offered, so its command is shadowed and not
    /// live (the registry's first-source-wins rule; the view reads <c>shadowed by &lt;id&gt;</c>).
    /// </summary>
    public IReadOnlyList<string> ShadowedCommandOwners { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The composed extension host of one session: it discovers the store at session start, loads each
/// extension with per-extension failure isolation, exposes the contributions the frontends wire
/// (hooks, renderers, widgets, model providers, MCP servers) and stops cleanly on disposal. A
/// discovery or load failure is logged through the host log and never aborts the session.
/// </summary>
internal sealed class ExtensionSession : IAsyncDisposable
{
    private static IReadOnlyList<BuiltInExtension> DefaultBuiltIns { get; } =
        RoslynBuiltInFactory
            .BuiltInFactories.Select(factory => new BuiltInExtension(
                factory.ManifestPath,
                factory.Manifest,
                factory.Factory
            ))
            .ToArray();

    internal static IReadOnlyList<string> CoreToolNames { get; } =
    ["read", "write", "edit", "bash", "grep", "find", "ls", "decide"];

    private readonly ExtensionLoader _loader;
    private readonly IExtensionLog _log;
    private readonly FileChangeBus _fileChanges;
    private readonly AgentHookAdapter _hooks;
    private readonly SingleFileExtensionRestorer _singleFileRestorer;
    private readonly SingleFileExtensionBuilder _singleFileBuilder;
    private readonly IExtensionTrustPrompt _prompt;
    private readonly Dictionary<string, List<ExtensionInactiveTool>> _inactiveTools = new(
        StringComparer.Ordinal
    );
    private ExtensionMcpServers? _mcpServers;
    private bool _disposed;

    private ExtensionSession(
        ExtensionLoader loader,
        IDecisionService? decisionService,
        IExtensionLog log,
        FileChangeBus fileChanges,
        SingleFileExtensionRestorer singleFileRestorer,
        SingleFileExtensionBuilder singleFileBuilder,
        IExtensionTrustPrompt prompt,
        IReadOnlyList<ExtensionSessionError> errors,
        IReadOnlyList<ExtensionLoadResult> loadResults
    )
    {
        _loader = loader;
        DecisionService = decisionService;
        _log = log;
        _fileChanges = fileChanges;
        _singleFileRestorer = singleFileRestorer;
        _singleFileBuilder = singleFileBuilder;
        _prompt = prompt;
        _hooks = new AgentHookAdapter(loader.Hooks);
        Errors = errors;
        LoadResults = loadResults;
    }

    /// <summary>
    /// Loads and composes the host; per-extension failures are isolated, not thrown. After the
    /// per-descriptor load loop the session-started event fires once, so every registered handler
    /// observes the full extension set; inspect mode fires nothing.
    /// </summary>
    internal static async Task<ExtensionSession> StartAsync(
        WorkspaceInfo workspace,
        ExtensionSeams seams,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace.WorktreeRoot);
        ArgumentNullException.ThrowIfNull(seams);

        string workingDirectory = workspace.WorktreeRoot;
        string repositoryIdentity = workspace.GitCommonDir ?? workspace.WorktreeRoot;
        IExtensionLog log = seams.Log ?? new StderrExtensionLog(Console.Error);
        IExtensionTrustPrompt prompt = seams.Prompt ?? new NonInteractiveTrustPrompt(log);
        var options = new ExtensionHostOptions
        {
            // A composed host knows its workspace, so this is the one place every extension
            // composition — interactive, print, ACP, a build subcommand — refuses a store the
            // repository itself could write to.
            StorePath =
                seams.StorePath
                ?? LunateHome.StoreOutsideWorkspace(workspace.WorktreeRoot, workspace.RepoRoot),
            Log = log,
            Notify = seams.Notice,
            ModelCatalog = seams.ModelCatalog,
            ClientFactory = seams.ClientFactory,
            GlobalPackagesFolder = seams.GlobalPackagesFolder,
            ReservedCommands = Commands.BuiltinNames,
            ReservedToolNames = CoreToolNames,
            // --no-extensions disables the flag's namesake entirely, built-ins included; the
            // built-ins-only composition (mcp roslyn) keeps them and still skips the store.
            BuiltIns = seams.NoExtensions ? [] : seams.BuiltIns ?? DefaultBuiltIns,
            BuiltInsOnly = seams.NoExtensions || seams.BuiltInsOnly,
            WorkspaceServiceCapLimit = seams.WorkspaceServiceCapLimit,
        };
        var loader = new ExtensionLoader(options);
        IDecisionService? decisionService = null;
        if (seams.AgentDecisionService is { } agentDecisionService)
        {
            decisionService = new DecisionServiceAdapter(agentDecisionService);
            loader.Services.RegisterCore("core/decision", decisionService);
        }

        // Created eagerly, but it only holds the factory: nothing NuGet-shaped exists until a
        // restore runs, which is why composing an all-built session loads no NuGet assembly.
        var singleFileRestorer = new SingleFileExtensionRestorer(
            options,
            seams.RestoreEngine ?? SingleFileRestoreFactory.Engine
        );
        // The build service both the CLI trigger and `/extensions reload` call: restore when the
        // lock cannot answer, compile against that lock, keep the build. Also lazy in the same way —
        // it holds two factories and touches neither until a build runs.
        var singleFileBuilder = new SingleFileExtensionBuilder(
            options,
            seams.RestoreEngine ?? SingleFileRestoreFactory.Engine,
            seams.Compiler ?? SingleFileCompileFactory.Compiler
        );
        // The session owns one bus, created before any extension loads so a subscriber
        // registering during load is subscribed for the whole session. The mutation queue
        // handle wraps the queue the core tools use, so extension mutations serialize with them.
        var fileChanges = new FileChangeBus(workspace.WorktreeRoot, log);
        loader.Services.RegisterCore("core/workspace", workspace);
        loader.Services.RegisterCore("core/file-bus", fileChanges);
        loader.Services.RegisterCore(
            "core/mutation-queue",
            new MutationQueueHandle(FileMutationQueue.Shared)
        );
        loader.Services.RegisterCore("core/process", new ProcessRunner());
        List<ExtensionSessionError> errors = [];

        loader.Discover(workingDirectory, repositoryIdentity);
        foreach (ExtensionDiscoveryError discovery in loader.DiscoveryErrors)
        {
            log.Warn(discovery.Message);
            errors.Add(new ExtensionSessionError(null, discovery.Message));
        }

        List<ExtensionLoadResult> loadResults = [];

        // Cancellation mid-compose (during a load or the session-started dispatch): stop whatever
        // already started (earlier extensions' background services) and release the bus before
        // surfacing; the host is never returned.
        async Task CleanUpAfterCancellationAsync()
        {
            fileChanges.Dispose();
            await loader.EndSessionAsync(CancellationToken.None).ConfigureAwait(false);
        }

        foreach (ExtensionDescriptor descriptor in loader.Descriptors)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await loader
                    .Load(
                        descriptor.Id,
                        workingDirectory,
                        repositoryIdentity,
                        prompt,
                        ct,
                        startSession: !seams.Inspect
                    )
                    .ConfigureAwait(false);
                loadResults.Add(
                    new ExtensionLoadResult(
                        descriptor.Id,
                        ExtensionLoadState.Loaded,
                        stopwatch.Elapsed,
                        null
                    )
                );
            }
            catch (OperationCanceledException)
            {
                await CleanUpAfterCancellationAsync().ConfigureAwait(false);
                throw;
            }
            catch (ExtensionTrustDeniedException exception)
            {
                log.Warn(exception.Message);
                errors.Add(new ExtensionSessionError(descriptor.Id, exception.Message));
                loadResults.Add(
                    new ExtensionLoadResult(
                        descriptor.Id,
                        ExtensionLoadState.TrustDenied,
                        stopwatch.Elapsed,
                        exception.Message
                    )
                );
            }
            catch (ExtensionLoadException exception)
            {
                log.Error(exception.Message);
                errors.Add(new ExtensionSessionError(descriptor.Id, exception.Message));
                loadResults.Add(
                    new ExtensionLoadResult(
                        descriptor.Id,
                        ExtensionLoadState.Failed,
                        stopwatch.Elapsed,
                        exception.Message
                    )
                );
            }
        }

        // The session-started event fires once, here — after every discovered extension has
        // loaded — so every registered handler observes it with the full extension set. Inspect
        // mode loads nothing into a session and fires nothing (design D2).
        if (!seams.Inspect)
        {
            try
            {
                await loader
                    .StartSessionAsync(workingDirectory, repositoryIdentity, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation during the session-started dispatch: same cleanup as a cancelled
                // load — the dispatch runs after the loop, so its cancellation must stop what
                // already started and release the bus before surfacing.
                await CleanUpAfterCancellationAsync().ConfigureAwait(false);
                throw;
            }
        }

        return new ExtensionSession(
            loader,
            decisionService,
            log,
            fileChanges,
            singleFileRestorer,
            singleFileBuilder,
            prompt,
            errors,
            loadResults
        );
    }

    /// <summary>The harness hook seam over the loaded extensions' hook runner.</summary>
    internal AgentHookAdapter Hooks => _hooks;

    /// <summary>The configured core decision service; null when no usable decision profile exists.</summary>
    internal IDecisionService? DecisionService { get; }

    /// <summary>The session's file change bus (the <c>core/file-bus</c> handle); the modes hand it to the harness.</summary>
    internal FileChangeBus FileChanges => _fileChanges;

    /// <summary>The extensions' manifest-matched write verifiers.</summary>
    internal WriteVerifierRegistry WriteVerifiers => _loader.WriteVerifiers;

    /// <summary>
    /// The loaded extensions' registered tools, in registration order; composition adds them before
    /// the MCP tools, so extension tools come first (design D7).
    /// </summary>
    internal IReadOnlyList<ITool> Tools => [.. _loader.Tools.List()];

    /// <summary>
    /// Filters the extension tools down to the active ones at <paramref name="workspace"/>'s root —
    /// the one activation evaluation of a composition — and records the withheld tools per
    /// extension with their condition and reason, the <c>/extensions</c> view's data. A later call
    /// (the next composition) replaces the record; a mid-session root change waits for it.
    /// </summary>
    internal IReadOnlyList<ITool> ComposeTools(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        (IReadOnlyList<ITool> active, IReadOnlyList<ExtensionInactiveTool> inactive) =
            ExtensionToolActivationFilter.Split(_loader.Tools.List(), workspace);
        _inactiveTools.Clear();
        foreach (
            IGrouping<string, ExtensionInactiveTool> group in inactive.GroupBy(
                report => report.ExtensionId,
                StringComparer.Ordinal
            )
        )
        {
            _inactiveTools[group.Key] = [.. group];
        }

        return active;
    }

    internal static bool RoslynIsActive(
        string workspaceRoot,
        IReadOnlyList<BuiltInExtension>? builtIns = null
    )
    {
        BuiltInExtension? roslyn = (builtIns ?? DefaultBuiltIns).FirstOrDefault(builtIn =>
            builtIn.Manifest.Id == "roslyn"
        );
        return roslyn is not null
            && roslyn.Manifest.ToolActivation.TryGetValue("*", out var rule)
            && ToolActivationEvaluator.Evaluate(rule, workspaceRoot);
    }

    /// <summary>
    /// Whether a tool registered by this host carries a manifest activation rule and therefore
    /// must be recomposed for a child workspace instead of inherited from the parent root.
    /// </summary>
    internal bool IsWorkspaceBoundTool(ITool tool) =>
        _loader
            .Tools.List()
            .OfType<ExtensionToolAdapter>()
            .Any(adapter => ReferenceEquals(adapter, tool) && adapter.ActivationRule is not null);

    /// <summary>
    /// The tools of one extension the composition withheld at the run root, each with the
    /// <c>inactive (&lt;condition&gt;): &lt;reason&gt;</c> report; empty when none or before the
    /// first composition.
    /// </summary>
    internal IReadOnlyList<ExtensionInactiveTool> InactiveTools(string extensionId) =>
        _inactiveTools.TryGetValue(extensionId, out List<ExtensionInactiveTool>? reports)
            ? [.. reports]
            : [];

    /// <summary>The raw hook runner behind the input pipeline.</summary>
    internal HookRunner HookRunner => _loader.Hooks;

    /// <summary>Where extension diagnostics are reported.</summary>
    internal IExtensionLog Log => _log;

    /// <summary>The loaded extensions' registered skill sources, each with the extension's own
    /// folder as its boundary (the skills discovery input).</summary>
    internal IReadOnlyList<ExtensionSkillSource> SkillContributions() =>
        [
            .. _loader
                .SkillSources.List()
                .Select(registered =>
                {
                    ExtensionDescriptor? descriptor = _loader.Descriptors.FirstOrDefault(
                        candidate =>
                            string.Equals(
                                candidate.Id,
                                registered.ExtensionId,
                                StringComparison.Ordinal
                            )
                    );
                    // A built-in has no own folder to bound a contributed skill directory to, so
                    // it contributes the source only through a real folder — a built-in cannot
                    // name one, and none does.
                    return descriptor?.Directory is { } directory
                        ? new ExtensionSkillSource(
                            registered.ExtensionId,
                            registered.Source.Id,
                            directory,
                            registered.Source.SkillDirectories
                        )
                        : null;
                })
                .OfType<ExtensionSkillSource>(),
        ];

    /// <summary>The loaded extensions' registered command sources with their load-time
    /// descriptors, in registration order (the session registry's extension slot).</summary>
    internal IReadOnlyList<ICommandSource> CommandSources() =>
        [
            .. _loader
                .CommandSources.List()
                .Select(registered => new ExtensionCommandSource(
                    registered.ExtensionId,
                    registered.Source,
                    registered.Descriptors
                )),
        ];

    /// <summary>The named service registry (core handles included).</summary>
    internal ServiceRegistry Services => _loader.Services;

    /// <summary>The loaded extensions' tool-result renderers.</summary>
    internal ToolResultRendererRegistry ToolResultRenderers => _loader.ToolResultRenderers;

    /// <summary>The loaded extensions' status widgets.</summary>
    internal StatusWidgetRegistry StatusWidgets => _loader.StatusWidgets;

    /// <summary>The live model registry of the loaded extensions.</summary>
    internal ModelRegistry ModelRegistry => _loader.ModelRegistry;

    /// <summary>The collectible contexts currently owned by this session's extension loader.</summary>
    internal int LoadContextCount => _loader.LoadContexts.Count;

    /// <summary>
    /// The extension MCP mapping built by <see cref="ComposeMcpServers"/>; null until composed.
    /// </summary>
    internal ExtensionMcpServers? McpServers => _mcpServers;

    /// <summary>
    /// The registrations skipped by <see cref="ComposeMcpServers"/> because a configured server
    /// owns the name; `/mcp` shows them as shadowed by the configured source. Empty until composed.
    /// </summary>
    internal IReadOnlyList<ShadowedMcpServer> ShadowedMcpServers { get; private set; } = [];

    /// <summary>
    /// Builds the MCP mapping over the loaded registrations: a registration whose name matches a
    /// configured server is skipped before listing (the configured server wins) and the collision
    /// is logged naming both sources; null or no configured servers keeps every registration.
    /// Idempotent; the mapping starts nothing until its first listing.
    /// </summary>
    internal ExtensionMcpServers ComposeMcpServers(
        IReadOnlyDictionary<string, string?>? configuredServers
    )
    {
        if (_mcpServers is { } existing)
        {
            return existing;
        }

        (IReadOnlyList<RegisteredMcpServer> kept, IReadOnlyList<ShadowedMcpServer> shadowed) =
            McpNameCollisions.Filter(_loader.McpServers.List(), configuredServers);
        foreach (ShadowedMcpServer entry in shadowed)
        {
            _log.Warn(
                $"extension MCP server '{entry.Name}' from extension '{entry.ExtensionId}' was skipped: the configured server '{entry.Name}' from {(entry.ConfiguredSource is { Length: > 0 } source ? $"'{source}'" : "the mcp.json config")} wins the name collision."
            );
        }

        ShadowedMcpServers = shadowed;
        _mcpServers = new ExtensionMcpServers(_log, kept);
        return _mcpServers;
    }

    /// <summary>Every discovered descriptor, loaded or not.</summary>
    internal IReadOnlyList<ExtensionDescriptor> Descriptors => _loader.Descriptors;

    /// <summary>
    /// Every discovered single-file candidate, global before project; a refused file is listed
    /// with its reason rather than dropped (the <c>/extensions</c> view's data).
    /// </summary>
    internal IReadOnlyList<SingleFileExtension> SingleFileExtensions =>
        _loader.SingleFileExtensions;

    /// <summary>
    /// Re-reads the single-file candidates of this session's roots, so a file the model wrote or
    /// edited mid-session is what <c>/extensions</c> lists and what a reload acts on. Reads small
    /// files only: no restore, no compile, no prompt.
    /// </summary>
    internal IReadOnlyList<SingleFileExtension> RefreshSingleFileExtensions() =>
        _loader.RefreshSingleFileExtensions();

    /// <summary>
    /// The session's single-file restorer: the compile step and <c>/extensions reload</c> ask it for
    /// a lock before they build anything. Composing a session never calls it.
    /// </summary>
    internal SingleFileExtensionRestorer SingleFileRestorer => _singleFileRestorer;

    /// <summary>
    /// The session's single-file builder: the one service a build or a reload goes through, so the
    /// order — trust, lock, cache, compiler — is the same everywhere.
    /// </summary>
    internal SingleFileExtensionBuilder SingleFileBuilder => _singleFileBuilder;

    /// <summary>
    /// What the load of one extension ignored because the host owns it (a package's or the
    /// directory's copy of the contract), or null when it shipped none: the row and the JSON's note.
    /// </summary>
    internal IgnoredHostCopy? IgnoredHostCopyOf(string extensionId) =>
        _loader.IgnoredHostCopyOf(extensionId);

    /// <summary>
    /// Reloads one single-file extension (<c>/extensions reload &lt;id&gt;</c>): the session's builder
    /// and the session's own trust prompt, so the question the preflight asks is the one this session
    /// has been asking all along. The candidates are re-read first, because the file may have been
    /// written or edited since composition — a model-written file is the case this exists for. The
    /// old build stays loaded when trust or the build refuses.
    /// </summary>
    internal ValueTask<SingleFileReloadOutcome> ReloadSingleFileAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        _loader.RefreshSingleFileExtensions();
        return new(
            _loader.ReloadSingleFileAsync(id, _singleFileBuilder, _prompt, cancellationToken)
        );
    }

    /// <summary>The discovery and load failures this session continued past.</summary>
    internal IReadOnlyList<ExtensionSessionError> Errors { get; }

    /// <summary>
    /// One compose outcome per discovered descriptor, in discovery order (the
    /// <c>/extensions</c> view's data; the <c>lunate extensions</c> path included).
    /// </summary>
    internal IReadOnlyList<ExtensionLoadResult> LoadResults { get; }

    /// <summary>
    /// The registered contribution counts of one extension — the view's counts: tools from the
    /// tool registry, commands from its registered command sources, hooks and services from their
    /// registries, plus the manifest commands no source provided.
    /// </summary>
    internal ExtensionCounts RegisteredCounts(
        string extensionId,
        IReadOnlyList<CommandShadow>? shadowed = null
    ) =>
        new(
            _loader
                .Tools.List()
                .Count(tool =>
                    string.Equals(tool.ExtensionId, extensionId, StringComparison.Ordinal)
                ),
            _loader
                .CommandSources.List()
                .Where(registered =>
                    string.Equals(registered.ExtensionId, extensionId, StringComparison.Ordinal)
                )
                .Sum(registered => registered.Descriptors.Count),
            _loader.Hooks.Count(extensionId),
            _loader.Services.Count(extensionId)
        )
        {
            DeclaredNotProvidedCommands = _loader.DeclaredNotProvidedCommands(extensionId),
            ShadowedCommandOwners = shadowed is null
                ? Array.Empty<string>()
                :
                [
                    .. shadowed
                        .Where(entry =>
                            string.Equals(entry.SourceId, extensionId, StringComparison.Ordinal)
                        )
                        .Select(entry => entry.OwnerId),
                ],
        };

    /// <summary>
    /// The per-hook timing snapshot of one extension — the view's data: empty when the extension
    /// dispatched nothing.
    /// </summary>
    internal IReadOnlyList<HookTimings> HookTimings(string extensionId) =>
        _loader.Hooks.Snapshot(extensionId);

    /// <summary>
    /// The most recent start + handshake duration of one loaded remote extension; null when it is
    /// not remote or never started.
    /// </summary>
    internal TimeSpan? RemoteStartDuration(string extensionId) =>
        _loader.RemoteStartDuration(extensionId);

    /// <summary>
    /// Starts the loaded remote extensions whose manifest declares the <c>background</c> startup
    /// mode; the interactive session calls this only after the UI is ready.
    /// </summary>
    internal ValueTask StartBackgroundAsync(CancellationToken cancellationToken = default) =>
        _loader.StartBackgroundExtensionsAsync(cancellationToken);

    /// <summary>
    /// Restarts one loaded remote extension (the <c>/extensions restart &lt;id&gt;</c> path);
    /// false when the id is not a loaded remote extension.
    /// </summary>
    internal ValueTask<bool> RestartExtensionAsync(
        string id,
        CancellationToken cancellationToken = default
    ) => _loader.RestartRemoteAsync(id, cancellationToken);

    /// <summary>The failed remote extensions and their status, in descriptor order.</summary>
    internal IReadOnlyList<(string Id, RemoteExtensionStatus Status)> FailedRemoteStatuses() =>
        [
            .. _loader
                .Descriptors.Select(descriptor =>
                    (descriptor.Id, Status: _loader.RemoteStatus(descriptor.Id))
                )
                .Where(entry => entry.Status is { IsFailed: true })
                .Select(entry => (entry.Id, entry.Status!)),
        ];

    /// <summary>
    /// Releases one loaded extension and its collectible context (the register-only
    /// <c>lunate extensions</c> teardown); unloading stops its background services when they
    /// started, which inspect mode never did. <paramref name="notifyToolsChanged"/> false unloads
    /// silently, skipping the <c>ToolsChanged</c> dispatch (design D3).
    /// </summary>
    internal ValueTask UnloadAsync(string id, bool notifyToolsChanged = true) =>
        _loader.Unload(id, notifyToolsChanged);

    /// <summary>
    /// Drains (and then disposes) the file change bus, runs the session-ending hooks and stops the
    /// MCP mappings; later calls do nothing. The drain delivers what was enqueued, each dispatch
    /// bounded by the handler timeout, so a slow handler cannot hang teardown.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await _fileChanges.DrainAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _fileChanges.Dispose();
        }

        await _loader.EndSessionAsync().ConfigureAwait(false);
        if (_mcpServers is { } servers)
        {
            _mcpServers = null;
            await servers.DisposeAsync().ConfigureAwait(false);
        }
    }
}
