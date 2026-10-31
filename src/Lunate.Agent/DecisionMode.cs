namespace Lunate.Agent;

/// <summary>How a configured decision profile participates in approval asks.</summary>
public enum DecisionMode
{
    /// <summary>Do not call the decision profile.</summary>
    Off,

    /// <summary>Record decision outcomes without changing the ordinary approval flow.</summary>
    Shadow,

    /// <summary>Escalate a decision denial to a prompt; unattended calls fail closed.</summary>
    Enforce,
}
