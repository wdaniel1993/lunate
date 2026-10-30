using Lunate.Agent;

namespace Lunate.Protocols;

/// <summary>Tracks no-user elicitation cancellations while one MCP tool call is in flight.</summary>
internal sealed class McpElicitationOutcome
{
    private int version;
    private long interactionId;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        long,
        Func<IReadOnlyList<UserQuestion>, CancellationToken, Task<UserQuestionResult>>
    > interactions = new();

    internal int Version => Volatile.Read(ref version);

    internal void RecordUnavailable() => Interlocked.Increment(ref version);

    internal bool WasRecordedSince(int previousVersion) => Version > previousVersion;

    internal IDisposable? UseInteraction(
        Func<IReadOnlyList<UserQuestion>, CancellationToken, Task<UserQuestionResult>>? interaction
    )
    {
        if (interaction is null)
        {
            return null;
        }

        long id = Interlocked.Increment(ref interactionId);
        interactions[id] = interaction;
        return new InteractionLease(this, id);
    }

    internal Func<
        IReadOnlyList<UserQuestion>,
        CancellationToken,
        Task<UserQuestionResult>
    >? CurrentInteraction => interactions.OrderBy(pair => pair.Key).FirstOrDefault().Value;

    private sealed class InteractionLease(McpElicitationOutcome owner, long id) : IDisposable
    {
        public void Dispose() => owner.interactions.TryRemove(id, out _);
    }
}
