using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The approval selection's layout: the <c>&gt; </c> marker before the selected bracket, the
/// 36-cell inline row at 40 columns, the stacked form below it, the letter-preserving clip, and the
/// <c>?</c> legend. Pure layout and frame checks, no terminal.
/// </summary>
public sealed class ApprovalSelectionLayoutTests
{
    private static readonly ApprovalPromptModel Prompt = new("bash", "dotnet format");

    [Theory]
    [InlineData(0, "> [y]es  [n]o  [a]lways this session")]
    [InlineData(1, "[y]es  > [n]o  [a]lways this session")]
    [InlineData(2, "[y]es  [n]o  > [a]lways this session")]
    public void The_marker_sits_before_the_selected_bracket(int selected, string expected)
    {
        ApprovalPanelLine row = ChoicesRow(Prompt with { Selected = selected }, width: 40);

        Assert.Equal(expected, row.Text);
        Assert.Equal(36, CellText.Width(row.Text));
    }

    [Fact]
    public void A_new_prompt_marks_no_and_keeps_every_choice_visible_at_forty_columns()
    {
        ApprovalPanelLine row = ChoicesRow(Prompt, width: 40);

        Assert.Equal("[y]es  > [n]o  [a]lways this session", row.Text);
        Assert.Equal(36, CellText.Width(row.Text));
        Assert.Equal(ApprovalPart.Question, row.Part);
    }

    [Fact]
    public void The_row_is_identical_without_colour_and_in_the_ascii_fallback()
    {
        string[] colour = Rows(Prompt, width: 40, ColorSystemSupport.NoColors, unicode: true);
        string[] ascii = Rows(Prompt, width: 40, ColorSystemSupport.NoColors, unicode: false);

        Assert.Equal(colour[^1], ascii[^1]);
        Assert.Equal("[y]es  > [n]o  [a]lways this session", ascii[^1]);
    }

    [Fact]
    public void Below_the_inline_row_the_choices_stack_with_the_gutter_kept()
    {
        var lines = ApprovalPromptRenderer.PlainLines(
            Prompt with
            {
                Selected = 1,
            },
            width: 30,
            ColorSystemSupport.NoColors,
            unicode: true
        );

        string[] choices = [.. lines.TakeLast(3).Select(line => line.Text)];
        Assert.Equal(["  [y]es", "> [n]o", "  [a]lways this session"], choices);
        Assert.All(lines, line => Assert.True(CellText.Width(line.Text) <= 30));
    }

    [Fact]
    public void A_stacked_label_clips_only_after_its_key_letter()
    {
        var lines = ApprovalPromptRenderer.PlainLines(
            Prompt with
            {
                Selected = 1,
            },
            width: 22,
            ColorSystemSupport.NoColors,
            unicode: true
        );

        string[] choices = [.. lines.TakeLast(3).Select(line => line.Text)];
        Assert.Equal(["  [y]es", "> [n]o", "  [a]lways this sessio"], choices);
        Assert.All(choices, choice => Assert.True(CellText.Width(choice) <= 22));
        Assert.Contains("[a]", choices[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(60)]
    [InlineData(80)]
    [InlineData(120)]
    public void No_rendered_line_exceeds_the_width(int width)
    {
        var prompt = new ApprovalPromptModel(
            "bash with a deliberately long tool name",
            string.Empty,
            Asker: "subagent (depth 1) with a long label",
            Detail:
            [
                "dotnet format --verify-no-changes --include src/Lunate/Tui/ApprovalPanelLayout.cs --severity info",
                "a detail line that is much longer than any of the widths under test, repeated for good measure",
            ],
            WorkspaceRoot: "~/a-rather-long-worktree-name/with/segments"
        );

        var lines = ApprovalPromptRenderer.PlainLines(
            prompt,
            width,
            ColorSystemSupport.NoColors,
            unicode: true
        );

        Assert.All(
            lines,
            line => Assert.True(CellText.Width(line.Text) <= width, $"'{line.Text}' > {width}")
        );
    }

    [Fact]
    public void At_forty_by_twelve_the_detail_leaves_before_a_choice_does()
    {
        var state = new LiveAreaState
        {
            Approval = new ApprovalPromptModel(
                "bash",
                "{}",
                Detail:
                [
                    "dotnet test --project tests/Lunate.Coding.Tests --filter Approval",
                    "  total: 42",
                    "  failed: 0",
                    "  succeeded: 42",
                    "  skipped: 0",
                    "  duration: 1s",
                ]
            ),
            Color = ColorSystemSupport.NoColors,
            Unicode = true,
            Footer = new StatusFooterModel(
                "deepseek-v4.1-flash",
                2100,
                1_000_000,
                "~/dev/lunate",
                "main"
            ),
            Size = new ConsoleSize(40, 12),
        };

        string[] lines = [.. LiveAreaRenderer.Render(state).Lines];

        Assert.True(lines.Length <= 12, $"the frame renders {lines.Length} rows");
        Assert.Contains(lines, line => line.Contains("Allow bash?", StringComparison.Ordinal));
        Assert.Contains(
            lines,
            line => line.Contains("[y]es  > [n]o  [a]lways this session", StringComparison.Ordinal)
        );
        Assert.Contains("  total: 42", lines);
        Assert.DoesNotContain(
            lines,
            line => line.Contains("duration: 1s", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void The_legend_names_every_key_and_keeps_the_question()
    {
        var lines = ApprovalPromptRenderer.PlainLines(
            Prompt with
            {
                Selected = 2,
                ShowLegend = true,
            },
            width: 80,
            ColorSystemSupport.NoColors,
            unicode: true
        );

        string[] legend =
        [
            .. lines.Where(line => line.Part == ApprovalPart.Legend).Select(l => l.Text),
        ];
        Assert.Equal(
            [
                "  y approve  n deny  a always for this session",
                "  Space choose  Enter or Escape deny",
            ],
            legend
        );
        Assert.Contains(
            lines,
            line => line.Text == "Allow bash?  [y]es  [n]o  > [a]lways this session"
        );
        Assert.Equal(ApprovalPart.Question, lines[^3].Part);
    }

    [Fact]
    public void The_legend_is_trimmed_before_the_headline_at_a_short_height()
    {
        var state = new LiveAreaState
        {
            Approval = Prompt with { ShowLegend = true },
            Color = ColorSystemSupport.NoColors,
            Unicode = true,
            Footer = new StatusFooterModel(
                "deepseek-v4.1-flash",
                2100,
                1_000_000,
                "~/dev/lunate",
                "main"
            ),
            Size = new ConsoleSize(40, 8),
        };

        string[] lines = [.. LiveAreaRenderer.Render(state).Lines];

        Assert.True(lines.Length <= 8, $"the frame renders {lines.Length} rows");
        Assert.Contains(
            lines,
            line => line.Contains("[y]es  > [n]o  [a]lways this session", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            lines,
            line => line.Contains("Space choose", StringComparison.Ordinal)
        );
    }

    private static ApprovalPanelLine ChoicesRow(ApprovalPromptModel prompt, int width) =>
        Assert.Single(
            ApprovalPromptRenderer.PlainLines(
                prompt,
                width,
                ColorSystemSupport.NoColors,
                unicode: true
            ),
            line => line.Text.Contains("[y]es", StringComparison.Ordinal)
        );

    private static string[] Rows(
        ApprovalPromptModel prompt,
        int width,
        ColorSystemSupport color,
        bool unicode
    ) => [.. ApprovalPromptRenderer.PlainLines(prompt, width, color, unicode).Select(l => l.Text)];
}
