using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using System.Security.Cryptography;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Lunate.Ai;

public sealed class ChatClientFactory : IChatClientFactory
{
    private const string AnthropicProvider = "anthropic";
    internal const string PlaceholderCredential = "unused-local-endpoint";

    /// <summary>
    /// The non-secret key an SDK client receives at construction: the credential is then placed
    /// only by <see cref="ProviderCredentialHandler"/>, so the value never sits in an SDK object
    /// (a log, an error option, a retry) — the wire is the only place it exists.
    /// </summary>
    internal const string SentinelCredential = "lunate-sentinel";
    private const string OpenTelemetryOptInVariable = "LUNATE_OTEL";
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string RecordVariable = "LUNATE_RECORD";
    private const string RecordPathVariable = "LUNATE_RECORD_PATH";
    private const string ReplayVariable = "LUNATE_REPLAY";

    private readonly ILoggerFactory _loggerFactory;
    private readonly bool _enableOpenTelemetry;
    private readonly Func<ModelInfo, string?, IChatClient>? _providerClientFactory;
    private readonly Func<IChatClient, IChatClient>? _recorderDecorator;
    private readonly Func<string, string?>? _namedKeySource;

    public ChatClientFactory(
        ILoggerFactory loggerFactory,
        bool? enableOpenTelemetry = null,
        Func<ModelInfo, string?, IChatClient>? providerClientFactory = null,
        Func<IChatClient, IChatClient>? recorderDecorator = null,
        Func<string, string?>? namedKeySource = null
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        _enableOpenTelemetry = enableOpenTelemetry ?? OpenTelemetryEnabledFromEnvironment();
        _providerClientFactory = providerClientFactory;
        _recorderDecorator = recorderDecorator;
        _namedKeySource = namedKeySource;
    }

    /// <summary>
    /// Creates the client for the standard resolution path: replay allowed, no credential held.
    /// When <c>LUNATE_REPLAY</c> names a fixture and no recording is requested, the pipeline
    /// replays it instead of resolving a credential or constructing a provider client.
    /// </summary>
    public IChatClient Create(ModelInfo model) =>
        Create(model, apiKey: null, replayAllowed: true, context: null);

    /// <summary>
    /// Creates the client with an explicit credential, bypassing the environment/auth-store
    /// resolution: declared extension providers hand the host-read secret in here, so the value
    /// only ever reaches the client's request headers. Replay is out of scope for this path.
    /// </summary>
    public IChatClient Create(ModelInfo model, string? apiKey) =>
        Create(model, apiKey, replayAllowed: false, context: null);

    /// <summary>
    /// Creates the client with the session's request identity: headers' {session.id} resolves over
    /// it; {run.id} resolves over the run the request executes in (the ambient context) or fails
    /// loudly. The session path stays replayable: when <c>LUNATE_REPLAY</c> names a fixture, the
    /// pipeline replays it and the request identity applies to no wire.
    /// </summary>
    public IChatClient Create(ModelInfo model, RequestContext? context) =>
        Create(model, apiKey: null, replayAllowed: true, context);

    /// <summary>
    /// Creates the client with both an explicit credential and the session's request identity:
    /// the declared extension providers' path, with the run's template context attached.
    /// </summary>
    public IChatClient Create(ModelInfo model, string? apiKey, RequestContext? context) =>
        Create(model, apiKey, replayAllowed: false, context);

    /// <param name="replayAllowed">
    /// Whether <c>LUNATE_REPLAY</c> may replace the provider client. Only the standard resolution
    /// path allows it; a caller that already holds a credential gets the provider client.
    /// </param>
    private IChatClient Create(
        ModelInfo model,
        string? apiKey,
        bool replayAllowed,
        RequestContext? context
    )
    {
        ArgumentNullException.ThrowIfNull(model);

        Func<IChatClient, IChatClient>? decorator = _recorderDecorator;
        if (decorator is null && RecordingRequestedFromEnvironment())
        {
            string path = RecordingPathFromEnvironment() ?? DefaultRecordingPath();
            decorator = client => new RecordingChatClient(client, path, model.Id);
        }

        IChatClient inner;
        if (decorator is not null)
        {
            // Recording wins over replay: with both requested the run records, nothing is replayed.
            inner = decorator(CreateInnerClient(model, apiKey, context));
        }
        else if (replayAllowed && ReplayPathFromEnvironment() is { } replayPath)
        {
            // Before credential resolution and provider construction: replay needs neither.
            inner = new ReplayChatClient(replayPath);
        }
        else
        {
            inner = CreateInnerClient(model, apiKey, context);
        }

        if (string.Equals(model.Provider, AnthropicProvider, StringComparison.OrdinalIgnoreCase))
        {
            // Below the turn merge (the rolling marker lands on the merged last block) and above the
            // recorder/provider (recordings see the wire request).
            inner = new AnthropicPromptCache(inner);
            inner = new AnthropicTurnMerge(inner);
        }

        inner = new StreamAccumulator(inner);

        ChatClientBuilder builder = new(inner);
        if (_enableOpenTelemetry)
        {
            builder = builder.UseOpenTelemetry(_loggerFactory);
        }

        return builder.UseLogging(_loggerFactory).Build();
    }

