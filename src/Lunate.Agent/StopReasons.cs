namespace Lunate.Agent;

/// <summary>
/// The vocabulary for <see cref="RunFinished.StopReason"/>. Model finish reasons
/// (<c>ChatFinishReason</c>) map to <see cref="Stop"/> for now; the loop (T-09) owns richer mapping.
/// </summary>
public static class StopReasons
{
    /// <summary>The model finished the turn without further tool calls.</summary>
    public const string Stop = "stop";

    /// <summary>The run was cancelled through its cancellation token.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The run reached the step limit (MaxSteps).</summary>
    public const string StepLimit = "step_limit";
}
