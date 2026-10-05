using Microsoft.Extensions.Logging;

namespace Lunate.Ai.Tests;

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
