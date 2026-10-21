using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The optional <c>find</c> tool: glob matching over the workspace walk, files by default and
/// directories on request, bounded at 100 with a 500 hard cap.
/// </summary>
public sealed class FindToolTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    [Fact]
    public void Tool_shape_is_pinned()
    {
        using var temp = new TempDirectory();
        FindTool tool = Tool(temp);

        Assert.Equal("find", tool.Name);
        Assert.Equal(ToolRisk.ReadOnly, tool.Risk);
        JsonElement schema = tool.ParametersSchema;
        Assert.Contains(
            schema.GetProperty("required").EnumerateArray(),
            element => element.GetString() == "pattern"
        );
        JsonElement properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("path", out _));
        Assert.True(properties.TryGetProperty("type", out _));
        Assert.True(properties.TryGetProperty("max_results", out _));
    }

    [Fact]
    public async Task A_glob_matches_files_at_any_depth()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("src"));
        File.WriteAllText(temp.File("src/a.cs"), string.Empty);
        File.WriteAllText(temp.File("b.txt"), string.Empty);

        ToolResult result = await FindAsync(temp, """{"pattern":"*.cs"}""");

        Assert.False(result.IsError);
        Assert.Equal("src/a.cs", result.Output);
    }

    [Fact]
    public async Task Files_are_the_default_type_and_directories_are_excluded()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("src"));
        File.WriteAllText(temp.File("src/a.cs"), string.Empty);
        File.WriteAllText(temp.File("src/notes.txt"), string.Empty);

        ToolResult result = await FindAsync(temp, """{"pattern":"*"}""");

        Assert.False(result.IsError);
        Assert.DoesNotContain("src/", result.Output.Split('\n'));
        Assert.Contains("src/a.cs", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/notes.txt", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_dir_returns_directories_with_a_trailing_slash()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("src"));
        Directory.CreateDirectory(temp.File("src/nested"));
        File.WriteAllText(temp.File("src/a.cs"), string.Empty);

        ToolResult result = await FindAsync(temp, """{"pattern":"*","type":"dir"}""");

        Assert.False(result.IsError);
        Assert.Equal("src/\nsrc/nested/", result.Output);
    }

    [Fact]
    public async Task The_default_cap_is_100_and_the_footer_reports_the_remainder()
    {
        using var temp = new TempDirectory();
        for (var index = 0; index < 600; index++)
        {
            File.WriteAllText(temp.File($"f{index:000}.txt"), string.Empty);
        }

        ToolResult result = await FindAsync(temp, """{"pattern":"*.txt"}""");

        string[] lines = result.Output.Split('\n');
        Assert.Equal(101, lines.Length);
        Assert.Equal("f000.txt", lines[0]);
        Assert.Equal("f099.txt", lines[99]);
        Assert.Equal("[500 more files]", lines[100]);
    }

    [Fact]
    public async Task The_cap_is_clamped_to_500()
    {
        using var temp = new TempDirectory();
        for (var index = 0; index < 600; index++)
        {
            File.WriteAllText(temp.File($"f{index:000}.txt"), string.Empty);
        }

        ToolResult result = await FindAsync(temp, """{"pattern":"*.txt","max_results":1000}""");

        string[] lines = result.Output.Split('\n');
        Assert.Equal(501, lines.Length);
        Assert.Equal("[100 more files]", lines[500]);
    }

    [Fact]
    public async Task A_path_scopes_the_match_and_the_output()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("src"));
        Directory.CreateDirectory(temp.File("tests"));
        File.WriteAllText(temp.File("src/a.cs"), string.Empty);
        File.WriteAllText(temp.File("tests/a.cs"), string.Empty);

        ToolResult result = await FindAsync(temp, """{"pattern":"*.cs","path":"src"}""");

        Assert.False(result.IsError);
        Assert.Equal("src/a.cs", result.Output);
    }

    [Fact]
    public async Task Gitignore_excludes_ignored_files()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File(".gitignore"), "bin/\n");
        Directory.CreateDirectory(temp.File("bin"));
        File.WriteAllText(temp.File("bin/skip.cs"), string.Empty);
        Directory.CreateDirectory(temp.File("src"));
        File.WriteAllText(temp.File("src/keep.cs"), string.Empty);

        ToolResult result = await FindAsync(temp, """{"pattern":"*.cs"}""");

        Assert.False(result.IsError);
        Assert.Equal("src/keep.cs", result.Output);
    }

    [Fact]
    public async Task Matching_is_case_sensitive_on_linux_and_insensitive_elsewhere()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Mixed.CS"), string.Empty);

        ToolResult result = await FindAsync(temp, """{"pattern":"*.cs"}""");

        Assert.False(result.IsError);
        if (OperatingSystem.IsLinux())
        {
            Assert.DoesNotContain("Mixed.CS", result.Output, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("Mixed.CS", result.Output);
        }
    }

    [Fact]
    public async Task A_pattern_with_a_slash_is_anchored()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("src"));
        Directory.CreateDirectory(temp.File("tests"));
        File.WriteAllText(temp.File("src/a.cs"), string.Empty);
        File.WriteAllText(temp.File("tests/a.cs"), string.Empty);

        ToolResult result = await FindAsync(temp, """{"pattern":"src/*.cs"}""");

        Assert.False(result.IsError);
        Assert.Equal("src/a.cs", result.Output);
    }

    [Fact]
    public async Task No_matches_names_the_pattern_and_the_root()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("src"));

        ToolResult result = await FindAsync(temp, """{"pattern":"*.cs","path":"src"}""");

        Assert.False(result.IsError);
        Assert.Equal("No files matching '*.cs' in src", result.Output);
    }

    [Fact]
    public async Task A_path_outside_the_workspace_is_refused()
    {
        using var temp = new TempDirectory();

        ToolResult result = await FindAsync(temp, """{"pattern":"*","path":"../.."}""");

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace", result.Output, StringComparison.Ordinal);
        Assert.Contains("allowed roots", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{}""", "pattern is required and must be a non-empty string")]
    [InlineData("{\"pattern\":\"\"}", "pattern is required and must be a non-empty string")]
    [InlineData("""{"pattern":"*","path":5}""", "path must be a string")]
    [InlineData("""{"pattern":"*","type":"symlink"}""", "type must be 'file' or 'dir'")]
    [InlineData("""{"pattern":"*","max_results":0}""", "max_results must be >= 1")]
    [InlineData("""{"pattern":"*","bogus":1}""", "unknown argument 'bogus'")]
    public async Task Malformed_arguments_are_errors(string arguments, string expected)
    {
        using var temp = new TempDirectory();

        ToolResult result = await FindAsync(temp, arguments);

        Assert.True(result.IsError);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    private static FindTool Tool(TempDirectory temp) => new(new Workspace(temp.Root));

    private static async Task<ToolResult> FindAsync(TempDirectory temp, string arguments)
    {
        JsonElement args = JsonDocument.Parse(arguments).RootElement.Clone();
        return await Tool(temp).ExecuteAsync(args, Context, CancellationToken.None);
    }
}
