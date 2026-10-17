using System.Globalization;
using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote;

/// <summary>
/// The out-of-process host adapter for one remote extension: declaration-driven hook and service
/// proxies (built without starting anything), a child started lazily on first use (or in
/// <c>background</c> mode after the UI is ready), the <c>initialize</c> handshake with version and
/// surface checks, hook and service bridging over the NDJSON wire, and the crash/restart and
/// session-end lifecycle. One instance serves the whole session; every call carries the run ids
/// and the workspace.
/// </summary>
public sealed partial class RemoteExtension : IExtension, IAsyncDisposable
{
    private readonly RemoteExtensionOptions _options;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly object _gate = new();
    private RemoteWire? _wire;
    private RemoteProcessChannel? _channel;
    private RemoteExtensionStatus _status = new(RemoteExtensionState.NotStarted, null);
    private int _automaticRestarts;
    private bool _shuttingDown;
    private bool _disposed;

    public RemoteExtension(RemoteExtensionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Manifest);
        ArgumentNullException.ThrowIfNull(options.Context);
        if (options.Manifest.Runtime is null)
        {
            throw new ArgumentException(
                "RemoteExtension requires a manifest with a 'runtime' declaration.",
                nameof(options)
            );
        }

        _options = options;
        RegisterProxies();
    }

    /// <summary>The extension id.</summary>
    public string Id => _options.Manifest.Id;

    /// <summary>The current lifecycle status.</summary>
    public RemoteExtensionStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>True when the manifest declares the <c>background</c> startup mode.</summary>
    public bool StartsInBackground => _options.Manifest.Runtime!.StartsInBackground;

    /// <summary>
    /// The host → child call timeout: the manifest's <c>callTimeout</c> or the default.
    /// </summary>
    public TimeSpan CallTimeout =>
        _options.Manifest.Runtime!.CallTimeout ?? _options.DefaultCallTimeout;

    /// <summary>
    /// Starts the child and performs the handshake when not already ready; lazy first use, the
    /// background startup path and manual restarts all funnel here. A refusal or an
    /// already-failed extension throws a teaching error instead of starting again.
    /// </summary>
    public async ValueTask<RemoteWire> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (TryGetReadyWire(out RemoteWire? ready) && ready is not null)
        {
            return ready;
        }

        lock (_gate)
        {
            if (_status.IsFailed)
            {
                throw new RemoteExtensionException(
                    $"extension '{Id}' is {_status.State.ToString().ToLowerInvariant()}: {_status.Reason}"
                );
            }
        }

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetReadyWire(out ready) && ready is not null)
            {
                return ready;
            }

            lock (_gate)
            {
                if (_status.IsFailed)
                {
                    throw new RemoteExtensionException(
                        $"extension '{Id}' is {_status.State.ToString().ToLowerInvariant()}: {_status.Reason}"
                    );
                }

                _status = new RemoteExtensionStatus(RemoteExtensionState.Starting, null);
            }

            return await StartCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <summary>
    /// Starts a <c>background</c>-mode extension after the UI is ready. Failures are contained:
    /// they log, set the failed status and never throw (the caller is the ready UI).
    /// </summary>
    public async ValueTask StartInBackgroundAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            _options.Context.Log.Info(
                $"extension '{Id}': started in the background after the UI was ready."
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _options.Context.Log.Warn(
                $"extension '{Id}': the background start failed: {Explain(exception)}"
            );
        }
    }

    /// <summary>
    /// Restarts the extension manually (the <c>/extensions</c> path): stops the current child and
    /// starts a fresh one, clearing a failed status. Automatic restarts are not granted anew.
    /// </summary>
    public async ValueTask RestartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await RestartCoreAsync(cancellationToken).ConfigureAwait(false);
        _options.Context.Log.Info($"extension '{Id}': restarted manually.");
    }

    /// <summary>Clears a failed status, stops the old child and starts a fresh one.</summary>
    private async ValueTask RestartCoreAsync(CancellationToken cancellationToken)
    {
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopChildAsync(CancellationToken.None).ConfigureAwait(false);
            _shuttingDown = false;
            _status = new RemoteExtensionStatus(RemoteExtensionState.NotStarted, null);
        }
        finally
        {
            _startGate.Release();
        }

        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends <c>shutdown</c>, waits the bounded grace for the child to exit, then kills the tree.
    /// Idempotent; callers do nothing to an extension that never started.
    /// </summary>
    public async ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _shuttingDown = true;
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopChildAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_status.State is not RemoteExtensionState.Failed)
                {
                    _status = new RemoteExtensionStatus(RemoteExtensionState.Stopped, null);
                }
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        _startGate.Dispose();
    }

    private bool TryGetReadyWire(out RemoteWire? wire)
    {
        lock (_gate)
        {
            wire = _status.State is RemoteExtensionState.Ready ? _wire : null;
            return wire is not null;
        }
    }

    private async Task<RemoteWire> StartCoreAsync(CancellationToken cancellationToken)
    {
        ExtensionManifest manifest = _options.Manifest;
        ExtensionRuntime runtime = manifest.Runtime!;
        string command = ResolveCommand(runtime.Command);
        IReadOnlyList<string> arguments = [.. runtime.Arguments.Select(ResolveArgument)];
        string workingDirectory = runtime.WorkingDirectory is { } declared
            ? ResolvePath(declared)
            : _options.Directory;
        IReadOnlyDictionary<string, string> environment = EnvironmentScrubber.Build(
            set: runtime.Environment,
            passthrough: runtime.EnvironmentPassthrough
        );

        RemoteProcessChannel channel;
        try
        {
            channel = await RemoteProcessChannel
                .StartAsync(
                    _options.ProcessRunner,
                    new ProcessSessionRequest(
                        command,
                        arguments,
                        workingDirectory,
                        environment,
                        MaxLineBytes: _options.MaxLineBytes
                    ),
                    Id,
                    _options.Context.Log,
                    _options.MaxLogBytes,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception
                    is System.ComponentModel.Win32Exception
                        or IOException
                        or InvalidOperationException
                        or PlatformNotSupportedException
                        or ArgumentException
            )
        {
            throw Fail(
                $"could not start '{command}': {Explain(exception)}. Check the runtime command in the extension manifest; the extension was not started.",
                exception
            );
        }

        var wire = new RemoteWire(
            channel,
            new RemoteWireOptions
            {
                DefaultCallTimeout = CallTimeout,
                MaxLineBytes = _options.MaxLineBytes,
                Log = _options.Context.Log,
            }
        );
        wire.RequestReceived += OnRequestAsync;
        wire.ProtocolFailed += exception =>
        {
            _options.Context.Log.Error(
                $"extension '{Id}': the wire protocol failed: {exception.Message}"
            );
            lock (_gate)
            {
                _status = new RemoteExtensionStatus(RemoteExtensionState.Failed, exception.Message);
            }
        };

        lock (_gate)
        {
            _wire = wire;
            _channel = channel;
        }

        wire.Start();
        try
        {
            await HandshakeAsync(wire, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await StopChildAsync(CancellationToken.None).ConfigureAwait(false);
            if (exception is RemoteExtensionException)
            {
                throw;
            }

            throw Fail(
                $"the handshake failed: {Explain(exception)}. The extension was stopped.",
                exception
            );
        }

        _ = WatchClosedAsync(wire, channel);
        lock (_gate)
        {
            _status = new RemoteExtensionStatus(RemoteExtensionState.Ready, null);
        }

        return wire;
    }

    private async Task HandshakeAsync(RemoteWire wire, CancellationToken cancellationToken)
    {
        ExtensionManifest manifest = _options.Manifest;
        IReadOnlyList<HookContract> declared = DeclaredHooks();
        var request = new InitializeRequest(
            RemoteProtocol.Version,
            RemoteProtocol.Capabilities,
            JsonSerializer.SerializeToElement(manifest, WireJson.Options),
            _options.Workspace,
            [.. declared.Select(contract => contract.Name)],
            manifest.Services
        );

        JsonElement? result = await wire.CallAsync(
                RemoteProtocol.InitializeMethod,
                JsonSerializer.SerializeToElement(request, WireJson.Options),
                timeout: _options.InitializeTimeout,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (result is null)
        {
            throw Refuse("the initialize call answered with no result.");
        }

        InitializeResult answer;
        try
        {
            answer =
                result.Value.Deserialize<InitializeResult>(WireJson.Options)
                ?? throw Refuse("the initialize result was null.");
        }
        catch (JsonException exception)
        {
            throw Refuse($"the initialize result was malformed: {exception.Message}");
        }

        if (answer.ProtocolVersion != RemoteProtocol.Version)
        {
            throw Refuse(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"it speaks protocol {answer.ProtocolVersion} but this host speaks {RemoteProtocol.Version}"
                )
            );
        }

        HashSet<string> declaredHooks = [.. declared.Select(contract => contract.Name)];
        foreach (string implemented in answer.Hooks)
        {
            if (!declaredHooks.Contains(implemented))
            {
                throw Refuse(
                    $"it implements hook '{implemented}' which the manifest does not declare"
                );
            }
        }

        HashSet<string> declaredServices = [.. manifest.Services];
        foreach (string implemented in answer.Services)
        {
            if (!declaredServices.Contains(implemented))
            {
                throw Refuse(
                    $"it implements service '{implemented}' which the manifest does not declare"
                );
            }
        }

        HashSet<string> implementedHooks = [.. answer.Hooks];
        List<HookContract> missing =
        [
            .. declared.Where(contract => !implementedHooks.Contains(contract.Name)),
        ];
        foreach (HookContract contract in missing)
        {
            _options.Context.Log.Warn(
                $"extension '{Id}': the manifest declares hook '{contract.Name}' but the child does not implement it; the declaration is dropped from the offered set."
            );
        }

        HashSet<string> implementedServices = [.. answer.Services];
        List<string> missingServices =
        [
            .. manifest.Services.Where(name => !implementedServices.Contains(name)),
        ];
        foreach (string name in missingServices)
        {
            _options.Context.Log.Warn(
                $"extension '{Id}': the manifest declares service '{name}' but the child does not implement it; the declaration is dropped from the offered set."
            );
        }

        SetMissingSurface(missing, missingServices);
    }

    /// <summary>
    /// Watches the child's end: a crash fails in-flight calls (the wire's close does that), logs
    /// one notice and attempts at most one automatic restart per session. A shutdown ends quietly.
    /// </summary>
    private async Task WatchClosedAsync(RemoteWire wire, RemoteProcessChannel channel)
    {
        try
        {
            await wire.Closed.ConfigureAwait(false);
        }
        catch (Exception) { }

        bool stale;
        bool shuttingDown;
        lock (_gate)
        {
            stale = !ReferenceEquals(_wire, wire);
            shuttingDown = _shuttingDown;
            if (!stale)
            {
                _wire = null;
                _channel = null;
            }
        }

        await channel.DisposeAsync().ConfigureAwait(false);
        if (stale || shuttingDown || _disposed)
        {
            return;
        }

        lock (_gate)
        {
            if (_status.State is RemoteExtensionState.Refused)
            {
                return;
            }

            _status = new RemoteExtensionStatus(
                RemoteExtensionState.Failed,
                "the extension process exited"
            );
        }

        _options.Context.Log.Error(
            $"extension '{Id}' failed: the extension process exited. The session continues without it; restart it from /extensions."
        );
        _options.Notify?.Invoke(
            $"extension '{Id}' failed: the extension process exited. Restart it from /extensions."
        );

        if (Interlocked.CompareExchange(ref _automaticRestarts, 1, 0) != 0)
        {
            return;
        }

        _options.Context.Log.Info($"extension '{Id}': restarting once automatically.");
        try
        {
            await RestartCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _options.Context.Log.Error(
                $"extension '{Id}': the automatic restart failed: {Explain(exception)} Let the session continue without it; restart it manually from /extensions."
            );
        }
    }

    private async ValueTask StopChildAsync(CancellationToken cancellationToken)
    {
        RemoteWire? wire;
        RemoteProcessChannel? channel;
        lock (_gate)
        {
            wire = _wire;
            channel = _channel;
            _wire = null;
            _channel = null;
        }

        if (wire is null || channel is null)
        {
            return;
        }

        try
        {
            if (!wire.IsClosed)
            {
                using var grace = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                grace.CancelAfter(_options.ShutdownGrace);
                try
                {
                    await wire.CallAsync(
                            RemoteProtocol.ShutdownMethod,
                            null,
                            _options.ShutdownGrace,
                            grace.Token
                        )
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The child may exit without answering; the grace and the kill below decide.
                }

                try
                {
                    await channel.Session.WaitForExitAsync(grace.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The grace expired (or the wait was cancelled): kill the tree.
                }

                if (!channel.Session.HasExited)
                {
                    _options.Context.Log.Warn(
                        $"extension '{Id}': the process did not stop within the grace; it is being killed."
                    );
                    await channel.Session.KillAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await wire.DisposeAsync().ConfigureAwait(false);
            await channel.DisposeAsync().ConfigureAwait(false);
        }
    }

    private RemoteExtensionException Refuse(string reason)
    {
        lock (_gate)
        {
            _status = new RemoteExtensionStatus(RemoteExtensionState.Refused, $"{reason}.");
        }

        var exception = new RemoteExtensionException(
            $"extension '{Id}' was refused: {reason}. The session continues without it."
        );
        _options.Context.Log.Error(exception.Message);
        _options.Notify?.Invoke(exception.Message);
        return exception;
    }

    private RemoteExtensionException Fail(string reason, Exception inner)
    {
        lock (_gate)
        {
            if (_status.State is not RemoteExtensionState.Refused)
            {
                _status = new RemoteExtensionStatus(RemoteExtensionState.Failed, reason);
            }
        }

        var exception = new RemoteExtensionException($"extension '{Id}': {reason}", inner);
        _options.Context.Log.Error(exception.Message);
        return exception;
    }

    private string ResolveCommand(string command) =>
        LooksLikePath(command) ? ResolvePath(command) : command;

    /// <summary>
    /// Resolves a relative argument against the extension directory. An explicitly relative path
    /// (<c>./</c> or <c>../</c>) is the author's intent and resolves even when the file is not
    /// there yet — the spawn error then names the resolved path. Any other token resolves only
    /// when it names an existing entry under the directory; a token that merely contains a
    /// separator (a script body, a pattern) is passed verbatim, and the child's working directory
    /// is the extension directory, so a path it creates later resolves the same way.
    /// </summary>
    private string ResolveArgument(string argument)
    {
        if (Path.IsPathRooted(argument))
        {
            return argument;
        }

        if (
            argument.StartsWith("./", StringComparison.Ordinal)
            || argument.StartsWith(".\\", StringComparison.Ordinal)
            || argument.StartsWith("../", StringComparison.Ordinal)
            || argument.StartsWith("..\\", StringComparison.Ordinal)
        )
        {
            return ResolvePath(argument);
        }

        if (!LooksLikePath(argument))
        {
            return argument;
        }

        string candidate = ResolvePath(argument);
        return File.Exists(candidate) || Directory.Exists(candidate) ? candidate : argument;
    }

    private static bool LooksLikePath(string token) =>
        token.StartsWith('.') || token.Contains('/') || token.Contains('\\');

    private string ResolvePath(string path) =>
        Path.GetFullPath(Path.Combine(_options.Directory, path));

    private static string Explain(Exception exception) =>
        exception.InnerException is { } inner && exception.Message.Length < 40
            ? $"{exception.GetType().Name}: {inner.Message}"
            : exception.Message;
}

/// <summary>The one serializer configuration the wire DTOs use (camelCase, the JSON boundary).</summary>
internal static class WireJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
