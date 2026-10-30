using System.Runtime.Loader;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility;

public sealed partial class ExtensionLoader
{
    private readonly ExtensionHostOptions _options;
    private readonly List<ExtensionDescriptor> _descriptors = [];
    private readonly Dictionary<string, ExtensionDescriptor> _descriptorsById = new(
        StringComparer.Ordinal
    );
    private readonly List<ExtensionDiscoveryError> _discoveryErrors = [];
    private readonly Dictionary<string, LoadedExtension> _loaded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BuiltInExtension> _builtInById = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, AssemblyLoadContext> _contexts = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, ExtensionContext> _extensionContexts = new(
        StringComparer.Ordinal
    );
    private readonly HookRunner _hooks;
    private readonly BackgroundServiceHost _backgroundServices;
    private readonly ModelRegistry _modelRegistry;
    private readonly ToolResultRendererRegistry _toolResultRenderers;
    private readonly StatusWidgetRegistry _statusWidgets;
    private readonly McpServerRegistry _mcpServers;
    private readonly SkillSourceRegistry _skillSources;
    private readonly CommandSourceRegistry _commandSources;
    private readonly ExtensionToolRegistry _tools;
    private readonly WriteVerifierRegistry _writeVerifiers = new();
    private readonly ExtensionWorkspaceServiceRegistry _workspaceServices;
    private readonly Dictionary<string, List<IDisposable>> _subscriptions = new(
        StringComparer.Ordinal
    );
    private string? _globalSingleFileRoot;
    private string? _projectSingleFileRoot;
    private InstalledPackages? _installed;
    private bool _sessionBegan;
    private bool _sessionStarted;
    private bool _sessionEnded;
    private SessionStartedPayload? _session;

    public ExtensionLoader(ExtensionHostOptions options)
        : this(options, null) { }

