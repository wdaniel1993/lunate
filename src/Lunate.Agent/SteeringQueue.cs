using System.Collections.Concurrent;

namespace Lunate.Agent;

/// <summary>
/// The steering seam: a thread-safe FIFO the frontend enqueues into while a run is active and the
/// loop drains before each model request (after the previous batch's tool results). The harness
/// never discards what it did not inject: messages left when the run ends stay queued for the
/// frontend to reclaim through <see cref="TryDequeue"/>.
/// </summary>
public sealed class SteeringQueue
{
    private readonly ConcurrentQueue<string> _messages = new();

    /// <summary>Adds one steering message; blank messages are rejected.</summary>
    public void Enqueue(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _messages.Enqueue(message);
    }

    /// <summary>The number of queued messages.</summary>
    public int Count => _messages.Count;

    /// <summary>Removes the oldest message when one is queued.</summary>
    public bool TryDequeue(out string? message) => _messages.TryDequeue(out message);
}
