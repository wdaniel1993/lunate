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
    private const string PlaceholderCredential = "unused-local-endpoint";
    private const string OpenTelemetryOptInVariable = "LUNATE_OTEL";
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string RecordVariable = "LUNATE_RECORD";
    private const string RecordPathVariable = "LUNATE_RECORD_PATH";

    private readonly ILoggerFactory _loggerFactory;
    private readonly bool _enableOpenTelemetry;
    private readonly Func<ModelInfo, IChatClient>? _providerClientFactory;
    private readonly Func<IChatClient, IChatClient>? _recorderDecorator;

    public ChatClientFactory(
        ILoggerFactory loggerFactory,
        bool? enableOpenTelemetry = null,
        Func<ModelInfo, IChatClient>? providerClientFactory = null,
        Func<IChatClient, IChatClient>? recorderDecorator = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        _enableOpenTelemetry = enableOpenTelemetry ?? OpenTelemetryEnabledFromEnvironment();
        _providerClientFactory = providerClientFactory;
        _recorderDecorator = recorderDecorator;
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

    private static IChatClient CreateProviderClient(ModelInfo model)
    {
        if (string.Equals(model.Provider, OpenAiProvider, StringComparison.OrdinalIgnoreCase))
        {
            string? apiKey = Environment.GetEnvironmentVariable(OpenAiApiKeyVariable);
            if (string.IsNullOrEmpty(apiKey))
            {
                if (model.Endpoint is null)
                {
                    throw new InvalidOperationException(
                        $"The {OpenAiApiKeyVariable} environment variable is not set. " +
                        $"Set {OpenAiApiKeyVariable} to an OpenAI API key; settings and auth.json support arrive with T-16.");
                }

                apiKey = PlaceholderCredential;
            }

            OpenAIClientOptions options = CreateOpenAiClientOptions(model);

            return new OpenAIClient(new ApiKeyCredential(apiKey), options)
                .GetChatClient(model.Id)
                .AsIChatClient();
        }

        if (string.Equals(model.Provider, AnthropicProvider, StringComparison.OrdinalIgnoreCase))
        {
            return CreateAnthropicClient(model).AsIChatClient(model.Id);
        }

        throw new NotSupportedException(
            $"Provider '{model.Provider}' is not supported. Supported providers: {OpenAiProvider}, {AnthropicProvider}.");
    }

    internal static OpenAIClientOptions CreateOpenAiClientOptions(ModelInfo model)
    {
        OpenAIClientOptions options = new()
        {
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };

        if (model.Endpoint is not null)
        {
            options.Endpoint = model.Endpoint;
        }

        return options;
    }

    internal static AnthropicClient CreateAnthropicClient(ModelInfo model)
    {
        string? apiKey = Environment.GetEnvironmentVariable(AnthropicApiKeyVariable);
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                $"The {AnthropicApiKeyVariable} environment variable is not set. " +
                $"Set {AnthropicApiKeyVariable} to an Anthropic API key; settings and auth.json support arrive with T-16.");
        }

        if (model.Endpoint is null)
        {
            return new AnthropicClient
            {
                ApiKey = apiKey,
                MaxRetries = 0,
            };
        }

        return new AnthropicClient
        {
            ApiKey = apiKey,
            MaxRetries = 0,
            BaseUrl = model.Endpoint.AbsoluteUri,
        };
    }

    internal static string DefaultRecordingPath() =>
        Path.Combine(
            "artifacts",
            "recordings",
            string.Concat(
                DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
                "-",
                Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4)),
                ".jsonl"));

    private static bool RecordingRequestedFromEnvironment()
    {
        string? value = Environment.GetEnvironmentVariable(RecordVariable);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? RecordingPathFromEnvironment()
    {
        string? path = Environment.GetEnvironmentVariable(RecordPathVariable);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static bool OpenTelemetryEnabledFromEnvironment()
    {
        string? optIn = Environment.GetEnvironmentVariable(OpenTelemetryOptInVariable);
        if (string.Equals(optIn, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(optIn, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OtlpEndpointVariable));
    }
}
