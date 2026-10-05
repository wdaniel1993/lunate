using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Lunate.Ai;

public sealed class ChatClientFactory : IChatClientFactory
{
    private const string OpenAiApiKeyVariable = "OPENAI_API_KEY";
    private const string OpenAiProvider = "openai";
    private const string OpenTelemetryOptInVariable = "LUNATE_OTEL";
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

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

        if (_recorderDecorator is not null)
        {
            inner = _recorderDecorator(inner);
        }

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
                throw new InvalidOperationException(
                    $"The {OpenAiApiKeyVariable} environment variable is not set. " +
                    $"Set {OpenAiApiKeyVariable} to an OpenAI API key; settings and auth.json support arrive with T-16.");
            }

            OpenAIClientOptions options = new();
            if (model.Endpoint is not null)
            {
                options.Endpoint = model.Endpoint;
            }

            return new OpenAIClient(new ApiKeyCredential(apiKey), options)
                .GetChatClient(model.Id)
                .AsIChatClient();
        }

        throw new NotSupportedException(
            $"Provider '{model.Provider}' is not supported. Supported providers: {OpenAiProvider}.");
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
