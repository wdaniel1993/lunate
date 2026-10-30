using System.Text.Json;
using Lunate.Agent;
using Lunate.TestSupport;

namespace Lunate.Protocols.Tests;

public sealed class McpNoUserElicitationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Form_elicitation_cancels_without_content_and_records_the_unanswered_result()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        await using var host = new McpServerHost(McpTestEnvironment.Options(marker));
        ITool tool = (await host.GetToolsAsync(Ct)).Single(candidate =>
            candidate.Name == "server__elicit"
        );

        ToolResult result = await tool.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { }),
                McpTestEnvironment.Context,
                Ct
            )
            .WaitAsync(TimeSpan.FromSeconds(2), Ct);

        Assert.False(result.IsError);
        Assert.Contains("action=cancel;content=empty", result.Output, StringComparison.Ordinal);
        Assert.Contains("unanswered: no user present", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "State an assumption and continue",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Contains(
            TestMcpServer.MarkerLines(marker),
            line => line == "elicitation action=cancel;content=empty"
        );
    }
}
