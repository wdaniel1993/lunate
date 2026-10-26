using System.Text;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>
/// The session file's line-ending contract (repo-quality spec: "canonical bytes for files Lunate
/// owns"): a record ends with LF on every platform, and a file written on one machine parses the
/// same on another. The writer already emitted LF — this is the guard that keeps it so.
/// </summary>
public sealed class SessionLineEndingTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_written_record_ends_with_LF_and_no_record_carries_a_carriage_return()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));

        session.AppendMessage(new ChatMessage(ChatRole.User, "one\r\ntwo"));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "three"), "gpt-4o-mini");

        byte[] bytes = File.ReadAllBytes(path);
        string text = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.EndsWith("\n", text);
        Assert.Equal(session.Entries.Count + 1, text.Split('\n').Length - 1);
        Assert.All(text.Split('\n')[..^1], line => Assert.NotEmpty(line));
    }

    [Fact]
    public void A_session_file_written_with_CRLF_still_parses_the_same()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session session = Session.Create(path, "/work", new FixedTimeProvider(Start));
        session.AppendMessage(new ChatMessage(ChatRole.User, "one"));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "two"), "gpt-4o-mini");

        File.WriteAllText(path, File.ReadAllText(path).ReplaceLineEndings("\r\n"));

        Session loaded = Session.Load(path, new FixedTimeProvider(Start));

        Assert.Equal(session.SessionId, loaded.SessionId);
        Assert.Equal(
            session.Entries.Select(entry => entry.Id),
            loaded.Entries.Select(entry => entry.Id)
        );
    }
}
