using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Protocols.Tests;

public sealed class McpToolServerRootTests
{
    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static async Task InitializeWithRootsAsync(McpWire wire, bool listChanged) =>
        await wire.RequestAsync(
            1,
            "initialize",
            new
            {
                protocolVersion = McpToolServer.ProtocolVersion,
                clientInfo = new { name = "test-client", version = "0.0.1" },
                capabilities = new { roots = new { listChanged } },
            }
        );

    [Fact]
    public async Task A_client_roots_list_selects_the_tools_before_they_are_listed()
    {
        using var temp = new TempDirectory();
        string clientRoot = Path.Combine(temp.Root, "client-root");
        Directory.CreateDirectory(clientRoot);
        File.WriteAllText(Path.Combine(clientRoot, "App.csproj"), "<Project />");
        var echo = new DeclaredTool(
            "echo",
            "Echo the input text back.",
            """{"type":"object","properties":{}}""",
            _ => new ToolResult("echo", false),
            readOnly: true
        );
        IReadOnlyList<string>? evaluatedRoots = null;
        await using var wire = McpWire.Start(
            [echo],
            temp.Root,
            roots: [new Uri(clientRoot + Path.DirectorySeparatorChar).AbsoluteUri],
            resolveToolSet: roots =>
            {
                evaluatedRoots = roots;
                return new McpWorkspaceToolSet(
                    roots?.Contains(clientRoot, PathComparer) == true ? [echo] : [],
                    clientRoot
                );
            }
        );

        await InitializeWithRootsAsync(wire, listChanged: true);
        JsonElement list = await wire.RequestAsync(2, "tools/list", new { });

        Assert.Equal([clientRoot], evaluatedRoots);
        Assert.Equal(
            ["echo"],
            list.GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
        );
    }

    [Fact]
    public async Task A_supported_roots_change_recomputes_the_tools_and_notifies_the_client()
    {
        using var temp = new TempDirectory();
        string clientRoot = Path.Combine(temp.Root, "client-root");
        Directory.CreateDirectory(clientRoot);
        File.WriteAllText(Path.Combine(clientRoot, "App.csproj"), "<Project />");
        var echo = new DeclaredTool(
            "echo",
            "Echo the input text back.",
            """{"type":"object","properties":{}}""",
            _ => new ToolResult("echo", false),
            readOnly: true
        );
        int evaluations = 0;
        await using var wire = McpWire.Start(
            [echo],
            temp.Root,
            roots: [new Uri(clientRoot + Path.DirectorySeparatorChar).AbsoluteUri],
            resolveToolSet: roots =>
            {
                evaluations++;
                return new McpWorkspaceToolSet(
                    roots?.Contains(clientRoot, PathComparer) == true ? [echo] : [],
                    roots?.FirstOrDefault() ?? temp.Root
                );
            }
        );
        await InitializeWithRootsAsync(wire, listChanged: true);
        JsonElement before = await wire.RequestAsync(2, "tools/list", new { });
        Assert.Single(before.GetProperty("result").GetProperty("tools").EnumerateArray());

        wire.Roots = [];
        await wire.SendNotificationAsync("notifications/roots/list_changed");
        JsonElement changed = await wire.WaitForNotificationAsync(
            "notifications/tools/list_changed"
        );
        JsonElement after = await wire.RequestAsync(3, "tools/list", new { });

        Assert.Equal(2, evaluations);
        Assert.Equal("notifications/tools/list_changed", changed.GetProperty("method").GetString());
        Assert.Empty(after.GetProperty("result").GetProperty("tools").EnumerateArray());
    }

    [Fact]
    public async Task A_roots_change_does_not_recompute_or_notify_without_client_support()
    {
        using var temp = new TempDirectory();
        string clientRoot = Path.Combine(temp.Root, "client-root");
        Directory.CreateDirectory(clientRoot);
        File.WriteAllText(Path.Combine(clientRoot, "App.csproj"), "<Project />");
        var echo = new DeclaredTool(
            "echo",
            "Echo the input text back.",
            """{"type":"object","properties":{}}""",
            _ => new ToolResult("echo", false),
            readOnly: true
        );
        int evaluations = 0;
        await using var wire = McpWire.Start(
            [echo],
            temp.Root,
            roots: [new Uri(clientRoot + Path.DirectorySeparatorChar).AbsoluteUri],
            resolveToolSet: roots =>
            {
                evaluations++;
                return new McpWorkspaceToolSet(
                    roots?.Contains(clientRoot, PathComparer) == true ? [echo] : [],
                    clientRoot
                );
            }
        );
        await InitializeWithRootsAsync(wire, listChanged: false);
        JsonElement before = await wire.RequestAsync(2, "tools/list", new { });
        Assert.Single(before.GetProperty("result").GetProperty("tools").EnumerateArray());

        wire.Roots = [];
        await wire.SendNotificationAsync("notifications/roots/list_changed");
        JsonElement after = await wire.RequestAsync(3, "tools/list", new { });

        Assert.Equal(1, evaluations);
        Assert.Single(after.GetProperty("result").GetProperty("tools").EnumerateArray());
        Assert.DoesNotContain(
            wire.Notifications,
            notification =>
                notification.GetProperty("method").GetString() == "notifications/tools/list_changed"
        );
    }
}
