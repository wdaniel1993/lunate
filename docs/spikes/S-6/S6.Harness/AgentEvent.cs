namespace S6.Harness;

/// <summary>
/// Spike-local mirror of the replayed <c>Lunate.Agent</c> event surface. The
/// real contract is <c>IAsyncEnumerable&lt;AgentEvent&gt;</c>; the spike only
/// needs enough shapes to drive the live area. Copied verbatim from
/// <c>docs/spikes/S-5/S5.Harness/AgentEvent.cs</c> so the S-5 scenario stays
/// identical.
/// </summary>
public abstract record AgentEvent
{
    public sealed record RunStarted(string Model) : AgentEvent;

    public sealed record TextDelta(string Text) : AgentEvent;

    public sealed record ApprovalRequested(string Tool, string Arguments) : AgentEvent;

    public sealed record ToolStarted(string Tool) : AgentEvent;

    public sealed record ToolFinished(string Tool, bool Ok, string Summary) : AgentEvent;

    public sealed record Usage(long InputTokens, long OutputTokens, double ContextPercent)
        : AgentEvent;

    public sealed record RunCompleted : AgentEvent;

    public sealed record RunCancelled(string Reason) : AgentEvent;

    public sealed record RunFailed(string Message) : AgentEvent;
}
