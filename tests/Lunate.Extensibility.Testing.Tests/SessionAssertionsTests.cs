using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility.Testing.Tests;

public sealed class SessionAssertionsTests
{
    [Fact]
    public void Counts_entries_by_kind_through_session_load()
    {
        using var temp = new TempDirectory();
        string path = temp.File("session.jsonl");
        Session session = Session.Create(path, temp.Root);
        session.AppendMessage(new ChatMessage(ChatRole.User, "hello"));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "hi"));
        session.AppendExtension("ext/template/note", """{"ok":true}""");
        session.AppendExtension("ext/other/note", """{"ok":false}""");
        session.AppendNestedCalls("call-1", [new SessionNestedCall("read", "{}", "ok", 1)]);

        Assert.Equal(2, SessionAssertions.CountMessages(path));
        Assert.Equal(1, SessionAssertions.CountMessages(path, ChatRole.Assistant));
        Assert.Equal(2, SessionAssertions.CountExtensions(path));
        Assert.Equal(1, SessionAssertions.CountExtensions(path, "template"));
        Assert.Equal(1, SessionAssertions.CountNestedCalls(path));
    }
}
