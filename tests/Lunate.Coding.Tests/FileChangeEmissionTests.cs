using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class FileChangeEmissionTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    [Fact]
    public async Task A_successful_write_emits_one_canonical_event()
    {
        using var temp = new TempDirectory();
        var sink = new RecordingFileChangeSink();
        var tool = new WriteTool(new Workspace(temp.Root), changes: sink);

        ToolResult result = await tool.ExecuteAsync(
            Args("""{"path":"nested/a.txt","content":"hello\n"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        string path = Assert.Single(sink.Paths);
        Assert.Equal(
            Path.Combine(Workspace.Canonicalize(temp.Root), "nested", "a.txt"),
            Workspace.Canonicalize(path)
        );
    }

    [Fact]
    public async Task A_successful_edit_emits_one_canonical_event()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("a.txt"), "hello\n");
        var sink = new RecordingFileChangeSink();
        var tool = new EditTool(new Workspace(temp.Root), changes: sink);

        ToolResult result = await tool.ExecuteAsync(
            Args("""{"path":"a.txt","old_text":"hello","new_text":"bye"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        string path = Assert.Single(sink.Paths);
        Assert.Equal(Path.Combine(Workspace.Canonicalize(temp.Root), "a.txt"), path);
    }

    [Fact]
    public async Task A_refused_edit_emits_nothing()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("a.txt"), "hello\n");
        var sink = new RecordingFileChangeSink();
        var tool = new EditTool(new Workspace(temp.Root), changes: sink);

        ToolResult result = await tool.ExecuteAsync(
            Args("""{"path":"a.txt","old_text":"missing","new_text":"x"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Empty(sink.Paths);
    }

    [Fact]
    public async Task An_ambiguous_edit_emits_nothing()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("a.txt"), "same\nsame\n");
        var sink = new RecordingFileChangeSink();
        var tool = new EditTool(new Workspace(temp.Root), changes: sink);

        ToolResult result = await tool.ExecuteAsync(
            Args("""{"path":"a.txt","old_text":"same","new_text":"x"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Empty(sink.Paths);
    }

    [Theory]
    [InlineData("""{"path":"../outside.txt","content":"x"}""")]
    [InlineData("""{"path":"sub","content":"x"}""")]
    [InlineData("""{"path":"a.txt"}""")]
    public async Task A_refused_write_emits_nothing(string arguments)
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("sub"));
        var sink = new RecordingFileChangeSink();
        var tool = new WriteTool(new Workspace(temp.Root), changes: sink);

        ToolResult result = await tool.ExecuteAsync(
            Args(arguments),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Empty(sink.Paths);
    }

    [Fact]
    public async Task No_sink_is_fine()
    {
        using var temp = new TempDirectory();
        var tool = new WriteTool(new Workspace(temp.Root));

        ToolResult result = await tool.ExecuteAsync(
            Args("""{"path":"a.txt","content":"x\n"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class RecordingFileChangeSink : IFileChangeSink
    {
        public List<string> Paths { get; } = [];

        public void Notify(string absolutePath) => Paths.Add(absolutePath);
    }
}
