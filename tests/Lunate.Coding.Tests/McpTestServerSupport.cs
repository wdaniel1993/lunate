using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>Writes mcp.json entries that point at the shared TestMcpServer.</summary>
internal static class McpTestServerSupport
{
    /// <summary>The mcp.json server entry for TestMcpServer, optionally writing a start marker.</summary>
    internal static string EntryJson(string? markerPath = null) =>
        markerPath is null
            ? $$"""{ "command": "{{Escape(TestMcpServer.DotnetPath)}}", "args": ["exec", "{{Escape(TestMcpServer.ServerDll)}}"] }"""
            : $$"""{ "command": "{{Escape(TestMcpServer.DotnetPath)}}", "args": ["exec", "{{Escape(TestMcpServer.ServerDll)}}"], "env": { "LUNATE_TEST_MCP_MARKER": "{{Escape(markerPath)}}" } }""";

    /// <summary>One complete temp mcp.json document configured with one TestMcpServer.</summary>
    internal static string ConfigJson(string name = "test", string? markerPath = null) =>
        $$"""{ "mcpServers": { "{{Escape(name)}}": {{EntryJson(markerPath)}} } }""";

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
