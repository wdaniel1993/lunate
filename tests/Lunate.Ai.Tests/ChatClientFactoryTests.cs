using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Reflection;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Lunate.Ai.Tests;

public sealed class ChatClientFactoryTests
{
    private const string OpenAiApiKeyVariable = "OPENAI_API_KEY";
    private const string AnthropicApiKeyVariable = "ANTHROPIC_API_KEY";
    private const string OpenTelemetryOptInVariable = "LUNATE_OTEL";
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    [Fact]
    public async Task Create_with_telemetry_enabled_observes_telemetry_logging_recorder_provider_in_order()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        var factory = CreateFactory(sequence, enableOpenTelemetry: true);

        IChatClient client = factory.Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal(new[] { "telemetry", "logging", "recorder", "provider" }, sequence);
    }

    [Fact]
    public async Task Create_with_telemetry_disabled_observes_logging_recorder_provider_in_order()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        var factory = CreateFactory(sequence, enableOpenTelemetry: false);

        IChatClient client = factory.Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal(new[] { "logging", "recorder", "provider" }, sequence);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public async Task Create_enables_telemetry_when_LUNATE_OTEL_is_truthy(string optIn)
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        using var environment = new EnvironmentScope(
            (OpenTelemetryOptInVariable, optIn),
            (OtlpEndpointVariable, null)
        );

        IChatClient client = CreateFactoryWithoutExplicitTelemetry(sequence).Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Contains("telemetry", sequence);
    }

    [Fact]
    public async Task Create_enables_telemetry_when_OTLP_endpoint_is_set()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        using var environment = new EnvironmentScope(
            (OpenTelemetryOptInVariable, null),
            (OtlpEndpointVariable, "http://localhost:4317")
        );

        IChatClient client = CreateFactoryWithoutExplicitTelemetry(sequence).Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Contains("telemetry", sequence);
    }

    [Fact]
    public async Task Create_keeps_telemetry_disabled_without_environment_opt_in()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        using var environment = new EnvironmentScope(
            (OpenTelemetryOptInVariable, null),
            (OtlpEndpointVariable, null)
        );

        IChatClient client = CreateFactoryWithoutExplicitTelemetry(sequence).Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain("telemetry", sequence);
    }

    [Fact]
    public async Task Create_explicit_telemetry_disabled_overrides_environment_opt_in()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        using var environment = new EnvironmentScope(
            (OpenTelemetryOptInVariable, "1"),
            (OtlpEndpointVariable, null)
        );

        IChatClient client = CreateFactory(sequence, enableOpenTelemetry: false)
            .Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain("telemetry", sequence);
    }

    [Fact]
    public async Task Create_never_wraps_the_provider_in_function_invoking_chat_client()
    {
        var sequence = new List<string>();
        MarkerChatClient? recorder = null;
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(() => sequence.Add("logging")),
            enableOpenTelemetry: true,
            providerClientFactory: _ => new ProviderStub(sequence),
            recorderDecorator: inner => recorder = new MarkerChatClient("recorder", sequence, inner)
        );

        IChatClient client = factory.Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.NotNull(recorder);
        IChatClient[] chain = [.. WalkChain(client)];
        Assert.DoesNotContain(chain, candidate => candidate is FunctionInvokingChatClient);
        int recorderIndex = Array.FindIndex(
            chain,
            candidate => ReferenceEquals(candidate, recorder)
        );
        Assert.True(recorderIndex > 0);
        Assert.IsType<StreamAccumulator>(chain[recorderIndex - 1]);
        Assert.IsType<ProviderStub>(chain[^1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_places_the_pipeline_layers_outermost_first(bool enableOpenTelemetry)
    {
        IChatClient client = CreateFactory([], enableOpenTelemetry).Create(TestModel());

        IChatClient[] chain = [.. WalkChain(client)];
        string[] expected = enableOpenTelemetry
            ?
            [
                "OpenTelemetryChatClient",
                "LoggingChatClient",
                "StreamAccumulator",
                "MarkerChatClient",
                "ProviderStub",
            ]
            : ["LoggingChatClient", "StreamAccumulator", "MarkerChatClient", "ProviderStub"];

        Assert.Equal(expected, chain.Select(candidate => candidate.GetType().Name));
    }

    [Fact]
    public void Constructor_throws_when_logger_factory_is_null()
    {
        Assert.Throws<ArgumentNullException>("loggerFactory", () => new ChatClientFactory(null!));
    }

    [Fact]
    public void Create_throws_when_model_is_null()
    {
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));

        Assert.Throws<ArgumentNullException>("model", () => factory.Create(null!));
    }

    [Theory]
    [InlineData("gemini")]
    [InlineData("Gemini")]
    public void Create_throws_NotSupportedException_for_unknown_provider(string provider)
    {
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var model = new ModelInfo("gemini-2.5-pro", provider, null, 128_000, true);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            factory.Create(model)
        );

        Assert.Contains(provider, exception.Message, StringComparison.Ordinal);
        Assert.Contains("openai", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anthropic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_assigns_the_model_endpoint_to_the_provider_client()
    {
        using var environment = new EnvironmentScope(
            (OpenAiApiKeyVariable, "test-key"),
            (OpenTelemetryOptInVariable, null),
            (OtlpEndpointVariable, null)
        );
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var endpoint = new Uri("https://example.test/v1");
        var model = new ModelInfo("gpt-4o-mini", "openai", endpoint, 128_000, true);

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal(endpoint, metadata.ProviderUri);
    }

    [Fact]
    public void Create_throws_InvalidOperationException_when_openai_api_key_is_not_set()
    {
        using var environment = new EnvironmentScope((OpenAiApiKeyVariable, null));

        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var model = new ModelInfo("gpt-4o-mini", "OpenAI", null, 128_000, true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Create(model)
        );

        Assert.Contains(OpenAiApiKeyVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains("T-16", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_with_a_custom_endpoint_and_no_openai_api_key_uses_a_placeholder_credential()
    {
        using var environment = new EnvironmentScope((OpenAiApiKeyVariable, null));
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var endpoint = new Uri("http://localhost:11434/v1");
        var model = new ModelInfo("local-llama", "openai", endpoint, 8192, true);

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal(endpoint, metadata.ProviderUri);
    }

    [Fact]
    public void Create_openai_client_options_disable_transport_retries()
    {
        OpenAIClientOptions options = ChatClientFactory.CreateOpenAiClientOptions(TestModel());

        ClientRetryPolicy policy = Assert.IsType<ClientRetryPolicy>(options.RetryPolicy);
        FieldInfo? maxRetries = typeof(ClientRetryPolicy).GetField(
            "_maxRetries",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(maxRetries);
        Assert.Equal(0, maxRetries.GetValue(policy));
    }

    [Fact]
    public void Create_with_anthropic_provider_builds_the_client_without_network()
    {
        using var environment = new EnvironmentScope(
            (AnthropicApiKeyVariable, "anthropic-test-key")
        );
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var model = new ModelInfo("claude-sonnet-5-5", "Anthropic", null, 1_000_000, true);

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal("anthropic", metadata.ProviderName);
        Assert.Equal(model.Id, metadata.DefaultModelId);
    }

    [Fact]
    public void Create_assigns_the_model_endpoint_to_the_anthropic_base_url()
    {
        using var environment = new EnvironmentScope(
            (AnthropicApiKeyVariable, "anthropic-test-key")
        );
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var endpoint = new Uri("https://anthropic.example.test");
        var model = new ModelInfo("claude-sonnet-5-5", "anthropic", endpoint, 1_000_000, true);

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal(endpoint, metadata.ProviderUri);
    }

    [Fact]
    public void Create_anthropic_client_disables_transport_retries()
    {
        using var environment = new EnvironmentScope(
            (AnthropicApiKeyVariable, "anthropic-test-key")
        );
        var model = new ModelInfo(
            "claude-sonnet-5-5",
            "anthropic",
            new Uri("https://anthropic.example.test"),
            1_000_000,
            true
        );

        AnthropicClient client = ChatClientFactory.CreateAnthropicClient(model);

        Assert.Equal(0, client.MaxRetries);
        Assert.Equal("anthropic-test-key", client.ApiKey);
        Assert.Equal(model.Endpoint!.AbsoluteUri, client.BaseUrl);
    }

    [Fact]
    public void Create_throws_InvalidOperationException_when_anthropic_api_key_is_not_set()
    {
        using var environment = new EnvironmentScope((AnthropicApiKeyVariable, null));
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var model = new ModelInfo("claude-sonnet-5-5", "anthropic", null, 1_000_000, true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Create(model)
        );

        Assert.Contains(AnthropicApiKeyVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains("T-16", exception.Message, StringComparison.Ordinal);
    }

    private static ChatClientFactory CreateFactory(
        List<string> sequence,
        bool enableOpenTelemetry
    ) =>
        new(
            new MarkingLoggerFactory(() => sequence.Add("logging")),
            enableOpenTelemetry,
            _ => new ProviderStub(sequence),
            inner => new MarkerChatClient("recorder", sequence, inner)
        );

    private static ChatClientFactory CreateFactoryWithoutExplicitTelemetry(List<string> sequence) =>
        new(
            new MarkingLoggerFactory(() => sequence.Add("logging")),
            providerClientFactory: _ => new ProviderStub(sequence),
            recorderDecorator: inner => new MarkerChatClient("recorder", sequence, inner)
        );

    private static ModelInfo TestModel() => new("gpt-4o-mini", "openai", null, 128_000, true);

    private static IEnumerable<IChatClient> WalkChain(IChatClient client)
    {
        IChatClient? current = client;
        while (current is not null)
        {
            yield return current;
            current = InnerClientOf(current);
        }
    }

    private static IChatClient? InnerClientOf(IChatClient client)
    {
        for (Type? type = client.GetType(); type is not null; type = type.BaseType)
        {
            PropertyInfo? property = type.GetProperty(
                "InnerClient",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (property is not null)
            {
                return (IChatClient?)property.GetValue(client);
            }
        }

        return null;
    }

    private static ActivityListener ListenForTelemetry(List<string> sequence)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                source.Name.Contains("Microsoft.Extensions.AI", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStarted = _ => sequence.Add("telemetry"),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class MarkerChatClient(string marker, List<string> sequence, IChatClient inner)
        : DelegatingChatClient(inner)
    {
        public IChatClient Inner => InnerClient;

        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            sequence.Add(marker);
            return base.GetResponseAsync(messages, options, cancellationToken);
        }
    }

    private sealed class ProviderStub(List<string> sequence) : IChatClient
    {
        public void Dispose() { }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            sequence.Add("provider");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "stub")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
