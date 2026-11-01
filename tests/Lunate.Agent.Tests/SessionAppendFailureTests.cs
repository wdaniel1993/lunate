using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

/// <summary>
/// A session append that throws (writer-lock refusal, disk full, permission error) must leave the
/// harness's parallel history lists one-per-message, so the next turn builds a request or reports
/// the original persistence error instead of an out-of-range index (#441).
/// </summary>
public sealed class SessionAppendFailureTests
{
    [Fact]
    public async Task A_failed_session_append_keeps_the_next_turn_buildable()
    {
        using var temp = new TempDirectory();
        string sessionPath = temp.File("session.jsonl");
        Session session = Session.Create(sessionPath, temp.Root);
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(),
            new AgentHarnessOptions { Session = session, WorkingDirectory = temp.Root }
        );

        DenyAppends(sessionPath);
        List<AgentEvent> failed;
        try
        {
            failed = await Run(harness, "first");
        }
        finally
        {
            AllowAppends(sessionPath);
        }

        RunError reported = Assert.Single(failed.OfType<RunError>());
        Assert.Contains(Path.GetFileName(sessionPath), reported.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("outside the bounds", reported.Message, StringComparison.Ordinal);

        List<AgentEvent> recovered = await Run(harness, "second");
        Assert.Empty(recovered.OfType<RunError>());
        Assert.Contains(
            recovered,
            finished => finished is RunFinished { StopReason: StopReasons.Stop }
        );

        // The next turn really built a request, from a history that no longer holds the
        // message the session refused.
        Assert.Single(client.Requests);
        Assert.All(
            client.Requests[0].Messages,
            message => Assert.DoesNotContain("first", message.Text, StringComparison.Ordinal)
        );
    }

    private static async Task<List<AgentEvent>> Run(AgentHarness harness, string input) =>
        await harness
            .RunAsync(input, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

    /// <summary>Makes the session file refuse a write, as a permission error would.</summary>
    private static void DenyAppends(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);
        }
        else
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead
            );
        }
    }

    /// <summary>Restores the owner-writable mode the product gives a session file.</summary>
    private static void AllowAppends(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
