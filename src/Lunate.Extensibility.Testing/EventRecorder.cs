using Lunate.Agent;

namespace Lunate.Extensibility.Testing;

/// <summary>
/// Collects the <see cref="AgentEvent"/>s of one or more runs and queries them by type and order.
/// The host feeds it; a tool may also emit through it (<see cref="IAgentEvents"/>).
/// </summary>
public sealed class EventRecorder : IAgentEvents
{
    private readonly List<AgentEvent> _events = [];

    /// <summary>Every recorded event in emission order.</summary>
    public IReadOnlyList<AgentEvent> Events => _events;

    public void Emit(AgentEvent agentEvent)
    {
        ArgumentNullException.ThrowIfNull(agentEvent);
        _events.Add(agentEvent);
    }

    /// <summary>All events of the given type, in emission order.</summary>
    public IReadOnlyList<T> Of<T>()
        where T : AgentEvent => [.. _events.OfType<T>()];

    /// <summary>The single event of the given type; throws when there is not exactly one.</summary>
    public T Single<T>()
        where T : AgentEvent => _events.OfType<T>().Single();

    /// <summary>The index of the first event of the given type; -1 when there is none.</summary>
    public int FirstIndex<T>()
        where T : AgentEvent => _events.FindIndex(agentEvent => agentEvent is T);

    /// <summary>Whether the first event of <typeparamref name="TFirst"/> precedes the first of <typeparamref name="TSecond"/>.</summary>
    public bool OccurredBefore<TFirst, TSecond>()
        where TFirst : AgentEvent
        where TSecond : AgentEvent
    {
        int first = FirstIndex<TFirst>();
        int second = FirstIndex<TSecond>();
        return first >= 0 && second >= 0 && first < second;
    }

    public void Clear() => _events.Clear();
}