    /// <summary>The provider client: the test seam when installed, the real adapter otherwise.</summary>
    private IChatClient CreateInnerClient(
        ModelInfo model,
        string? apiKey,
        RequestContext? context
    ) =>
        _providerClientFactory is not null
            ? _providerClientFactory(model, apiKey)
            : CreateProviderClient(model, apiKey, context);

    private IChatClient CreateProviderClient(
        ModelInfo model,
        string? apiKey,
        RequestContext? context
    )
    {
        ProviderPreset preset =
            ProviderPresets.Find(model.Provider) ?? throw UnsupportedProvider(model.Provider);
        string wireId = WireModelId(model, preset);
        ProviderApiFamily family = ProviderFamilyResolver.Resolve(
            model.ApiFamily,
            preset.Families,
            model.Provider,
            model.Id
        );

        // An unwired family is a slice error before it is an auth error: the check owns the
        // message naming the family and the families this slice serves.
        if (family is not (ProviderApiFamily.OpenAiChat or ProviderApiFamily.AnthropicMessages))
        {
            throw NotImplementedFamily(family, model, model.Provider);
        }

        // Resolved exactly once: the SDK clients get only the sentinel; the credential handler
        // places the real value on the wire in the declared place.
        string credential = apiKey ?? ResolveApiKey(model, preset, _namedKeySource);
        CredentialPlacement placement = ResolvePlacement(model, preset, family);
        var headersHandler = HeadersHandler(model, preset, context);
        var credentialHandler = new ProviderCredentialHandler(credential, placement);

        switch (family)
        {
            // The native OpenAI preset keeps the SDK default endpoint; every compatible preset
            // routes to its own base URL unless the entry declares one.
            case ProviderApiFamily.OpenAiChat:
                return CreateOpenAiClient(model, preset, headersHandler, credentialHandler)
                    .GetChatClient(wireId)
                    .AsIChatClient();
            case ProviderApiFamily.AnthropicMessages:
                return CreateAnthropicClient(model, preset, headersHandler, credentialHandler)
                    .AsIChatClient(wireId);
            default:
                throw NotImplementedFamily(family, model, model.Provider);
        }
    }

    /// <summary>
    /// The placement the resolved family's wire uses: the entry's declaration (carried on the
    /// model for its resolved family) wins over the preset's, and an unusable compact form is a
    /// load-quality error naming the provider, not a silent default.
    /// </summary>
    private static CredentialPlacement ResolvePlacement(
        ModelInfo model,
        ProviderPreset preset,
        ProviderApiFamily family
    )
    {
        if (model.CredentialPlacement is not { } declared)
        {
            return preset.PlacementFor(family);
        }

        if (
            !CredentialPlacement.TryParseCompact(
                declared,
                out CredentialPlacement? placement,
                out string? error
            )
        )
        {
            throw new InvalidDataException(
                $"Provider '{model.Provider}' declares an invalid credentialPlacement '{declared}': {error}"
            );
        }

        return placement!;
    }

    /// <summary>
    /// The wire's declared-header handler of one created client: fresh per call (an SDK refuses a
    /// handler that is already attached somewhere). The preset's headers go out; the entry's,
    /// carried on the model, add to and override the same name — the user entry is the explicit
    /// intent.
    /// </summary>
    private ProviderHeadersHandler HeadersHandler(
        ModelInfo model,
        ProviderPreset preset,
        RequestContext? context
    )
    {
        var declared = new List<(string Name, string Raw)>();
        if (preset.Headers is { Count: > 0 } presetHeaders)
        {
            declared.AddRange(
                presetHeaders
                    .Where(header => model.ProviderHeaders?.ContainsKey(header.Key) != true)
                    .Select(header => (header.Key, header.Value))
            );
        }

        if (model.ProviderHeaders is { Count: > 0 } entryHeaders)
        {
            declared.AddRange(entryHeaders.Select(header => (header.Key, header.Value)));
        }

        return new ProviderHeadersHandler(declared, context, _namedKeySource, null);
    }

