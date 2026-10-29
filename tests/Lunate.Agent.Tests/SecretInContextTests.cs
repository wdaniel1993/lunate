using Lunate.Ai;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

/// <summary>
/// Credentials on the way into the context (issue 187, part 1): the host-supplied mask replaces
/// the values the session's redactor knows in every tool result at admission, before truncation,
/// so a value that crosses a cut cannot send a fragment and the ToolCallResult event carries the
/// same masked text the model saw. The session's redactor keeps masking at rest as defence in
/// depth; a no-session run masks too, because the host supplies the same redactor on its own.
/// </summary>
public sealed class SecretInContextTests
{
    private const string Secret = "sk-live-9f8e7d6c5b4a";
    private const string Marker = "[redacted:OPENAI_API_KEY]";
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_tool_result_joins_the_conversation_masked()
    {
        using var temp = new TempDirectory();
        var redactor = new CredentialRedactor([("OPENAI_API_KEY", Secret)]);
        Session session = Session.Create(
            temp.File("sessions/s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        session.Redactor = redactor;
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", ".env"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(ReadTool($"KEY={Secret}\nOTHER=1\n")),
            new AgentHarnessOptions { Session = session, ToolOutputMask = redactor.Redact }
        );

        List<AgentEvent> events = await Run(harness);

        // The model reads the named placeholder, never the value: the write guard can rely on the
        // model not being able to echo the real value back.
        string context = ResultText(client.Requests[1]);
        Assert.Contains(Marker, context, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, context, StringComparison.Ordinal);

        // The display event carries the same masked text: the admission mask runs before the
        // result is emitted, so the event, the history and the session entry hold what the model
        // saw, and the at-rest pass meets nothing left to replace.
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.Contains(Marker, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.Output, StringComparison.Ordinal);

        // The session mirrors the conversation so a resumed run reads what the model read, and the
        // persistence pass finds nothing left to replace in the lines the mirror wrote.
        string mirrored = MirroredResultText(session);
        Assert.Contains(Marker, mirrored, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, mirrored, StringComparison.Ordinal);
        Assert.Contains(Marker, File.ReadAllText(session.Path), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, File.ReadAllText(session.Path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_without_a_session_masks_the_result()
    {
        var redactor = new CredentialRedactor([("OPENAI_API_KEY", Secret)]);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", ".env"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(ReadTool($"KEY={Secret}\n")),
            new AgentHarnessOptions { ToolOutputMask = redactor.Redact }
        );

        List<AgentEvent> events = await Run(harness);

        // No session exists to mask at rest, so the admission mask is the only line: the request
        // and the event both carry the marker, not the value.
        string context = ResultText(client.Requests[1]);
        Assert.Contains(Marker, context, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, context, StringComparison.Ordinal);
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.Contains(Marker, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_value_across_the_head_truncation_cut_is_masked_before_the_cut()
    {
        // The limit is 40, split into a 20-character head and tail: the value starts at 10 and
        // ends at 29, so the head cut at 20 falls inside it. Today the head keeps its first
        // fragment; masking first replaces the whole value before any cut is chosen.
        string straddling = new string('A', 10) + Secret + new string('B', 15);
        var redactor = new CredentialRedactor([("OPENAI_API_KEY", Secret)]);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", ".env"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(ReadTool(straddling)),
            new AgentHarnessOptions { ToolOutputLimit = 40, ToolOutputMask = redactor.Redact }
        );

        List<AgentEvent> events = await Run(harness);

        // The marker's opening survives the head; no six-character fragment of the value does.
        string context = ResultText(client.Requests[1]);
        LeavesNoFragmentOfSecret(context);
        Assert.Contains("[redacted:", context, StringComparison.Ordinal);
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        LeavesNoFragmentOfSecret(result.Output);
        Assert.Contains("[redacted:", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_value_across_the_tail_truncation_cut_is_masked_before_the_cut()
    {
        // The limit is 40: the value starts at 20 and the tail cut at 25 falls inside it, so the
        // kept tail would today start with a long fragment of the value.
        string straddling = new string('A', 20) + Secret + new string('B', 5);
        var redactor = new CredentialRedactor([("OPENAI_API_KEY", Secret)]);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", ".env"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(ReadTool(straddling)),
            new AgentHarnessOptions { ToolOutputLimit = 40, ToolOutputMask = redactor.Redact }
        );

        List<AgentEvent> events = await Run(harness);

        // The marker's closing survives the tail; no six-character fragment of the value does.
        string context = ResultText(client.Requests[1]);
        LeavesNoFragmentOfSecret(context);
        Assert.Contains("OPENAI_API_KEY]", context, StringComparison.Ordinal);
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        LeavesNoFragmentOfSecret(result.Output);
        Assert.Contains("OPENAI_API_KEY]", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every fragment of the value of at least six characters — the head-side fragments that
    /// carry the "sk-live" prefix and the tail-side ones that do not — must be gone.
    /// </summary>
    private static void LeavesNoFragmentOfSecret(string text)
    {
        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        for (int start = 0; start + 6 <= Secret.Length; start++)
        {
            Assert.DoesNotContain(Secret.Substring(start, 6), text, StringComparison.Ordinal);
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
