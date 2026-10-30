using Lunate.Agent;
using Lunate.Ai;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class QuestionPairPresentationTests
{
    [Fact]
    public async Task Interactive_transcript_keeps_question_and_MCP_pairs_in_order()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.HandleEvent(new RunStarted("run"));
        host.Session.HandleEvent(new ToolCallStart("run", "ask-call", "ask_user"));
        host.Session.HandleEvent(
            new ToolCallArgs("run", "ask-call", "{\"header\":\"Project plan\"}")
        );
        host.Session.HandleEvent(new ToolCallResult("run", "ask-call", "Answer: guided", false));
        host.Session.HandleEvent(new ToolCallStart("run", "mcp-call", "catalog__lookup"));
        host.Session.HandleEvent(
            new ToolCallArgs("run", "mcp-call", "{\"query\":\"project templates\"}")
        );
        host.Session.HandleEvent(
            new ToolCallResult(
                "run",
                "mcp-call",
                "MCP elicitation from server 'catalog'\nQuestion: Choose a template.\nAnswer: {\"template\":\"starter\"}",
                false
            )
        );
        host.Advance(40);

        TranscriptToolEntry[] entries = host
            .Session.Transcript.OfType<TranscriptToolEntry>()
            .ToArray();
        Assert.Equal(
            ["ask_user", "catalog__lookup"],
            entries.Select(entry => entry.Block.ToolName)
        );
        Assert.Contains("Project plan", entries[0].Block.ArgsSummary, StringComparison.Ordinal);
        Assert.Equal("Answer: guided", entries[0].Block.Output);
        Assert.Contains("MCP elicitation from server 'catalog'", entries[1].Block.Output);
        Assert.Contains("Question: Choose a template.", entries[1].Block.Output);
        Assert.Contains("Answer: {\"template\":\"starter\"}", entries[1].Block.Output);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public void Jsonl_export_keeps_question_and_MCP_pairs_in_order()
    {
        using var temp = new TempDirectory();
        Session session = BuildSession(temp);
        string destination = temp.File("questions.jsonl");

        SessionExporter.Write(
            Session.Load(session.Path),
            new ExportRequest(ExportFormat.Jsonl, destination, Force: false),
            destination,
            "questions",
            CredentialRedactor.Empty
        );

        string jsonl = File.ReadAllText(destination);
        AssertInOrder(
            jsonl,
            "ask_user",
            "Answer: guided",
            "catalog__lookup",
            "MCP elicitation from server",
            "Question: Choose a template.",
            "Answer: {"
        );
    }

    [Fact]
    public void Html_export_keeps_question_and_MCP_pairs_in_order()
    {
        using var temp = new TempDirectory();
        Session session = BuildSession(temp);

        string html = SessionHtmlWriter.Write(
            Session.Load(session.Path),
            "questions",
            CredentialRedactor.Empty
        );

        AssertInOrder(
            html,
            "ask_user",
            "Answer: guided",
            "catalog__lookup",
            "MCP elicitation from server &#39;catalog&#39;",
            "Question: Choose a template.",
            "Answer: {&quot;template&quot;:&quot;starter&quot;}"
        );
    }

    private static Session BuildSession(TempDirectory temp)
    {
        Session session = Session.Create(temp.File("questions.jsonl"), temp.Root);
        session.AppendMessage(new ChatMessage(ChatRole.User, "Choose a project plan."));
        session.AppendMessage(
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
        session.AppendMessage(
            new ChatMessage(
                ChatRole.Tool,
                [new FunctionResultContent("ask-call", "Answer: guided")]
            )
        );
        session.AppendMessage(
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
        session.AppendMessage(
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
        return session;
    }

    private static void AssertInOrder(string text, params string[] values)
    {
        int previous = -1;
        foreach (string value in values)
        {
            int next = text.IndexOf(value, StringComparison.Ordinal);
            Assert.True(next > previous, $"'{value}' was not found after position {previous}.");
            previous = next;
        }
    }
}
