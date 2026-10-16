using System.Collections.Concurrent;

namespace Lunate.Agent;

/// <summary>One queued steering entry: the message text and the skill invocation metadata when the
/// message was submitted by <c>/skill:&lt;name&gt;</c>.</summary>
internal sealed record SteeringMessage(string Text, SessionSkillMetadata? Skill);

/// <summary>
/// The steering seam: a thread-safe FIFO the frontend enqueues into while a run is active and the
/// loop drains before each model request (after the previous batch's tool results). The harness
/// never discards what it did not inject: messages left when the run ends stay queued for the
/// frontend to reclaim through <see cref="TryDequeue"/>.
/// </summary>
public sealed class SteeringQueue
{
    private readonly ConcurrentQueue<SteeringMessage> _messages = new();

    /// <summary>Adds one steering message; blank messages are rejected.</summary>
    public void Enqueue(string message) => Enqueue(message, skill: null);

    /// <summary>
    /// Adds one steering message carrying its skill invocation metadata; blank messages are
    /// rejected.
    /// </summary>
    public void Enqueue(string message, SessionSkillMetadata? skill)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _messages.Enqueue(new SteeringMessage(message, skill));
    }

    /// <summary>The number of queued messages.</summary>
    public int Count => _messages.Count;

    /// <summary>Removes the oldest message when one is queued.</summary>
    public bool TryDequeue(out string? message)
    {
        if (_messages.TryDequeue(out SteeringMessage? entry))
        {
            message = entry.Text;
            return true;
        }

        message = null;
        return false;
    }

    /// <summary>Removes the oldest message with its skill metadata when one is queued.</summary>
    internal bool TryDequeueMessage(out SteeringMessage? message) =>
        _messages.TryDequeue(out message);
}
