namespace Lunate.Agent;

/// <summary>How dangerous a tool is, for the approval policy.</summary>
public enum ToolRisk
{
    /// <summary>Observes only; runs without approval.</summary>
    ReadOnly,

    /// <summary>Changes files or other state; the policy may ask for approval.</summary>
    Write,

    /// <summary>Runs commands or other processes; the policy may ask for approval.</summary>
    Execute,
}
