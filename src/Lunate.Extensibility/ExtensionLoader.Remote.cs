using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility;

public sealed partial class ExtensionLoader
{
    private readonly Dictionary<string, RemoteExtension> _remoteExtensions = new(
        StringComparer.Ordinal
    );

    /// <summary>
    /// Loads one remote extension: trust (the same per-repository content hash over the manifest
    /// and the extension directory), a <see cref="RemoteExtension"/> that builds declaration-driven
    /// proxies and starts nothing, and — when the session starts — this extension's background
    /// services. The session-started event is not fired by a load: the composition fires it once,
    /// after every extension loaded at session start has loaded (design D1).
    /// </summary>
    private async Task<LoadedExtension> LoadRemoteAsync(
        ExtensionDescriptor descriptor,
        string workingDirectory,
        CancellationToken cancellationToken,
        bool startSession
    )
    {
        if (
            !_backgroundServices.Services.TryGetCore<IProcessRunner>(
                "core/process",
                out IProcessRunner? runner
            ) || runner is null
        )
        {
            throw new ExtensionLoadException(
                $"extension '{descriptor.Id}': the manifest declares a runtime, but the host has no core/process capability; the extension was not loaded."
            );
        }

        WorkspaceInfo workspace =
            _backgroundServices.Services.TryGetCore<WorkspaceInfo>(
                "core/workspace",
                out WorkspaceInfo? registered
            ) && registered is not null
                ? registered
                : new WorkspaceInfo(Path.GetFullPath(workingDirectory), null, null);

        IExtensionSecrets secrets = ExtensionSecretsStore.Load(_options, descriptor.Id);
        var remote = new RemoteExtension(
            new RemoteExtensionOptions
            {
                Manifest = descriptor.Manifest,
                Directory = descriptor.Directory,
                Workspace = workspace,
                ProcessRunner = runner,
                Context = CreateExtensionContext(descriptor, secrets),
                Notify = _options.Notify,
                UnregisterHook = handler => _hooks.Unregister(descriptor.Id, handler),
            }
        );
        _remoteExtensions[descriptor.Id] = remote;
        if (startSession && descriptor.Manifest.Commands.Count > 0)
        {
            await StartForDeclaredCommandsAsync(remote, descriptor).ConfigureAwait(false);
        }

        await CrossCheckManifestCommandsAsync(descriptor, cancellationToken).ConfigureAwait(false);
        var loaded = new LoadedExtension(descriptor.Id, remote, descriptor);
        _loaded[descriptor.Id] = loaded;
        if (startSession)
        {
            if (!_sessionEnded)
            {
                _sessionBegan = true;
                await _backgroundServices.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return loaded;
    }

    /// <summary>
    /// Starts one remote extension before its manifest commands are cross-checked, because the
    /// capability that decides the command listing (`commands: true` in <c>initialize</c>) only
    /// exists after the handshake: a declared command is either offered and dispatchable, or
    /// reported as declared, not provided — never offered and dead. Contained: a child that
    /// cannot start leaves the extension's commands declarations and the load continues. The
    /// register-only inspect mode passes <c>startSession: false</c> and starts nothing.
    /// </summary>
    private async Task StartForDeclaredCommandsAsync(
        RemoteExtension remote,
        ExtensionDescriptor descriptor
    )
    {
        try
        {
            await remote.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (_options.Log ?? NullExtensionLog.Instance).Warn(
                $"extension '{descriptor.Id}': the child could not start while its declared commands were being listed: {exception.Message} Its commands are reported as declared, not provided."
            );
        }
    }

    /// <summary>
    /// Starts the loaded remote extensions whose manifest declares the <c>background</c> startup
    /// mode. The interactive session calls this only after the UI is ready; discovery, load and
    /// the startup path never start a child.
    /// </summary>
    public async ValueTask StartBackgroundExtensionsAsync(
        CancellationToken cancellationToken = default
    )
    {
        // Marking the session live first delivers a deferred session-started to a subscriber
        // (starting its child here, post-ready, never on the startup path); background children
        // start next, and both paths are idempotent.
        foreach (RemoteExtension extension in _remoteExtensions.Values)
        {
            await extension.MarkSessionLiveAsync(cancellationToken).ConfigureAwait(false);
        }

        RemoteExtension[] background =
        [
            .. _remoteExtensions.Values.Where(extension => extension.StartsInBackground),
        ];
        foreach (RemoteExtension extension in background)
        {
            await extension.StartInBackgroundAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Starts one loaded remote extension now, instead of on its first hook: the child process
    /// spawn and <c>initialize</c> happen here, outside any handler budget. Returns false when the
    /// id is not a loaded remote extension. Idempotent, like every other start path.
    /// </summary>
    public async ValueTask<bool> StartRemoteAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (!_remoteExtensions.TryGetValue(id, out RemoteExtension? extension) || extension is null)
        {
            return false;
        }

        await extension.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Restarts one loaded remote extension by id (the <c>/extensions</c> manual-restart path);
    /// returns false when the id is not a loaded remote extension.
    /// </summary>
    public async ValueTask<bool> RestartRemoteAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!_remoteExtensions.TryGetValue(id, out RemoteExtension? extension))
        {
            return false;
        }

        await extension.RestartAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>The status of one loaded remote extension, or null when it is not one.</summary>
    public RemoteExtensionStatus? RemoteStatus(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _remoteExtensions.TryGetValue(id, out RemoteExtension? extension)
            ? extension.Status
            : null;
    }

    /// <summary>
    /// The most recent start + handshake duration of one loaded remote extension, or null when it
    /// is not remote or never started.
    /// </summary>
    public TimeSpan? RemoteStartDuration(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _remoteExtensions.TryGetValue(id, out RemoteExtension? extension)
            ? extension.LastStartDuration
            : null;
    }

    private async ValueTask ShutdownRemotesAsync(CancellationToken cancellationToken)
    {
        foreach (RemoteExtension extension in _remoteExtensions.Values)
        {
            await extension.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
