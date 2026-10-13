using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class FileChangeEmissionTests
{
    [Fact]
    public async Task A_successful_write_emits_a_created_event_with_the_context_stamp()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        var sink = new RecordingFileChangeSink();
        var tool = new WriteTool(workspace);

        ToolResult result = await tool.ExecuteAsync(
            Args("""{"path":"nested/a.txt","content":"hello\n"}"""),
            For(workspace, sink),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        FileChangeEvent change = Assert.Single(sink.Events);
        Assert.Equal(
            Path.Combine(Workspace.Canonicalize(temp.Root), "nested", "a.txt"),
            Workspace.Canonicalize(change.AbsolutePath)
        );
        Assert.Equal("nested/a.txt", change.RelativePath);
        Assert.Equal(workspace.WorktreeRoot, change.WorkspaceId);
        Assert.Equal("run_test", change.RunId);
        Assert.Equal(FileChangeKind.Created, change.Kind);
    }

    [Fact]
    public async Task A_successful_edit_emits_a_modified_event()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        File.WriteAllText(temp.File("a.txt"), "hello\n");
        var sink = new RecordingFileChangeSink();
        var tool = new EditTool(workspace);

        ToolResult result = await tool.ExecuteAsync(
            Args("""{"path":"a.txt","old_text":"hello","new_text":"bye"}"""),
            For(workspace, sink),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        FileChangeEvent change = Assert.Single(sink.Events);
        Assert.Equal(Path.Combine(Workspace.Canonicalize(temp.Root), "a.txt"), change.AbsolutePath);
        Assert.Equal("a.txt", change.RelativePath);
        Assert.Equal(FileChangeKind.Modified, change.Kind);
        Assert.Equal("run_test", change.RunId);
    }

    [Fact]
    public async Task A_write_with_identical_content_still_emits_modified()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        File.WriteAllText(temp.File("a.txt"), "same\n");
        var sink = new RecordingFileChangeSink();
        var tool = new WriteTool(workspace);

        ToolResult result = await tool.ExecuteAsync(
            Args("""{"path":"a.txt","content":"same\n"}"""),
            For(workspace, sink),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Equal(FileChangeKind.Modified, Assert.Single(sink.Events).Kind);
    }

    [Fact]
    public async Task A_refused_edit_emits_nothing()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        File.WriteAllText(temp.File("a.txt"), "hello\n");
        var sink = new RecordingFileChangeSink();
        var tool = new EditTool(workspace);

        ToolResult result = await tool.ExecuteAsync(
            Args("""{"path":"a.txt","old_text":"missing","new_text":"x"}"""),
            For(workspace, sink),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task An_ambiguous_edit_emits_nothing()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        File.WriteAllText(temp.File("a.txt"), "same\nsame\n");
        var sink = new RecordingFileChangeSink();
        var tool = new EditTool(workspace);

        ToolResult result = await tool.ExecuteAsync(
            Args("""{"path":"a.txt","old_text":"same","new_text":"x"}"""),
            For(workspace, sink),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Empty(sink.Events);
    }

    [Theory]
    [InlineData("""{"path":"../outside.txt","content":"x"}""")]
    [InlineData("""{"path":"sub","content":"x"}""")]
    [InlineData("""{"path":"a.txt"}""")]
    public async Task A_refused_write_emits_nothing(string arguments)
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        Directory.CreateDirectory(temp.File("sub"));
        var sink = new RecordingFileChangeSink();
        var tool = new WriteTool(workspace);

        ToolResult result = await tool.ExecuteAsync(
            Args(arguments),
            For(workspace, sink),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task No_sink_is_fine()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        var tool = new WriteTool(workspace);

        ToolResult result = await tool.ExecuteAsync(
            Args("""{"path":"a.txt","content":"x\n"}"""),
            new ToolContext(workspace.WorktreeRoot, new NullAgentEvents()),
            CancellationToken.None
        );

        Assert.False(result.IsError);
    }

    private static ToolContext For(
        Workspace workspace,
        IFileChangeSink sink,
        string? runId = "run_test"
    ) => new(workspace.WorktreeRoot, new NullAgentEvents()) { RunId = runId, FileChanges = sink };

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class RecordingFileChangeSink : IFileChangeSink
    {
        public List<FileChangeEvent> Events { get; } = [];

        public void Notify(FileChangeEvent change) => Events.Add(change);
    }
}