    private static NotSupportedException NotImplementedFamily(
        ProviderApiFamily family,
        ModelInfo model,
        string provider
    ) =>
        new(
            $"API family '{ProviderApiFamilies.Name(family)}' is not implemented in this slice of the "
                + "provider-families change: model '"
                + model.Id
                + "' on provider '"
                + provider
                + "' resolves to it, but only 'openai-chat' and 'anthropic-messages' have clients. "
                + "Pick a model of a wired family, or wait for the family's client."
        );

    /// <summary>
    /// The id the provider receives. A declared wire id always wins; a qualified id on a
    /// compatible preset without one sends the part after the preset prefix, never the prefix.
    /// </summary>
    private static string WireModelId(ModelInfo model, ProviderPreset preset)
    {
        if (!string.IsNullOrEmpty(model.WireId))
        {
            return model.WireId;
        }

        string prefix = preset.Id + "/";
        return
            preset.Adapter == ProviderAdapter.OpenAiCompatible
            && model.Id.StartsWith(prefix, StringComparison.Ordinal)
            ? model.Id[prefix.Length..]
            : model.Id;
    }

    private static NotSupportedException UnsupportedProvider(string provider) =>
        new(
            $"Provider '{provider}' is not supported. Supported providers: "
                + string.Join(", ", ProviderPresets.All.Select(static preset => preset.Id))
                + "."
        );

    internal OpenAIClient CreateOpenAiClient(
        ModelInfo model,
        Func<string, string?>? namedKeySource = null,
        string? apiKey = null
    ) => CreateOpenAiClient(model, PresetFor(model), headersHandler: null, credentialHandler: null);

    private OpenAIClient CreateOpenAiClient(
        ModelInfo model,
        ProviderPreset? preset,
        DelegatingHandler? headersHandler,
        DelegatingHandler? credentialHandler
    )
    {
        // The SDK holds the sentinel only: the credential handler places the real value on the
        // wire, in the declared place, and the SDK object never carries it.
        return new OpenAIClient(
            new ApiKeyCredential(SentinelCredential),
            CreateOpenAiClientOptions(
                model,
                preset,
                headersHandler,
                credentialHandler,
                BaseTransportFactory
            )
        );
    }

    internal static OpenAIClientOptions CreateOpenAiClientOptions(
        ModelInfo model,
        ProviderPreset? preset = null
    ) =>
        CreateOpenAiClientOptions(
            model,
            preset,
            headersHandler: null,
            credentialHandler: null,
            null
        );

    private static OpenAIClientOptions CreateOpenAiClientOptions(
        ModelInfo model,
        ProviderPreset? preset,
        DelegatingHandler? headersHandler,
        DelegatingHandler? credentialHandler,
        Func<HttpMessageHandler>? baseTransport
    )
    {
        OpenAIClientOptions options = new() { RetryPolicy = new ClientRetryPolicy(maxRetries: 0) };

        Uri? endpoint =
            model.Endpoint
            ?? (preset?.Adapter == ProviderAdapter.OpenAiCompatible ? preset.BaseUrl : null);
        if (endpoint is not null)
        {
            options.Endpoint = endpoint;
        }

        if (headersHandler is not null && credentialHandler is not null)
        {
            options.Transport = new HttpClientPipelineTransport(
                TransportHttpClient(headersHandler, credentialHandler, baseTransport)
            );
        }

        return options;
    }

