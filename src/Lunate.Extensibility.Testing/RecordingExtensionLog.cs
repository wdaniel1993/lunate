using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Testing;

public enum ExtensionLogLevel
{
    Info,
    Warn,
    Error,
}

/// <summary>One ordered log line an extension emitted through <see cref="IExtensionLog"/>.</summary>
public sealed record ExtensionLogEntry(ExtensionLogLevel Level, string Message);

/// <summary>Records every extension log line in emission order for assertions.</summary>
public sealed class RecordingExtensionLog : IExtensionLog
{
    private readonly List<ExtensionLogEntry> _entries = [];

    public IReadOnlyList<ExtensionLogEntry> Entries => _entries;

    /// <summary>The raw messages in emission order, without their level.</summary>
    public IReadOnlyList<string> Messages => [.. _entries.Select(entry => entry.Message)];

    public void Info(string message) => Add(ExtensionLogLevel.Info, message);

    public void Warn(string message) => Add(ExtensionLogLevel.Warn, message);

    public void Error(string message) => Add(ExtensionLogLevel.Error, message);

    public int Count(ExtensionLogLevel level, string? contains = null) =>
        _entries.Count(entry =>
            entry.Level == level
            && (contains is null || entry.Message.Contains(contains, StringComparison.Ordinal))
        );

    public bool Contains(string text) =>
        _entries.Any(entry => entry.Message.Contains(text, StringComparison.Ordinal));

    private void Add(ExtensionLogLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _entries.Add(new ExtensionLogEntry(level, message));
    }
}
