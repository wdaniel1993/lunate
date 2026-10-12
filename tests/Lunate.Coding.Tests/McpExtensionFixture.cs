using System.Text.Json;

namespace Lunate.Coding.Tests;

/// <summary>
/// Installs the built McpExtension fixture in a temp store: it registers one MCP server driven by
/// settings (command, arguments, serverName, marker), so tests can point it at TestMcpServer.
/// </summary>
internal static class McpExtensionFixture
{
    internal static string SourceDirectory =>
        Path.Combine(AppContext.BaseDirectory, "TestExtensions", "McpExtension");

    internal static string Install(
        string storePath,
        string id = "mcp",
        string serverName = "test",
        string? markerPath = null
    )
    {
        string target = Path.Combine(storePath, "extensions", id);
        HelloFixture.CopyDirectory(SourceDirectory, target);
        File.WriteAllText(
            Path.Combine(target, "extension.json"),
            $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"^1.5.0","entryAssembly":"McpExtension.dll"}"""
        );
        string settingsDirectory = Path.Combine(storePath, "extensions-settings");
        Directory.CreateDirectory(settingsDirectory);
        File.WriteAllText(
            Path.Combine(settingsDirectory, id + ".json"),
            JsonSerializer.Serialize(
                new
                {
                    command = McpTestServerSupport.DotnetPath,
                    arguments = new[] { "exec", McpTestServerSupport.ServerDll },
                    marker = markerPath,
                    serverName,
                }
            )
        );
        return target;
    }
}
