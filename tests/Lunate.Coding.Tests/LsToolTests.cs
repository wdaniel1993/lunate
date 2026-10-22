using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The optional <c>ls</c> tool: one directory listing or a bounded tree, directories first, files
/// with their size, hidden entries included and <c>.git</c> always skipped.
/// </summary>
public sealed class LsToolTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    [Fact]
    public void Tool_shape_is_pinned()
    {
        using var temp = new TempDirectory();
        LsTool tool = new(new Workspace(temp.Root));

        Assert.Equal("ls", tool.Name);
        Assert.Equal(ToolRisk.ReadOnly, tool.Risk);
        JsonElement properties = tool.ParametersSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("path", out _));
        Assert.True(properties.TryGetProperty("depth", out _));
        Assert.True(properties.TryGetProperty("max_entries", out _));
        Assert.False(tool.ParametersSchema.TryGetProperty("required", out _));
    }

    [Fact]
    public async Task Depth_one_lists_directories_first_with_file_sizes()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("z"));
        Directory.CreateDirectory(temp.File("a"));
        File.WriteAllText(temp.File("a.txt"), "ab");
        File.WriteAllText(temp.File("b.txt"), "abcde");

        ToolResult result = await LsAsync(temp, "{}");

        Assert.False(result.IsError);
        Assert.Equal("a/\nz/\na.txt  2\nb.txt  5", result.Output);
    }

    [Fact]
    public async Task Depth_renders_an_indented_tree()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("src/nested"));
        File.WriteAllText(temp.File("src/a.cs"), string.Empty);
        File.WriteAllText(temp.File("src/nested/b.cs"), string.Empty);
        File.WriteAllText(temp.File("README.md"), string.Empty);

        ToolResult result = await LsAsync(temp, """{"depth":3}""");

        Assert.False(result.IsError);
        Assert.Equal("src/\n  nested/\n    b.cs  0\n  a.cs  0\nREADME.md  0", result.Output);
    }

    [Fact]
    public async Task Depth_is_clamped_to_three()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("d1/d2/d3/d4"));
        File.WriteAllText(temp.File("d1/d2/d3/d4/deep.txt"), string.Empty);

        ToolResult result = await LsAsync(temp, """{"depth":10}""");

        Assert.False(result.IsError);
        Assert.Contains("d3/", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("d4", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Git_is_skipped_at_every_depth()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File(".git/objects"));
        File.WriteAllText(temp.File(".git/config"), string.Empty);
        File.WriteAllText(temp.File(".git/objects/x"), string.Empty);
        File.WriteAllText(temp.File("keep.txt"), string.Empty);

        ToolResult result = await LsAsync(temp, """{"depth":3}""");

        Assert.False(result.IsError);
        Assert.Equal("keep.txt  0", result.Output);
    }

    [Fact]
    public async Task Hidden_entries_are_included()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File(".config"));
        File.WriteAllText(temp.File(".hidden"), string.Empty);
        File.WriteAllText(temp.File("visible.txt"), string.Empty);

        ToolResult result = await LsAsync(temp, "{}");

        Assert.False(result.IsError);
        Assert.Equal(".config/\n.hidden  0\nvisible.txt  0", result.Output);
    }

    [Fact]
    public async Task The_entry_cap_is_500_and_the_footer_reports_the_remainder()
    {
        using var temp = new TempDirectory();
        for (var index = 0; index < 510; index++)
        {
            File.WriteAllText(temp.File($"f{index:000}.txt"), string.Empty);
        }

        ToolResult result = await LsAsync(temp, "{}");

        string[] lines = result.Output.Split('\n');
        Assert.Equal(501, lines.Length);
        Assert.Equal("f000.txt  0", lines[0]);
        Assert.Equal("f499.txt  0", lines[499]);
        Assert.Equal("[10 more entries]", lines[500]);
    }

    [Fact]
    public async Task An_empty_directory_is_named()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("empty"));

        ToolResult result = await LsAsync(temp, """{"path":"empty"}""");

        Assert.False(result.IsError);
        Assert.Equal("[empty directory]", result.Output);
    }

    [Fact]
    public async Task A_file_path_is_refused()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("a.txt"), string.Empty);

        ToolResult result = await LsAsync(temp, """{"path":"a.txt"}""");

        Assert.True(result.IsError);
        Assert.Contains("is a file", result.Output, StringComparison.Ordinal);
        Assert.Contains("ls lists directories", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_path_outside_the_workspace_is_refused()
    {
        using var temp = new TempDirectory();

        ToolResult result = await LsAsync(temp, """{"path":"../.."}""");

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace", result.Output, StringComparison.Ordinal);
        Assert.Contains("allowed roots", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"path":5}""", "path must be a string")]
    [InlineData("""{"depth":"two"}""", "depth must be an integer")]
    [InlineData("""{"depth":0}""", "depth must be >= 1")]
    [InlineData("""{"depth":1.5}""", "depth must be an integer")]
    [InlineData("""{"bogus":1}""", "unknown argument 'bogus'")]
    public async Task Malformed_arguments_are_errors(string arguments, string expected)
    {
        using var temp = new TempDirectory();

        ToolResult result = await LsAsync(temp, arguments);

        Assert.True(result.IsError);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    private static async Task<ToolResult> LsAsync(TempDirectory temp, string arguments)
    {
        JsonElement args = JsonDocument.Parse(arguments).RootElement.Clone();
        return await new LsTool(new Workspace(temp.Root)).ExecuteAsync(
            args,
            Context,
            CancellationToken.None
        );
    }
}
