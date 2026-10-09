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
    private const string AnthropicApiKeyVariable = "ANTHROPIC_API_KEY";
    private const string AnthropicProvider = "anthropic";
    private const string OpenAiApiKeyVariable = "OPENAI_API_KEY";
    private const string OpenAiProvider = "openai";
    internal const string PlaceholderCredential = "unused-local-endpoint";
    private const string OpenTelemetryOptInVariable = "LUNATE_OTEL";
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string RecordVariable = "LUNATE_RECORD";
    private const string RecordPathVariable = "LUNATE_RECORD_PATH";

    private readonly ILoggerFactory _loggerFactory;
    private readonly bool _enableOpenTelemetry;
    private readonly Func<ModelInfo, IChatClient>? _providerClientFactory;
    private readonly Func<IChatClient, IChatClient>? _recorderDecorator;
    private readonly Func<string, string?>? _namedKeySource;

    public ChatClientFactory(
        ILoggerFactory loggerFactory,
        bool? enableOpenTelemetry = null,
        Func<ModelInfo, IChatClient>? providerClientFactory = null,
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

    public IChatClient Create(ModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);

        IChatClient inner = _providerClientFactory is not null
            ? _providerClientFactory(model)
            : CreateProviderClient(model);

        Func<IChatClient, IChatClient>? decorator = _recorderDecorator;
        if (decorator is null && RecordingRequestedFromEnvironment())
        {
            string path = RecordingPathFromEnvironment() ?? DefaultRecordingPath();
            decorator = client => new RecordingChatClient(client, path, model.Id);
        }

        if (decorator is not null)
        {
            inner = decorator(inner);
        }

        inner = new StreamAccumulator(inner);

        ChatClientBuilder builder = new(inner);
        if (_enableOpenTelemetry)
        {
            builder = builder.UseOpenTelemetry(_loggerFactory);
        }

        return builder.UseLogging(_loggerFactory).Build();
    }

    private IChatClient CreateProviderClient(ModelInfo model)
    {
        if (string.Equals(model.Provider, OpenAiProvider, StringComparison.OrdinalIgnoreCase))
        {
            return CreateOpenAiClient(model, _namedKeySource)
                .GetChatClient(model.Id)
                .AsIChatClient();
        }

        if (string.Equals(model.Provider, AnthropicProvider, StringComparison.OrdinalIgnoreCase))
        {
            return CreateAnthropicClient(model, _namedKeySource).AsIChatClient(model.Id);
        }

        throw new NotSupportedException(
            $"Provider '{model.Provider}' is not supported. Supported providers: {OpenAiProvider}, {AnthropicProvider}."
        );
    }

    internal static OpenAIClient CreateOpenAiClient(
        ModelInfo model,
        Func<string, string?>? namedKeySource = null
    )
    {
        string apiKey = ResolveApiKey(model, OpenAiApiKeyVariable, namedKeySource);

        return new OpenAIClient(new ApiKeyCredential(apiKey), CreateOpenAiClientOptions(model));
    }

    internal static OpenAIClientOptions CreateOpenAiClientOptions(ModelInfo model)
    {
        OpenAIClientOptions options = new() { RetryPolicy = new ClientRetryPolicy(maxRetries: 0) };

        if (model.Endpoint is not null)
        {
            options.Endpoint = model.Endpoint;
        }

        return options;
    }

    internal static AnthropicClient CreateAnthropicClient(
        ModelInfo model,
        Func<string, string?>? namedKeySource = null
    )
    {
        string apiKey = ResolveApiKey(model, AnthropicApiKeyVariable, namedKeySource);

        if (model.Endpoint is null)
        {
            return new AnthropicClient { ApiKey = apiKey, MaxRetries = 0 };
        }

        return new AnthropicClient
        {
            ApiKey = apiKey,
            MaxRetries = 0,
            BaseUrl = model.Endpoint.AbsoluteUri,
        };
    }

    /// <summary>
    /// Resolves the credential for a model. A declared <see cref="ModelInfo.AuthRef"/> resolves the
    /// named key from the user's auth store and sends it to the declared endpoint - the explicit
    /// opt-in. Without a reference, the environment key is used only for the provider's default
    /// endpoint (the auth store is the fallback); a model with a declared endpoint gets the
    /// placeholder credential, so real keys never reach third-party endpoints.
    /// </summary>
    internal static string ResolveApiKey(
        ModelInfo model,
        string apiKeyVariable,
        Func<string, string?>? namedKeySource = null
    )
    {
        if (!string.IsNullOrEmpty(model.AuthRef))
        {
            string? named = namedKeySource?.Invoke(model.AuthRef);
            if (string.IsNullOrEmpty(named))
            {
                throw new InvalidOperationException(
                    $"The auth reference '{model.AuthRef}' for model '{model.Id}' cannot be resolved. "
                        + $"Add a key named '{model.AuthRef}' to auth.json or set the matching environment variable."
                );
            }

            return named;
        }

        if (model.Endpoint is not null)
        {
            return PlaceholderCredential;
        }

        string providerName = ProviderNameFor(apiKeyVariable);
        string? apiKey = Environment.GetEnvironmentVariable(apiKeyVariable);
        if (string.IsNullOrEmpty(apiKey) && namedKeySource is not null)
        {
            apiKey = namedKeySource(providerName);
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                $"The {apiKeyVariable} environment variable is not set and no '{providerName}' key is stored. "
                    + $"Set {apiKeyVariable} or add the key to auth.json."
            );
        }

        return apiKey;
    }

    private static string ProviderNameFor(string apiKeyVariable) =>
        apiKeyVariable == AnthropicApiKeyVariable ? AnthropicProvider : OpenAiProvider;

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
