using System.Text;
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
            new AgentHarnessOptions
            {
                Session = session,
                WorkingDirectory = temp.Root,
                ToolOutputLimit = 100_000,
                SpillThresholdTokens = 100_000,
            }
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
        string originalRead = session
            .Entries.OfType<SessionMessageEntry>()
            .SelectMany(entry => entry.Message.Contents)
            .OfType<FunctionResultContent>()
            .Single(result => result.CallId == "read-call")
            .Result!.ToString()!;
        long savedTokens =
            (
                Encoding.UTF8.GetByteCount(originalRead)
                - Encoding.UTF8.GetByteCount(priorRead.Result!.ToString()!)
            ) / 4;
        Assert.True(savedTokens >= 4_000, $"the stale read saves {savedTokens} estimated tokens");
    }

    [Fact]
    public async Task A_reread_after_a_change_allows_the_next_edit()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("small.txt"), "first\nsecond\n");
        var scripted = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("read-1", "read", LoopScripts.Args(("path", "small.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "edit-1",
                    "edit",
                    LoopScripts.Args(
                        ("path", "small.txt"),
                        ("old_text", "first"),
                        ("new_text", "changed")
                    )
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("read-2", "read", LoopScripts.Args(("path", "small.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "edit-2",
                    "edit",
                    LoopScripts.Args(
                        ("path", "small.txt"),
                        ("old_text", "second"),
                        ("new_text", "updated")
                    )
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            scripted,
            Registry(
                new ReadTool(new Workspace(temp.Root)),
                new EditTool(new Workspace(temp.Root))
            ),
            new AgentHarnessOptions { WorkingDirectory = temp.Root }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult secondEdit = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "edit-2"
        );
        Assert.False(secondEdit.IsError);
        Assert.Equal("changed\nupdated\n", File.ReadAllText(temp.File("small.txt")));
    }

    [Fact]
    public async Task An_out_of_band_change_refuses_an_edit_from_the_old_read()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("small.txt"), "before\n");
        var external = new ScriptedTool(
            "external",
            "Changes a file without publishing an event.",
            """{"type":"object"}"""
        )
        {
            OnExecute = (_, _) =>
            {
                File.WriteAllText(temp.File("small.txt"), "outside\n");
                return new ToolResult("changed outside the file tools", IsError: false);
            },
        };
        var scripted = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("read", "read", LoopScripts.Args(("path", "small.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("external-call", "external", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "edit",
                    "edit",
                    LoopScripts.Args(
                        ("path", "small.txt"),
                        ("old_text", "before"),
                        ("new_text", "after")
                    )
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var workspace = new Workspace(temp.Root);
        var harness = new AgentHarness(
            scripted,
            Registry(new ReadTool(workspace), new EditTool(workspace), external),
            new AgentHarnessOptions { WorkingDirectory = temp.Root }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult refused = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "edit"
        );
        Assert.True(refused.IsError);
        Assert.Contains(
            "small.txt changed after your last read",
            refused.Output,
            StringComparison.Ordinal
        );
        Assert.Equal("outside\n", File.ReadAllText(temp.File("small.txt")));
    }

    [Fact]
    public async Task A_resumed_history_requires_a_reread_before_editing()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("small.txt"), "before\n");
        Session session = Session.Create(temp.File("s.jsonl"), temp.Root);
        var initialClient = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("read", "read", LoopScripts.Args(("path", "small.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        await Run(
            new AgentHarness(
                initialClient,
                Registry(new ReadTool(new Workspace(temp.Root))),
                new AgentHarnessOptions { Session = session, WorkingDirectory = temp.Root }
            )
        );

        Session resumed = Session.Load(session.Path);
        var editClient = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call(
                    "resume-edit",
                    "edit",
                    LoopScripts.Args(
                        ("path", "small.txt"),
                        ("old_text", "before"),
                        ("new_text", "after")
                    )
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            editClient,
            Registry(new EditTool(new Workspace(temp.Root))),
            new AgentHarnessOptions { Session = resumed, WorkingDirectory = temp.Root }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult refused = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "resume-edit"
        );
        Assert.True(refused.IsError);
        Assert.Contains(
            "small.txt changed after your last read",
            refused.Output,
            StringComparison.Ordinal
        );
        Assert.Equal("before\n", File.ReadAllText(temp.File("small.txt")));
    }

    [Fact]
    public async Task A_write_after_a_published_change_is_refused_until_reread()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("small.txt"), "before\n");
        var scripted = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("read", "read", LoopScripts.Args(("path", "small.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "write-1",
                    "write",
                    LoopScripts.Args(("path", "small.txt"), ("content", "first\n"))
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "write-2",
                    "write",
                    LoopScripts.Args(("path", "small.txt"), ("content", "second\n"))
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var workspace = new Workspace(temp.Root);
        var harness = new AgentHarness(
            scripted,
            Registry(new ReadTool(workspace), new WriteTool(workspace)),
            new AgentHarnessOptions { WorkingDirectory = temp.Root }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult refused = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "write-2"
        );
        Assert.True(refused.IsError);
        Assert.Contains(
            "small.txt changed after your last read",
            refused.Output,
            StringComparison.Ordinal
        );
        Assert.Equal("first\n", File.ReadAllText(temp.File("small.txt")));
    }

    private static string Line(int number) => $"line-{number:D4}";
}
