using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The welcome header's render contract (design D1/D2/D4): a pure function of
/// (state, capabilities, width) → markup lines, pinned cell by cell for the snapshot matrix.
/// </summary>
public sealed class WelcomeHeaderRendererTests
{
    private static readonly WelcomeHeaderState Full = new()
    {
        Version = "0.1.0",
        ModelId = "deepseek-v4.1-flash",
        ContextWindow = 1_000_000,
        Workspace = "~/dev/lunate",
        CSharpWorkspace = true,
    };

    private static readonly WelcomeHeaderState FirstRun = Full with
    {
        Fix = "add the 'openai' key to ~/.lunate/auth.json",
    };

    /// <summary>The first-run form of a session with no model configured at all (design D8 #9).</summary>
    private static readonly WelcomeHeaderState FirstRunNoModel = Full with
    {
        ModelId = string.Empty,
        ContextWindow = 0,
        Fix = "set 'model' in ~/.lunate/settings.json",
    };

    private static readonly string[] BlockMarkup =
    [
        "[cyan]\u03f9[/]",
        "[bold]lunate[/][dim]  v0.1.0[/]",
        "[dim]deepseek-v4.1-flash · 1,000,000 ctx[/]",
        "[dim]~/dev/lunate[/]",
        "[dim]C# workspace · Roslyn[/]",
        "[dim]extensions … · MCP … · skills …[/]",
        "[dim]/help · /model · /skills · /extensions · Esc cancels · /quit[/]",
    ];

    private static readonly string[] BlockPlain =
    [
        "\u03f9",
        "lunate  v0.1.0",
        "deepseek-v4.1-flash · 1,000,000 ctx",
        "~/dev/lunate",
        "C# workspace · Roslyn",
        "extensions … · MCP … · skills …",
        "/help · /model · /skills · /extensions · Esc cancels · /quit",
    ];

    private static readonly string[] SingleLineMarkup =
    [
        "[bold]lunate[/][dim] v0.1.0 (C# · Roslyn) — /help for commands[/]",
    ];

    private static readonly string[] SingleLinePlain =
    [
        "lunate v0.1.0 (C# · Roslyn) — /help for commands",
    ];

    private static readonly string[] AsciiMarkup =
    [
        "[bold]lunate[/][dim] v0.1.0 (C# - Roslyn) - /help for commands[/]",
    ];

    private static readonly string[] AsciiPlain =
    [
        "lunate v0.1.0 (C# - Roslyn) - /help for commands",
    ];

    /// <summary>The snapshot matrix: widths {80, 120, 40} × Unicode/ASCII × color/NO_COLOR.</summary>
    public static TheoryData<int, bool, bool> Matrix =>
        new()
        {
            { 80, true, true },
            { 80, true, false },
            { 80, false, true },
            { 80, false, false },
            { 120, true, true },
            { 120, true, false },
            { 120, false, true },
            { 120, false, false },
            { 40, true, true },
            { 40, true, false },
            { 40, false, true },
            { 40, false, false },
        };

    /// <summary>The matrix's form axis alone: width × Unicode/ASCII.</summary>
    public static TheoryData<int, bool> FormMatrix =>
        new()
        {
            { 80, true },
            { 80, false },
            { 120, true },
            { 120, false },
            { 40, true },
            { 40, false },
        };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void The_header_matches_the_snapshot_matrix_cell(
        int width,
        bool unicode,
        bool colored
    ) =>
        Assert.Equal(
            Expected(width, unicode, colored),
            Markup(Full, width, Capabilities(unicode, colored))
        );

    [Theory]
    [MemberData(nameof(FormMatrix))]
    public void No_color_leaves_the_structure_unchanged(int width, bool unicode) =>
        Assert.Equal(
            Text(Full, width, Capabilities(unicode, colored: true)),
            Text(Full, width, Capabilities(unicode, colored: false))
        );

    [Fact]
    public void A_colorless_terminal_emits_no_escape_sequences() =>
        Assert.DoesNotContain(
            "\u001b[",
            ConsoleOutput(Full, 80, Capabilities(unicode: true, colored: false)),
            StringComparison.Ordinal
        );

    [Fact]
    public void A_colored_terminal_emits_the_mark_crescent_and_the_bold_name() =>
        Assert.Contains(
            "\u001b[38;5;14m\u03f9",
            ConsoleOutput(Full, 80, Capabilities(unicode: true, colored: true)),
            StringComparison.Ordinal
        );

    [Fact]
    public void Counts_replace_the_placeholder_when_they_are_known() =>
        Assert.Equal(
            "extensions 4 · MCP 2 · skills 7",
            Text(
                Full with
                {
                    Counts = new WelcomeHeaderCounts(4, 2, 7),
                },
                width: 80,
                Capabilities(unicode: true, colored: true)
            )[5]
        );

    [Fact]
    public void The_counts_line_renders_zeros_honestly() =>
        Assert.Equal(
            "[dim]extensions 0 · MCP 0 · skills 0[/]",
            WelcomeHeaderRenderer.CountsLine(
                new WelcomeHeaderCounts(0, 0, 0),
                Capabilities(unicode: true, colored: true)
            )
        );

