using Lunate.TestSupport;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>
/// A session file's one writer, enforced on every platform (spec: "The session file"). Windows
/// refuses a second writer through the share mode; POSIX grants it, so the claim is an exclusive
/// handle on a lock file beside the session, and the proof runs a real second process instead of
/// asserting a share-mode constant.
/// </summary>
public sealed class SessionWriterClaimTests
{
    private const string SessionVariable = "LUNATE_TEST_SESSION_PATH";

    /// <summary>The marker the child is spawned with: this method's own name.</summary>
    private const string ChildMethod = nameof(
        A_second_process_is_refused_a_session_file_that_is_being_appended_to
    );

    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_second_process_is_refused_a_session_file_that_is_being_appended_to()
    {
        string? inherited = Environment.GetEnvironmentVariable(SessionVariable);
        if (TestChildProcess.IsChild(ChildMethod))
        {
            // The child half: the parent is appending to the file, so this process may not.
            ChildExpectsRefusal(inherited!);
            return;
        }

        using var temp = new TempDirectory();
        string path = temp.File("sessions/s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));
        long before = new FileInfo(path).Length;

        (int exitCode, string output) = await TestChildProcess.RunAsync(
            typeof(SessionWriterClaimTests),
            ChildMethod,
            new Dictionary<string, string?> { [SessionVariable] = path }
        );

        Assert.True(exitCode == 0, $"the child process failed:\n{output}");

        // The refusal is the child's alone: the file has no line from it, not even a fragment.
        Assert.Equal(before, new FileInfo(path).Length);
        session.AppendMessage(new ChatMessage(ChatRole.User, "two"));
        Assert.Equal(2, Session.Load(path).Entries.Count);
    }

    [Fact]
    public void The_claim_is_a_lock_file_beside_the_session_the_listing_ignores()
    {
        using var temp = new TempDirectory();
        string directory = temp.File("sessions");
        // Built with Path.Combine: the assertion compares a returned path, and a literal '/'
        // would read as a different string on Windows for the same file.
        string path = Path.Combine(directory, "s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));

        string lockPath = SessionFiles.WriterLockPath(path);
        Assert.Equal(Path.Combine(directory, "s.lock"), lockPath);
        Assert.True(File.Exists(lockPath), $"the writer lock '{lockPath}' must exist");
        Assert.Equal(path, Assert.Single(Session.List(directory)).Path);
    }

    [Fact]
    public void A_second_session_in_this_process_shares_the_claim_of_the_first()
    {
        using var temp = new TempDirectory();
        string path = temp.File("sessions/s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));

        // One process is one writer: a resume, a mirror and a test that reload the same file keep
        // appending through it, and the claim is not a latch that closes the file for this process.
        Session resumed = Session.Load(path);
        resumed.AppendMessage(new ChatMessage(ChatRole.User, "two"));

        Assert.Equal(2, Session.Load(path).Entries.Count);
    }

    private static void ChildExpectsRefusal(string path)
    {
        Session loaded = Session.Load(path);
        Assert.Single(loaded.Entries);

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
            loaded.AppendMessage(new ChatMessage(ChatRole.User, "from the child"))
        );

        Assert.Contains(path, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("one writer", refusal.Message, StringComparison.Ordinal);
        Assert.Single(loaded.Entries);
    }
}
