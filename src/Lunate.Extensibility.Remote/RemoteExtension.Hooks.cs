using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote;

public sealed partial class RemoteExtension
{
    private readonly Dictionary<string, IHookHandler> _hookProxies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RemoteServiceProxy> _serviceProxies = new(
        StringComparer.Ordinal
    );
    private HashSet<string> _disabledHooks = [];
    private HashSet<string> _disabledServices = [];

    /// <summary>
    /// Builds the declaration-driven proxies: registration only, nothing starts. Hooks the wire
    /// catalogue does not know are warned and ignored; file-change subscriptions go through the
    /// context's subscription path. Services register as JSON-callable proxies.
    /// </summary>
    private void RegisterProxies()
    {
        ExtensionManifest manifest = _options.Manifest;
        foreach (string declaration in manifest.Hooks)
        {
            HookContract? contract = HookContracts.FindByDeclaration(declaration);
            if (contract is null)
            {
                _options.Context.Log.Warn(
                    $"extension '{Id}': the manifest declares unknown hook '{declaration}'; the declaration is ignored."
                );
                continue;
            }

            IHookHandler handler = contract.Name switch
            {
                "ProjectTrust" => new RemoteProjectTrustHandler(this, contract),
                "SessionStarted" => new RemoteSessionStartedHandler(this, contract),
                "SessionEnding" => new RemoteSessionEndingHandler(this, contract),
                "InputReceived" => new RemoteInputReceivedHandler(this, contract),
                "RunStarting" => new RemoteRunStartingHandler(this, contract),
                "ContextBuilding" => new RemoteContextBuildingHandler(this, contract),
                "ProviderStreamEvent" => new RemoteProviderStreamEventHandler(this, contract),
                "MessageCompleted" => new RemoteMessageCompletedHandler(this, contract),
                "ToolCalling" => new RemoteToolCallingHandler(this, contract),
                "ToolResultReady" => new RemoteToolResultReadyHandler(this, contract),
                "TurnEnded" => new RemoteTurnEndedHandler(this, contract),
                "RunSettled" => new RemoteRunSettledHandler(this, contract),
                "Compacting" => new RemoteCompactingHandler(this, contract),
                "ModelChanged" => new RemoteModelChangedHandler(this, contract),
                "ToolsChanged" => new RemoteToolsChangedHandler(this, contract),
                "FileChanged" => new RemoteFileChangedHandler(this, contract),
                _ => throw new InvalidOperationException(
                    $"the catalogue gained hook '{contract.Name}' without a proxy; add it to RemoteExtension.RegisterProxies."
                ),
            };
            _hookProxies[contract.Name] = handler;
            if (handler is IFileChangedHandler fileChanged)
            {
                _options.Context.SubscribeFileChanged(fileChanged);
            }
            else
            {
                _options.Context.Register(handler);
            }
        }

        foreach (string name in manifest.Services)
        {
            var proxy = new RemoteServiceProxy(this, name);
            _serviceProxies[name] = proxy;
            _options.Context.RegisterService(name, proxy);
        }
    }

    /// <summary>
    /// The dropped declared surface after the handshake: the hooks are unregistered (the
    /// in-process "declared but not registered" rule) and the services refuse calls with a
    /// teaching error.
    /// </summary>
    private void SetMissingSurface(
        IReadOnlyList<HookContract> missingHooks,
        IReadOnlyList<string> missingServices
    )
    {
        _disabledHooks = [.. missingHooks.Select(contract => contract.Name)];
        _disabledServices = [.. missingServices];
        if (_options.UnregisterHook is not { } unregister)
        {
            return;
        }

        foreach (HookContract contract in missingHooks)
        {
            if (_hookProxies.TryGetValue(contract.Name, out IHookHandler? proxy))
            {
                unregister(proxy);
            }
        }
    }

    /// <summary>The manifest's declared hooks resolved to catalogue rows.</summary>
    private IReadOnlyList<HookContract> DeclaredHooks() =>
        [.. _options.Manifest.Hooks.Select(HookContracts.FindByDeclaration).OfType<HookContract>()];

