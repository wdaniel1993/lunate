using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The optional <c>grep</c> tool. Every test runs the internal fallback engine directly, so the
/// assertions are the same whether or not ripgrep is installed on the machine.
/// </summary>
public sealed class GrepToolTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    [Fact]
    public void Tool_shape_is_pinned()
    {
        using var temp = new TempDirectory();
        GrepTool tool = Tool(temp);

        Assert.Equal("grep", tool.Name);
        Assert.Equal(ToolRisk.ReadOnly, tool.Risk);
        JsonElement schema = tool.ParametersSchema;
        Assert.Contains(
            schema.GetProperty("required").EnumerateArray(),
            element => element.GetString() == "pattern"
        );
        JsonElement properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("path", out _));
        Assert.True(properties.TryGetProperty("glob", out _));
        Assert.True(properties.TryGetProperty("case_sensitive", out _));
        Assert.True(properties.TryGetProperty("max_results", out _));
        Assert.Contains("ripgrep", tool.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Matches_are_sorted_by_path_and_then_line()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("b.txt"), "needle\n");
        File.WriteAllText(temp.File("a.txt"), "first\nneedle\nneedle\n");

        ToolResult result = await GrepAsync(temp, """{"pattern":"needle"}""");

        Assert.False(result.IsError);
        Assert.Equal("a.txt:2: needle\na.txt:3: needle\nb.txt:1: needle", result.Output);
    }

    [Fact]
    public async Task The_default_cap_is_100_and_the_footer_reports_the_remainder()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("big.txt"),
            string.Join('\n', Enumerable.Range(1, 600).Select(line => $"hit {line}"))
        );

        ToolResult result = await GrepAsync(temp, """{"pattern":"hit"}""");

        string[] lines = result.Output.Split('\n');
        Assert.Equal(101, lines.Length);
        Assert.Equal("big.txt:1: hit 1", lines[0]);
        Assert.Equal("big.txt:100: hit 100", lines[99]);
        Assert.Equal("[500 more matches]", lines[100]);
    }

    [Fact]
    public async Task The_cap_is_clamped_to_500()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("big.txt"),
            string.Join('\n', Enumerable.Range(1, 600).Select(line => $"hit {line}"))
        );

        ToolResult result = await GrepAsync(temp, """{"pattern":"hit","max_results":1000}""");

        string[] lines = result.Output.Split('\n');
        Assert.Equal(501, lines.Length);
        Assert.Equal("[100 more matches]", lines[500]);
    }

    [Fact]
    public async Task An_invalid_pattern_is_a_teaching_error()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("a.txt"), "needle\n");

        ToolResult result = await GrepAsync(temp, """{"pattern":"["}""");

        Assert.True(result.IsError);
        Assert.Contains("not a valid regular expression", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_path_outside_the_workspace_is_refused_naming_the_path_and_roots()
    {
        using var temp = new TempDirectory();

        ToolResult result = await GrepAsync(temp, """{"pattern":"needle","path":"../.."}""");

        Assert.True(result.IsError);
        Assert.Contains("'../..'", result.Output, StringComparison.Ordinal);
        Assert.Contains("outside the workspace", result.Output, StringComparison.Ordinal);
        Assert.Contains("allowed roots", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gitignore_excludes_ignored_files()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File(".gitignore"), "bin/\n");
        Directory.CreateDirectory(temp.File("bin"));
        File.WriteAllText(temp.File("bin/skip.txt"), "needle\n");
        Directory.CreateDirectory(temp.File("src"));
        File.WriteAllText(temp.File("src/keep.txt"), "needle\n");

        ToolResult result = await GrepAsync(temp, """{"pattern":"needle"}""");

        Assert.False(result.IsError);
        Assert.Equal("src/keep.txt:1: needle", result.Output);
    }

    [Fact]
    public async Task Binary_files_are_skipped()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("blob.bin"), [.. "needle"u8, 0, .. "tail"u8]);

        ToolResult result = await GrepAsync(temp, """{"pattern":"needle"}""");

        Assert.False(result.IsError);
        Assert.Equal("No matches for 'needle' in .", result.Output);
    }

    [Fact]
    public async Task A_glob_limits_the_files_searched()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("a.cs"), "needle\n");
        File.WriteAllText(temp.File("a.txt"), "needle\n");

        ToolResult result = await GrepAsync(temp, """{"pattern":"needle","glob":"*.cs"}""");

        Assert.False(result.IsError);
        Assert.Equal("a.cs:1: needle", result.Output);
    }

    [Fact]
    public async Task Matching_is_case_sensitive_by_default()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("upper.txt"), "Needle\n");
        File.WriteAllText(temp.File("lower.txt"), "needle\n");

        ToolResult result = await GrepAsync(temp, """{"pattern":"Needle"}""");

        Assert.False(result.IsError);
        Assert.Equal("upper.txt:1: Needle", result.Output);
    }

    [Fact]
    public async Task Case_can_be_ignored()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("upper.txt"), "Needle\n");
        File.WriteAllText(temp.File("lower.txt"), "needle\n");

        ToolResult result = await GrepAsync(
            temp,
            """{"pattern":"needle","case_sensitive":false}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("lower.txt:1: needle\nupper.txt:1: Needle", result.Output);
    }

    [Fact]
    public async Task A_file_path_searches_only_that_file()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("a.txt"), "needle\n");
        File.WriteAllText(temp.File("b.txt"), "needle\n");

        ToolResult result = await GrepAsync(temp, """{"pattern":"needle","path":"a.txt"}""");

        Assert.False(result.IsError);
        Assert.Equal("a.txt:1: needle", result.Output);
    }

    [Fact]
    public async Task No_matches_names_the_pattern_and_the_root()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("src"));
        File.WriteAllText(temp.File("src/a.txt"), "haystack\n");

        ToolResult result = await GrepAsync(
            temp,
            """{"pattern":"needle","path":"src","glob":"*.txt"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("No matches for 'needle' in src (glob '*.txt')", result.Output);
    }

    [Fact]
    public async Task The_fallback_engine_reports_the_same_shape_directly()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("a.txt"), "one\nneedle\n");

        GrepSearch result = await new FallbackGrepEngine().SearchAsync(
            new GrepRequest(
                Pattern: "needle",
                Root: temp.Root,
                RelativeRoot: ".",
                SearchIsFile: false,
                Glob: null,
                CaseSensitive: true,
                MaxResults: 100
            ),
            CancellationToken.None
        );

        GrepMatch match = Assert.Single(result.Matches);
        Assert.Equal("a.txt", match.Path);
        Assert.Equal(2, match.Line);
        Assert.Equal("needle", match.Text);
        Assert.Equal(0, result.More);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task A_directory_symlink_is_not_followed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        using var outside = new TempDirectory();
        File.WriteAllText(outside.File("secret.txt"), "needle\n");
        try
        {
            Directory.CreateSymbolicLink(temp.File("link"), outside.Root);
        }
        catch (IOException)
        {
            return;
        }

        ToolResult result = await GrepAsync(temp, """{"pattern":"needle"}""");

        Assert.False(result.IsError);
        Assert.Equal("No matches for 'needle' in .", result.Output);
    }

    [Fact]
    public async Task A_file_symlink_is_not_searched()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        using var outside = new TempDirectory();
        File.WriteAllText(outside.File("auth.json"), "needle\n");
        try
        {
            File.CreateSymbolicLink(temp.File("auth.json"), outside.File("auth.json"));
        }
        catch (IOException)
        {
            return;
        }

        ToolResult result = await GrepAsync(temp, """{"pattern":"needle"}""");

        Assert.False(result.IsError);
        Assert.Equal("No matches for 'needle' in .", result.Output);
    }

    [Fact]
    public async Task A_symlinked_file_named_as_the_search_path_is_refused_and_named()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        using var outside = new TempDirectory();
        File.WriteAllText(outside.File("auth.json"), "needle\n");
        try
        {
            File.CreateSymbolicLink(temp.File("auth.json"), outside.File("auth.json"));
        }
        catch (IOException)
        {
            return;
        }

        ToolResult result = await GrepAsync(temp, """{"pattern":"needle","path":"auth.json"}""");

        Assert.True(result.IsError);
        Assert.Contains("'auth.json'", result.Output, StringComparison.Ordinal);
        Assert.Contains("outside the workspace", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>RIPGREP_CONFIG_PATH</c> is read by every <c>rg</c> process, so a config that injects
    /// <c>--follow</c> would otherwise let the engine search through a symlink out of the workspace
    /// while the fallback does not. The proof runs in a child process because the variable is
    /// process-wide.
    /// </summary>
    [Fact]
    public async Task A_ripgrep_config_with_follow_does_not_change_the_results()
    {
        const string Method = nameof(A_ripgrep_config_with_follow_does_not_change_the_results);
        if (TestChildProcess.IsChild(Method))
        {
            if (RipgrepGrepEngine.TryCreate() is not { } ripgrep)
            {
                return;
            }

            using var temp = new TempDirectory();
            using var outside = new TempDirectory();
            File.WriteAllText(outside.File("secret.txt"), "needle\n");
            try
            {
                File.CreateSymbolicLink(temp.File("link.txt"), outside.File("secret.txt"));
                Directory.CreateSymbolicLink(temp.File("linkdir"), outside.Root);
            }
            catch (IOException)
            {
                return;
            }

            string config = temp.File("rg.conf");
            File.WriteAllText(config, "--follow\n");
            Environment.SetEnvironmentVariable("RIPGREP_CONFIG_PATH", config);
            try
            {
                var request = new GrepRequest(
                    Pattern: "needle",
                    Root: temp.Root,
                    RelativeRoot: ".",
                    SearchIsFile: false,
                    Glob: null,
                    CaseSensitive: true,
                    MaxResults: 100
                );

                GrepSearch expected = await new FallbackGrepEngine().SearchAsync(
                    request,
                    CancellationToken.None
                );
                GrepSearch actual = await ripgrep.SearchAsync(request, CancellationToken.None);

                Assert.Null(actual.Error);
                Assert.Empty(expected.Matches);
                Assert.Equal(expected.Matches, actual.Matches);
                Assert.Equal(expected.More, actual.More);
            }
            finally
            {
                Environment.SetEnvironmentVariable("RIPGREP_CONFIG_PATH", null);
            }

            return;
        }

        (int exitCode, string output) = await TestChildProcess.RunAsync(
            typeof(GrepToolTests),
            Method
        );
        Assert.True(exitCode == 0, $"the child probe failed (exit {exitCode}):{output}");
    }

    [Fact]
    public async Task The_ripgrep_engine_agrees_with_the_fallback()
    {
        if (RipgrepGrepEngine.TryCreate() is not { } ripgrep)
        {
            return;
        }

        using var temp = new TempDirectory();
        File.WriteAllText(temp.File(".gitignore"), "bin/\n");
        Directory.CreateDirectory(temp.File("bin"));
        File.WriteAllText(temp.File("bin/skip.txt"), "needle\n");
        File.WriteAllText(temp.File("a.txt"), "one\nneedle\n");
        File.WriteAllText(temp.File("b.txt"), "needle\n");
        File.WriteAllBytes(temp.File("blob.bin"), [.. "needle"u8, 0]);

        var request = new GrepRequest(
            Pattern: "needle",
            Root: temp.Root,
            RelativeRoot: ".",
            SearchIsFile: false,
            Glob: null,
            CaseSensitive: true,
            MaxResults: 100
        );

        GrepSearch expected = await new FallbackGrepEngine().SearchAsync(
            request,
            CancellationToken.None
        );
        GrepSearch actual = await ripgrep.SearchAsync(request, CancellationToken.None);

        Assert.Null(actual.Error);
        Assert.Equal(expected.Matches, actual.Matches);
        Assert.Equal(expected.More, actual.More);

        var globRequest = request with { Glob = "*.TXT" };
        GrepSearch expectedGlob = await new FallbackGrepEngine().SearchAsync(
            globRequest,
            CancellationToken.None
        );
        GrepSearch actualGlob = await ripgrep.SearchAsync(globRequest, CancellationToken.None);

        Assert.Null(actualGlob.Error);
        Assert.Equal(expectedGlob.Matches, actualGlob.Matches);
        Assert.Equal(expectedGlob.More, actualGlob.More);
    }

    [Theory]
    [InlineData("""{}""", "pattern is required and must be a non-empty string")]
    [InlineData("""{"pattern":5}""", "pattern must be a string")]
    [InlineData("""{"pattern":""}""", "pattern is required and must be a non-empty string")]
    [InlineData("""{"pattern":"x","path":5}""", "path must be a string")]
    [InlineData("""{"pattern":"x","glob":5}""", "glob must be a string")]
    [InlineData(
        """{"pattern":"x","case_sensitive":"yes"}""",
        "case_sensitive must be true or false"
    )]
    [InlineData("""{"pattern":"x","max_results":0}""", "max_results must be >= 1")]
    [InlineData("""{"pattern":"x","max_results":1.5}""", "max_results must be an integer")]
    [InlineData("""{"pattern":"x","bogus":1}""", "unknown argument 'bogus'")]
    public async Task Malformed_arguments_are_errors(string arguments, string expected)
    {
        using var temp = new TempDirectory();

        ToolResult result = await GrepAsync(temp, arguments);

        Assert.True(result.IsError);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
    }

    private static GrepTool Tool(TempDirectory temp) =>
        new(new Workspace(temp.Root), new FallbackGrepEngine());

    private static async Task<ToolResult> GrepAsync(TempDirectory temp, string arguments)
    {
        JsonElement args = JsonDocument.Parse(arguments).RootElement.Clone();
        return await Tool(temp).ExecuteAsync(args, Context, CancellationToken.None);
    }
}