    [Fact]
    public void The_counts_line_is_plain_without_color() =>
        Assert.Equal(
            "extensions 0 · MCP 0 · skills 0",
            WelcomeHeaderRenderer.CountsLine(
                new WelcomeHeaderCounts(0, 0, 0),
                Capabilities(unicode: true, colored: false)
            )
        );

    [Fact]
    public void The_first_run_form_prints_one_fix_and_no_counts_or_hint() =>
        Assert.Equal(
            [
                "[cyan]\u03f9[/]",
                "[bold]lunate[/][dim]  v0.1.0[/]",
                "add the 'openai' key to ~/.lunate/auth.json",
                "[dim]~/dev/lunate[/]",
            ],
            Markup(FirstRun, width: 120, Capabilities(unicode: true, colored: true))
        );

    [Fact]
    public void The_first_run_single_line_form_names_the_fix_instead_of_the_hint() =>
        Assert.Equal(
            ["lunate v0.1.0 (C# · Roslyn) — add the 'openai' key to ~/.lunate/auth.json"],
            Text(FirstRun, width: 40, Capabilities(unicode: true, colored: true))
        );

    /// <summary>
    /// The no-model first-run state (design D4/D8 #9): no catalog entry is named, so the fix alone
    /// carries the block and no model, counts or hint line appears.
    /// </summary>
    [Fact]
    public void A_state_without_a_model_renders_the_fix_and_no_counts_or_hint() =>
        Assert.Equal(
            [
                "[cyan]\u03f9[/]",
                "[bold]lunate[/][dim]  v0.1.0[/]",
                "set 'model' in ~/.lunate/settings.json",
                "[dim]~/dev/lunate[/]",
            ],
            Markup(FirstRunNoModel, width: 120, Capabilities(unicode: true, colored: true))
        );

    [Fact]
    public void The_csharp_line_and_segment_follow_detection()
    {
        WelcomeHeaderState plainHost = Full with { CSharpWorkspace = false };

        Assert.DoesNotContain(
            "C# workspace · Roslyn",
            Text(plainHost, width: 80, Capabilities(unicode: true, colored: true))
        );
        Assert.Equal(
            ["lunate v0.1.0 — /help for commands"],
            Text(plainHost, width: 40, Capabilities(unicode: true, colored: true))
        );
        Assert.Equal(
            ["lunate v0.1.0 - /help for commands"],
            Text(plainHost, width: 40, Capabilities(unicode: false, colored: true))
        );
    }

    [Fact]
    public void An_unknown_context_window_leaves_the_model_line_bare() =>
        Assert.Equal(
            "deepseek-v4.1-flash",
            Text(
                Full with
                {
                    ContextWindow = 0,
                },
                width: 80,
                Capabilities(unicode: true, colored: true)
            )[2]
        );

    [Theory]
    [InlineData(80, true, true)]
    [InlineData(120, true, true)]
    [InlineData(79, true, false)]
    [InlineData(40, true, false)]
    [InlineData(80, false, false)]
    [InlineData(120, false, false)]
    public void The_block_shows_only_at_width_with_unicode(
        int width,
        bool unicode,
        bool expected
    ) =>
        Assert.Equal(
            expected,
            WelcomeHeaderRenderer.ShowsBlock(Capabilities(unicode, colored: true), width)
        );

    /// <summary>The block needs width ≥ 80 and Unicode; styling needs color; a non-Unicode
    /// terminal gets the ASCII punctuation.</summary>
    private static string[] Expected(int width, bool unicode, bool colored) =>
        (width >= 80, unicode, colored) switch
        {
            (true, true, true) => BlockMarkup,
            (true, true, false) => BlockPlain,
            (_, true, true) => SingleLineMarkup,
            (_, true, false) => SingleLinePlain,
            (_, false, true) => AsciiMarkup,
            _ => AsciiPlain,
        };

    private static TerminalCapabilities Capabilities(bool unicode, bool colored) =>
        new(colored ? ColorSystemSupport.TrueColor : ColorSystemSupport.NoColors, Unicode: unicode);

    private static string[] Markup(
        WelcomeHeaderState state,
        int width,
        TerminalCapabilities capabilities
    ) => [.. WelcomeHeaderRenderer.Render(state, capabilities, width)];

    private static string[] Text(
        WelcomeHeaderState state,
        int width,
        TerminalCapabilities capabilities
    ) => Text(WelcomeHeaderRenderer.Render(state, capabilities, width));

    /// <summary>The visible text of markup lines; a plain test console emits no escapes.</summary>
    private static string[] Text(IReadOnlyList<string> markupLines)
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        foreach (string line in markupLines)
        {
            console.Write(new Markup(line + "\n"));
        }

        return [.. console.Output.TrimEnd('\n').Split('\n')];
    }

    private static string ConsoleOutput(
        WelcomeHeaderState state,
        int width,
        TerminalCapabilities capabilities
    )
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        console.EmitAnsiSequences = true;
        foreach (string line in WelcomeHeaderRenderer.Render(state, capabilities, width))
        {
            console.Write(new Markup(line + "\n"));
        }

        return console.Output;
    }
}
