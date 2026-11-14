using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// One transcript under each built-in theme: the same words in the same layout, with only the
/// colours the theme maps changing — <c>default</c> paints the palette's hex and named values,
/// <c>ansi16</c> only names the sixteen, and <c>mono</c> emits no colour attribute at all. Also
/// the capability downgrade a user token goes through: hex to 256, to the sixteen, to nothing.
/// Regenerate deliberately with
/// <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests</c>.
/// </summary>
public sealed class ThemeGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "Lunate.Tui.Tests", "fixtures", "themes");

    public static TheoryData<string> BuiltIns => new() { "default", "ansi16", "mono" };

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void One_transcript_matches_the_golden_of_its_theme(string name)
    {
        TuiTheme theme = TuiTheme.Get(name);
        var viewer = new TranscriptViewerRenderer(
            new TerminalCapabilities(ColorSystemSupport.TrueColor, Unicode: true)
        );
        viewer.SetColor(theme.Plan(ColorSystemSupport.TrueColor));
        viewer.SetTheme(theme);

        string actual = string.Join(
            '\n',
            viewer
                .Lines(FixtureTranscript(), new ConsoleSize(60, 40), scrolledLines: 0)
                .Select(line => line.ToMarkup())
        );

        GoldenFiles.AssertMatchesText(Path.Combine(GoldensDirectory, name + ".txt"), actual);
    }

    /// <summary>A user's hex token walks the capability ladder: 256, sixteen, then no colour.</summary>
    [Theory]
    [InlineData(ColorSystemSupport.TrueColor, "\u001b[38;2;255;136;0m")]
    [InlineData(ColorSystemSupport.EightBit, "\u001b[38;5;208m")]
    [InlineData(ColorSystemSupport.Standard, "\u001b[93m")]
    public void A_hex_token_downgrades_to_the_depth_the_terminal_has(
        ColorSystemSupport color,
        string sgr
    )
    {
        var theme = TuiTheme.Default.WithTokens(
            new Dictionary<ThemeToken, ThemeColor> { [ThemeToken.Prompt] = Hex("#ff8800") }
        );

        string[] lines = LiveAreaRenderer
            .Render(
                new LiveAreaState
                {
                    Input = new InputLineState("go", 2),
                    Size = new ConsoleSize(40, 8),
                    Color = color,
                    Theme = theme,
                }
            )
            .Lines.ToArray();

        Assert.Contains(lines, line => line.StartsWith(sgr, StringComparison.Ordinal));
    }

    [Fact]
    public void No_color_beats_a_theme_that_colours_every_token()
    {
        var overlay = new Dictionary<ThemeToken, ThemeColor>();
        foreach (ThemeToken token in ThemeTokens.All)
        {
            overlay[token] = Hex("#ff8800");
        }

        string[] lines = LiveAreaRenderer
            .Render(
                new LiveAreaState
                {
                    Input = new InputLineState("go", 2),
                    Thinking = new ThinkingState { Text = "considering", ElapsedSeconds = 3 },
                    Footer = new StatusFooterModel("m", 1, 2, "/repo", "main"),
                    Size = new ConsoleSize(40, 8),
                    Color = TuiTheme.Default.WithTokens(overlay).Plan(ColorSystemSupport.NoColors),
                    Theme = TuiTheme.Default.WithTokens(overlay),
                }
            )
            .Lines.ToArray();

        Assert.Equal(
            ColorSystemSupport.NoColors,
            TuiTheme.Default.WithTokens(overlay).Plan(ColorSystemSupport.NoColors)
        );
        Assert.DoesNotContain(lines, line => line.Contains('\u001b'));
    }

    private static ThemeColor Hex(string value)
    {
        Assert.True(ThemeColor.TryParse(value, out ThemeColor color, out _));
        return color;
    }

    /// <summary>One transcript carrying the thinking block, a tool call and its diff, and prose.</summary>
    private static TranscriptModel FixtureTranscript() =>
        new([
            new TranscriptUserEntry("Add validation to TodoList.Add"),
            new TranscriptThinkingEntry(
                new ThinkingCommitModel(
                    "Null titles must throw before the list mutates.",
                    12,
                    false
                )
            ),
            new TranscriptAssistantEntry(
                "The check belongs at the top of the method, before the list mutates."
            ),
            new TranscriptToolEntry(
                new ToolBlockModel(
                    "edit",
                    """{"path":"src/TodoList.cs","old_text":"_items.Add(title)","new_text":"throw"}""",
                    ToolBlockStatus.Ok,
                    "edited src/TodoList.cs lines 2\u20132 (match: exact)",
                    new ToolDiffInfo(
                        "src/TodoList.cs",
                        "exact",
                        "--- a/src/TodoList.cs\n"
                            + "+++ b/src/TodoList.cs\n"
                            + "@@ -2,4 +2,4 @@\n"
                            + " public void Add(string title)\n"
                            + " {\n"
                            + "-    _items.Add(title);\n"
                            + "+    throw new ArgumentException(nameof(title));\n"
                            + " }"
                    )
                )
            ),
        ]);
}
