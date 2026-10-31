namespace Lunate.Agent;

/// <summary>Derives a tool's risk from its annotations when the tool does not pin one explicitly.</summary>
public static class ToolRiskResolver
{
    /// <summary>
    /// The safest reading of the annotations. A tool that declares nothing runs unknown code, so it
    /// is an execute; a destructive or open-world hint is an execute even beside read-only, because
    /// a hint that contradicts itself must not downgrade the call; only a plain read-only hint is
    /// read-only. Everything else is a write.
    /// </summary>
    public static ToolRisk FromAnnotations(ToolAnnotations? annotations) =>
        annotations switch
        {
            null => ToolRisk.Execute,
            { Destructive: true } or { OpenWorld: true } => ToolRisk.Execute,
            { ReadOnly: true } => ToolRisk.ReadOnly,
            _ => ToolRisk.Write,
        };
}