    /// <summary>Sends an observer hook as a fire-and-forget notification.</summary>
    internal async ValueTask ObserveAsync(
        HookContract contract,
        object payload,
        CancellationToken cancellationToken
    )
    {
        // The session-started dispatch happens during load — before the session is live. A remote
        // child must never start on the startup path: the payload is held and delivered by
        // MarkSessionLiveAsync (the post-ready point), which starts the child then.
        if (string.Equals(contract.Name, SessionStartedContract.Name, StringComparison.Ordinal))
        {
            lock (_sessionGate)
            {
                // Only a dispatch that would START the child is deferred: an already started (or
                // failed) extension proceeds normally — its failure must still be dropped and
                // reported like any other observer.
                if (!_sessionLive && Status.State == RemoteExtensionState.NotStarted)
                {
                    _pendingSessionStart = (SessionStartedPayload)payload;
                    return;
                }
            }
        }

        RemoteWire wire = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        await wire.NotifyAsync(contract.Method, EncodeCall(contract, payload), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Marks the session live (the post-ready point) and delivers a deferred <c>SessionStarted</c>
    /// to an extension that subscribed to it — the earliest moment a non-background child may
    /// start. Background children start through their own path; both are idempotent.
    /// </summary>
    public async ValueTask MarkSessionLiveAsync(CancellationToken cancellationToken = default)
    {
        lock (_sessionGate)
        {
            _sessionLive = true;
        }

        await FlushPendingSessionStartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Delivers a held <c>SessionStarted</c>, once, to the child. Called by the ready point and
    /// by the first successful start (whichever comes first); a delivery failure is contained
    /// like any other observer's — dropped and reported.
    /// </summary>
    private async ValueTask FlushPendingSessionStartAsync(CancellationToken cancellationToken)
    {
        SessionStartedPayload? pending;
        lock (_sessionGate)
        {
            pending = _pendingSessionStart;
            _pendingSessionStart = null;
        }

        if (pending is null)
        {
            return;
        }

        try
        {
            await ObserveAsync(SessionStartedContract, pending, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _options.Context.Log.Warn(
                $"extension '{Id}': the session-started delivery failed: {Explain(exception)}"
            );
        }
    }

    private static readonly HookContract SessionStartedContract = HookContracts.FindByDeclaration(
        "session-started"
    )!;

    /// <summary>
    /// Sends an interceptor hook as a request and decodes the result. Cancellation maps to
    /// <c>$/cancelRequest</c>; a closed channel or malformed answer throws and the hook runner
    /// applies the catalogue's failure policy.
    /// </summary>
    internal async ValueTask<TResult> CallHookAsync<TResult>(
        HookContract contract,
        object payload,
        CancellationToken cancellationToken
    )
        where TResult : IHookResult
    {
        RemoteWire wire = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        JsonElement? result;
        try
        {
            result = await wire.CallAsync(
                    contract.Method,
                    EncodeCall(contract, payload),
                    timeout: null,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (RemoteChannelClosedException exception)
        {
            throw new RemoteExtensionException(
                $"extension '{Id}': the process exited while hook '{contract.Name}' was in flight: {exception.Message}",
                exception
            );
        }

        if (result is null)
        {
            throw new RemoteCallException(
                -32603,
                $"hook '{contract.Name}' answered with no result"
            );
        }

        return (TResult)HookResultCodec.DecodeResult(contract, result.Value);
    }

    /// <summary>The hook's no-opinion answer for a declaration the child did not implement.</summary>
    internal static TResult NoOpinion<TResult>(HookContract contract)
        where TResult : IHookResult =>
        contract.Name switch
        {
            "InputReceived" => (TResult)(object)new InputReceivedResult.PassThrough(),
            "RunStarting" => (TResult)(object)new RunStartingResult.None(),
            "ContextBuilding" => (TResult)(object)ContextBuildingResult.None,
            "MessageCompleted" => (TResult)(object)new MessageCompletedResult.Keep(),
            "ToolCalling" => (TResult)(object)new ToolCallingResult.Proceed(null),
            "ToolResultReady" => (TResult)(object)new ToolResultReadyResult(string.Empty, null),
            "TurnEnded" => (TResult)(object)new TurnEndedResult.None(),
            "Compacting" => (TResult)(object)new CompactingResult.UseDefault(),
            "ProjectTrust" => (TResult)(object)new ProjectTrustResult.Allow(),
            _ => throw new InvalidOperationException(
                $"no no-opinion result for '{contract.Name}'."
            ),
        };

    private JsonElement EncodeCall(HookContract contract, object payload) =>
        JsonSerializer.SerializeToElement(
            new HookCallEnvelope(
                RemoteCallContext.ForPayload(payload, _options.Workspace),
                HookResultCodec.EncodePayload(contract, payload)
            ),
            WireJson.Options
        );

    /// <summary>The shared base for the declaration-driven hook proxies.</summary>
    internal abstract class RemoteHookHandler(RemoteExtension extension, HookContract contract)
        : IHookHandler,
            IPreparationTimeReporter
    {
        protected RemoteExtension Extension { get; } = extension;

        protected HookContract Contract { get; } = contract;

        public int Priority => 0;

        public TimeSpan ConsumePreparationTime() => Extension.ConsumePreparationTime();

        protected ValueTask ObserveAsync(object payload, CancellationToken cancellationToken) =>
            Extension.ObserveAsync(Contract, payload, cancellationToken);

        protected async ValueTask<TResult> CallAsync<TResult>(
            object payload,
            CancellationToken cancellationToken
        )
            where TResult : IHookResult =>
            Extension._disabledHooks.Contains(Contract.Name)
                ? NoOpinion<TResult>(Contract)
                : await Extension
                    .CallHookAsync<TResult>(Contract, payload, cancellationToken)
                    .ConfigureAwait(false);
    }

    private sealed class RemoteProjectTrustHandler(RemoteExtension extension, HookContract contract)
        : RemoteHookHandler(extension, contract),
            IProjectTrustHandler
    {
        public ValueTask<ProjectTrustResult> HandleAsync(
            ProjectTrustPayload payload,
            CancellationToken cancellationToken
        ) => CallAsync<ProjectTrustResult>(payload, cancellationToken);
    }

    private sealed class RemoteSessionStartedHandler(
        RemoteExtension extension,
        HookContract contract
    ) : RemoteHookHandler(extension, contract), ISessionStartedHandler
    {
        public ValueTask HandleAsync(
            SessionStartedPayload payload,
            CancellationToken cancellationToken
        ) => ObserveAsync(payload, cancellationToken);
    }

    private sealed class RemoteSessionEndingHandler(
        RemoteExtension extension,
        HookContract contract
    ) : RemoteHookHandler(extension, contract), ISessionEndingHandler
    {
        public ValueTask HandleAsync(
            SessionEndingPayload payload,
            CancellationToken cancellationToken
        ) => ObserveAsync(payload, cancellationToken);
    }

    private sealed class RemoteInputReceivedHandler(
        RemoteExtension extension,
        HookContract contract
    ) : RemoteHookHandler(extension, contract), IInputReceivedHandler
    {
        public ValueTask<InputReceivedResult> HandleAsync(
            InputReceivedPayload payload,
            CancellationToken cancellationToken
        ) => CallAsync<InputReceivedResult>(payload, cancellationToken);
    }

    private sealed class RemoteRunStartingHandler(RemoteExtension extension, HookContract contract)
        : RemoteHookHandler(extension, contract),
            IRunStartingHandler
    {
        public ValueTask<RunStartingResult> HandleAsync(
            RunStartingPayload payload,
            CancellationToken cancellationToken
        ) => CallAsync<RunStartingResult>(payload, cancellationToken);
    }

    private sealed class RemoteContextBuildingHandler(
        RemoteExtension extension,
        HookContract contract
    ) : RemoteHookHandler(extension, contract), IContextBuildingHandler
    {
        public ValueTask<ContextBuildingResult> HandleAsync(
            ContextBuildingPayload payload,
            CancellationToken cancellationToken
        ) => CallAsync<ContextBuildingResult>(payload, cancellationToken);
    }

    private sealed class RemoteProviderStreamEventHandler(
        RemoteExtension extension,
        HookContract contract
    ) : RemoteHookHandler(extension, contract), IProviderStreamEventHandler
    {
        public ValueTask ObserveAsync(
            ProviderStreamEventPayload payload,
            CancellationToken cancellationToken
        ) => ObserveAsync((object)payload, cancellationToken);
    }

    private sealed class RemoteMessageCompletedHandler(
        RemoteExtension extension,
        HookContract contract
    ) : RemoteHookHandler(extension, contract), IMessageCompletedHandler
    {
        public ValueTask<MessageCompletedResult> HandleAsync(
            MessageCompletedPayload payload,
            CancellationToken cancellationToken
        ) => CallAsync<MessageCompletedResult>(payload, cancellationToken);
    }

    private sealed class RemoteToolCallingHandler(RemoteExtension extension, HookContract contract)
        : RemoteHookHandler(extension, contract),
            IToolCallingHandler
    {
        public ValueTask<ToolCallingResult> HandleAsync(
            ToolCallingPayload payload,
            CancellationToken cancellationToken
        ) => CallAsync<ToolCallingResult>(payload, cancellationToken);
    }

    private sealed class RemoteToolResultReadyHandler(
        RemoteExtension extension,
        HookContract contract
    ) : RemoteHookHandler(extension, contract), IToolResultReadyHandler
    {
        public ValueTask<ToolResultReadyResult> HandleAsync(
            ToolResultReadyPayload payload,
            CancellationToken cancellationToken
        ) => CallAsync<ToolResultReadyResult>(payload, cancellationToken);
    }

    private sealed class RemoteTurnEndedHandler(RemoteExtension extension, HookContract contract)
        : RemoteHookHandler(extension, contract),
            ITurnEndedHandler
    {
        public ValueTask<TurnEndedResult> HandleAsync(
            TurnEndedPayload payload,
            CancellationToken cancellationToken
        ) => CallAsync<TurnEndedResult>(payload, cancellationToken);
    }

    private sealed class RemoteRunSettledHandler(RemoteExtension extension, HookContract contract)
        : RemoteHookHandler(extension, contract),
            IRunSettledHandler
    {
        public ValueTask ObserveAsync(
            RunSettledPayload payload,
            CancellationToken cancellationToken
        ) => ObserveAsync((object)payload, cancellationToken);
    }

    private sealed class RemoteCompactingHandler(RemoteExtension extension, HookContract contract)
        : RemoteHookHandler(extension, contract),
            ICompactingHandler
    {
        public ValueTask<CompactingResult> HandleAsync(
            CompactingPayload payload,
            CancellationToken cancellationToken
        ) => CallAsync<CompactingResult>(payload, cancellationToken);
    }

    private sealed class RemoteModelChangedHandler(RemoteExtension extension, HookContract contract)
        : RemoteHookHandler(extension, contract),
            IModelChangedHandler
    {
        public ValueTask ObserveAsync(
            ModelChangedPayload payload,
            CancellationToken cancellationToken
        ) => ObserveAsync((object)payload, cancellationToken);
    }

    private sealed class RemoteToolsChangedHandler(RemoteExtension extension, HookContract contract)
        : RemoteHookHandler(extension, contract),
            IToolsChangedHandler
    {
        public ValueTask ObserveAsync(
            ToolsChangedPayload payload,
            CancellationToken cancellationToken
        ) => ObserveAsync((object)payload, cancellationToken);
    }

    private sealed class RemoteFileChangedHandler(RemoteExtension extension, HookContract contract)
        : RemoteHookHandler(extension, contract),
            IFileChangedHandler
    {
        public ValueTask OnFileChangedAsync(
            FileChangedPayload payload,
            CancellationToken cancellationToken
        ) => ObserveAsync(payload, cancellationToken);
    }
}
