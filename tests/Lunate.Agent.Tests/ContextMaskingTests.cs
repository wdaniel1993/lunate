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
        var scripted = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "secret.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            new ContextAccountingChatClient(scripted),
            Registry(ReadTool(output)),
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
