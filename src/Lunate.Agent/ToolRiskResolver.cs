namespace Lunate.Agent;

/// <summary>Derives a tool's risk from its annotations when the tool does not pin one explicitly.</summary>
public static class ToolRiskResolver
{
    /// <summary>Read-only when the annotations declare it; a write otherwise.</summary>
    public static ToolRisk FromAnnotations(ToolAnnotations? annotations) =>
        annotations is { ReadOnly: true } ? ToolRisk.ReadOnly : ToolRisk.Write;
}
