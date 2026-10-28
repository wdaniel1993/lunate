using Lunate.TestSupport;

namespace Lunate.Agent.Tests;

/// <summary>
/// A whole session file written while another process holds it open. The write runs through the
/// same <see cref="Lunate.Ai.FileReplacement"/> seam the settings store uses: on Windows a rename
/// over a held destination is refused even when the holder shares delete, while
/// <see cref="File.Replace(string, string, string?)"/> succeeds with such a holder and fails only
/// when the holder denies delete. The second case keeps the old bytes and surfaces the OS error
/// itself, so a lock cannot turn an unrelated failure into a different report.
/// </summary>
/// <remarks>
/// The proof runs a real second process, because the point is a handle held by another process.
/// Unix sharing is advisory, so the shared-delete holder cannot make the write fail there; the
/// held handle still tells the two writes apart, because truncating in place rewrites the bytes
/// under it while a replace leaves it on the old file. The Windows leg additionally enforces the
/// sharing rule the replace exists for.
/// </remarks>
public sealed class SessionFileReplaceTests
{
    private const string SessionVariable = "LUNATE_TEST_SESSION_PATH";
    private const string ChildSessionId = "s_child_replaced";
    private const string DeniedSessionId = "s_child_denied";

    /// <summary>The marker the child is spawned with: the method's own name.</summary>
    private const string SharedDeleteChild = nameof(
        A_whole_file_write_replaces_a_session_while_another_process_reads_it
    );

    private const string DenyingDeleteChild = nameof(
        A_write_that_cannot_replace_reports_the_lock_and_keeps_the_session
    );

    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_whole_file_write_replaces_a_session_while_another_process_reads_it()
    {
        string? inherited = Environment.GetEnvironmentVariable(SessionVariable);
        if (TestChildProcess.IsChild(SharedDeleteChild))
        {
            ChildWritesNewSession(inherited!);
            return;
        }

        using var temp = new TempDirectory();
        string path = temp.File("sessions/s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendModelChange("old");
        string before = SessionFiles.ReadText(path);

        // A read-only handle that permits a rename/delete but not a write. Truncating the file in
        // place is refused on Windows because the holder denies write; ReplaceFile is not, because
        // it needs delete access, which this holder grants.
        using (
            var held = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete
            )
        )
        {
            (int exitCode, string output) = await TestChildProcess.RunAsync(
                typeof(SessionFileReplaceTests),
                SharedDeleteChild,
                new Dictionary<string, string?> { [SessionVariable] = path }
            );

            Assert.True(exitCode == 0, $"the child process failed:\n{output}");

            // The holder keeps reading the session it opened: the name moved to a new file rather
            // than the bytes being truncated under it.
            using var reader = new StreamReader(
                held,
                SessionFiles.Utf8NoBom,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true
            );
            Assert.Equal(before, reader.ReadToEnd());
        }

        Session replaced = Session.Load(path);
        Assert.Equal(ChildSessionId, replaced.SessionId);
        AssertNoTemporaryFiles(path);
    }

    [Fact]
    public async Task A_write_that_cannot_replace_reports_the_lock_and_keeps_the_session()
    {
        string? inherited = Environment.GetEnvironmentVariable(SessionVariable);
        if (TestChildProcess.IsChild(DenyingDeleteChild))
        {
            ChildExpectsLockedWrite(inherited!);
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix sharing is advisory: a holder cannot refuse a replacement.");
        }

        using var temp = new TempDirectory();
        string path = temp.File("sessions/s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendModelChange("old");

        // No delete sharing: the replace cannot take the name, so the write fails with the OS'
        // own sharing violation. The session is left byte for byte as it was, and the failure is
        // not converted into a second, misleading "the session is locked" report.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            (int exitCode, string output) = await TestChildProcess.RunAsync(
                typeof(SessionFileReplaceTests),
                DenyingDeleteChild,
                new Dictionary<string, string?> { [SessionVariable] = path }
            );

            Assert.True(exitCode == 0, $"the child process failed:\n{output}");
        }

        Assert.Equal(session.SessionId, Session.Load(path).SessionId);
        AssertNoTemporaryFiles(path);
    }

    private static void ChildWritesNewSession(string path)
    {
        // A whole-file write to the session another process holds open. Before the replace seam
        // this opened the destination with FileMode.Create and the holder's denied write made it
        // fail with its own lock error.
        Session created = Session.Create(
            path,
            "/work",
            ChildSessionId,
            new FixedTimeProvider(Start)
        );
        Assert.Equal(ChildSessionId, created.SessionId);
    }

    private static void ChildExpectsLockedWrite(string path)
    {
        string before = File.ReadAllText(path);
        Exception? failure = Record.Exception(() =>
            Session.Create(path, "/work", DeniedSessionId, new FixedTimeProvider(Start))
        );

        Assert.True(
            failure is IOException or UnauthorizedAccessException,
            $"expected the replace to report its lock, got {failure?.GetType().Name ?? "none"}"
        );
        Assert.Equal(before, File.ReadAllText(path));
    }

    private static void AssertNoTemporaryFiles(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        Assert.DoesNotContain(
            Directory.GetFiles(directory),
            file => file.EndsWith(".tmp", StringComparison.Ordinal)
        );
    }
}
