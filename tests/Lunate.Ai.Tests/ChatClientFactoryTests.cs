using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Lunate.Ai.Tests;

public sealed class ChatClientFactoryTests
{
    private const string OpenAiApiKeyVariable = "OPENAI_API_KEY";

    private static readonly PropertyInfo InnerClientProperty = typeof(DelegatingChatClient)
        .GetProperty("InnerClient", BindingFlags.Instance | BindingFlags.NonPublic)!;

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

    [Fact]
    public async Task Create_never_wraps_the_provider_in_function_invoking_chat_client()
    {
        var sequence = new List<string>();
        var factory = CreateFactory(sequence, enableOpenTelemetry: true);

        IChatClient client = factory.Create(TestModel());
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

        var visited = new List<string>();
        foreach (IChatClient layer in Walk(client))
        {
            visited.Add(layer.GetType().FullName ?? layer.GetType().Name);
        }

        Assert.DoesNotContain(visited, name => name.Contains("FunctionInvokingChatClient", StringComparison.Ordinal));
        Assert.Contains(visited, name => name.Contains("OpenTelemetryChatClient", StringComparison.Ordinal));
        Assert.Contains(visited, name => name.Contains("LoggingChatClient", StringComparison.Ordinal));
        Assert.Contains(visited, name => name.Contains(nameof(MarkerChatClient), StringComparison.Ordinal));
        Assert.Contains(visited, name => name.Contains(nameof(ProviderStub), StringComparison.Ordinal));
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
    public void Create_throws_InvalidOperationException_when_openai_api_key_is_not_set()
    {
        Assert.SkipWhen(
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OpenAiApiKeyVariable)),
            $"The {OpenAiApiKeyVariable} environment variable is set; the missing-key path cannot be exercised.");

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

    private static IEnumerable<IChatClient> Walk(IChatClient client)
    {
        for (IChatClient? current = client; current is not null; current = GetInnerClient(current))
        {
            yield return current;
        }
    }

    private static IChatClient? GetInnerClient(IChatClient client) =>
        client is DelegatingChatClient delegating
            ? InnerClientProperty.GetValue(delegating) as IChatClient
            : null;

    private sealed class MarkerChatClient(string marker, List<string> sequence, IChatClient inner)
        : DelegatingChatClient(inner)
    {
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
