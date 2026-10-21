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
    /// Creates the client for the standard resolution path. When <c>LUNATE_REPLAY</c> names a
    /// fixture and no recording is requested, the pipeline replays it instead of resolving a
    /// credential or constructing a provider client.
    /// </summary>
    public IChatClient Create(ModelInfo model) => Create(model, apiKey: null, replayAllowed: true);

    /// <summary>
    /// Creates the client with an explicit credential, bypassing the environment/auth-store
    /// resolution: declared extension providers hand the host-read secret in here, so the value
    /// only ever reaches the client's request headers. Replay is out of scope for this path.
    /// </summary>
    public IChatClient Create(ModelInfo model, string? apiKey) =>
        Create(model, apiKey, replayAllowed: false);

    /// <param name="replayAllowed">
    /// Whether <c>LUNATE_REPLAY</c> may replace the provider client. Only the standard resolution
    /// path allows it; a caller that already holds a credential gets the provider client.
    /// </param>
    private IChatClient Create(ModelInfo model, string? apiKey, bool replayAllowed)
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
            inner = decorator(CreateInnerClient(model, apiKey));
        }
        else if (replayAllowed && ReplayPathFromEnvironment() is { } replayPath)
        {
            // Before credential resolution and provider construction: replay needs neither.
            inner = new ReplayChatClient(replayPath);
        }
        else
        {
            inner = CreateInnerClient(model, apiKey);
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
    private IChatClient CreateInnerClient(ModelInfo model, string? apiKey) =>
        _providerClientFactory is not null
            ? _providerClientFactory(model, apiKey)
            : CreateProviderClient(model, apiKey);

    private IChatClient CreateProviderClient(ModelInfo model, string? apiKey)
    {
        ProviderPreset preset =
            ProviderPresets.Find(model.Provider) ?? throw UnsupportedProvider(model.Provider);
        string wireId = WireModelId(model, preset);

        if (preset.Adapter == ProviderAdapter.Anthropic)
        {
            return CreateAnthropicClient(model, preset, _namedKeySource, apiKey)
                .AsIChatClient(wireId);
        }

        // The native OpenAI preset keeps the SDK default endpoint; every compatible preset routes
        // to its own base URL unless the entry declares one.
        return CreateOpenAiClient(model, preset, _namedKeySource, apiKey)
            .GetChatClient(wireId)
            .AsIChatClient();
    }

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

    internal static OpenAIClient CreateOpenAiClient(
        ModelInfo model,
        Func<string, string?>? namedKeySource = null,
        string? apiKey = null
    ) => CreateOpenAiClient(model, PresetFor(model), namedKeySource, apiKey);

    private static OpenAIClient CreateOpenAiClient(
        ModelInfo model,
        ProviderPreset? preset,
        Func<string, string?>? namedKeySource,
        string? apiKey
    )
    {
        string credential =
            apiKey
            ?? ResolveApiKey(
                model,
                preset ?? throw UnsupportedProvider(model.Provider),
                namedKeySource
            );

        return new OpenAIClient(
            new ApiKeyCredential(credential),
            CreateOpenAiClientOptions(model, preset)
        );
    }

    internal static OpenAIClientOptions CreateOpenAiClientOptions(
        ModelInfo model,
        ProviderPreset? preset = null
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

        return options;
    }

    internal static AnthropicClient CreateAnthropicClient(
        ModelInfo model,
        Func<string, string?>? namedKeySource = null,
        string? apiKey = null
    ) => CreateAnthropicClient(model, PresetFor(model), namedKeySource, apiKey);

    private static AnthropicClient CreateAnthropicClient(
        ModelInfo model,
        ProviderPreset? preset,
        Func<string, string?>? namedKeySource,
        string? apiKey
    )
    {
        string credential =
            apiKey
            ?? ResolveApiKey(
                model,
                preset ?? throw UnsupportedProvider(model.Provider),
                namedKeySource
            );

        if (model.Endpoint is null)
        {
            return new AnthropicClient { ApiKey = credential, MaxRetries = 0 };
        }

        return new AnthropicClient
        {
            ApiKey = credential,
            MaxRetries = 0,
            BaseUrl = model.Endpoint.AbsoluteUri,
        };
    }

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

        if (model.Endpoint is not null && !ProviderEndpoint.IsOwn(model.Endpoint, preset.BaseUrl))
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
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lunate",
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
