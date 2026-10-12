using Lunate.Agent;
using Lunate.Extensibility.Testing;

namespace McpFromExtensionExtension.Tests;

/// <summary>
/// The kit-level composition: namespaced tool names and exposures, one server start across
/// listings, and process ownership (stopping the host stops the server).
/// </summary>
public sealed class McpFromExtensionCompositionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Composition_lists_namespaced_tools_with_exposures_and_starts_once()
    {
        using var temp = new TempDirectory();
        string marker = Path.Combine(temp.Root, "marker.txt");
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, marker);

        IReadOnlyList<ExtensionMcpServerStatus> statuses = await host.ComposeMcpServersAsync(Ct);

        ExtensionMcpServerStatus status = Assert.Single(statuses);
        Assert.Equal("fake", status.Name);
        Assert.Equal(ExtensionMcpServerState.Started, status.State);
        Assert.Equal(4, status.ToolCount);
        Assert.Equal("extension 'mcp-from-extension'", status.Source);
        Assert.True(TestSupport.IsProcessAlive(TestSupport.StartedPid(marker)));

        Assert.Equal(
            ["fake__add", "fake__crash", "fake__echo", "fake__secret"],
            host.Tools.Tools.Select(tool => tool.Name).Order(StringComparer.Ordinal)
        );
        Assert.All(host.Tools.Tools, tool => Assert.Equal("fake", tool.Namespace?.Name));
        Assert.Equal(ToolExposure.Direct, Exposure(host, "fake__echo"));
        Assert.Equal(ToolExposure.Direct, Exposure(host, "fake__add"));
        Assert.Equal(ToolExposure.Direct, Exposure(host, "fake__crash"));
        Assert.Equal(ToolExposure.Hidden, Exposure(host, "fake__secret"));

        IReadOnlyList<ExtensionMcpServerStatus> again = await host.ComposeMcpServersAsync(Ct);

        Assert.Equal(ExtensionMcpServerState.Started, Assert.Single(again).State);
        Assert.Single(
            TestSupport.MarkerLines(marker),
            line => line.StartsWith("started pid=", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Stop_stops_the_server_process()
    {
        using var temp = new TempDirectory();
        string marker = Path.Combine(temp.Root, "marker.txt");
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, marker);

        await host.ComposeMcpServersAsync(Ct);
        int pid = TestSupport.StartedPid(marker);

        await host.StopAsync(Ct);

        await TestSupport.WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(10));
        Assert.False(TestSupport.IsProcessAlive(pid));
    }

    private static ToolExposure Exposure(ExtensionTestHost host, string name) =>
        host.Tools.Tools.Single(tool => tool.Name == name).Exposure;
}
