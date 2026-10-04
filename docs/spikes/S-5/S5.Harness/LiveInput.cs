namespace S5.Harness;

/// <summary>
/// Everything the live area reacts to. Variants merge these sources in their
/// own way: variant A writes them into one <c>Channel&lt;LiveInput&gt;</c>,
/// variant B merges observables of the same shapes.
/// </summary>
public enum ApprovalDecision
{
    Yes,
    No,
    Always,
}

public abstract record LiveInput
{
    public sealed record Event(AgentEvent Value) : LiveInput;

    public sealed record Key(ConsoleKeyInfo Value, TimeSpan Now) : LiveInput;

    public sealed record Approval(ApprovalDecision Decision) : LiveInput;

    public sealed record Resize : LiveInput;

    /// <summary>Frame-clock tick; carries no state, only the clock.</summary>
    public sealed record Frame : LiveInput;

    /// <summary>Test/runner rendezvous: completes once all earlier inputs are applied.</summary>
    public sealed record Drain(TaskCompletionSource Completion) : LiveInput;
}
