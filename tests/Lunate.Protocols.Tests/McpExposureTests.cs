using Lunate.Agent;

namespace Lunate.Protocols.Tests;

public sealed class McpExposureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tools_carry_the_server_namespace_and_direct_exposure_by_default()
    {
        using var temp = new TempDirectory();
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(temp.File("marker.txt"))
        );

        var tools = await host.GetToolsAsync(Ct);

        Assert.NotEmpty(tools);
        Assert.All(
            tools,
            tool =>
            {
                Assert.Equal("server", tool.Namespace?.Name);
                Assert.Null(tool.Namespace?.Description);
                Assert.Null(tool.Namespace?.Instructions);
                Assert.Equal(ToolExposure.Direct, tool.Exposure);
            }
        );
    }

    [Fact]
    public async Task Namespace_name_follows_the_options_while_tool_names_keep_the_server_prefix()
    {
        using var temp = new TempDirectory();
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(temp.File("marker.txt")) with
            {
                NamespaceName = "custom",
            }
        );

        var tools = await host.GetToolsAsync(Ct);

        Assert.All(tools, tool => Assert.Equal("custom", tool.Namespace?.Name));
        Assert.All(
            tools,
            tool => Assert.StartsWith("server__", tool.Name, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Exposure_and_overrides_follow_the_options_and_nothing_is_logged()
    {
        using var temp = new TempDirectory();
        var logs = new List<string>();
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(temp.File("marker.txt")) with
            {
                Exposure = ToolExposure.ModelOnly,
                ExposureOverrides = new Dictionary<string, ToolExposure>
                {
                    ["echo"] = ToolExposure.Hidden,
                    ["add"] = ToolExposure.Programmatic,
                },
            },
            onToolsChanged: null,
            log: logs.Add
        );

        var tools = await host.GetToolsAsync(Ct);

        Assert.Equal(
            ToolExposure.Hidden,
            tools.Single(tool => tool.Name == "server__echo").Exposure
        );
        Assert.Equal(
            ToolExposure.Programmatic,
            tools.Single(tool => tool.Name == "server__add").Exposure
        );
        Assert.All(
            tools.Where(tool => tool.Name is not ("server__echo" or "server__add")),
            tool => Assert.Equal(ToolExposure.ModelOnly, tool.Exposure)
        );
        Assert.Empty(logs);
    }

    [Fact]
    public async Task An_override_naming_no_listed_tool_is_logged_and_the_rest_proceeds()
    {
        using var temp = new TempDirectory();
        var logs = new List<string>();
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(temp.File("marker.txt")) with
            {
                ExposureOverrides = new Dictionary<string, ToolExposure>
                {
                    ["echo"] = ToolExposure.Hidden,
                    ["no-such-tool"] = ToolExposure.Deferred,
                },
            },
            onToolsChanged: null,
            log: logs.Add
        );

        var tools = await host.GetToolsAsync(Ct);

        Assert.Contains(
            logs,
            line =>
                line.Contains("'no-such-tool'", StringComparison.Ordinal)
                && line.Contains("'server'", StringComparison.Ordinal)
        );
        Assert.Equal(
            ToolExposure.Hidden,
            tools.Single(tool => tool.Name == "server__echo").Exposure
        );
        Assert.Equal(
            ToolExposure.Direct,
            tools.Single(tool => tool.Name == "server__add").Exposure
        );
    }
}
