using Lunate.Agent;

namespace Lunate.Protocols;

/// <summary>Tracks no-user elicitation cancellations while one MCP tool call is in flight.</summary>
internal sealed class McpElicitationOutcome
{
    private int version;
    private long interactionId;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        long,
        InteractionLease
    > interactions = new();

    internal int Version => Volatile.Read(ref version);

    internal void RecordUnavailable() => Interlocked.Increment(ref version);

    internal bool WasRecordedSince(int previousVersion) => Version > previousVersion;

    internal InteractionLease UseInteraction(
        Func<IReadOnlyList<UserQuestion>, CancellationToken, Task<UserQuestionResult>>? interaction
    )
    {
        long id = Interlocked.Increment(ref interactionId);
        var lease = new InteractionLease(this, id, interaction);
        interactions[id] = lease;
        return lease;
    }

    internal InteractionLease? CurrentInteraction =>
        interactions.OrderBy(pair => pair.Key).FirstOrDefault().Value;

    internal sealed class InteractionLease(
        McpElicitationOutcome owner,
        long id,
        Func<IReadOnlyList<UserQuestion>, CancellationToken, Task<UserQuestionResult>>? interaction
    ) : IDisposable
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            int,
            string
        > elicitations = new();
        private int elicitationId;

        internal Func<
            IReadOnlyList<UserQuestion>,
            CancellationToken,
            Task<UserQuestionResult>
        >? Interaction { get; } = interaction;

        internal int BeginElicitation() => Interlocked.Increment(ref elicitationId);

        internal void CompleteElicitation(int id, string exchange) => elicitations[id] = exchange;

        internal IReadOnlyList<string> Elicitations =>
            elicitations.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();

        public void Dispose() => owner.interactions.TryRemove(id, out _);
    }
}
