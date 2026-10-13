using Lunate.Extensibility;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// Composition order in a real session: extension tools are declared before MCP tools (design D7).
/// </summary>
[Collection("mcp-processes")]
public sealed class ExtensionToolCompositionTests
{
    [Fact]
    public async Task Extension_tools_are_declared_before_mcp_tools()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        ToolFixture.Install(store);
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("test"));
        using var host = new InteractiveSessionHost(
            mcpConfigPath: config,
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() }
        );
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        string system = host
            .Client.Requests[0]
            .First(message => message.Role == ChatRole.System)
            .Text!;
        int extension = system.IndexOf("tools__echo", StringComparison.Ordinal);
        int mcp = system.IndexOf("test__echo", StringComparison.Ordinal);
        Assert.True(extension >= 0, "the extension tool is not declared:\n" + system);
        Assert.True(mcp >= 0, "the MCP tool is not declared:\n" + system);
        Assert.True(extension < mcp, "extension tools must come before MCP tools:\n" + system);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}