    public ExtensionLoader(ExtensionHostOptions options, HookRunner? hooks)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _tools = new ExtensionToolRegistry(options.ReservedToolNames);
        _hooks = hooks ?? new HookRunner(null, options.Log);
        _backgroundServices = new BackgroundServiceHost(options.Log);
        _toolResultRenderers = new ToolResultRendererRegistry(options.Log);
        _statusWidgets = new StatusWidgetRegistry(options.Log);
        _mcpServers = new McpServerRegistry(options.Log);
        _skillSources = new SkillSourceRegistry(options.Log);
        _commandSources = new CommandSourceRegistry(options.Log, options.Notify);
        _modelRegistry = new ModelRegistry(options.ModelCatalog, options.ClientFactory);
        _workspaceServices = new ExtensionWorkspaceServiceRegistry(
            options.WorkspaceServiceCapLimit
        );
        foreach (BuiltInExtension builtIn in options.BuiltIns)
        {
            // The supplied list is a host invariant; the first entry wins and the fold's refusal
            // path (design §4) reports anything the composition cannot keep.
            _builtInById.TryAdd(builtIn.Manifest.Id, builtIn);
        }
    }

    /// <summary>The hook runner handlers registered through extension contexts dispatch to.</summary>
    public HookRunner Hooks => _hooks;

    /// <summary>The tool-result renderers the loaded extensions registered.</summary>
    public ToolResultRendererRegistry ToolResultRenderers => _toolResultRenderers;

    /// <summary>The status widgets the loaded extensions registered.</summary>
    public StatusWidgetRegistry StatusWidgets => _statusWidgets;

    /// <summary>The MCP servers the loaded extensions registered.</summary>
    public McpServerRegistry McpServers => _mcpServers;

    /// <summary>The skill sources the loaded extensions registered.</summary>
    public SkillSourceRegistry SkillSources => _skillSources;

    /// <summary>The command sources the loaded extensions registered, in registration order.</summary>
    public CommandSourceRegistry CommandSources => _commandSources;

    /// <summary>The tools the loaded extensions registered, in registration order.</summary>
    public ExtensionToolRegistry Tools => _tools;

    /// <summary>The manifest-matched read-only verifiers registered by loaded extensions.</summary>
    public WriteVerifierRegistry WriteVerifiers => _writeVerifiers;

    /// <summary>The workspace services the loaded extensions registered, per (extension, name).</summary>
    internal ExtensionWorkspaceServiceRegistry WorkspaceServices => _workspaceServices;

    /// <summary>
    /// The named service registry shared with extensions (core handles included). Register core
    /// handles before loading extensions: subscriptions made at creation time resolve then.
    /// </summary>
    public ServiceRegistry Services => _backgroundServices.Services;

    /// <summary>The live model registry of the loaded extensions (declarations included).</summary>
    public ModelRegistry ModelRegistry => _modelRegistry;

    public IReadOnlyList<ExtensionDescriptor> Descriptors => [.. _descriptors];

    /// <summary>
    /// What <c>&lt;root&gt;/packages/</c> says this machine installed: the trust digest of an
    /// installed extension binds the package it came from (design D7). Read once per loader, because
    /// installs are a separate command and never race a session.
    /// </summary>
    private InstalledPackages Installed =>
        _installed ??= InstalledPackages.Read(_options.StorePath);

    /// <summary>The collectible load contexts of the loaded extensions, by extension id.</summary>
    internal IReadOnlyDictionary<string, AssemblyLoadContext> LoadContexts => _contexts;

    /// <summary>Whether one id is loaded right now (test and reload paths read it).</summary>
    internal bool IsLoaded(string id) => _loaded.ContainsKey(id);

    public IReadOnlyList<ExtensionDiscoveryError> DiscoveryErrors => [.. _discoveryErrors];

    public IReadOnlyList<ExtensionDescriptor> Discover(
        string workingDirectory,
        string repositoryIdentity
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(repositoryIdentity);

        _descriptors.Clear();
        _descriptorsById.Clear();
        _discoveryErrors.Clear();
        _singleFile.Clear();

        FoldBuiltIns();
        if (_options.BuiltInsOnly)
        {
            // The --no-extensions composition: no store scan, no single-file candidates, so no
            // third-party extension is ever discovered, trusted or loaded (design §1).
            return Descriptors;
        }

        ScanDirectory(_options.ExtensionsPath, ExtensionScope.Global);
        string projectExtensions = Path.Combine(
            Path.GetFullPath(workingDirectory),
            ".lunate",
            "extensions"
        );
        ScanDirectory(projectExtensions, ExtensionScope.Project);
        _globalSingleFileRoot = _options.ExtensionsPath;
        _projectSingleFileRoot = projectExtensions;
        DiscoverSingleFileExtensions(_options.ExtensionsPath, projectExtensions, _discoveryErrors);

        return Descriptors;
    }

    /// <summary>
    /// Re-reads the single-file candidates of the roots the last <see cref="Discover"/> scanned, so
    /// a file the model wrote or edited during the session is what <c>/extensions</c> lists and what
    /// a reload acts on — discovery-time is not the last word once a session can author a file. It
    /// reads a handful of small files and nothing else: no manifest rescan, no restore, no compile,
    /// no prompt. The rows carry each candidate's state and reason, so a refusal is still visible;
    /// the composition's discovery errors are not re-reported, because they belong to session start.
    /// </summary>
    public IReadOnlyList<SingleFileExtension> RefreshSingleFileExtensions()
    {
        if (_globalSingleFileRoot is not { } global || _projectSingleFileRoot is not { } project)
        {
            return SingleFileExtensions;
        }

        _singleFile.Clear();
        DiscoverSingleFileExtensions(global, project, [], warnAboutHeaders: false);

        return SingleFileExtensions;
    }

    /// <summary>
    /// Unloads one extension and removes its contributions. <paramref name="notifyToolsChanged"/>
    /// false unloads silently — the register-only inspect teardown (design D3) — skipping the
    /// <c>ToolsChanged</c> dispatch. A built-in cannot be unloaded: its code lives in the host's
    /// own assemblies and there is no context to release; the message teaches that.
    /// </summary>
    public async ValueTask Unload(string id, bool notifyToolsChanged = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (
            _descriptorsById.TryGetValue(id, out ExtensionDescriptor? descriptor)
            && descriptor.Scope is ExtensionScope.Builtin
        )
        {
            throw new ExtensionLoadException(
                $"extension '{id}' is a built-in; built-ins load with the host and cannot be reloaded or unloaded."
            );
        }

        if (!_loaded.Remove(id))
        {
            return;
        }

        _singleFileKeys.Remove(id);
        _ignoredCopies.Remove(id);
        IReadOnlyList<string> removedTools = _tools.Unregister(id);
        _writeVerifiers.Unregister(id);
        _workspaceServices.Unregister(id);
        _hooks.Unregister(id);
        await _backgroundServices.DropAsync(id).ConfigureAwait(false);
        _modelRegistry.Unregister(id);
        _toolResultRenderers.Unregister(id);
        _statusWidgets.Unregister(id);
        _mcpServers.Unregister(id);
        _skillSources.Unregister(id);
        // Cancel, notify, drain, then unload (design D3): a handler must not keep running against
        // a host that already reports its extension gone, and one that ignores its token must not
        // hold teardown open past the hook-handler budget. The drain is bounded, not cancellable:
        // a half-waited drain would unload the context under a live handler.
        await _commandSources
            .CancelInFlightAsync(id, _hooks.Options.HandlerTimeout, CancellationToken.None)
            .ConfigureAwait(false);
        _commandSources.Unregister(id);
        _declaredNotProvidedCommands.Remove(id);
        DropSubscriptions(id);
        if (_contexts.Remove(id, out AssemblyLoadContext? context))
        {
            context.Unload();
        }

        if (_remoteExtensions.Remove(id, out RemoteExtension? remote))
        {
            await remote.DisposeAsync().ConfigureAwait(false);
        }

        // The drain has ended or timed out: a handler that outlived its extension now throws
        // ObjectDisposedException on its next host call, which the command lease contains and logs
        // with the command id (sign-off Q2). Marked after the remote teardown so a remote host's
        // own dispose path can still log.
        if (_extensionContexts.Remove(id, out ExtensionContext? extensionContext))
        {
            extensionContext.MarkDisposed();
        }

        if (notifyToolsChanged && removedTools.Count > 0)
        {
            await NotifyToolsChangedAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fires <c>ToolsChanged</c> with the extension tool names now registered. Add and remove
    /// happen outside a run, so the payload carries an empty run id.
    /// </summary>
    private ValueTask NotifyToolsChangedAsync() =>
        _hooks.RunToolsChangedAsync(
            new ToolsChangedPayload(string.Empty, [.. _tools.List().Select(tool => tool.Name)])
        );

    private void SubscribeFileChanged(
        string extensionId,
        IFileChangedHandler handler,
        string? pathPattern
    )
    {
        if (
            !_backgroundServices.Services.TryGetCore<IFileChangeBus>(
                "core/file-bus",
                out IFileChangeBus? bus
            ) || bus is null
        )
        {
            (_options.Log ?? NullExtensionLog.Instance).Warn(
                $"extension '{extensionId}' subscribed to file changes, but no core/file-bus service is registered; the subscription will not receive events."
            );
            return;
        }

        IDisposable subscription = bus.Subscribe(handler, pathPattern);
        if (!_subscriptions.TryGetValue(extensionId, out List<IDisposable>? subscriptions))
        {
            subscriptions = [];
            _subscriptions[extensionId] = subscriptions;
        }

        subscriptions.Add(subscription);
    }

    private void DropSubscriptions(string extensionId)
    {
        if (_subscriptions.Remove(extensionId, out List<IDisposable>? subscriptions))
        {
            foreach (IDisposable subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }
    }

    /// <summary>
    /// Ends the session once, when one began: the session-ending hooks run only when the
    /// session-started event fired, while the extensions' background services and the remote
    /// extensions stop whenever a load started them (a compose cancelled before the event still
    /// stops the earlier extensions' services). Calls before anything started and later calls do
    /// nothing.
    /// </summary>
    public async ValueTask EndSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_sessionEnded || (!_sessionStarted && !_sessionBegan))
        {
            return;
        }

        _sessionEnded = true;
        // A quit cancels in-flight commands silently: the notice is for a user who is still
        // looking at the session, and the TUI is already torn down by then (sign-off Q2).
        foreach (string loadedId in _loaded.Keys.ToArray())
        {
            await _commandSources
                .CancelInFlightAsync(
                    loadedId,
                    _hooks.Options.HandlerTimeout,
                    notify: false,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }

        if (_sessionStarted && _session is { } session)
        {
            await _hooks
                .RunSessionEndingAsync(
                    new SessionEndingPayload(session.WorkingDirectory, session.RepositoryIdentity),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        await _backgroundServices.StopAsync(cancellationToken).ConfigureAwait(false);
        await ShutdownRemotesAsync(cancellationToken).ConfigureAwait(false);
        _workspaceServices.EndSession();

        // The contexts are marked disposed only now: a session-ending hook or a background
        // service's stop still makes host calls, and those must not throw (design D3, sign-off Q2).
        foreach (string loadedId in _extensionContexts.Keys.ToArray())
        {
            if (_extensionContexts.Remove(loadedId, out ExtensionContext? extensionContext))
            {
                extensionContext.MarkDisposed();
            }
        }
    }

    /// <summary>
    /// Fires <c>SessionStarted</c> once per session, after every extension discovered at session
    /// start has loaded, so every registered handler observes it with the full extension set. A
    /// later-loaded extension does not observe it; later calls do nothing.
    /// </summary>
    public async ValueTask StartSessionAsync(
        string workingDirectory,
        string repositoryIdentity,
        CancellationToken cancellationToken = default
    )
    {
        if (_sessionStarted)
        {
            return;
        }

        // The flag is set before the dispatch: a cancelled or failed dispatch still counts as
        // fired, so no retry can double-fire the handlers (the composition cleans up and never
        // retries; a later-loaded extension is documented as not observing the event).
        _sessionStarted = true;
        _session = new SessionStartedPayload(workingDirectory, repositoryIdentity);
        await _hooks.RunSessionStartedAsync(_session, cancellationToken).ConfigureAwait(false);
    }

    private static string DiscoveredIds(IReadOnlyList<ExtensionDescriptor> descriptors) =>
        descriptors.Count == 0
            ? "none"
            : string.Join(", ", descriptors.Select(descriptor => descriptor.Id));

    private void ScanDirectory(string root, ExtensionScope scope)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        IEnumerable<string> directories = Directory
            .EnumerateDirectories(root)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal);

        foreach (string directory in directories)
        {
            string name = Path.GetFileName(directory);
            if (name.StartsWith('.'))
            {
                continue;
            }

            string manifestPath = Path.Combine(directory, "extension.json");
            if (!File.Exists(manifestPath))
            {
                _discoveryErrors.Add(
                    new ExtensionDiscoveryError(
                        directory,
                        $"extension directory '{directory}' has no extension.json manifest."
                    )
                );
                continue;
            }

            ExtensionManifest manifest;
            try
            {
                manifest = ExtensionManifest.Parse(File.ReadAllText(manifestPath), manifestPath);
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or InvalidDataException
                )
            {
                string message =
                    exception is InvalidDataException
                        ? exception.Message
                        : $"extension manifest '{manifestPath}' could not be read: {exception.Message}";
                _discoveryErrors.Add(new ExtensionDiscoveryError(manifestPath, message));
                continue;
            }

            if (ExtensionApiGate.RefuseRange(manifest.Id, manifest.ApiVersion) is { } apiRefusal)
            {
                _discoveryErrors.Add(new ExtensionDiscoveryError(manifestPath, apiRefusal));
                continue;
            }

            if (manifest.BuiltIn)
            {
                // A directory manifest cannot claim built-in provenance (spec scenario); the field
                // is the host's, and the discovery error names the path nothing loaded from.
                _discoveryErrors.Add(
                    new ExtensionDiscoveryError(
                        manifestPath,
                        $"{manifestPath}: field 'builtin' of a discovered manifest cannot be enabled; built-ins are supplied by the host."
                    )
                );
                continue;
            }

            if (_descriptorsById.TryGetValue(manifest.Id, out ExtensionDescriptor? existing))
            {
                _discoveryErrors.Add(
                    existing.Scope is ExtensionScope.Builtin
                        ? new ExtensionDiscoveryError(
                            manifestPath,
                            $"extension '{manifest.Id}' was skipped: the id is supplied by the host as a built-in ('{_builtInById[manifest.Id].ManifestPath}'), the discovered manifest ('{manifestPath}') is not loaded, and a built-in is never shadowed."
                        )
                        : new ExtensionDiscoveryError(
                            manifestPath,
                            $"extension '{manifest.Id}' is declared more than once: '{existing.Directory}' and '{directory}'."
                        )
                );
                continue;
            }

            var descriptor = new ExtensionDescriptor(
                manifest.Id,
                manifest.Version,
                scope,
                directory,
                manifest
            );
            _descriptors.Add(descriptor);
            _descriptorsById[manifest.Id] = descriptor;
        }
    }
}
