using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class WriteToolTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    [Fact]
    public void Tool_shape_is_pinned()
    {
        var tool = new WriteTool(new Workspace(Path.GetTempPath()));

        Assert.Equal("write", tool.Name);
        Assert.Equal(ToolRisk.Write, tool.Risk);
        var required = tool
            .ParametersSchema.GetProperty("required")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToHashSet();
        Assert.Equal(["content", "path"], required.Order());
    }

    [Fact]
    public async Task Creating_a_file_creates_parent_directories()
    {
        using var temp = new TempDirectory();

        var result = await WriteAsync(
            temp,
            """{"path":"nested/deep/File.txt","content":"hello\n"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("wrote 1 lines to nested/deep/File.txt (created)", result.Output);
        Assert.Equal("hello\n", File.ReadAllText(temp.File("nested/deep/File.txt")));
    }

    [Fact]
    public async Task An_absolute_path_inside_reports_the_relative_display_form()
    {
        using var temp = new TempDirectory();
        var args = JsonSerializer.Serialize(new { path = temp.File("Abs.txt"), content = "x\n" });

        var result = await WriteAsync(temp, args);

        Assert.False(result.IsError);
        Assert.Equal("wrote 1 lines to Abs.txt (created)", result.Output);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("a\n", 1)]
    [InlineData("a\nb", 2)]
    [InlineData("a\nb\n", 2)]
    public async Task Line_counts_follow_the_content(string content, int expectedLines)
    {
        using var temp = new TempDirectory();
        var args = JsonSerializer.Serialize(new { path = "Count.txt", content });

        var result = await WriteAsync(temp, args);

        Assert.False(result.IsError);
        Assert.Equal($"wrote {expectedLines} lines to Count.txt (created)", result.Output);
    }

    [Fact]
    public async Task Replacing_a_file_reports_replaced_with_a_diff()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "old\n");

        var result = await WriteAsync(temp, """{"path":"File.txt","content":"new\n"}""");

        Assert.False(result.IsError);
        Assert.Equal("wrote 1 lines to File.txt (replaced)", result.Output);
        var details = Assert.IsType<WriteDetails>(result.Details);
        Assert.Equal("File.txt", details.Path);
        Assert.False(details.Created);
        Assert.Equal(1, details.Lines);
        Assert.Equal("--- a/File.txt\n+++ b/File.txt\n@@ -1,1 +1,1 @@\n-old\n+new", details.Diff);
    }

    [Fact]
    public async Task Creating_a_file_diff_shows_all_added_lines()
    {
        using var temp = new TempDirectory();

        var result = await WriteAsync(temp, """{"path":"New.txt","content":"a\nb\n"}""");

        var details = Assert.IsType<WriteDetails>(result.Details);
        Assert.True(details.Created);
        Assert.Equal(2, details.Lines);
        Assert.Equal("--- a/New.txt\n+++ b/New.txt\n@@ -0,0 +1,2 @@\n+a\n+b", details.Diff);
    }

    [Fact]
    public async Task A_replacement_diff_uses_three_context_lines()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "a\nb\nc\nd\ne\n");

        var result = await WriteAsync(temp, """{"path":"File.txt","content":"a\nb\nX\nd\ne\n"}""");

        var details = Assert.IsType<WriteDetails>(result.Details);
        Assert.Equal(
            "--- a/File.txt\n+++ b/File.txt\n@@ -1,5 +1,5 @@\n a\n b\n-c\n+X\n d\n e",
            details.Diff
        );
    }

    [Fact]
    public async Task Writing_identical_content_has_an_empty_diff()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Same.txt"), "same\n");

        var result = await WriteAsync(temp, """{"path":"Same.txt","content":"same\n"}""");

        Assert.False(result.IsError);
        Assert.Equal("wrote 1 lines to Same.txt (replaced)", result.Output);
        var details = Assert.IsType<WriteDetails>(result.Details);
        Assert.Equal("", details.Diff);
    }

    [Theory]
    [InlineData("a\r\nb\r\n")]
    [InlineData("a\nb")]
    [InlineData("Gr\u00fc\u00dfe, \u4e16\u754c\n")]
    public async Task Content_is_written_exactly_without_a_BOM(string content)
    {
        using var temp = new TempDirectory();
        var args = JsonSerializer.Serialize(new { path = "Bytes.txt", content });

        var result = await WriteAsync(temp, args);

        Assert.False(result.IsError);
        Assert.Equal(Encoding.UTF8.GetBytes(content), File.ReadAllBytes(temp.File("Bytes.txt")));
    }

    [Fact]
    public async Task Replacing_keeps_the_file_permissions()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not exist on Windows.");
            return;
        }

        using var temp = new TempDirectory();
        var path = temp.File("Script.sh");
        File.WriteAllText(path, "old\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
        var before = File.GetUnixFileMode(path);

        var result = await WriteAsync(temp, """{"path":"Script.sh","content":"new\n"}""");

        Assert.False(result.IsError);
        Assert.Equal(before, File.GetUnixFileMode(path));
    }

    [Fact]
    public async Task A_path_outside_the_workspace_is_refused_and_touches_nothing()
    {
        using var temp = new TempDirectory();
        var work = temp.File("work");
        Directory.CreateDirectory(work);
        File.WriteAllText(temp.File("outside.txt"), "keep");

        var result = await WriteAsync(work, """{"path":"../outside.txt","content":"changed"}""");

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace", result.Output);
        Assert.Equal("keep", File.ReadAllText(temp.File("outside.txt")));
    }

    [Fact]
    public async Task An_existing_directory_is_refused()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("dir"));

        var result = await WriteAsync(temp, """{"path":"dir","content":"x"}""");

        Assert.True(result.IsError);
        Assert.Equal("dir is a directory", result.Output);
    }

    [Theory]
    [InlineData("""{}""", "path is required and must be a string")]
    [InlineData("""{"path":5,"content":"x"}""", "path is required and must be a string")]
    [InlineData("""{"path":"File.txt"}""", "content is required and must be a string")]
    [InlineData("""{"path":"File.txt","content":5}""", "content is required and must be a string")]
    public async Task Malformed_arguments_are_errors(string arguments, string expected)
    {
        using var temp = new TempDirectory();

        var result = await WriteAsync(temp, arguments);

        Assert.True(result.IsError);
        Assert.Equal(expected, result.Output);
    }

    [Fact]
    public async Task Writes_are_routed_through_the_mutation_queue()
    {
        using var temp = new TempDirectory();
        var queue = new RecordingMutationQueue();
        var tool = new WriteTool(new Workspace(temp.Root), queue);
        var args = JsonDocument
            .Parse("""{"path":"Queued.txt","content":"x\n"}""")
            .RootElement.Clone();

        var result = await tool.ExecuteAsync(args, Context, CancellationToken.None);

        Assert.False(result.IsError);
        string path = Assert.Single(queue.Paths);
        Assert.Equal("Queued.txt", Path.GetFileName(path));
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.Equal("x\n", File.ReadAllText(temp.File("Queued.txt")));
    }

    private static Task<ToolResult> WriteAsync(TempDirectory temp, string arguments) =>
        WriteAsync(temp.Root, arguments);

    private static Task<ToolResult> WriteAsync(string root, string arguments)
    {
        var tool = new WriteTool(new Workspace(root));
        var args = JsonDocument.Parse(arguments).RootElement.Clone();

        return tool.ExecuteAsync(args, Context, CancellationToken.None);
    }

    private sealed class RecordingMutationQueue : IFileMutationQueue
    {
        public List<string> Paths { get; } = [];

        public Task<T> RunAsync<T>(
            string path,
            Func<CancellationToken, Task<T>> mutation,
            CancellationToken ct
        )
        {
            Paths.Add(path);
            return mutation(ct);
        }
    }
}
