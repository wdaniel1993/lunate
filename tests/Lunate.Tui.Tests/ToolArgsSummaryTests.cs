namespace Lunate.Tui.Tests;

public sealed class ToolArgsSummaryTests
{
    [Theory]
    [InlineData("read", """{"path":"src/a.cs","offset":1}""", "src/a.cs")]
    [InlineData("write", """{"path":"src/a.cs","content":"x"}""", "src/a.cs")]
    [InlineData("edit", """{"path":"src/a.cs","old_text":"a","new_text":"b"}""", "src/a.cs")]
    [InlineData("cs_rename", """{"name":"Counter"}""", "Counter")]
    [InlineData("cs_find_symbol", """{"name":"System.String"}""", "System.String")]
    [InlineData("cs_find_references", """{"name":"Widget.Method"}""", "Widget.Method")]
    [InlineData("cs_outline", """{"file":"src/a.cs"}""", "src/a.cs")]
    [InlineData("unknown_tool", """{"query":"find the bug","limit":3}""", "find the bug")]
    [InlineData("unknown_tool", """{"limit":3,"name":"second string"}""", "second string")]
    public void Known_fields_are_summarized(string tool, string args, string expected) =>
        Assert.Equal(expected, ToolArgsSummary.Summarize(tool, args));

    [Theory]
    [InlineData("""{"command":"git status","timeout_ms":1000}""", "git status")]
    [InlineData("{\"command\":\"git add .\\ngit commit -m x\"}", "git add .")]
    public void Bash_uses_the_first_command_line(string args, string expected) =>
        Assert.Equal(expected, ToolArgsSummary.Summarize("bash", args));

    [Fact]
    public void Bash_command_is_elided_at_sixty_characters()
    {
        string command = new('x', 100);
        string args = "{\"command\":\"" + command + "\"}";

        string summary = ToolArgsSummary.Summarize("bash", args);

        Assert.Equal(60, summary.Length);
        Assert.StartsWith(new string('x', 59), summary);
        Assert.EndsWith("\u2026", summary);
    }

    [Theory]
    [InlineData("not json", "not json")]
    [InlineData("  padded raw  ", "padded raw")]
    [InlineData("""{"path":42}""", """{"path":42}""")]
    [InlineData("{}", "{}")]
    [InlineData("""{"limit":3}""", """{"limit":3}""")]
    [InlineData("[1,2]", "[1,2]")]
    [InlineData("42", "42")]
    public void Invalid_or_unsupported_args_fall_back_to_the_trimmed_raw_text(
        string args,
        string expected
    ) => Assert.Equal(expected, ToolArgsSummary.Summarize("read", args));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_args_summarize_to_nothing(string? args) =>
        Assert.Equal("", ToolArgsSummary.Summarize("read", args));

    [Fact]
    public void Values_with_newlines_use_their_first_line()
    {
        Assert.Equal(
            "src/a.cs",
            ToolArgsSummary.Summarize("read", "{\"path\":\"src/a.cs\\nsrc/b.cs\"}")
        );
        Assert.Equal("line", ToolArgsSummary.Summarize("unknown", "{\"q\":\"line\\nrest\"}"));
    }

    [Fact]
    public void Values_are_trimmed()
    {
        Assert.Equal("src/a.cs", ToolArgsSummary.Summarize("read", """{"path":"  src/a.cs  "}"""));
    }

    [Fact]
    public void Json_escapes_are_unescaped_for_display()
    {
        Assert.Equal(
            "weird\"name.cs",
            ToolArgsSummary.Summarize("read", "{\"path\":\"weird\\\"name.cs\"}")
        );
    }

    [Fact]
    public void Huge_strings_do_not_throw()
    {
        string huge = new('p', 100_000);

        string summary = ToolArgsSummary.Summarize("read", "{\"path\":\"" + huge + "\"}");

        Assert.Equal(huge, summary);
    }

    [Fact]
    public void Missing_known_field_falls_through_to_the_generic_summary()
    {
        Assert.Equal(
            "fallback.cs",
            ToolArgsSummary.Summarize("read", """{"other":"fallback.cs"}""")
        );
    }

    [Theory]
    [InlineData(1, 13, "src/a.cs (1-13)")]
    [InlineData(2, 3, "src/a.cs (2-4)")]
    public void Read_offset_and_limit_become_the_line_range(int offset, int limit, string expected)
    {
        string args = $$"""{"path":"src/a.cs","offset":{{offset}},"limit":{{limit}}}""";

        Assert.Equal(expected, ToolArgsSummary.Summarize("read", args));
    }

    [Fact]
    public void Read_offset_alone_ends_the_range_at_the_result_line_count()
    {
        string output = string.Join('\n', Numbers(4));

        Assert.Equal(
            "src/a.cs (2-5)",
            ToolArgsSummary.Summarize("read", """{"path":"src/a.cs","offset":2}""", output)
        );
    }

    [Fact]
    public void Read_without_a_range_reports_the_result_line_count()
    {
        string output = string.Join('\n', Numbers(13));

        Assert.Equal(
            "src/a.cs (13 lines)",
            ToolArgsSummary.Summarize("read", """{"path":"src/a.cs"}""", output)
        );
    }

    [Fact]
    public void Read_single_line_result_counts_as_one_line()
    {
        Assert.Equal(
            "src/a.cs (1 line)",
            ToolArgsSummary.Summarize("read", """{"path":"src/a.cs"}""", "one")
        );
    }

    [Fact]
    public void Read_line_count_ignores_the_continue_hint()
    {
        string output =
            "     1|a\n     2|b\n[lines 1\u20132 of 10, use offset to continue]\n";

        Assert.Equal(
            "src/a.cs (2 lines)",
            ToolArgsSummary.Summarize("read", """{"path":"src/a.cs"}""", output)
        );
    }

    [Fact]
    public void Read_without_a_range_or_output_keeps_the_path()
    {
        Assert.Equal(
            "src/a.cs",
            ToolArgsSummary.Summarize("read", """{"path":"src/a.cs"}""", output: null)
        );
    }

    [Theory]
    [InlineData("myserver__search", """{"query":"find the bug","limit":3}""", "find the bug")]
    [InlineData("linear__create_issue", """{"title":"Fix the flaky test"}""", "Fix the flaky test")]
    public void Extension_and_mcp_tools_fall_back_to_the_first_string_value(
        string tool,
        string args,
        string expected
    ) => Assert.Equal(expected, ToolArgsSummary.Summarize(tool, args));

    private static IEnumerable<string> Numbers(int count) =>
        Enumerable.Range(1, count).Select(number => $"line {number}");
}
