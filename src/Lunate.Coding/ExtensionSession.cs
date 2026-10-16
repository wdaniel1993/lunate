using System.Diagnostics;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

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

    /// <summary>The session catalog the model registry falls back to; null disables the fallback.</summary>
    public ModelCatalog? ModelCatalog { get; init; }

    /// <summary>The client factory the model registry uses; null leaves those paths teaching errors.</summary>
    public IChatClientFactory? ClientFactory { get; init; }

    /// <summary>
    /// True skips composing the extension host entirely (the <c>--no-extensions</c> flag):
    /// extension hooks, renderers, widgets and extension MCP servers are absent. Configured
    /// <c>mcp.json</c> servers are unaffected.
    /// </summary>
    public bool NoExtensions { get; init; }

    /// <summary>
    /// True composes in register-only inspect mode (the <c>lunate extensions</c> path): trusted
    /// extensions load and register, but nothing starts — no session-start hooks, no background
    /// services and no <c>ToolsChanged</c> dispatch (design D3). Never prompts beyond the seam's
    /// default.
    /// </summary>
    public bool Inspect { get; init; }
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

/// <summary>The four contribution counts of one extension (the <c>/extensions</c> view's count row).</summary>
internal readonly record struct ExtensionCounts(int Tools, int Commands, int Hooks, int Services);

/// <summary>
/// The composed extension host of one session: it discovers the store at session start, loads each
/// extension with per-extension failure isolation, exposes the contributions the frontends wire
/// (hooks, renderers, widgets, model providers, MCP servers) and stops cleanly on disposal. A
/// discovery or load failure is logged through the host log and never aborts the session.
/// </summary>
internal sealed class ExtensionSession : IAsyncDisposable
{
    private readonly ExtensionLoader _loader;
    private readonly IExtensionLog _log;
    private readonly FileChangeBus _fileChanges;
    private readonly AgentHookAdapter _hooks;
    private ExtensionMcpServers? _mcpServers;
    private bool _disposed;

    private ExtensionSession(
        ExtensionLoader loader,
        IExtensionLog log,
        FileChangeBus fileChanges,
        IReadOnlyList<ExtensionSessionError> errors,
        IReadOnlyList<ExtensionLoadResult> loadResults
    )
    {
        _loader = loader;
        _log = log;
        _fileChanges = fileChanges;
        _hooks = new AgentHookAdapter(loader.Hooks);
        Errors = errors;
        LoadResults = loadResults;
    }

    /// <summary>Loads and composes the host; per-extension failures are isolated, not thrown.</summary>
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
        var loader = new ExtensionLoader(
            new ExtensionHostOptions
            {
                StorePath = seams.StorePath ?? ExtensionHostOptions.Default.StorePath,
                Log = log,
                ModelCatalog = seams.ModelCatalog,
                ClientFactory = seams.ClientFactory,
            }
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
                // Cancellation mid-compose: stop whatever already started (earlier extensions'
                // background services) and release the bus before surfacing; the host is never
                // returned.
                fileChanges.Dispose();
                await loader.EndSessionAsync(CancellationToken.None).ConfigureAwait(false);
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

        return new ExtensionSession(loader, log, fileChanges, errors, loadResults);
    }

    /// <summary>The harness hook seam over the loaded extensions' hook runner.</summary>
    internal AgentHookAdapter Hooks => _hooks;

    /// <summary>The session's file change bus (the <c>core/file-bus</c> handle); the modes hand it to the harness.</summary>
    internal FileChangeBus FileChanges => _fileChanges;

    /// <summary>
    /// The loaded extensions' registered tools, in registration order; composition adds them before
    /// the MCP tools, so extension tools come first (design D7).
    /// </summary>
    internal IReadOnlyList<ITool> Tools => [.. _loader.Tools.List()];

    /// <summary>The raw hook runner behind the input pipeline.</summary>
    internal HookRunner HookRunner => _loader.Hooks;

    /// <summary>The named service registry (core handles included).</summary>
    internal ServiceRegistry Services => _loader.Services;

    /// <summary>The loaded extensions' tool-result renderers.</summary>
    internal ToolResultRendererRegistry ToolResultRenderers => _loader.ToolResultRenderers;

    /// <summary>The loaded extensions' status widgets.</summary>
    internal StatusWidgetRegistry StatusWidgets => _loader.StatusWidgets;

    /// <summary>The live model registry of the loaded extensions.</summary>
    internal ModelRegistry ModelRegistry => _loader.ModelRegistry;

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

    /// <summary>The discovery and load failures this session continued past.</summary>
    internal IReadOnlyList<ExtensionSessionError> Errors { get; }

    /// <summary>
    /// One compose outcome per discovered descriptor, in discovery order (the
    /// <c>/extensions</c> view's data; the <c>lunate extensions</c> path included).
    /// </summary>
    internal IReadOnlyList<ExtensionLoadResult> LoadResults { get; }

    /// <summary>
    /// The registered contribution counts of one extension — the view's counts: tools from the
    /// tool registry, hooks and services from their registries; registered commands are always 0
    /// (no registration API exists, design D5).
    /// </summary>
    internal ExtensionCounts RegisteredCounts(string extensionId) =>
        new(
            _loader
                .Tools.List()
                .Count(tool =>
                    string.Equals(tool.ExtensionId, extensionId, StringComparison.Ordinal)
                ),
            0,
            _loader.Hooks.Count(extensionId),
            _loader.Services.Count(extensionId)
        );

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
