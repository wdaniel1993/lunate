namespace Lunate.Agent;

/// <summary>How a configured decision profile participates in approval asks.</summary>
public enum DecisionMode
{
    /// <summary>Do not call the decision profile.</summary>
    Off,

    /// <summary>Record decision outcomes without changing the ordinary approval flow.</summary>
    Shadow,

    /// <summary>Allow a decision to veto an ask; the ordinary approver must still approve it.</summary>
    Enforce,
}
