using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// Records every extension log line in emission order for assertions. Thread-safe: a handler runs
/// on its own thread while the test thread enumerates, and a plain <see cref="List{T}"/> would throw
/// "Collection was modified" from the assertion instead of reporting the real result.
/// </summary>
internal sealed class RecordingExtensionLog : IExtensionLog
{
    private readonly object _gate = new();
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }

    public void Info(string message) => Add($"info: {message}");

    public void Warn(string message) => Add($"warn: {message}");

    public void Error(string message) => Add($"error: {message}");

    /// <summary>Whether any recorded line contains <paramref name="text"/> (ordinal).</summary>
    public bool Contains(string text) =>
        Messages.Any(message => message.Contains(text, StringComparison.Ordinal));

    private void Add(string message)
    {
        lock (_gate)
        {
            _messages.Add(message);
        }
    }
}
