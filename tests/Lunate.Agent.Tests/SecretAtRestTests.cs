using Lunate.Ai;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

/// <summary>
/// Secrets at rest: what a tool echoes reaches the model, but the session file and the spill file
/// hold a marker instead of the known credential; and the files that carry session content,
/// spill output, trust records and install records are owner-only where the platform has modes.
/// </summary>
public sealed class SecretAtRestTests
{
    private const string Secret = "sk-live-9f8e7d6c5b4a";
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_known_credential_is_absent_from_the_session_and_spill_files_but_kept_in_memory()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("sessions/s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        session.Redactor = new CredentialRedactor([("OPENAI_API_KEY", Secret)]);
        string full = Secret + "\n" + string.Join("\n", Enumerable.Repeat("line", 200));
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(ReadTool(full)),
            new AgentHarnessOptions { Session = session, ToolOutputLimit = 100 }
        );

        List<AgentEvent> events = await Run(harness);

        // The live result is what the model sees: the value is still there.
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.Contains(Secret, result.Output, StringComparison.Ordinal);
        Assert.Contains(
            session.Entries.OfType<SessionMessageEntry>(),
            entry =>
                entry.Message.Contents.Any(content =>
                    content is FunctionResultContent function
                    && function.Result?.ToString()?.Contains(Secret, StringComparison.Ordinal)
                        == true
                )
        );

        // The spill holds the full output with the value replaced, not the value.
        string spill = ToolOutputSpillTests.PathFromMarker(result.Output);
        string spilled = File.ReadAllText(spill);
        Assert.DoesNotContain(Secret, spilled, StringComparison.Ordinal);
        Assert.Contains("[redacted:OPENAI_API_KEY]", spilled, StringComparison.Ordinal);

        // The session file holds the truncated result with the value replaced, not the value.
        string written = File.ReadAllText(session.Path);
        Assert.DoesNotContain(Secret, written, StringComparison.Ordinal);
        Assert.Contains("[redacted:OPENAI_API_KEY]", written, StringComparison.Ordinal);
    }

    [Fact]
    public void The_session_directory_and_the_files_it_holds_are_owner_only_on_posix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not exist on Windows.");
            return;
        }

        using var temp = new TempDirectory();
        string directory = temp.File("sessions");
        Session session = Session.Create(
            Path.Combine(directory, "s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        session.AppendMessage(new ChatMessage(ChatRole.User, "hello"));
        ToolOutputSpillResult spill = ToolOutputSpill.TryWrite(session, "run_1", 1, "output");
        Assert.NotNull(spill.Path);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(directory)
        );
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(session.Path)
        );
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(spill.Path)!)
        );
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(spill.Path)
        );
    }
}
