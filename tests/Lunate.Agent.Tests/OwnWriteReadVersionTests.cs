using Lunate.Coding;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class OwnWriteReadVersionTests
{
    [Fact]
    public async Task Consecutive_edits_after_a_read_use_the_latest_agent_write()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("small.txt"), "first\nsecond\nthird\n");
        var scripted = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("read", "read", LoopScripts.Args(("path", "small.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "edit-1",
                    "edit",
                    LoopScripts.Args(
                        ("path", "small.txt"),
                        ("old_text", "first"),
                        ("new_text", "FIRST")
                    )
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "edit-2",
                    "edit",
                    LoopScripts.Args(
                        ("path", "small.txt"),
                        ("old_text", "second"),
                        ("new_text", "SECOND")
                    )
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var workspace = new Workspace(temp.Root);
        var harness = new AgentHarness(
            scripted,
            Registry(new ReadTool(workspace), new EditTool(workspace)),
            new AgentHarnessOptions { WorkingDirectory = temp.Root }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult firstEdit = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "edit-1"
        );
        ToolCallResult secondEdit = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "edit-2"
        );
        Assert.False(firstEdit.IsError);
        Assert.False(secondEdit.IsError);
        Assert.Equal("FIRST\nSECOND\nthird\n", File.ReadAllText(temp.File("small.txt")));
    }

    [Fact]
    public async Task An_out_of_band_change_after_an_edit_still_refuses_the_next_edit()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("small.txt"), "first\nsecond\n");
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
                LoopScripts.Call(
                    "edit-1",
                    "edit",
                    LoopScripts.Args(
                        ("path", "small.txt"),
                        ("old_text", "first"),
                        ("new_text", "FIRST")
                    )
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("external-call", "external", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "edit-2",
                    "edit",
                    LoopScripts.Args(
                        ("path", "small.txt"),
                        ("old_text", "second"),
                        ("new_text", "SECOND")
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
            result => result.CallId == "edit-2"
        );
        Assert.True(refused.IsError);
        Assert.Contains("small.txt changed after your last read", refused.Output);
        Assert.Contains("re-read", refused.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("outside\n", File.ReadAllText(temp.File("small.txt")));
    }

    [Fact]
    public async Task Consecutive_writes_after_a_read_use_the_latest_agent_write()
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

        ToolCallResult firstWrite = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "write-1"
        );
        ToolCallResult secondWrite = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "write-2"
        );
        Assert.False(firstWrite.IsError);
        Assert.False(secondWrite.IsError);
        Assert.Equal("second\n", File.ReadAllText(temp.File("small.txt")));
    }

    [Fact]
    public async Task A_refused_write_does_not_refresh_an_invalidated_read()
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
        var restore = new ScriptedTool(
            "restore",
            "Restores a file without publishing an event.",
            """{"type":"object"}"""
        )
        {
            OnExecute = (_, _) =>
            {
                File.WriteAllText(temp.File("small.txt"), "before\n");
                return new ToolResult("restored outside the file tools", IsError: false);
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
                    "refused-write",
                    "write",
                    LoopScripts.Args(("path", "small.txt"), ("content", "agent\n"))
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("restore-call", "restore", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "refused-edit",
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
            Registry(
                new ReadTool(workspace),
                new WriteTool(workspace),
                new EditTool(workspace),
                external,
                restore
            ),
            new AgentHarnessOptions { WorkingDirectory = temp.Root }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult refusedWrite = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "refused-write"
        );
        ToolCallResult refusedEdit = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "refused-edit"
        );
        Assert.True(refusedWrite.IsError);
        Assert.True(refusedEdit.IsError);
        Assert.Contains("small.txt changed after your last read", refusedWrite.Output);
        Assert.Contains("small.txt changed after your last read", refusedEdit.Output);
        Assert.Equal("before\n", File.ReadAllText(temp.File("small.txt")));
    }
}
