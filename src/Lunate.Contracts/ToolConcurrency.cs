namespace Lunate.Agent;

/// <summary>A tool's scheduling intent for the future parallel executor.</summary>
public enum ToolConcurrency
{
    /// <summary>Calls may run concurrently; the default.</summary>
    Parallel,

    /// <summary>Calls should be scheduled one at a time.</summary>
    Sequential,
}
