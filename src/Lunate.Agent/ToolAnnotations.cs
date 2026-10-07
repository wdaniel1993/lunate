namespace Lunate.Agent;

/// <summary>
/// MCP-style tool annotations. All default to false; add a hint only when the tool declares it.
/// </summary>
public sealed record ToolAnnotations(
    bool ReadOnly = false,
    bool Destructive = false,
    bool Idempotent = false,
    bool OpenWorld = false
);

/// <summary>
/// The stable string form of <see cref="ToolAnnotations"/>: lowercase kebab wire names in
/// declaration order. The single source for every layer that serializes or exposes annotations.
/// </summary>
public static class ToolAnnotationNames
{
    /// <summary>The declared annotations in declaration order; empty when none are declared.</summary>
    public static IReadOnlyList<string> WireNames(ToolAnnotations? annotations)
    {
        if (annotations is null)
        {
            return [];
        }

        List<string> names = [];
        if (annotations.ReadOnly)
        {
            names.Add("read-only");
        }

        if (annotations.Destructive)
        {
            names.Add("destructive");
        }

        if (annotations.Idempotent)
        {
            names.Add("idempotent");
        }

        if (annotations.OpenWorld)
        {
            names.Add("open-world");
        }

        return names;
    }
}
