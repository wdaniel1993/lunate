using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class EditMatchingTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    [Fact]
    public void The_schema_carries_the_optional_start_line()
    {
        var schema = new EditTool(new Workspace(Path.GetTempPath())).ParametersSchema;
        var startLine = schema.GetProperty("properties").GetProperty("start_line");

        Assert.Equal("integer", startLine.GetProperty("type").GetString());
        Assert.Equal(
            "Optional 1-based line where old_text starts; disambiguates when old_text matches several places",
            startLine.GetProperty("description").GetString()
        );
        Assert.DoesNotContain(
            "start_line",
            schema.GetProperty("required").EnumerateArray().Select(element => element.GetString())
        );
    }

    [Fact]
    public async Task A_unique_indent_match_applies_at_tier_3()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("File.txt"),
            "class X\n{\n    void M()\n    {\n        return;\n    }\n}\n"
        );

        var result = await EditAsync(
            temp,
            "void M()\n{\n    return;\n}",
            "void N()\n{\n    return 1;\n}"
        );

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 3\u20136 (match: indent)", result.Output);
        Assert.Equal(
            "class X\n{\n    void N()\n    {\n        return 1;\n    }\n}\n",
            File.ReadAllText(temp.File("File.txt"))
        );
    }

    [Fact]
    public async Task Tier_3_ignores_trailing_whitespace_too()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "alpha\n    x\n    y\nomega\n");

        var result = await EditAsync(temp, "x  \ny ", "X\nY");

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 2\u20133 (match: indent)", result.Output);
        Assert.Equal("alpha\n    X\n    Y\nomega\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task A_unique_tier_2_match_wins_over_tier_3()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "x \n  x\n");

        var result = await EditAsync(temp, "x", "X");

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 1\u20131 (match: normalized)", result.Output);
        Assert.Equal("X\n  x\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task Tier_3_ignores_each_lines_whitespace_and_offsets_from_the_first_line()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "    x\n      y\n");

        var result = await EditAsync(temp, "x\ny", "a\nb");

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 1\u20132 (match: indent)", result.Output);
        Assert.Equal("    a\n    b\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task Tier_3_maps_blank_old_lines_to_whitespace_only_file_lines()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "    a\n   \n    b\n");

        var result = await EditAsync(temp, "a\n\nb", "x\n\ny");

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 1\u20133 (match: indent)", result.Output);
        Assert.Equal("    x\n\n    y\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task A_tier_3_ambiguity_is_an_error_listing_every_match()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "    same\nother\n    same\n");

        var result = await EditAsync(temp, "same", "single");

        Assert.True(result.IsError);
        Assert.Equal(
            "old_text matches 2 places in File.txt at lines 1, 3; make it longer so it matches once",
            result.Output
        );
        Assert.Equal("    same\nother\n    same\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task Start_line_narrows_a_tier_3_ambiguity()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "    same\na\nb\nc\nd\n    same\n");

        var result = await EditAsync(temp, "same", "FOUND", startLine: 6);

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 6\u20136 (match: indent)", result.Output);
        Assert.Equal("    same\na\nb\nc\nd\n    FOUND\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task Start_line_boundary_at_three_lines_is_inclusive()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "same\na\nb\nc\nd\ne\nf\ng\nsame\n");

        var result = await EditAsync(temp, "same", "FIRST", startLine: 4);

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 1\u20131 (match: exact)", result.Output);
        Assert.Equal("FIRST\na\nb\nc\nd\ne\nf\ng\nsame\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task Start_line_beyond_three_lines_from_every_match_is_an_ambiguity()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "same\na\nb\nc\nd\ne\nf\ng\nsame\n");

        var result = await EditAsync(temp, "same", "FIRST", startLine: 5);

        Assert.True(result.IsError);
        Assert.Equal(
            "old_text matches 2 places in File.txt at lines 1, 9; make it longer so it matches once",
            result.Output
        );
        Assert.Equal("same\na\nb\nc\nd\ne\nf\ng\nsame\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task A_single_match_ignores_start_line()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "a\nb\nc\nd\ne\ntarget\n");

        var result = await EditAsync(temp, "target", "TARGET", startLine: 1);

        Assert.False(result.IsError);
        Assert.Equal("edited File.txt lines 6\u20136 (match: exact)", result.Output);
        Assert.Equal("a\nb\nc\nd\ne\nTARGET\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("\"two\"")]
    [InlineData("1.5")]
    [InlineData("null")]
    public async Task A_malformed_start_line_is_an_error(string value)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "a\nb\n");
        var tool = new EditTool(new Workspace(temp.Root));
        var args = JsonDocument
            .Parse($$"""{"path":"File.txt","old_text":"a","new_text":"c","start_line":{{value}}}""")
            .RootElement.Clone();

        var result = await tool.ExecuteAsync(args, Context, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("start_line must be an integer greater than or equal to 1", result.Output);
        Assert.Equal("a\nb\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Theory]
    [InlineData("script.py")]
    [InlineData("data.yaml")]
    [InlineData("data.yml")]
    [InlineData("rules.mk")]
    [InlineData("Makefile")]
    [InlineData("makefile")]
    [InlineData("MAKEFILE")]
    public async Task Whitespace_significant_files_refuse_the_indent_tier(string name)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File(name), "def f():\n    return 1\n");

        var result = await EditAsync(temp, name, "return 1", "return 2");

        Assert.True(result.IsError);
        Assert.Equal(
            "could not find old_text in "
                + name
                + "; the indent tier is disabled for whitespace-significant files \u2014 re-read the file and edit with the exact text",
            result.Output
        );
        Assert.Equal("def f():\n    return 1\n", File.ReadAllText(temp.File(name)));
    }

    [Theory]
    [InlineData("def f():\n    return 1\n", "    return 1", "    return 2", "exact")]
    [InlineData("def f():\n    return 1   \n", "    return 1", "    return 2", "normalized")]
    public async Task Tiers_1_and_2_still_apply_on_whitespace_significant_files(
        string content,
        string oldText,
        string newText,
        string tier
    )
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("script.py"), content);

        var result = await EditAsync(temp, "script.py", oldText, newText);

        Assert.False(result.IsError);
        Assert.Equal($"edited script.py lines 2\u20132 (match: {tier})", result.Output);
        Assert.Equal("def f():\n    return 2\n", File.ReadAllText(temp.File("script.py")));
    }

    [Fact]
    public async Task A_tie_for_the_closest_region_uses_the_earliest_window()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "a\nb\na\nb\n");

        var result = await EditAsync(temp, "a\nx", "p\nq");

        Assert.True(result.IsError);
        Assert.Equal(
            "could not find old_text in File.txt; closest region (lines 1-2):\na\nb",
            result.Output
        );
        Assert.Equal("a\nb\na\nb\n", File.ReadAllText(temp.File("File.txt")));
    }

    [Fact]
    public async Task Nothing_similar_gives_the_plain_not_found_message()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("File.txt"), "one\ntwo\n");

        var result = await EditAsync(temp, "red\nblue", "p\nq");

        Assert.True(result.IsError);
        Assert.Equal("could not find old_text in File.txt", result.Output);
    }

    private static Task<ToolResult> EditAsync(
        TempDirectory temp,
        string oldText,
        string newText,
        int? startLine = null
    ) => EditAsync(temp, "File.txt", oldText, newText, startLine);

    private static Task<ToolResult> EditAsync(
        TempDirectory temp,
        string path,
        string oldText,
        string newText,
        int? startLine = null
    )
    {
        var args = new Dictionary<string, object?>
        {
            ["path"] = path,
            ["old_text"] = oldText,
            ["new_text"] = newText,
        };
        if (startLine is not null)
        {
            args["start_line"] = startLine;
        }

        return new EditTool(new Workspace(temp.Root)).ExecuteAsync(
            JsonSerializer.SerializeToElement(args),
            Context,
            CancellationToken.None
        );
    }
}