    /// <summary>
    /// The HTTP client a custom transport wraps: no protocol timeouts inside the transport (the
    /// SDK and the caller own cancellation), handlers ordered headers outermost, credential
    /// innermost, so the credential lands after the SDK built its request.
    /// </summary>
    private static HttpClient TransportHttpClient(
        DelegatingHandler outer,
        DelegatingHandler inner,
        Func<HttpMessageHandler>? baseTransport
    )
    {
        inner.InnerHandler = baseTransport?.Invoke() ?? new HttpClientHandler();
        outer.InnerHandler = inner;
        return new HttpClient(outer) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// The base handler the factory-shaped transport wraps. Tests set it to observe exactly the
    /// wire the installed pipeline builds; product code never does.
    /// </summary>
    internal Func<HttpMessageHandler>? BaseTransportFactory { get; set; }

    internal static AnthropicClient CreateAnthropicClient(
        ModelInfo model,
        Func<string, string?>? namedKeySource = null,
        string? apiKey = null
    ) =>
        CreateAnthropicClient(
            model,
            PresetFor(model),
            headersHandler: null,
            credentialHandler: null
        );

    private static AnthropicClient CreateAnthropicClient(
        ModelInfo model,
        ProviderPreset? preset,
        DelegatingHandler? headersHandler,
        DelegatingHandler? credentialHandler
    )
    {
        if (model.Endpoint is null)
        {
            return new AnthropicClient
            {
                ApiKey = SentinelCredential,
                MaxRetries = 0,
                Handlers = WireHandlers(headersHandler, credentialHandler),
            };
        }

        return new AnthropicClient
        {
            ApiKey = SentinelCredential,
            MaxRetries = 0,
            BaseUrl = model.Endpoint.AbsoluteUri,
            Handlers = WireHandlers(headersHandler, credentialHandler),
        };
    }

    /// <summary>
    /// The DelegatingHandler chain the Anthropic SDK attaches as options.Handlers: headers
    /// outermost (each handler wraps the next, the last wraps the client), credential innermost
    /// so the placement applies after the SDK built its request. An empty chain keeps the probe
    /// path's SDK defaults.
    /// </summary>
    private static IReadOnlyList<DelegatingHandler> WireHandlers(
        DelegatingHandler? headersHandler,
        DelegatingHandler? credentialHandler
    ) =>
        headersHandler is null || credentialHandler is null
            ? []
            : [headersHandler, credentialHandler];

    private static ProviderPreset PresetFor(ModelInfo model) =>
        ProviderPresets.Find(model.Provider) ?? throw UnsupportedProvider(model.Provider);

    /// <summary>
    /// Resolves the credential for a model. A declared <see cref="ModelInfo.AuthRef"/> resolves the
    /// named key from the user's auth store and sends it to the declared endpoint - the explicit
    /// opt-in. Without a reference, the preset's environment variable (its alias second) is used
    /// for the provider's own endpoint - no endpoint, or one equal to or under the preset base URL -
    /// with the auth store under the preset id as the fallback. A model with a foreign declared
    /// endpoint gets the placeholder credential, so real keys never reach third-party endpoints.
    /// </summary>
    internal static string ResolveApiKey(
        ModelInfo model,
        ProviderPreset preset,
        Func<string, string?>? namedKeySource = null
    )
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (!string.IsNullOrWhiteSpace(model.AuthRef))
        {
            string? named = namedKeySource?.Invoke(model.AuthRef);
            if (string.IsNullOrWhiteSpace(named))
            {
                throw new InvalidOperationException(
                    $"The auth reference '{model.AuthRef}' for model '{model.Id}' cannot be resolved. "
                        + $"Add a key named '{model.AuthRef}' to auth.json or set the matching environment variable."
                );
            }

            return named;
        }

        if (model.Endpoint is not null && !ProviderEndpoint.IsOwn(model.Endpoint, preset))
        {
            return PlaceholderCredential;
        }

        string? apiKey = Environment.GetEnvironmentVariable(preset.KeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey) && preset.KeyVariableAlias is { } alias)
        {
            apiKey = Environment.GetEnvironmentVariable(alias);
        }

        if (string.IsNullOrWhiteSpace(apiKey) && namedKeySource is not null)
        {
            apiKey = namedKeySource(preset.Id);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            string aliases = preset.KeyVariableAlias is { } aliasVariable
                ? $" (or {aliasVariable})"
                : string.Empty;
            throw new InvalidOperationException(
                $"The {preset.KeyVariable} environment variable is not set and no '{preset.Id}' key is stored. "
                    + $"Set {preset.KeyVariable}{aliases} or add the key to auth.json."
            );
        }

        return apiKey;
    }

    internal static string DefaultRecordingPath() =>
        Path.Combine(
            LunateHome.Store,
            "recordings",
            string.Concat(
                DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
                "-",
                Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4)),
                ".jsonl"
            )
        );

    private static bool RecordingRequestedFromEnvironment()
    {
        string? value = Environment.GetEnvironmentVariable(RecordVariable);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? RecordingPathFromEnvironment()
    {
        string? path = Environment.GetEnvironmentVariable(RecordPathVariable);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static string? ReplayPathFromEnvironment()
    {
        string? path = Environment.GetEnvironmentVariable(ReplayVariable);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static bool OpenTelemetryEnabledFromEnvironment()
    {
        string? optIn = Environment.GetEnvironmentVariable(OpenTelemetryOptInVariable);
        if (
            string.Equals(optIn, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(optIn, "true", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OtlpEndpointVariable));
    }
}
