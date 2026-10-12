using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Testing;

namespace McpFromExtensionExtension.Tests;

/// <summary>The registration contract: the factory collects the server and starts nothing.</summary>
public sealed class McpFromExtensionRegistrationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registration_collects_the_server_and_starts_nothing()
    {
        using var temp = new TempDirectory();
        string marker = Path.Combine(temp.Root, "marker.txt");
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, marker);
        await host.StartAsync(Ct);

        RegisteredMcpServer registered = Assert.Single(host.Runner.McpServers.List());
        Assert.Equal("fake", registered.Name);
        Assert.Equal("mcp-from-extension", registered.ExtensionId);
        Assert.Equal("dotnet", registered.Registration.Config.Command);
        Assert.Contains(
            "FakeMcpServer.dll",
            string.Join("\n", registered.Registration.Config.Arguments),
            StringComparison.Ordinal
        );
        Assert.Equal(ToolExposure.Direct, registered.Registration.Exposure);
        Assert.Equal(ToolExposure.Hidden, registered.Registration.ExposureOverrides!["secret"]);
        Assert.Equal(marker, registered.Registration.Config.Environment!["LUNATE_FAKE_MCP_MARKER"]);
        Assert.False(File.Exists(marker), "registration must start nothing");
    }
}
