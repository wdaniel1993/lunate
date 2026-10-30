using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class QuestionSessionReplayTests
{
    [Fact]
    public void Question_and_MCP_elicitation_pairs_replay_in_append_order()
    {
        using var temp = new TempDirectory();
        string path = temp.File("questions.jsonl");
        Session recorded = Session.Create(path, "/work");
        recorded.AppendMessage(new ChatMessage(ChatRole.User, "Choose a project plan."));
        recorded.AppendMessage(
            new ChatMessage(
                ChatRole.Assistant,
                [
                    new FunctionCallContent(
                        "ask-call",
                        "ask_user",
                        new Dictionary<string, object?> { ["header"] = "Project plan" }
                    ),
                ]
            )
        );
        recorded.AppendMessage(
            new ChatMessage(
                ChatRole.Tool,
                [new FunctionResultContent("ask-call", "Answer: guided")]
            )
        );
        recorded.AppendMessage(
            new ChatMessage(
                ChatRole.Assistant,
                [
                    new FunctionCallContent(
                        "mcp-call",
                        "catalog__lookup",
                        new Dictionary<string, object?> { ["query"] = "project templates" }
                    ),
                ]
            )
        );
        recorded.AppendMessage(
            new ChatMessage(
                ChatRole.Tool,
                [
                    new FunctionResultContent(
                        "mcp-call",
                        "MCP elicitation from server 'catalog'\nQuestion: Choose a template.\nAnswer: {\"template\":\"starter\"}"
                    ),
                ]
            )
        );

        Session replayed = Session.Load(path);
        List<ChatMessage> history = replayed.ToHistory();

        Assert.Equal(recorded.Schema, replayed.Schema);
        Assert.Equal(
            ["user", "assistant", "tool", "assistant", "tool"],
            history.Select(message => message.Role.Value)
        );
        FunctionCallContent ask = Assert.IsType<FunctionCallContent>(
            Assert.Single(history[1].Contents)
        );
        FunctionResultContent askResult = Assert.IsType<FunctionResultContent>(
            Assert.Single(history[2].Contents)
        );
        FunctionCallContent mcp = Assert.IsType<FunctionCallContent>(
            Assert.Single(history[3].Contents)
        );
        FunctionResultContent mcpResult = Assert.IsType<FunctionResultContent>(
            Assert.Single(history[4].Contents)
        );
        Assert.Equal("ask_user", ask.Name);
        Assert.Equal("ask-call", askResult.CallId);
        Assert.Equal("Answer: guided", askResult.Result?.ToString());
        Assert.Equal("catalog__lookup", mcp.Name);
        Assert.Equal("mcp-call", mcpResult.CallId);
        Assert.Equal(
            "MCP elicitation from server 'catalog'\nQuestion: Choose a template.\nAnswer: {\"template\":\"starter\"}",
            mcpResult.Result?.ToString()
        );
        Assert.Equal(SessionFormat.SchemaVersion, replayed.Schema);
    }
}
