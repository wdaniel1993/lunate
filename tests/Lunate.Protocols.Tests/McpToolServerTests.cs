using System.Text.Json;
using Lunate.Agent;
using ModelContextProtocol.Protocol;

namespace Lunate.Protocols.Tests;

/// <summary>
/// The stdio server over <see cref="ITool"/>s: initialize and tools/list answer from the tool's own
/// declarations, tools/call runs the tool, and every stdout line is a JSON-RPC frame.
/// </summary>
public sealed class McpToolServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initialize_and_tools_list_answer_from_the_tool_declarations()
    {
        using var temp = new TempDirectory();
        var echo = new DeclaredTool(
            "echo",
            "Echo the input text back.",
            """
            {
              "type": "object",
              "properties": { "text": { "type": "string" } },
              "required": ["text"]
            }
            """,
            args => new ToolResult(args.GetProperty("text").GetString() ?? string.Empty, false),
            readOnly: true
        );
        await using var wire = McpWire.Start([echo], temp.Root);

        JsonElement initialize = await wire.RequestAsync(
            1,
            "initialize",
            new
            {
                protocolVersion = McpToolServer.ProtocolVersion,
                clientInfo = new { name = "test-client", version = "0.0.1" },
                capabilities = new { },
            }
        );

        JsonElement initResult = initialize.GetProperty("result");
        Assert.Equal(
            "test-server",
            initResult.GetProperty("serverInfo").GetProperty("name").GetString()
        );
        Assert.True(initResult.GetProperty("capabilities").TryGetProperty("tools", out _));

        JsonElement list = await wire.RequestAsync(2, "tools/list", new { });
        JsonElement listed = Assert.Single(
            list.GetProperty("result").GetProperty("tools").EnumerateArray()
        );
        Assert.Equal("echo", listed.GetProperty("name").GetString());
        Assert.Equal("Echo the input text back.", listed.GetProperty("description").GetString());
        Assert.True(
            listed
                .GetProperty("inputSchema")
                .GetProperty("properties")
                .TryGetProperty("text", out _)
        );
        Assert.True(listed.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
    }

    [Fact]
    public async Task A_tool_call_runs_the_tool_and_maps_its_result()
    {
        using var temp = new TempDirectory();
        JsonElement? seen = null;
        var echo = new DeclaredTool(
            "echo",
            "Echo the input text back.",
            """{"type":"object","properties":{"text":{"type":"string"}}}""",
            args =>
            {
                seen = args.Clone();
                return new ToolResult($"echo: {args.GetProperty("text").GetString()}", false);
            },
            readOnly: true
        );
        await using var wire = McpWire.Start([echo], temp.Root);

        JsonElement response = await wire.RequestAsync(
            1,
            "tools/call",
            new { name = "echo", arguments = new { text = "hi" } }
        );

        JsonElement result = response.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean());
        Assert.Equal("echo: hi", result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("hi", seen?.GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_failing_tool_call_comes_back_as_an_error_result()
    {
        using var temp = new TempDirectory();
        var failing = new DeclaredTool(
            "fail",
            "Always fails.",
            """{"type":"object","properties":{}}""",
            _ => new ToolResult("the tool failed on purpose", true),
            readOnly: true
        );
        await using var wire = McpWire.Start([failing], temp.Root);

        JsonElement response = await wire.RequestAsync(
            1,
            "tools/call",
            new { name = "fail", arguments = new { } }
        );

        JsonElement result = response.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal(
            "the tool failed on purpose",
            result.GetProperty("content")[0].GetProperty("text").GetString()
        );
    }

    [Fact]
    public async Task An_unknown_method_is_a_json_rpc_error_not_a_stdout_contamination()
    {
        using var temp = new TempDirectory();
        await using var wire = McpWire.Start([], temp.Root);

        JsonElement response = await wire.RequestAsync(1, "lunate/bogus", new { });

        Assert.Equal(-32601, response.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task A_tool_that_could_change_the_workspace_is_refused()
    {
        var write = new DeclaredTool(
            "write",
            "Writes files.",
            """{"type":"object","properties":{}}""",
            _ => new ToolResult("written", false)
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                McpToolServer.RunAsync(
                    [write],
                    "test-server",
                    "1.0.0",
                    "test instructions",
                    Path.GetTempPath(),
                    Stream.Null,
                    Stream.Null,
                    Ct
                )
        );

        Assert.Contains(
            "'write' does not declare readOnly",
            exception.Message,
            StringComparison.Ordinal
        );
    }
}
