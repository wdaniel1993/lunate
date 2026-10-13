using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// Adds composed tools to a session's registry, skipping — with one single-line diagnostic naming
/// both sources — any tool whose model-facing name is already taken. Extension tools compose first,
/// so an extension tool wins over a later MCP tool with the same name; the registry's own duplicate
/// throw stays as the second line of defence (review of add-extension-tools, finding 2).
/// </summary>
internal static class ToolComposition
{
    internal static void AddSkippingTaken(
        ToolRegistry tools,
        IEnumerable<ITool> candidates,
        string source,
        Action<string>? warn
    )
    {
        foreach (ITool tool in candidates)
        {
            if (tools.Find(tool.Name) is { } existing)
            {
                warn?.Invoke(Diagnostic(tool, source, Describe(existing)));
                continue;
            }

            tools.Add(tool);
        }
    }

    /// <summary>The same rule while composing a plain list (the ACP host composes before its registry exists).</summary>
    internal static void AddSkippingTaken(
        List<ITool> tools,
        IEnumerable<ITool> candidates,
        string source,
        Action<string>? warn
    )
    {
        foreach (ITool tool in candidates)
        {
            if (
                tools.Find(existing =>
                    string.Equals(existing.Name, tool.Name, StringComparison.Ordinal)
                ) is
                { } taken
            )
            {
                warn?.Invoke(Diagnostic(tool, source, Describe(taken)));
                continue;
            }

            tools.Add(tool);
        }
    }

    private static string Diagnostic(ITool skipped, string source, string existing) =>
        $"tool '{skipped.Name}' from {source} skipped: the name is already provided by {existing} — first composition wins.";

    private static string Describe(ITool tool) =>
        tool.Namespace is { } group ? $"'{tool.Name}' ({group.Name})" : $"'{tool.Name}'";
}
