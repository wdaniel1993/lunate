using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class TextFileAccessSeamTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    [Fact]
    public async Task The_read_tool_defaults_to_the_local_disk()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Local.txt"), "local content\n");

        var result = await ExecuteAsync(
            new ReadTool(new Workspace(temp.Root)),
            """{"path":"Local.txt"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("     1|local content", result.Output);
    }

    [Fact]
    public async Task The_read_tool_reads_through_the_access_seam_and_never_the_disk()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Buffer.txt"), "on disk\n");
        var root = new Workspace(temp.Root).WorktreeRoot;
        var files = new FakeTextFileAccess();
        files.Files[Path.Combine(root, "Buffer.txt")] = "from the editor buffer\n";

        var result = await ExecuteAsync(
            new ReadTool(new Workspace(temp.Root), files),
            """{"path":"Buffer.txt"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("     1|from the editor buffer", result.Output);
    }

    [Fact]
    public async Task The_read_tool_reports_a_missing_seam_file_as_not_found()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Disk.txt"), "on disk\n");

        var result = await ExecuteAsync(
            new ReadTool(new Workspace(temp.Root), new FakeTextFileAccess()),
            """{"path":"Disk.txt"}"""
        );

        Assert.True(result.IsError);
        Assert.Equal("file not found: Disk.txt", result.Output);
    }

    [Fact]
    public async Task The_write_tool_writes_through_the_access_seam()
    {
        using var temp = new TempDirectory();
        var root = new Workspace(temp.Root).WorktreeRoot;
        var files = new FakeTextFileAccess();

        var result = await ExecuteAsync(
            new WriteTool(new Workspace(temp.Root), files: files),
            """{"path":"New.txt","content":"seam write\n"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("wrote 1 lines to New.txt (created)", result.Output);
        var write = Assert.Single(files.Writes);
        Assert.Equal(Path.Combine(root, "New.txt"), write.Path);
        Assert.Equal("seam write\n", write.Text);
        Assert.False(File.Exists(temp.File("New.txt")));
    }

    [Fact]
    public async Task The_edit_tool_reads_and_writes_through_the_access_seam()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Edit.txt"), "disk old\n");
        var root = new Workspace(temp.Root).WorktreeRoot;
        var files = new FakeTextFileAccess();
        files.Files[Path.Combine(root, "Edit.txt")] = "buffer old\n";

        var result = await ExecuteAsync(
            new EditTool(new Workspace(temp.Root), files: files),
            """{"path":"Edit.txt","old_text":"buffer old","new_text":"buffer new"}"""
        );

        Assert.False(result.IsError, result.Output);
        var write = Assert.Single(files.Writes);
        Assert.Equal("buffer new\n", write.Text);
        Assert.False(write.HasBom);
        Assert.Equal("disk old\n", File.ReadAllText(temp.File("Edit.txt")));
    }

    [Fact]
    public async Task The_edit_tool_reports_a_rethrown_read_failure_as_could_not_be_read()
    {
        using var temp = new TempDirectory();
        var files = new FakeTextFileAccess { ExistsError = new IOException("editor down") };

        var result = await ExecuteAsync(
            new EditTool(new Workspace(temp.Root), files: files),
            """{"path":"Edit.txt","old_text":"old","new_text":"new"}"""
        );

        Assert.True(result.IsError);
        Assert.Equal("could not be read: editor down", result.Output);
    }

    [Fact]
    public async Task The_edit_tool_reports_a_rethrown_write_failure_as_could_not_be_written()
    {
        using var temp = new TempDirectory();
        var root = new Workspace(temp.Root).WorktreeRoot;
        var files = new FakeTextFileAccess { WriteError = new IOException("editor down") };
        files.Files[Path.Combine(root, "Edit.txt")] = "buffer old\n";

        var result = await ExecuteAsync(
            new EditTool(new Workspace(temp.Root), files: files),
            """{"path":"Edit.txt","old_text":"buffer old","new_text":"buffer new"}"""
        );

        Assert.True(result.IsError);
        Assert.Equal("could not be written: editor down", result.Output);
    }

    [Fact]
    public async Task The_write_tool_reports_a_rethrown_read_failure_as_could_not_be_read()
    {
        using var temp = new TempDirectory();
        var files = new FakeTextFileAccess { ExistsError = new IOException("editor down") };

        var result = await ExecuteAsync(
            new WriteTool(new Workspace(temp.Root), files: files),
            """{"path":"New.txt","content":"seam write\n"}"""
        );

        Assert.True(result.IsError);
        Assert.Equal("could not be read: editor down", result.Output);
    }

    [Fact]
    public async Task The_write_tool_reports_a_rethrown_write_failure_as_could_not_be_written()
    {
        using var temp = new TempDirectory();
        var files = new FakeTextFileAccess { WriteError = new IOException("editor down") };

        var result = await ExecuteAsync(
            new WriteTool(new Workspace(temp.Root), files: files),
            """{"path":"New.txt","content":"seam write\n"}"""
        );

        Assert.True(result.IsError);
        Assert.Equal("could not be written: editor down", result.Output);
    }

    private sealed class FakeTextFileAccess : ITextFileAccess
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public List<(string Path, string Text, bool HasBom)> Writes { get; } = [];

        public Exception? ExistsError { get; set; }

        public Exception? WriteError { get; set; }

        public void WriteAllText(string path, string content) => WriteRaw(path, content, false);

        public void WriteRaw(string path, string text, bool hasBom)
        {
            if (WriteError is { } error)
            {
                throw error;
            }

            Files[path] = text;
            Writes.Add((path, text, hasBom));
        }

        public bool Exists(string path) =>
            ExistsError is { } error ? throw error : Files.ContainsKey(path);

        public (string Text, long Length)? ReadPrefix(string path, int maxBytes) => null;

        public string ReadAllText(string path) => Files[path];

        public (string Text, bool HasBom) ReadRaw(string path) => (Files[path], false);
    }

    private static Task<ToolResult> ExecuteAsync(ITool tool, string arguments) =>
        tool.ExecuteAsync(JsonDocument.Parse(arguments).RootElement.Clone(), Context, default);
}
