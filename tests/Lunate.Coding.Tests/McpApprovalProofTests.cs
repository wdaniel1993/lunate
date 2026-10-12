using System.Text.Json;

namespace Lunate.Coding.Tests;

/// <summary>
/// The approval proof with the real policy: every MCP tool carries risk Execute, so the default
/// <c>ask</c> policy refuses a call with an approval error result and <c>--yolo</c> runs it.
/// </summary>
[Collection("mcp-processes")]
public sealed class McpApprovalProofTests
{
    [Fact]
    public async Task The_default_ask_policy_refuses_an_mcp_call()
    {
        using var temp = new TempDirectory();
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson());
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "test__echo", Scripts.Args(("text", "round trip"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("Cannot."), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Echo it.",
            McpConfigPath = config,
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("Cannot.", output);
        Assert.Contains(
            "tool 'test__echo' denied: approval policy 'ask' (pass --yolo to run unattended)",
            errors,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Yolo_executes_the_same_mcp_call()
    {
        using var temp = new TempDirectory();
        string config = temp.File("mcp.json");
        File.WriteAllText(config, McpTestServerSupport.ConfigJson());
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "test__echo", Scripts.Args(("text", "round trip"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("Done."), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Echo it.",
            Json = true,
            Yolo = true,
            McpConfigPath = config,
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        JsonElement result = Assert.Single(
            PrintModeTestSupport.ParseLines(output),
            line => PrintModeTestSupport.TypeOf(line) == "tool_call_result"
        );
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Equal("round trip", result.GetProperty("output").GetString());
    }
}
