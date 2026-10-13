using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class FileChangeSinkTests
{
    [Fact]
    public void The_default_sink_is_a_no_op()
    {
        var options = new AgentHarnessOptions();

        options.FileChanges.Notify(
            new FileChangeEvent(
                "/anywhere/a.txt",
                "a.txt",
                "/anywhere",
                null,
                FileChangeKind.Modified
            )
        );
    }

    [Fact]
    public async Task The_harness_stamps_the_run_id_and_workspace_onto_context_published_events()
    {
        var sink = new RecordingFileChangeSink();
        ScriptedTool tool = new("publish", "Publishes.", """{"type":"object"}""")
        {
            OnExecute = (_, ctx) =>
            {
                // The tool supplies only what it knows; the harness stamps the run identity.
                ctx.FileChanges.Notify(
                    new FileChangeEvent(
                        "/work/a.cs",
                        "a.cs",
                        string.Empty,
                        null,
                        FileChangeKind.Modified
                    )
                );
                return new ToolResult("published", IsError: false);
            },
        };
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "publish", LoopScripts.Args(("path", "a.cs"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions { WorkingDirectory = "/work", FileChanges = sink }
        );

        List<AgentEvent> events = await Run(harness);

        string runId = Assert.IsType<RunStarted>(events[0]).RunId;
        FileChangeEvent change = Assert.Single(sink.Events);
        Assert.Equal("/work/a.cs", change.AbsolutePath);
        Assert.Equal("a.cs", change.RelativePath);
        Assert.Equal("/work", change.WorkspaceId);
        Assert.Equal(runId, change.RunId);
        Assert.Equal(FileChangeKind.Modified, change.Kind);
    }

    private sealed class RecordingFileChangeSink : IFileChangeSink
    {
        public List<FileChangeEvent> Events { get; } = [];

        public void Notify(FileChangeEvent change) => Events.Add(change);
    }
}
