using System.Text.Json;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentToolOutputLimitTests
{
    [Fact]
    public void Options_default_the_tool_output_limit_to_the_shared_default()
    {
        var options = new AgentHarnessOptions();

        Assert.Equal(30_000, options.ToolOutputLimit);
        Assert.Equal(ToolOutput.DefaultLimit, options.ToolOutputLimit);
    }

    [Fact]
    public async Task A_small_limit_truncates_top_level_tool_output_before_the_history()
    {
        string longOutput = new('x', 1000);
        ScriptedTool tool = ReadTool(longOutput);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions { ToolOutputLimit = 100 }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.Equal(ToolOutput.Truncate(longOutput, 100), result.Output);
        Assert.Contains("characters truncated", result.Output, StringComparison.Ordinal);
        Assert.True(result.Output.Length < longOutput.Length);
        ChatMessage toolMessage = Assert.Single(
            client.Requests[1].Messages,
            message => message.Role == ChatRole.Tool
        );
        FunctionResultContent functionResult = Assert.IsType<FunctionResultContent>(
            Assert.Single(toolMessage.Contents)
        );
        Assert.Equal(result.Output, functionResult.Result);
    }

    [Fact]
    public async Task A_small_limit_truncates_nested_tool_output()
    {
        string longOutput = new('x', 1000);
        ScriptedTool inner = ReadTool(longOutput, "inner");
        ScriptedTool outer = NestedCaller("outer", "inner");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "outer", LoopScripts.Args(("x", "1"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(outer, inner),
            new AgentHarnessOptions { ToolOutputLimit = 100 }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult nested = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "call_1/1"
        );
        Assert.Contains("characters truncated", nested.Output, StringComparison.Ordinal);
        Assert.True(nested.Output.Length < longOutput.Length);
    }

    private static ScriptedTool NestedCaller(string name, string nestedName)
    {
        var tool = new ScriptedTool(name, $"Calls {nestedName}.", """{"type":"object"}""");
        tool.OnExecuteAsync = async (_, ctx, ct) =>
        {
            var execute =
                ctx.ExecuteToolAsync
                ?? throw new InvalidOperationException("The context has no nested executor.");
            ToolResult nested = await execute(nestedName, EmptyArgs(), ct);
            return new ToolResult($"outer saw: {nested.Output}", IsError: nested.IsError);
        };
        return tool;
    }

    private static JsonElement EmptyArgs()
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
