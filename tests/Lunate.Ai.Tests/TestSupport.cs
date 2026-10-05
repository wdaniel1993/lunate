using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Lunate.Ai.Tests;

internal sealed class NamedTool(string name) : AITool
{
    public override string Name => name;
}

internal sealed class ThrowingChatClient : IChatClient
{
    public bool Called { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Called = true;
        throw new InvalidOperationException("The provider client must not be called during replay.");
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Called = true;
        throw new InvalidOperationException("The provider client must not be called during replay.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal static class TestPaths
{
    public static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Could not find lunate.sln above {AppContext.BaseDirectory}.");
    }
}

internal sealed class EnvironmentScope : IDisposable
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

internal sealed class MarkingLoggerFactory(Action onFirstLog) : ILoggerFactory
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
