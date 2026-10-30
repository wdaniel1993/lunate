using System.Text.Json;
using Lunate.Coding;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class ContextStaleReadTests
{
    [Fact]
    public async Task An_edit_from_an_invalidated_read_is_refused_and_stubbed()
    {
        using var temp = new TempDirectory();
        string contents = string.Join('\n', Enumerable.Range(1, 2_000).Select(Line)) + "\n";
        File.WriteAllText(temp.File("large.txt"), contents);
        Session session = Session.Create(
            temp.File("s.jsonl"),
            temp.Root,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero))
        );
        var scripted = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("read-call", "read", LoopScripts.Args(("path", "large.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "first-edit",
                    "edit",
                    LoopScripts.Args(
                        ("path", "large.txt"),
                        ("old_text", Line(1)),
                        ("new_text", "first-edit")
                    )
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "stale-edit",
                    "edit",
                    LoopScripts.Args(
                        ("path", "large.txt"),
                        ("old_text", Line(2_000)),
                        ("new_text", "stale-edit")
                    )
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            new ContextAccountingChatClient(scripted),
            Registry(
                new ReadTool(new Workspace(temp.Root)),
                new EditTool(new Workspace(temp.Root))
            ),
            new AgentHarnessOptions { Session = session, WorkingDirectory = temp.Root }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult stale = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "stale-edit"
        );
        Assert.True(stale.IsError);
        Assert.Contains(
            "large.txt changed after your last read",
            stale.Output,
            StringComparison.Ordinal
        );
        Assert.Contains("re-read", stale.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "first-edit\n" + string.Join('\n', Enumerable.Range(2, 1_999).Select(Line)) + "\n",
            File.ReadAllText(temp.File("large.txt"))
        );

        FunctionResultContent priorRead = Assert.Single(
            scripted
                .Requests[2]
                .Messages.Where(message => message.Role == ChatRole.Tool)
                .SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>(),
            result => result.CallId == "read-call"
        );
        Assert.Contains("[stale:", priorRead.Result!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("     1|", priorRead.Result.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            session.Entries.OfType<SessionMessageEntry>(),
            entry =>
                entry
                    .Message.Contents.OfType<FunctionResultContent>()
                    .Any(result =>
                        result.CallId == "read-call"
                        && result.Result!.ToString()!.Contains("     1|", StringComparison.Ordinal)
                    )
        );
    }

    private static string Line(int number) => $"line-{number:D4}";
}
