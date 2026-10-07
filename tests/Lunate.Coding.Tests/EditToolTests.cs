using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class EditToolTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    [Fact]
    public void Tool_shape_is_pinned()
    {
        var tool = new EditTool(new Workspace(Path.GetTempPath()));

        Assert.Equal("edit", tool.Name);
        Assert.Equal(ToolRisk.Write, tool.Risk);
        var required = tool
            .ParametersSchema.GetProperty("required")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToHashSet();
        Assert.Equal(["new_text", "old_text", "path"], required.Order());
    }

    [Fact]
    public async Task A_unique_exact_match_applies_at_tier_1()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "one\ntwo\nthree\n");

        var result = await EditAsync(
            temp,
            """{"path":"File.txt","old_text":"two","new_text":"TWO"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 2\u20132 (match: exact)", result.Output);
        Assert.Equal("one\nTWO\nthree\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task The_details_carry_the_range_the_tier_and_the_diff()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "one\ntwo\nthree\n");

        var result = await EditAsync(
            temp,
            """{"path":"File.txt","old_text":"two","new_text":"TWO"}"""
        );

        var details = Assert.IsType<EditDetails>(result.Details);
        Assert.Equal("File.txt", details.Path);
        Assert.Equal(2, details.FirstLine);
        Assert.Equal(2, details.LastLine);
        Assert.Equal("exact", details.MatchTier);
        Assert.Equal(
            "--- a/File.txt\n+++ b/File.txt\n@@ -1,3 +1,3 @@\n one\n-two\n+TWO\n three",
            details.Diff
        );
    }

    [Fact]
    public async Task A_unique_normalized_match_applies_at_tier_2()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("Dos.txt"), Encoding.UTF8.GetBytes("one\r\ntwo\r\nthree\r\n"));

        var result = await EditAsync(
            temp,
            """{"path":"Dos.txt","old_text":"two","new_text":"TWO"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("edited Dos.txt lines 2\u20132 (match: normalized)", result.Output);
        Assert.Equal(
            Encoding.UTF8.GetBytes("one\r\nTWO\r\nthree\r\n"),
            File.ReadAllBytes(temp.File("Dos.txt"))
        );
    }

    [Fact]
    public async Task Trailing_whitespace_is_ignored_at_tier_2()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "alpha\nvalue = 1   \nomega\n");

        var result = await EditAsync(
            temp,
            """{"path":"File.txt","old_text":"value = 1","new_text":"value = 2"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 2\u20132 (match: normalized)", result.Output);
        Assert.Equal("alpha\nvalue = 2\nomega\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task A_tier_1_match_wins_over_an_ambiguous_tier_2()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "keep\n x\nx \nx\n");

        var result = await EditAsync(temp, """{"path":"File.txt","old_text":"x","new_text":"X"}""");

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 4\u20134 (match: exact)", result.Output);
        Assert.Equal("keep\n x\nx \nX\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task Ambiguity_is_an_error_listing_every_match()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "same\nother\nsame\n");

        var result = await EditAsync(
            temp,
            """{"path":"File.txt","old_text":"same","new_text":"single"}"""
        );

        Assert.True(result.IsError);
        Assert.Equal(
            "old_text matches 2 places in File.txt at lines 1, 3; make it longer so it matches once",
            result.Output
        );
        Assert.Equal("same\nother\nsame\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task A_normalized_ambiguity_lists_every_match()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "same\nother\nsame\n");

        var result = await EditAsync(
            temp,
            """{"path":"File.txt","old_text":"same\r","new_text":"single"}"""
        );

        Assert.True(result.IsError);
        Assert.Equal(
            "old_text matches 2 places in File.txt at lines 1, 3; make it longer so it matches once",
            result.Output
        );
        Assert.Equal("same\nother\nsame\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task A_missing_match_is_an_error()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "one\ntwo\n");

        var result = await EditAsync(
            temp,
            """{"path":"File.txt","old_text":"absent","new_text":"x"}"""
        );

        Assert.True(result.IsError);
        Assert.Equal("could not find old_text in File.txt", result.Output);
        Assert.Equal("one\ntwo\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task An_empty_old_text_is_an_error()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "one\ntwo\n");

        var result = await EditAsync(temp, """{"path":"File.txt","old_text":"","new_text":"x"}""");

        Assert.True(result.IsError);
        Assert.Equal("old_text must not be empty", result.Output);
    }

    [Fact]
    public async Task Identical_texts_are_an_error()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "one\ntwo\n");

        var result = await EditAsync(
            temp,
            """{"path":"File.txt","old_text":"two","new_text":"two"}"""
        );

        Assert.True(result.IsError);
        Assert.Equal("old_text and new_text are identical; nothing to change", result.Output);
    }

    [Fact]
    public async Task A_path_outside_the_workspace_is_an_error()
    {
        using var temp = new TempDirectory();
        var work = temp.File("work");
        Directory.CreateDirectory(work);
        File.WriteAllText(temp.File("outside.txt"), "keep");

        var result = await EditAsync(
            work,
            """{"path":"../outside.txt","old_text":"keep","new_text":"changed"}"""
        );

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace", result.Output);
        Assert.Equal("keep", File.ReadAllText(temp.File("outside.txt")));
    }

    [Fact]
    public async Task A_missing_file_is_an_error()
    {
        using var temp = new TempDirectory();

        var result = await EditAsync(
            temp,
            """{"path":"Missing.txt","old_text":"a","new_text":"b"}"""
        );

        Assert.True(result.IsError);
        Assert.Equal("file not found: Missing.txt", result.Output);
    }

    [Fact]
    public async Task A_directory_is_an_error()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("dir"));

        var result = await EditAsync(temp, """{"path":"dir","old_text":"a","new_text":"b"}""");

        Assert.True(result.IsError);
        Assert.Equal("dir is a directory", result.Output);
    }

    [Fact]
    public async Task CRLF_files_stay_CRLF()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("Dos.txt"), Encoding.UTF8.GetBytes("one\r\ntwo\r\nthree\r\n"));

        var result = await EditAsync(
            temp,
            """{"path":"Dos.txt","old_text":"one\r\ntwo","new_text":"first"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal(
            Encoding.UTF8.GetBytes("first\r\nthree\r\n"),
            File.ReadAllBytes(temp.File("Dos.txt"))
        );
    }

    [Fact]
    public async Task A_BOM_is_kept()
    {
        using var temp = new TempDirectory();
        byte[] input = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("one\ntwo\nthree\n")];
        File.WriteAllBytes(temp.File("Bom.txt"), input);

        var result = await EditAsync(
            temp,
            """{"path":"Bom.txt","old_text":"two","new_text":"TWO"}"""
        );

        Assert.False(result.IsError);
        byte[] expected = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("one\nTWO\nthree\n")];
        Assert.Equal(expected, File.ReadAllBytes(temp.File("Bom.txt")));
    }

    [Fact]
    public async Task A_missing_trailing_newline_is_preserved()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "one\ntwo");

        var result = await EditAsync(
            temp,
            """{"path":"File.txt","old_text":"two","new_text":"TWO"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("one\nTWO", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task Leading_whitespace_of_new_text_is_preserved()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "one\ntwo\n");

        var result = await EditAsync(
            temp,
            """{"path":"File.txt","old_text":"two","new_text":"  TWO"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("one\n  TWO\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task An_LF_file_with_CRLF_new_text_stays_LF()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "one\ntwo\n");

        var result = await EditAsync(
            temp,
            """{"path":"File.txt","old_text":"two","new_text":"x\r\ny"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 2\u20133 (match: exact)", result.Output);
        Assert.Equal("one\nx\ny\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task A_multiline_replacement_reports_its_range()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "one\ntwo\nthree\nfour\n");

        var result = await EditAsync(
            temp,
            """{"path":"File.txt","old_text":"two\nthree","new_text":"a\nb\nc"}"""
        );

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 2\u20134 (match: exact)", result.Output);
        Assert.Equal("one\na\nb\nc\nfour\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Theory]
    [InlineData("""[]""", "arguments must be a JSON object")]
    [InlineData("""{}""", "path is required and must be a string")]
    [InlineData(
        """{"path":5,"old_text":"a","new_text":"b"}""",
        "path is required and must be a string"
    )]
    [InlineData(
        """{"path":"File.txt","new_text":"b"}""",
        "old_text is required and must be a string"
    )]
    [InlineData(
        """{"path":"File.txt","old_text":5,"new_text":"b"}""",
        "old_text is required and must be a string"
    )]
    [InlineData(
        """{"path":"File.txt","old_text":"a"}""",
        "new_text is required and must be a string"
    )]
    [InlineData(
        """{"path":"File.txt","old_text":"a","new_text":5}""",
        "new_text is required and must be a string"
    )]
    public async Task Malformed_arguments_are_errors(string arguments, string expected)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "a\nb\n");

        var result = await EditAsync(temp, arguments);

        Assert.True(result.IsError);
        Assert.Equal(expected, result.Output);
    }

    [Fact]
    public async Task A_five_megabyte_file_applies_under_the_ci_tripwire()
    {
        using var temp = new TempDirectory();
        var content = new StringBuilder();
        var number = 0;
        while (content.Length < 5 * 1024 * 1024)
        {
            number++;
            content
                .Append("line ")
                .Append(number.ToString("D7", CultureInfo.InvariantCulture))
                .Append('\n');
        }

        File.WriteAllText(temp.File("Big.txt"), content.ToString());
        var args = JsonSerializer.Serialize(
            new
            {
                path = "Big.txt",
                old_text = "line 0060000",
                new_text = "changed",
            }
        );

        var stopwatch = Stopwatch.StartNew();
        var result = await EditAsync(temp, args);
        stopwatch.Stop();

        Assert.False(result.IsError);
        // CI tripwire: the corpus target is 200 ms on a development machine (design.md);
        // shared CI runners are several times slower, so this gate only catches pathological
        // regressions (for example a quadratic match or diff over 300k lines).
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(1500),
            $"edit took {stopwatch.ElapsedMilliseconds} ms"
        );
        Assert.Contains(
            "line 0059999\nchanged\nline 0060001\n",
            File.ReadAllText(temp.File("Big.txt"))
        );
    }

    private static Task<ToolResult> EditAsync(TempDirectory temp, string arguments) =>
        EditAsync(temp.Root, arguments);

    private static Task<ToolResult> EditAsync(string root, string arguments)
    {
        var tool = new EditTool(new Workspace(root));
        var args = JsonDocument.Parse(arguments).RootElement.Clone();

        return tool.ExecuteAsync(args, Context, CancellationToken.None);
    }
}
