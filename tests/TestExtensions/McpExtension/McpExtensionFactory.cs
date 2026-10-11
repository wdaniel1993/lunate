using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace McpExtension;

public sealed class McpExtension : IExtension;

public sealed class McpExtensionFactory : IExtensionFactory
{
    private const string MissingCommand = "lunate-mcp-fixture-command-that-cannot-exist";

    public IExtension Create(IExtensionContext context)
    {
        string command = ReadString(context, "command") ?? MissingCommand;
        string serverName = ReadString(context, "serverName") ?? "test";
        string? marker = ReadString(context, "marker");
        IReadOnlyDictionary<string, string>? environment = marker is null
            ? null
            : new Dictionary<string, string> { ["LUNATE_TEST_MCP_MARKER"] = marker };

        context.RegisterMcpServer(
            new McpServerRegistration(
                serverName,
                new McpServerConfig(command, ReadStrings(context, "arguments"), environment),
                ToolExposure.Direct,
                new Dictionary<string, ToolExposure> { ["echo"] = ToolExposure.Hidden }
            )
        );
        return new McpExtension();
    }

    private static string? ReadString(IExtensionContext context, string path) =>
        context.Settings.TryGet(path, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadStrings(IExtensionContext context, string path) =>
        context.Settings.TryGet(path, out JsonElement value)
        && value.ValueKind == JsonValueKind.Array
            ?
            [
                .. value
                    .EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!),
            ]
            : [];
}
