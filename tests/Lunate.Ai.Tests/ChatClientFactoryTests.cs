using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Lunate.Ai.Tests;

public sealed class ChatClientFactoryTests
{
    private const string OpenAiApiKeyVariable = "OPENAI_API_KEY";
    private const string OpenTelemetryOptInVariable = "LUNATE_OTEL";
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    [Fact]
    public async Task Create_with_telemetry_enabled_observes_telemetry_logging_recorder_provider_in_order()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        var factory = CreateFactory(sequence, enableOpenTelemetry: true);

        IChatClient client = factory.Create(TestModel());
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "telemetry", "logging", "recorder", "provider" }, sequence);
    }

    [Fact]
    public async Task Create_with_telemetry_disabled_observes_logging_recorder_provider_in_order()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        var factory = CreateFactory(sequence, enableOpenTelemetry: false);

        IChatClient client = factory.Create(TestModel());
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

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
            (OtlpEndpointVariable, null));

        IChatClient client = CreateFactoryWithoutExplicitTelemetry(sequence).Create(TestModel());
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("telemetry", sequence);
    }

    [Fact]
    public async Task Create_enables_telemetry_when_OTLP_endpoint_is_set()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        using var environment = new EnvironmentScope(
            (OpenTelemetryOptInVariable, null),
            (OtlpEndpointVariable, "http://localhost:4317"));

        IChatClient client = CreateFactoryWithoutExplicitTelemetry(sequence).Create(TestModel());
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("telemetry", sequence);
    }

    [Fact]
    public async Task Create_keeps_telemetry_disabled_without_environment_opt_in()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        using var environment = new EnvironmentScope(
            (OpenTelemetryOptInVariable, null),
            (OtlpEndpointVariable, null));

        IChatClient client = CreateFactoryWithoutExplicitTelemetry(sequence).Create(TestModel());
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("telemetry", sequence);
    }

    [Fact]
    public async Task Create_explicit_telemetry_disabled_overrides_environment_opt_in()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        using var environment = new EnvironmentScope(
            (OpenTelemetryOptInVariable, "1"),
            (OtlpEndpointVariable, null));

        IChatClient client = CreateFactory(sequence, enableOpenTelemetry: false).Create(TestModel());
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

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
            recorderDecorator: inner => recorder = new MarkerChatClient("recorder", sequence, inner));

        IChatClient client = factory.Create(TestModel());
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(recorder);
        Assert.IsType<ProviderStub>(recorder.Inner);
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
    [InlineData("anthropic")]
    [InlineData("Anthropic")]
    public void Create_throws_NotSupportedException_for_unknown_provider(string provider)
    {
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var model = new ModelInfo("claude-sonnet-4-5", provider, null, 200_000, true);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => factory.Create(model));

        Assert.Contains(provider, exception.Message, StringComparison.Ordinal);
        Assert.Contains("openai", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_assigns_the_model_endpoint_to_the_provider_client()
    {
        using var environment = new EnvironmentScope(
            (OpenAiApiKeyVariable, "test-key"),
            (OpenTelemetryOptInVariable, null),
            (OtlpEndpointVariable, null));
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

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => factory.Create(model));

        Assert.Contains(OpenAiApiKeyVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains("T-16", exception.Message, StringComparison.Ordinal);
    }

    private static ChatClientFactory CreateFactory(List<string> sequence, bool enableOpenTelemetry) =>
        new(
            new MarkingLoggerFactory(() => sequence.Add("logging")),
            enableOpenTelemetry,
            _ => new ProviderStub(sequence),
            inner => new MarkerChatClient("recorder", sequence, inner));

    private static ChatClientFactory CreateFactoryWithoutExplicitTelemetry(List<string> sequence) =>
        new(
            new MarkingLoggerFactory(() => sequence.Add("logging")),
            providerClientFactory: _ => new ProviderStub(sequence),
            recorderDecorator: inner => new MarkerChatClient("recorder", sequence, inner));

    private static ModelInfo TestModel() => new("gpt-4o-mini", "openai", null, 128_000, true);

    private static ActivityListener ListenForTelemetry(List<string> sequence)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name.Contains("Microsoft.Extensions.AI", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
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
            CancellationToken cancellationToken = default)
        {
            sequence.Add(marker);
            return base.GetResponseAsync(messages, options, cancellationToken);
        }
    }

    private sealed class ProviderStub(List<string> sequence) : IChatClient
    {
        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            sequence.Add("provider");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "stub")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly (string Name, string? Value)[] _previous;

        public EnvironmentScope(params (string Name, string? Value)[] variables)
        {
            _previous = [.. variables.Select(
                variable => (variable.Name, Environment.GetEnvironmentVariable(variable.Name)))];
            foreach ((string name, string? value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach ((string name, string? value) in _previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private sealed class MarkingLoggerFactory(Action onFirstLog) : ILoggerFactory
    {
        private readonly MarkingLogger _logger = new(onFirstLog);

        public ILogger CreateLogger(string categoryName) => _logger;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class MarkingLogger(Action onFirstLog) : ILogger
        {
            private int _logged;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (Interlocked.Exchange(ref _logged, 1) == 0)
                {
                    onFirstLog();
                }
            }
        }
    }
}
