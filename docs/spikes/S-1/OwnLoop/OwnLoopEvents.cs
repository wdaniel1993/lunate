using System.Text.Json;

namespace Spike.OwnLoop;

public enum RunStopReason
{
    Completed,
    Cancelled,
    StepLimit,
}

public enum ApprovalDecision
{
    Deny,
    AllowOnce,
    AllowForSession,
}

public abstract record OwnLoopEvent
{
    public abstract string Line { get; }
}

public sealed record RunStarted(string RunId) : OwnLoopEvent
{
    public override string Line => $"run_started {this.RunId}";
}

public sealed record TextMessageStart(string MessageId) : OwnLoopEvent
{
    public override string Line => $"text_message_start {this.MessageId}";
}

public sealed record TextMessageContent(string MessageId, string Delta) : OwnLoopEvent
{
    public override string Line =>
        $"text_message_content {this.MessageId} {JsonSerializer.Serialize(this.Delta)}";
}

public sealed record TextMessageEnd(string MessageId) : OwnLoopEvent
{
    public override string Line => $"text_message_end {this.MessageId}";
}

public sealed record ToolCallStart(string CallId, string Name) : OwnLoopEvent
{
    public override string Line => $"tool_call_start {this.CallId} {this.Name}";
}

public sealed record ToolCallArgs(string CallId, string Json) : OwnLoopEvent
{
    public override string Line => $"tool_call_args {this.CallId} {this.Json}";
}

public sealed record ToolCallEnd(string CallId) : OwnLoopEvent
{
    public override string Line => $"tool_call_end {this.CallId}";
}

public sealed record ToolCallResult(string CallId, string Output, bool IsError) : OwnLoopEvent
{
    public override string Line =>
        $"tool_call_result {this.CallId} is_error={this.IsError.ToString().ToLowerInvariant()} {JsonSerializer.Serialize(this.Output)}";
}

public sealed record ApprovalRequested(string CallId, string Name, string ArgsJson) : OwnLoopEvent
{
    public override string Line => $"approval_requested {this.CallId} {this.Name} {this.ArgsJson}";
}

public sealed record ApprovalDecided(string CallId, ApprovalDecision Decision) : OwnLoopEvent
{
    public override string Line => $"approval_decided {this.CallId} {this.Decision}";
}

public sealed record SteeringInjected(string Text) : OwnLoopEvent
{
    public override string Line => $"steering_injected {JsonSerializer.Serialize(this.Text)}";
}

public sealed record UsageUpdated(long? InputTokens, long? OutputTokens) : OwnLoopEvent
{
    public override string Line =>
        $"usage_updated in={this.InputTokens?.ToString() ?? "-"} out={this.OutputTokens?.ToString() ?? "-"}";
}

public sealed record RunFinished(RunStopReason Reason) : OwnLoopEvent
{
    public override string Line => $"run_finished {this.Reason}";
}

public sealed record RunError(string Message) : OwnLoopEvent
{
    public override string Line => $"run_error {JsonSerializer.Serialize(this.Message)}";
}
