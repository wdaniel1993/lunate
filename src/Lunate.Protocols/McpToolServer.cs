using Lunate.Agent;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SdkServerOptions = ModelContextProtocol.Server.McpServerOptions;

namespace Lunate.Protocols;

/// <summary>
/// A stdio MCP server over a list of <see cref="ITool"/>: <c>initialize</c> and <c>tools/list</c>
/// answer from the tools' own declarations, <c>tools/call</c> runs the tool in-process. Stdout
/// carries JSON-RPC only; callers own their diagnostics and write them to stderr, matching the ACP
/// mode's purity contract. A tool that does not declare itself read-only is refused, because there
/// is no approval panel behind this server.
/// </summary>
internal static class McpToolServer
{
    /// <summary>The stable protocol revision this server negotiates.</summary>
    internal const string ProtocolVersion = "2025-11-25";

    internal static async Task RunAsync(
        IReadOnlyList<ITool> tools,
        string serverName,
        string serverVersion,
        string instructions,
        string workingDirectory,
        Stream input,
        Stream output,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var served = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (ITool tool in tools)
        {
            // There is no approval panel behind this server: a tool that can change the workspace
            // must not be exposable by accident.
            if (tool.Annotations?.ReadOnly is not true)
            {
                throw new InvalidOperationException(
                    $"the MCP server exposes read-only tools only; '{tool.Name}' does not declare readOnly"
                );
            }

            served.Add(new McpServerToolAdapter(tool, workingDirectory));
        }

        var options = new SdkServerOptions
        {
            ServerInfo = new Implementation { Name = serverName, Version = serverVersion },
            ServerInstructions = instructions,
            ProtocolVersion = ProtocolVersion,
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability { ListChanged = false },
            },
            ToolCollection = served,
        };

        await using var transport = new StreamServerTransport(input, output, serverName, null);
        await using var server = McpServer.Create(transport, options);
        await server.RunAsync(ct).ConfigureAwait(false);
    }
}
