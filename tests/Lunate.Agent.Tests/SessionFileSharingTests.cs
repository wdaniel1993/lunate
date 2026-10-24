namespace Lunate.Agent.Tests;

/// <summary>
/// The handle discipline for session files, stated as the rule Windows enforces and POSIX does
/// not: any number of readers may open a session file while one writer appends to it, a finished
/// writer releases before its caller returns, and a session always has exactly one writer.
/// </summary>
/// <remarks>
/// These tests are the platform-independent half of the guarantee. The behaviour they describe is
/// refused by Windows and granted by POSIX, so the share modes are asserted directly instead of
/// being observed through an exception that only one platform raises.
/// </remarks>
public sealed class SessionFileSharingTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_reader_and_a_writer_admit_each_other_and_neither_admits_a_second_writer()
    {
        Assert.True(
            Admits(
                FileAccess.Write,
                SessionFiles.WriterShare,
                FileAccess.Read,
                SessionFiles.ReaderShare
            ),
            "a reader must open a session file while a writer appends to it"
        );
        Assert.True(
            Admits(
                FileAccess.Read,
                SessionFiles.ReaderShare,
                FileAccess.Write,
                SessionFiles.WriterShare
            ),
            "a writer must append to a session file a reader already has open"
        );
        Assert.True(
            Admits(
                FileAccess.Read,
                SessionFiles.ReaderShare,
                FileAccess.Read,
                SessionFiles.ReaderShare
            ),
            "any number of readers must open a session file at once"
        );
        Assert.True(
            SessionFiles.ReaderShare.HasFlag(FileShare.Delete),
            "a rename or delete must not wait for a reader to finish"
        );
        Assert.True(
            SessionFiles.WriterShare.HasFlag(FileShare.Delete),
            "a finished session file must stay renameable by its caller"
        );
        Assert.False(
            Admits(
                FileAccess.Write,
                SessionFiles.WriterShare,
                FileAccess.Write,
                SessionFiles.WriterShare
            ),
            "a second writer stays refused: a session has exactly one writer"
        );
    }

    [Fact]
    public void A_reader_sees_the_written_prefix_while_an_append_is_in_flight()
    {
        using var temp = new TempDirectory();
        string directory = temp.File("sessions");
        string path = temp.File("sessions/s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendModelChange("first");
        session.AppendModelChange("second");

        // An append is in flight: the writer holds the file exactly as Session.AddEntry does while
        // it writes its line. Windows refuses the readers below unless their share mode admits a
        // writer that already holds the file.
        using (FileStream inFlight = SessionFiles.OpenAppend(path))
        {
            Session reloaded = Session.Load(path);
            Assert.Equal(2, reloaded.Entries.Count);
            Assert.Equal(
                session.Entries.Select(entry => entry.Id),
                reloaded.Entries.Select(entry => entry.Id)
            );

            SessionSummary summary = Assert.Single(Session.List(directory));
            Assert.Equal(session.SessionId, summary.Id);
        }

        // What the reader saw is a prefix of what the file became: the next append only extends it.
        session.AppendModelChange("third");
        Session after = Session.Load(path);
        Assert.Equal(3, after.Entries.Count);
        Assert.Equal("third", Assert.IsType<SessionModelChangeEntry>(after.Entries[^1]).Model);
    }

    [Fact]
    public void A_finished_append_releases_the_file_for_a_rename_and_a_delete()
    {
        using var temp = new TempDirectory();
        string path = temp.File("sessions/s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));

        session.AppendModelChange("first");

        // Windows refuses both of these while any handle on the file is still open, so they are the
        // observable proof that the append closed its handle before it returned to the caller.
        string renamed = temp.File("sessions/renamed.jsonl");
        File.Move(path, renamed);
        Assert.True(File.Exists(renamed));
        Assert.False(File.Exists(path));

        File.Delete(renamed);
        Assert.False(File.Exists(renamed));
    }

    [Fact]
    public void A_title_rewrite_replaces_the_sidecar_while_a_reader_holds_it()
    {
        using var temp = new TempDirectory();
        string path = temp.File("sessions/s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        SessionTitle.Write(session.Path, "first");

        // The write replaces the sidecar by renaming a temp file over it; Windows refuses that while
        // a reader holds the sidecar without admitting a delete.
        using (
            StreamReader reader = SessionFiles.OpenTextReader(SessionTitle.PathFor(session.Path))
        )
        {
            Assert.Equal("first", reader.ReadLine());
            SessionTitle.Write(session.Path, "second");
        }

        Assert.Equal("second", SessionTitle.Read(session.Path));
    }

    /// <summary>
    /// Windows grants an open only when both directions hold: the access the new handle asks for is
    /// allowed by the existing handle's share mode, and the new handle's share mode allows the
    /// access the existing handle already has.
    /// </summary>
    private static bool Admits(
        FileAccess heldAccess,
        FileShare heldShare,
        FileAccess wantedAccess,
        FileShare wantedShare
    ) => Allows(heldShare, wantedAccess) && Allows(wantedShare, heldAccess);

    private static bool Allows(FileShare share, FileAccess access) =>
        access switch
        {
            FileAccess.Read => share.HasFlag(FileShare.Read),
            FileAccess.Write => share.HasFlag(FileShare.Write),
            FileAccess.ReadWrite => share.HasFlag(FileShare.ReadWrite),
            _ => false,
        };
}
