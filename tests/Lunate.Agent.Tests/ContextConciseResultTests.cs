using Lunate.Coding;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class ContextConciseResultTests
{
    [Fact]
    public async Task Bash_keeps_a_long_stdout_transcript_out_of_the_request()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            temp.Root,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero))
        );
        string command = LongOutputCommand();
        var scripted = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "bash", LoopScripts.Args(("command", command))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var client = new ContextAccountingChatClient(scripted);
        var harness = new AgentHarness(
            client,
            Registry(new BashTool(new Workspace(temp.Root))),
            new AgentHarnessOptions { Session = session, WorkingDirectory = temp.Root }
        );

        List<AgentEvent> events = await Run(harness);

        long inputTokens = Assert
            .IsType<SessionUsage>(
                session
                    .Entries.OfType<SessionMessageEntry>()
                    .Last(entry => entry.Message.Role == ChatRole.Assistant)
                    .Usage
            )
            .Input;
        Assert.True(
            inputTokens < 700,
            $"the request carrying a 20,000-character bash transcript was {inputTokens} estimated tokens"
        );
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("line-181", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("line-001", result.Output, StringComparison.Ordinal);
        Assert.Contains("stderr:\nfailure line", result.Output, StringComparison.Ordinal);
        Assert.Contains("exit code: 7", result.Output, StringComparison.Ordinal);
        Assert.Contains("full output at ", result.Output, StringComparison.Ordinal);
        string transcript = File.ReadAllText(ToolOutputSpillTests.PathFromMarker(result.Output));
        Assert.Contains("failure line", transcript, StringComparison.Ordinal);
        Assert.Contains("line-200", transcript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_short_bash_command_does_not_spill_its_transcript()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), temp.Root);
        var scripted = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "bash", LoopScripts.Args(("command", "echo short"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            scripted,
            Registry(new BashTool(new Workspace(temp.Root))),
            new AgentHarnessOptions { Session = session, WorkingDirectory = temp.Root }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.Equal("short\nexit code: 0", result.Output);
        Assert.DoesNotContain("full output at ", result.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(temp.Root, session.SessionId, "tool-output")));
    }

    // `bash` is the tool's shell on every platform — the Windows runners invoke Git Bash, so a
    // PowerShell pipeline here would die with a bash syntax error before the transcript exists.
    private static string LongOutputCommand() =>
        "i=1; while [ $i -le 200 ]; do printf 'line-%03d %090d\\n' $i $i; i=$((i + 1)); done; printf 'failure line\\n' >&2; exit 7";
}
