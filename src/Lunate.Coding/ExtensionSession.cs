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

    /// <summary>
    /// True skips composing the extension host entirely (the <c>--no-extensions</c> flag):
    /// extension hooks, renderers, widgets and extension MCP servers are absent. Configured
    /// <c>mcp.json</c> servers are unaffected.
    /// </summary>
    public bool NoExtensions { get; init; }
}

/// <summary>A discovery or load failure the session reported and continued past.</summary>
internal sealed record ExtensionSessionError(string? ExtensionId, string Message);

/// <summary>
/// The composed extension host of one session: it discovers the store at session start, loads each
/// extension with per-extension failure isolation, exposes the contributions the frontends wire
/// (hooks, renderers, widgets, model providers, MCP servers) and stops cleanly on disposal. A
/// discovery or load failure is logged through the host log and never aborts the session.
/// </summary>
internal sealed class ExtensionSession : IAsyncDisposable
{
    private readonly ExtensionLoader _loader;
    private readonly ExtensionMcpServers _mcpServers;
    private readonly AgentHookAdapter _hooks;
    private bool _disposed;

    private ExtensionSession(
        ExtensionLoader loader,
        ExtensionMcpServers mcpServers,
        IReadOnlyList<ExtensionSessionError> errors
    )
    {
        _loader = loader;
        _mcpServers = mcpServers;
        _hooks = new AgentHookAdapter(loader.Hooks);
        Errors = errors;
    }

    /// <summary>Loads and composes the host; per-extension failures are isolated, not thrown.</summary>
    internal static async Task<ExtensionSession> StartAsync(
        string workingDirectory,
        string repositoryIdentity,
        ExtensionSeams seams,
        CancellationToken ct = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);
        ArgumentNullException.ThrowIfNull(seams);

        IExtensionLog log = seams.Log ?? new StderrExtensionLog(Console.Error);
        IExtensionTrustPrompt prompt = seams.Prompt ?? new NonInteractiveTrustPrompt(log);
        var loader = new ExtensionLoader(
            new ExtensionHostOptions
            {
                StorePath = seams.StorePath ?? ExtensionHostOptions.Default.StorePath,
                Log = log,
            }
        );
        List<ExtensionSessionError> errors = [];

        loader.Discover(workingDirectory, repositoryIdentity);
        foreach (ExtensionDiscoveryError discovery in loader.DiscoveryErrors)
        {
            log.Warn(discovery.Message);
            errors.Add(new ExtensionSessionError(null, discovery.Message));
        }

        foreach (ExtensionDescriptor descriptor in loader.Descriptors)
        {
            try
            {
                await loader
                    .Load(descriptor.Id, workingDirectory, repositoryIdentity, prompt, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation mid-compose: stop whatever already started (earlier extensions'
                // background services) before surfacing; the host is never returned.
                await loader.EndSessionAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (ExtensionTrustDeniedException exception)
            {
                log.Warn(exception.Message);
                errors.Add(new ExtensionSessionError(descriptor.Id, exception.Message));
            }
            catch (ExtensionLoadException exception)
            {
                log.Error(exception.Message);
                errors.Add(new ExtensionSessionError(descriptor.Id, exception.Message));
            }
        }

        return new ExtensionSession(
            loader,
            new ExtensionMcpServers(log, loader.McpServers.List()),
            errors
        );
    }

    /// <summary>The harness hook seam over the loaded extensions' hook runner.</summary>
    internal AgentHookAdapter Hooks => _hooks;

    /// <summary>The raw hook runner behind the input pipeline.</summary>
    internal HookRunner HookRunner => _loader.Hooks;

    /// <summary>The loaded extensions' tool-result renderers.</summary>
    internal ToolResultRendererRegistry ToolResultRenderers => _loader.ToolResultRenderers;

    /// <summary>The loaded extensions' status widgets.</summary>
    internal StatusWidgetRegistry StatusWidgets => _loader.StatusWidgets;

    /// <summary>The loaded extensions' model provider declarations; no consumer yet.</summary>
    internal ModelProviderRegistry ModelProviders => _loader.ModelProviders;

    /// <summary>The loaded extensions' MCP servers; registration starts nothing until listing.</summary>
    internal ExtensionMcpServers McpServers => _mcpServers;

    /// <summary>Every discovered descriptor, loaded or not.</summary>
    internal IReadOnlyList<ExtensionDescriptor> Descriptors => _loader.Descriptors;

    /// <summary>The discovery and load failures this session continued past.</summary>
    internal IReadOnlyList<ExtensionSessionError> Errors { get; }

    /// <summary>Runs the session-ending hooks and stops the MCP mappings; later calls do nothing.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _loader.EndSessionAsync().ConfigureAwait(false);
        await _mcpServers.DisposeAsync().ConfigureAwait(false);
    }
}
