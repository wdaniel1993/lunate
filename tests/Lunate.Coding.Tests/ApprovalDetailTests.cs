using System.Text.Json;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The approval panel's prepared detail: one test per tool shape — bash command, edit and write
/// diffs, read range, another tool's summary and unknown raw args — all bounded to the model's
/// line budget.
/// </summary>
public sealed class ApprovalDetailTests
{
    private const string Marker = "\u2026 +";
    private const string Minus = "\u2212";

    [Fact]
    public void Bash_command_wraps_at_the_cell_budget()
    {
        var detail = Build("bash", Args(("command", new string('x', 80))));

        Assert.Equal([new string('x', 72), new string('x', 8)], detail);
    }

    [Fact]
    public void Bash_command_wrap_never_splits_a_wide_rune()
    {
        string command = new('漢', 40);

        var detail = Build("bash", Args(("command", command)));

        Assert.Equal([new string('漢', 36), new string('漢', 4)], detail);
    }

    [Fact]
    public void Bash_multi_line_command_is_bounded_with_a_marker()
    {
        string command = string.Join('\n', Enumerable.Range(1, 9).Select(n => $"echo {n}"));

        var detail = Build("bash", Args(("command", command)));

        Assert.Equal("echo 1", detail[0]);
        Assert.Equal(
            ["echo 2", "echo 3", "echo 4", "echo 5", "echo 6", "\u2026 +3 lines"],
            detail.Skip(1)
        );
        Assert.Equal(ApprovalPromptModel.MaxDetailLines + 1, detail.Count);
    }

    [Fact]
    public void Bash_seven_line_command_keeps_six_lines_and_a_marker()
    {
        string command = string.Join('\n', Enumerable.Range(1, 7).Select(n => $"echo {n}"));

        var detail = Build("bash", Args(("command", command)));

        Assert.Equal(["echo 1", "echo 2", "echo 3", "echo 4", "echo 5", "echo 6"], detail.Take(6));
        Assert.Equal("\u2026 +1 lines", detail[^1]);
    }

    [Fact]
    public void Bash_marker_is_ascii_without_unicode()
    {
        string command = string.Join('\n', Enumerable.Range(1, 9).Select(n => $"echo {n}"));

        var detail = Build("bash", Args(("command", command)), unicode: false);

        Assert.Equal("... +3 lines", detail[^1]);
    }

    [Fact]
    public void Edit_shows_the_path_counts_and_diff()
    {
        var detail = Build(
            "edit",
            Args(("path", "src/a.cs"), ("old_text", "a\nb\n"), ("new_text", "a\nB\n"))
        );

        Assert.Equal($"src/a.cs  +1 {Minus}1", detail[0]);
        Assert.Equal(["@@ -1,2 +1,2 @@", " a", "-b", "+B"], detail.Skip(1));
    }

    [Fact]
    public void Edit_counts_are_ascii_without_unicode()
    {
        var detail = Build(
            "edit",
            Args(("path", "src/a.cs"), ("old_text", "b\n"), ("new_text", "B\n")),
            unicode: false
        );

        Assert.Equal("src/a.cs  +1 -1", detail[0]);
    }

    [Fact]
    public void Write_shows_the_diff_against_the_existing_file()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("a.txt"), "old\n");

        var detail = Build("write", Args(("path", "a.txt"), ("content", "new\n")), temp.Root);

        Assert.Equal($"a.txt  +1 {Minus}1", detail[0]);
        Assert.Equal(["@@ -1,1 +1,1 @@", "-old", "+new"], detail.Skip(1));
    }

    [Fact]
    public void Write_of_a_missing_file_shows_every_line_added()
    {
        using var temp = new TempDirectory();

        var detail = Build("write", Args(("path", "new.txt"), ("content", "hello\n")), temp.Root);

        Assert.Equal($"new.txt  +1 {Minus}0", detail[0]);
        Assert.Equal(["@@ -0,0 +1,1 @@", "+hello"], detail.Skip(1));
    }

    [Fact]
    public void Write_outside_the_root_previews_a_creation()
    {
        using var workspace = new TempDirectory();
        using var outside = new TempDirectory();
        File.WriteAllText(outside.File("a.txt"), "old\n");

        var detail = Build(
            "write",
            Args(("path", outside.File("a.txt")), ("content", "new\n")),
            workspace.Root
        );

        Assert.EndsWith($"  +1 {Minus}0", detail[0]);
        Assert.DoesNotContain("-old", detail);
    }

    [Fact]
    public void Read_shows_the_path_and_range()
    {
        var detail = Build("read", Args(("path", "src/a.cs"), ("offset", 10), ("limit", 5)));

        Assert.Equal(["src/a.cs (10-14)"], detail);
    }

    [Fact]
    public void Another_tool_shows_its_argument_summary()
    {
        var detail = Build("cs_find_symbol", Args(("symbol", "Widget")));

        Assert.Equal(["Widget"], detail);
    }

    [Fact]
    public void Unknown_tool_raw_args_are_wrapped_and_bounded()
    {
        var detail = Build(
            "mystery",
            JsonDocument
                .Parse(
                    """
                    {
                      "alpha": 1,
                      "beta": 2,
                      "gamma": 3,
                      "delta": 4,
                      "epsilon": 5,
                      "zeta": 6,
                      "eta": 7,
                      "theta": 8
                    }
                    """
                )
                .RootElement
        );

        Assert.Equal("{", detail[0]);
        Assert.Equal(ApprovalPromptModel.MaxDetailLines + 1, detail.Count);
        Assert.Equal("\u2026 +4 lines", detail[^1]);
    }

    [Fact]
    public void Undefined_args_have_no_detail() =>
        Assert.Empty(ApprovalDetail.Build("bash", default, null, unicode: true));

    private static IReadOnlyList<string> Build(
        string toolName,
        JsonElement args,
        string? workspaceRoot = null,
        bool unicode = true
    ) => ApprovalDetail.Build(toolName, args, workspaceRoot, unicode);

    private static JsonElement Args(params (string Key, object Value)[] fields) =>
        JsonSerializer.SerializeToElement(
            fields.ToDictionary(field => field.Key, field => field.Value)
        );
}
