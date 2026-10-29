using Lunate.Ai;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

/// <summary>
/// Credentials on the way into the context (issue 187, part 1): the values the session knows are
/// replaced in every tool result before the result joins the conversation, so the model reads the
/// named placeholder instead of the secret it would otherwise echo back. The display event keeps
/// the tool's own output; the persistence pass stays exactly where it is.
/// </summary>
public sealed class SecretInContextTests
{
    private const string Secret = "sk-live-9f8e7d6c5b4a";
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_tool_result_joins_the_conversation_masked()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("sessions/s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        session.Redactor = new CredentialRedactor([("OPENAI_API_KEY", Secret)]);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", ".env"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(ReadTool($"KEY={Secret}\nOTHER=1\n")),
            new AgentHarnessOptions { Session = session }
        );

        List<AgentEvent> events = await Run(harness);

        // The model reads the named placeholder, never the value: the write guard can rely on the
        // model not being able to echo the real value back.
        string context = ResultText(client.Requests[1]);
        Assert.Contains("[redacted:OPENAI_API_KEY]", context, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, context, StringComparison.Ordinal);

        // The display event keeps what the tool produced; masking is the context's change only.
        Assert.Contains(
            Secret,
            Assert.Single(events.OfType<ToolCallResult>()).Output,
            StringComparison.Ordinal
        );

        // The session mirrors the conversation so a resumed run reads what the model read, and the
        // persistence pass finds nothing left to replace in the lines the mirror wrote.
        string mirrored = MirroredResultText(session);
        Assert.Contains("[redacted:OPENAI_API_KEY]", mirrored, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, mirrored, StringComparison.Ordinal);
        Assert.Contains(
            "[redacted:OPENAI_API_KEY]",
            File.ReadAllText(session.Path),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(Secret, File.ReadAllText(session.Path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_without_a_session_keeps_the_result_untouched()
    {
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", ".env"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(ReadTool($"KEY={Secret}\n")));

        await Run(harness);

        Assert.Contains(Secret, ResultText(client.Requests[1]), StringComparison.Ordinal);
    }

    private static string ResultText(ScriptedRequest request) =>
        Assert
            .IsType<FunctionResultContent>(
                Assert.Single(request.Messages.Single(message => message.Role == ChatRole.Tool).Contents)
            )
            .Result!
            .ToString()!;

    private static string MirroredResultText(Session session) =>
        string.Join(
            "\n",
            session
                .Entries.OfType<SessionMessageEntry>()
                .SelectMany(entry => entry.Message.Contents)
                .OfType<FunctionResultContent>()
                .Select(result => result.Result?.ToString() ?? string.Empty)
        );
}
