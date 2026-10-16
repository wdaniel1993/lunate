using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>Writes mcp.json entries that point at the shared TestMcpServer.</summary>
internal static class McpTestServerSupport
{
    /// <summary>
    /// The mcp.json server entry for TestMcpServer, optionally writing a start marker and carrying
    /// one extra environment entry (the missing-variable tests). Every value is escaped for JSON —
    /// never interpolate a path into an entry by hand: a Windows temp path's backslashes make the
    /// document invalid JSON and the server silently disappears.
    /// </summary>
    internal static string EntryJson(
        string? markerPath = null,
        (string Name, string Value)? extraEnv = null
    )
    {
        List<string> entries = [];
        if (markerPath is not null)
        {
            entries.Add($"\"LUNATE_TEST_MCP_MARKER\": \"{Escape(markerPath)}\"");
        }

        if (extraEnv is { } extra)
        {
            entries.Add($"\"{Escape(extra.Name)}\": \"{Escape(extra.Value)}\"");
        }

        string env =
            entries.Count == 0 ? string.Empty : ", \"env\": { " + string.Join(", ", entries) + " }";
        return $$"""{ "command": "{{Escape(TestMcpServer.DotnetPath)}}", "args": ["exec", "{{Escape(TestMcpServer.ServerDll)}}"]{{env}} }""";
    }

    /// <summary>One complete temp mcp.json document configured with one TestMcpServer.</summary>
    internal static string ConfigJson(
        string name = "test",
        string? markerPath = null,
        (string Name, string Value)? extraEnv = null
    ) => $$"""{ "mcpServers": { "{{Escape(name)}}": {{EntryJson(markerPath, extraEnv)}} } }""";

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
