using System.Text;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>
/// What a load does with a session whose last append never finished (spec: the session file). The
/// bytes after the last newline are a fragment, not a record: they are dropped so the session that
/// every line before them describes resumes, and only they are dropped — a malformed line with a
/// terminator after it is corruption and still stops the load.
/// </summary>
public sealed class SessionTornAppendTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_torn_final_append_is_dropped_and_the_session_resumes()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "two"));
        long complete = Bytes(path);

        // An append that stopped part-way through its line: the id and the parent are there, the
        // rest of the record is not.
        File.AppendAllText(path, "{\"type\":\"message\",\"id\":\"e_03\",\"parent\":\"e_02\",\"ro");

        Session resumed = Session.Load(path);

        Assert.Equal(["e_01", "e_02"], resumed.Entries.Select(entry => entry.Id).ToArray());
        Assert.Equal(complete, Bytes(path));
        Assert.EndsWith(
            "\n",
            Encoding.UTF8.GetString(File.ReadAllBytes(path)),
            StringComparison.Ordinal
        );

        // The recovered session writes where the fragment was: the next id follows the last complete
        // record, and the file it produces loads without dropping anything.
        resumed.AppendMessage(new ChatMessage(ChatRole.User, "three"));
        Session reloaded = Session.Load(path);
        Assert.Equal(
            ["e_01", "e_02", "e_03"],
            reloaded.Entries.Select(entry => entry.Id).ToArray()
        );
        Assert.Equal(
            "three",
            Assert.IsType<SessionMessageEntry>(reloaded.Entries[^1]).Message.Text
        );
    }

    [Fact]
    public void A_torn_final_append_that_ends_mid_character_is_cut_to_the_last_record()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));
        long complete = Bytes(path);

        // The fragment ends inside a multi-byte character, which decodes to one replacement
        // character: the cut has to come from the bytes, not from the decoded text.
        byte[] torn = [.. Encoding.UTF8.GetBytes("{\"type\":\"message\",\"text\":\""), 0xE2, 0x9C];
        using (FileStream stream = new(path, FileMode.Append, FileAccess.Write))
        {
            stream.Write(torn);
        }

        Session resumed = Session.Load(path);

        Assert.Single(resumed.Entries);
        Assert.Equal(complete, Bytes(path));
    }

    [Fact]
    public void A_malformed_line_in_the_middle_is_still_refused()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "two"));

        string[] lines = File.ReadAllText(path).Split('\n');
        // Break the record before the last one and keep the file terminated, so nothing about it
        // looks like an interrupted append.
        lines[1] = lines[1][..12];
        File.WriteAllText(path, string.Join('\n', lines));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Session.Load(path)
        );
        Assert.Contains("line 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_terminated_final_record_is_still_refused()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));

        // A terminator after the fragment is what an interrupted append cannot have written: the
        // fragment is the file's last record, but the newline says it finished, so it is corruption.
        File.AppendAllText(path, "{\"type\":\"message\",\"id\":\"e_02\"\n");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Session.Load(path)
        );
        Assert.Contains("line 3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_complete_final_record_that_lost_only_its_newline_is_kept_and_terminated()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));

        string text = File.ReadAllText(path);
        File.WriteAllText(path, text.TrimEnd('\n'));

        Session resumed = Session.Load(path);

        Assert.Single(resumed.Entries);
        // Terminating the record is the whole repair: no byte of it is rewritten, and the next
        // append starts on its own line.
        Assert.Equal(text, File.ReadAllText(path));
        resumed.AppendMessage(new ChatMessage(ChatRole.User, "two"));
        Assert.Equal(2, Session.Load(path).Entries.Count);
    }

    [Fact]
    public void A_failed_append_leaves_the_entries_the_file_has()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));

        // The path now names a directory, so the append cannot land: the entry must not land either.
        File.Move(path, temp.File("moved.jsonl"));
        Directory.CreateDirectory(path);

        Exception? failure = Record.Exception(() =>
            session.AppendMessage(new ChatMessage(ChatRole.User, "two"))
        );

        Assert.NotNull(failure);
        Assert.True(
            failure is IOException or UnauthorizedAccessException,
            $"the append failed with {failure.GetType().Name}, which was not expected"
        );
        Assert.Single(session.Entries);
    }

    private static long Bytes(string path) => new FileInfo(path).Length;
}
