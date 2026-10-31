using Lunate.Coding;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class ContextStaleReadTests
{
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
}
