using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Protocols;
using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

[Collection("mcp-processes")]
public sealed class McpNameCollisionsTests
{
    [Fact]
    public void A_registration_sharing_a_configured_name_is_shadowed()
    {
        IReadOnlyDictionary<string, string?> configured = new Dictionary<string, string?>
        {
            ["test"] = "/store/mcp.json",
        };

        (IReadOnlyList<RegisteredMcpServer> kept, IReadOnlyList<ShadowedMcpServer> shadowed) =
            McpNameCollisions.Filter(
                [Registration("test", "ext-a"), Registration("other", "ext-b")],
                configured
            );

        RegisteredMcpServer remaining = Assert.Single(kept);
        Assert.Equal("other", remaining.Name);
        ShadowedMcpServer entry = Assert.Single(shadowed);
        Assert.Equal("test", entry.Name);
        Assert.Equal("ext-a", entry.ExtensionId);
        Assert.Equal("/store/mcp.json", entry.ConfiguredSource);
    }

    [Fact]
    public void Without_configured_servers_every_registration_is_kept()
    {
        IReadOnlyList<RegisteredMcpServer> registrations = [Registration("test", "ext")];

        (IReadOnlyList<RegisteredMcpServer> kept, IReadOnlyList<ShadowedMcpServer> shadowed) =
            McpNameCollisions.Filter(registrations, null);

        Assert.Same(registrations, kept);
        Assert.Empty(shadowed);
    }

    [Fact]
    public async Task Configured_servers_map_names_to_their_sources()
    {
        Assert.Null(McpNameCollisions.ConfiguredServers(null));

        using var temp = new TempDirectory();
        string source = temp.File("mcp.json");
        await using var set = new McpToolSet([
            new McpServerOptions(
                "test",
                TestMcpServer.DotnetPath,
                ["exec", TestMcpServer.ServerDll],
                new Dictionary<string, string>
                {
                    ["LUNATE_TEST_MCP_MARKER"] = temp.File("marker.txt"),
                },
                Source: source
            ),
        ]);

        IReadOnlyDictionary<string, string?>? configured = McpNameCollisions.ConfiguredServers(set);

        Assert.NotNull(configured);
        Assert.Equal(source, configured!["test"]);
    }

    private static RegisteredMcpServer Registration(string name, string extensionId) =>
        new(
            extensionId,
            new McpServerRegistration(name, new McpServerConfig("dotnet", ["exec", "x.dll"]))
        );
}
