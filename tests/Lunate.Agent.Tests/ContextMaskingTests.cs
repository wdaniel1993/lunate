using Lunate.Ai;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class ContextMaskingTests
{
    private const string Secret = "sk-live-context-hygiene-sentinel-187";
    private const string Marker = "[redacted:OPENAI_API_KEY]";

    [Fact]
    public async Task A_known_credential_never_reaches_the_request_event_session_or_spill()
    {
        using var temp = new TempDirectory();
        var redactor = new CredentialRedactor([("OPENAI_API_KEY", Secret)]);
        Session session = Session.Create(
            temp.File("sessions/s.jsonl"),
            temp.Root,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero))
        );
        session.Redactor = redactor;
        string output = Secret + "\n" + string.Join('\n', Enumerable.Repeat("build output", 100));
        var tool = new ScriptedTool(
            "read",
            "Returns a credential-bearing transcript.",
            """{"type":"object"}"""
        )
        {
            OnExecute = (_, _) =>
                new ToolResult(output, IsError: false)
                {
                    FullOutput = output + "\nfull transcript " + Secret,
                },
        };
        var scripted = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "secret.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            new ContextAccountingChatClient(scripted),
            Registry(tool),
            new AgentHarnessOptions
            {
                Session = session,
                ToolOutputLimit = 100,
                ToolOutputMask = redactor.Redact,
                WorkingDirectory = temp.Root,
            }
        );

        List<AgentEvent> events = await Run(harness);

        string requestText = ResultText(scripted.Requests[1]);
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        string sessionText = session
            .Entries.OfType<SessionMessageEntry>()
            .SelectMany(entry => entry.Message.Contents)
            .OfType<FunctionResultContent>()
            .Single()
            .Result!.ToString()!;
        string spill = File.ReadAllText(ToolOutputSpillTests.PathFromMarker(result.Output));

        foreach (string text in new[] { requestText, result.Output, sessionText, spill })
        {
            Assert.Contains(Marker, text, StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        }

        Assert.Contains("full transcript " + Marker, spill, StringComparison.Ordinal);
        Assert.Equal(Marker, redactor.Redact(Marker), StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_null_tool_output_mask_is_identity()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "plain.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(ReadTool("ordinary output")),
            new AgentHarnessOptions { WorkingDirectory = temp.Root }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Equal("ordinary output", Assert.Single(events.OfType<ToolCallResult>()).Output);
    }

    [Fact]
    public async Task The_tool_output_mask_runs_once_for_an_equal_full_output()
    {
        using var temp = new TempDirectory();
        int calls = 0;
        const string raw = "credential-sentinel";
        const string masked = "[redacted:credential]";
        var tool = new ScriptedTool("answer", "Returns output.", """{"type":"object"}""")
        {
            OnExecute = (_, _) => new ToolResult(raw, IsError: false) { FullOutput = raw },
        };
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "answer", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions
            {
                WorkingDirectory = temp.Root,
                ToolOutputMask = text =>
                {
                    calls++;
                    return text.Replace(raw, masked, StringComparison.Ordinal);
                },
            }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Equal(masked, Assert.Single(events.OfType<ToolCallResult>()).Output);
        Assert.Equal(1, calls);
    }

    private static string ResultText(ScriptedRequest request) =>
        Assert
            .IsType<FunctionResultContent>(
                Assert.Single(
                    request.Messages.Single(message => message.Role == ChatRole.Tool).Contents
                )
            )
            .Result!.ToString()!;
}
