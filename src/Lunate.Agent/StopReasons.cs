namespace Lunate.Agent;

/// <summary>
/// The vocabulary for <see cref="RunFinished.StopReason"/>. The loop maps the final turn's model
/// finish reason: <c>ChatFinishReason.Length</c> to <see cref="Length"/>, everything else to
/// <see cref="Stop"/>.
/// </summary>
public static class StopReasons
{
    /// <summary>The model finished the turn without further tool calls.</summary>
    public const string Stop = "stop";

    /// <summary>The run was cancelled through its cancellation token.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The model reached its token limit and the turn was cut off.</summary>
    public const string Length = "length";

    /// <summary>The run reached the step limit (MaxSteps).</summary>
    public const string StepLimit = "step_limit";
}
