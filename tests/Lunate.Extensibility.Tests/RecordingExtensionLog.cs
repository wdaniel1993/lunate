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

    public void Info(string message) => Add(message, $"info: {message}");

    public void Warn(string message) => Add(message, $"warn: {message}");

    public void Error(string message) => Add(message, $"error: {message}");

    /// <summary>
    /// Observes each line as it is logged, before the level prefix. A test uses it to act at a
    /// known point in a load (cancelling a token, for one).
    /// </summary>
    public Action<string>? OnMessage { get; init; }

    /// <summary>Whether any recorded line contains <paramref name="text"/> (ordinal).</summary>
    public bool Contains(string text) =>
        Messages.Any(message => message.Contains(text, StringComparison.Ordinal));

    private void Add(string raw, string message)
    {
        lock (_gate)
        {
            _messages.Add(message);
        }

        OnMessage?.Invoke(raw);
    }
}
