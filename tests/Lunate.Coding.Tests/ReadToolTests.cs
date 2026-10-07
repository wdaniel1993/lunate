using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

internal sealed class NullAgentEvents : IAgentEvents
{
    public void Emit(AgentEvent agentEvent) { }
}

public sealed class ReadToolTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    [Fact]
    public void Tool_shape_is_pinned()
    {
        var tool = new ReadTool(new Workspace(Path.GetTempPath()));

        Assert.Equal("read", tool.Name);
        Assert.Equal(ToolRisk.ReadOnly, tool.Risk);
        var schema = tool.ParametersSchema;
        Assert.Contains(
            schema.GetProperty("required").EnumerateArray(),
            element => element.GetString() == "path"
        );
        var properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("path", out _));
        Assert.True(properties.TryGetProperty("offset", out _));
        Assert.True(properties.TryGetProperty("limit", out _));
    }

    [Fact]
    public async Task Lines_are_numbered_and_the_footer_names_the_window()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Lines.txt"), "a\nb\nc\nd\n");

        var result = await ReadAsync(temp, """{"path":"Lines.txt","limit":2}""");

        Assert.False(result.IsError);
        Assert.Equal(
            "     1|a\n     2|b\n[lines 1\u20132 of 4, use offset to continue]",
            result.Output
        );
    }

    [Fact]
    public async Task Paging_returns_exactly_the_requested_window()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Lines.txt"), "a\nb\nc\nd\n");

        var result = await ReadAsync(temp, """{"path":"Lines.txt","offset":2,"limit":2}""");

        Assert.False(result.IsError);
        Assert.Equal(
            "     2|b\n     3|c\n[lines 2\u20133 of 4, use offset to continue]",
            result.Output
        );
    }

    [Fact]
    public async Task The_final_window_has_no_footer()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Lines.txt"), "a\nb\nc");

        var result = await ReadAsync(temp, """{"path":"Lines.txt"}""");

        Assert.False(result.IsError);
        Assert.Equal("     1|a\n     2|b\n     3|c", result.Output);
    }

    [Fact]
    public async Task Limit_above_the_maximum_is_clamped_to_2000()
    {
        using var temp = new TempDirectory();
        var content = string.Join('\n', Enumerable.Range(1, 2005)) + "\n";
        File.WriteAllText(temp.File("Many.txt"), content);

        var result = await ReadAsync(temp, """{"path":"Many.txt","limit":9000}""");

        Assert.False(result.IsError);
        var lines = result.Output.Split('\n');
        Assert.Equal(2001, lines.Length);
        Assert.Equal("     1|1", lines[0]);
        Assert.Equal("  2000|2000", lines[1999]);
        Assert.Equal("[lines 1\u20132000 of 2005, use offset to continue]", lines[^1]);
    }

    [Fact]
    public async Task An_empty_file_reads_as_empty()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Empty.txt"), "");

        var result = await ReadAsync(temp, """{"path":"Empty.txt"}""");

        Assert.False(result.IsError);
        Assert.Equal("[empty file]", result.Output);
    }

    [Fact]
    public async Task CRLF_and_a_BOM_are_normalized_for_display()
    {
        using var temp = new TempDirectory();
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("one\r\ntwo\r\n")];
        File.WriteAllBytes(temp.File("Dos.txt"), bytes);

        var result = await ReadAsync(temp, """{"path":"Dos.txt"}""");

        Assert.False(result.IsError);
        Assert.Equal("     1|one\n     2|two", result.Output);
    }

    [Fact]
    public async Task A_missing_file_is_an_error()
    {
        using var temp = new TempDirectory();

        var result = await ReadAsync(temp, """{"path":"Missing.txt"}""");

        Assert.True(result.IsError);
        Assert.Equal("file not found: Missing.txt", result.Output);
    }

    [Fact]
    public async Task A_directory_is_an_error_that_points_at_bash_ls()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("dir"));

        var result = await ReadAsync(temp, """{"path":"dir"}""");

        Assert.True(result.IsError);
        Assert.Equal("dir is a directory; use bash ls", result.Output);
    }

    [Fact]
    public async Task A_binary_file_is_an_error_naming_its_size()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("bin.dat"), [0x01, 0x00, 0x02]);

        var result = await ReadAsync(temp, """{"path":"bin.dat"}""");

        Assert.True(result.IsError);
        Assert.Equal("bin.dat is a binary file (3 bytes); read handles text files", result.Output);
    }

    [Fact]
    public async Task A_path_outside_the_workspace_is_an_error()
    {
        using var temp = new TempDirectory();
        var work = temp.File("work");
        Directory.CreateDirectory(work);
        File.WriteAllText(temp.File("outside.txt"), "secret");

        var result = await ReadAsync(work, """{"path":"../outside.txt"}""");

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace", result.Output);
    }

    [Fact]
    public async Task An_offset_past_the_end_is_an_error_naming_the_total()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Two.txt"), "a\nb\n");

        var result = await ReadAsync(temp, """{"path":"Two.txt","offset":3}""");

        Assert.True(result.IsError);
        Assert.Equal("offset 3 is past the end of Two.txt (2 lines)", result.Output);
    }

    [Theory]
    [InlineData("""{"path":"Lines.txt","offset":0}""", "offset must be >= 1")]
    [InlineData("""{"path":"Lines.txt","offset":-1}""", "offset must be >= 1")]
    [InlineData("""{"path":"Lines.txt","limit":0}""", "limit must be >= 1")]
    public async Task Offset_and_limit_must_be_at_least_one(string arguments, string expected)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Lines.txt"), "a\n");

        var result = await ReadAsync(temp, arguments);

        Assert.True(result.IsError);
        Assert.Equal(expected, result.Output);
    }

    [Theory]
    [InlineData("""{}""", "path is required and must be a string")]
    [InlineData("""{"path":5}""", "path is required and must be a string")]
    [InlineData("""{"path":"Lines.txt","offset":"2"}""", "offset must be an integer")]
    [InlineData("""{"path":"Lines.txt","limit":1.5}""", "limit must be an integer")]
    public async Task Malformed_arguments_are_errors(string arguments, string expected)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Lines.txt"), "a\n");

        var result = await ReadAsync(temp, arguments);

        Assert.True(result.IsError);
        Assert.Equal(expected, result.Output);
    }

    private static async Task<ToolResult> ReadAsync(TempDirectory temp, string arguments) =>
        await ReadAsync(temp.Root, arguments);

    private static async Task<ToolResult> ReadAsync(string root, string arguments)
    {
        var tool = new ReadTool(new Workspace(root));
        var args = JsonDocument.Parse(arguments).RootElement.Clone();

        return await tool.ExecuteAsync(args, Context, CancellationToken.None);
    }
}
