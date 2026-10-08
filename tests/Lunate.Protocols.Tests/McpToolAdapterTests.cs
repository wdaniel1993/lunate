using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Agent;

namespace Lunate.Protocols.Tests;

public sealed class McpToolAdapterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Add_round_trip_returns_text_and_structured_content()
    {
        using var temp = new TempDirectory();
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(temp.File("marker.txt"))
        );
        var add = (await host.GetToolsAsync(Ct)).Single(tool => tool.Name == "server__add");

        var result = await add.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { a = 2, b = 3 }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.False(result.IsError);
        Assert.Contains("5", result.Output, StringComparison.Ordinal);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal(5, result.StructuredContent.Value.GetProperty("sum").GetInt32());
    }

    [Fact]
    public async Task Boom_tool_returns_an_error_result_and_the_client_stays_usable()
    {
        using var temp = new TempDirectory();
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(temp.File("marker.txt"))
        );
        var tools = await host.GetToolsAsync(Ct);
        var boom = tools.Single(tool => tool.Name == "server__boom");

        var result = await boom.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.True(result.IsError);
        Assert.Contains("boom", result.Output, StringComparison.Ordinal);

        var echo = tools.Single(tool => tool.Name == "server__echo");
        var echoResult = await echo.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { text = "still here" }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.False(echoResult.IsError);
        Assert.Equal("still here", echoResult.Output);
    }

    [Fact]
    public async Task Slow_call_cancelled_with_the_turn_and_the_server_observes_it()
    {
        using var temp = new TempDirectory();
        var marker = temp.File("marker.txt");
        await using var host = new McpServerHost(McpTestEnvironment.Options(marker));
        var slow = (await host.GetToolsAsync(Ct)).Single(tool => tool.Name == "server__slow");

        using var cancellation = new CancellationTokenSource();
        var call = slow.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { seconds = 30 }),
            McpTestEnvironment.Context,
            cancellation.Token
        );

        await McpTestEnvironment.WaitForMarkerLineAsync(
            marker,
            "slow_started",
            TimeSpan.FromSeconds(15)
        );

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        await McpTestEnvironment.WaitForMarkerLineAsync(
            marker,
            "slow_cancelled",
            TimeSpan.FromSeconds(15)
        );
    }

    [Fact]
    public async Task Slow_call_times_out_as_an_error_result_and_the_client_stays_usable()
    {
        using var temp = new TempDirectory();
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(
                temp.File("marker.txt"),
                callTimeout: TimeSpan.FromMilliseconds(200)
            )
        );
        var tools = await host.GetToolsAsync(Ct);
        var slow = tools.Single(tool => tool.Name == "server__slow");

        var result = await slow.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { seconds = 30 }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.True(result.IsError);
        Assert.Contains("timed out", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("server", result.Output, StringComparison.Ordinal);

        var echo = tools.Single(tool => tool.Name == "server__echo");
        var echoResult = await echo.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { text = "still here" }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.False(echoResult.IsError);
        Assert.Equal("still here", echoResult.Output);
    }

    [Fact]
    public void Non_object_result_becomes_a_malformed_error_result()
    {
        var result = McpToolAdapter.MapCallResult("server", "tool", JsonNode.Parse("42"));

        AssertMalformed(result);
    }

    [Fact]
    public void Wrong_typed_content_becomes_a_malformed_error_result()
    {
        var result = McpToolAdapter.MapCallResult(
            "server",
            "tool",
            JsonNode.Parse("""{"content":"not-a-list"}""")
        );

        AssertMalformed(result);
    }

    [Fact]
    public void Non_object_structured_content_becomes_a_malformed_error_result()
    {
        var result = McpToolAdapter.MapCallResult(
            "server",
            "tool",
            JsonNode.Parse("""{"structuredContent":42}""")
        );

        AssertMalformed(result);
    }

    private static void AssertMalformed(ToolResult result)
    {
        Assert.True(result.IsError);
        Assert.Contains("server", result.Output, StringComparison.Ordinal);
        Assert.Contains("tool", result.Output, StringComparison.Ordinal);
        Assert.Contains("malformed", result.Output, StringComparison.OrdinalIgnoreCase);
    }
}
