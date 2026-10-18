using Spectre.Console;

namespace Lunate.Tui;

/// <summary>The extension, MCP server and skill counts the welcome header shows.</summary>
internal sealed record WelcomeHeaderCounts(int Extensions, int McpServers, int Skills);

/// <summary>
/// The welcome header's render state: the product version, the session's model and workspace
/// (`~`-shortened by the session), whether a C# workspace was detected, the counts (null while
/// composition runs, which renders the <c>…</c> placeholder) and the one actionable fix of the
/// first-run state (null when a provider or key resolves, design D4).
/// </summary>
internal sealed record WelcomeHeaderState
{
    public required string Version { get; init; }

    public required string Workspace { get; init; }

    public string ModelId { get; init; } = string.Empty;

    public long ContextWindow { get; init; }

    public bool CSharpWorkspace { get; init; }

    public WelcomeHeaderCounts? Counts { get; init; }

    public string? Fix { get; init; }
}

/// <summary>
/// The welcome header (design D1/D2/D4): a pure function of (state, capabilities, width) → markup
/// lines, printed as finished output into scrollback. The full block renders only with Unicode at
/// width ≥ 80 — anything else renders the single ASCII line; colourless capabilities (<c>NO_COLOR</c>,
/// a non-interactive console) render the same structure with no styling. The first-run state prints
/// one actionable fix and no counts or hint block (design D4). Every number is formatted
/// culture-invariantly.
/// </summary>
internal static class WelcomeHeaderRenderer
{
    /// <summary>The narrowest terminal that fits the full block (the `Ϲ` lockup).</summary>
    internal const int MinimumBlockWidth = 80;

    /// <summary>Variant V2's mark: U+03F9 GREEK CAPITAL LUNATE SIGMA, the small crescent.</summary>
    internal const string Mark = "\u03f9";

    private const string VersionPrefix = "  v";
    private const string CountsPlaceholder = "extensions … · MCP … · skills …";
    private const string Hint = "/help · /model · /skills · /extensions · Esc cancels · /quit";
    private const string HelpHint = "/help for commands";
    private const string CSharp = "C# workspace · Roslyn";
    private const string CSharpSegment = "(C# · Roslyn)";
    private const string AsciiCSharpSegment = "(C# - Roslyn)";
    private const string Separator = " — ";
    private const string AsciiSeparator = " - ";

    private static readonly SpanStyle MarkStyle = new(Color: "cyan");

    /// <summary>Whether the terminal shows the full block: Unicode support and width ≥ 80.</summary>
    internal static bool ShowsBlock(TerminalCapabilities capabilities, int width)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return capabilities.Unicode && width >= MinimumBlockWidth;
    }

    /// <summary>The header's lines as Spectre markup; one entry per printed line.</summary>
    internal static IReadOnlyList<string> Render(
        WelcomeHeaderState state,
        TerminalCapabilities capabilities,
        int width
    )
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(capabilities);
        return ShowsBlock(capabilities, width)
            ? Block(state, capabilities)
            : [SingleLine(state, capabilities)];
    }

    /// <summary>The counts line; a null record renders the placeholder of a running composition.</summary>
    internal static string CountsLine(
        WelcomeHeaderCounts? counts,
        TerminalCapabilities capabilities
    )
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return Line(Colored(capabilities), (CountsText(counts), SpanStyle.Dim));
    }

    private static IReadOnlyList<string> Block(
        WelcomeHeaderState state,
        TerminalCapabilities capabilities
    )
    {
        bool colored = Colored(capabilities);
        List<string> lines =
        [
            Line(colored, (Mark, MarkStyle)),
            Line(
                colored,
                ("lunate", SpanStyle.Bold),
                (VersionPrefix + state.Version, SpanStyle.Dim)
            ),
        ];

        if (state.Fix is { Length: > 0 } fix)
        {
            // The first-run form: one actionable fix and the workspace, nothing else (D4).
            lines.Add(Line(colored, (fix, SpanStyle.Plain)));
            lines.Add(Line(colored, (state.Workspace, SpanStyle.Dim)));
            return lines;
        }

        if (state.ModelId.Length > 0)
        {
            lines.Add(Line(colored, (ModelLine(state), SpanStyle.Dim)));
        }

        lines.Add(Line(colored, (state.Workspace, SpanStyle.Dim)));
        if (state.CSharpWorkspace)
        {
            lines.Add(Line(colored, (CSharp, SpanStyle.Dim)));
        }

        lines.Add(CountsLine(state.Counts, capabilities));
        lines.Add(Line(colored, (Hint, SpanStyle.Dim)));
        return lines;
    }

    private static string SingleLine(WelcomeHeaderState state, TerminalCapabilities capabilities)
    {
        // A terminal without Unicode gets the same line in ASCII punctuation: LUNATE_ASCII and
        // TERM=dumb mean the session's output stays printable on a legacy console.
        bool unicode = capabilities.Unicode;
        var tail = new System.Text.StringBuilder();
        tail.Append(' ').Append('v').Append(state.Version);
        if (state.CSharpWorkspace)
        {
            tail.Append(' ').Append(unicode ? CSharpSegment : AsciiCSharpSegment);
        }

        tail.Append(unicode ? Separator : AsciiSeparator);
        tail.Append(state.Fix is { Length: > 0 } fix ? fix : HelpHint);

        return Line(
            Colored(capabilities),
            ("lunate", SpanStyle.Bold),
            (tail.ToString(), SpanStyle.Dim)
        );
    }

    private static string ModelLine(WelcomeHeaderState state) =>
        state.ContextWindow > 0
            ? FormattableString.Invariant($"{state.ModelId} · {state.ContextWindow:N0} ctx")
            : state.ModelId;

    private static string CountsText(WelcomeHeaderCounts? counts) =>
        counts is { } known
            ? FormattableString.Invariant(
                $"extensions {known.Extensions} · MCP {known.McpServers} · skills {known.Skills}"
            )
            : CountsPlaceholder;

    /// <summary>Whether the capabilities carry color (<c>NO_COLOR</c> and a non-interactive console do not).</summary>
    private static bool Colored(TerminalCapabilities capabilities) =>
        capabilities.Color != ColorSystemSupport.NoColors;

    /// <summary>One line of markup; colourless capabilities keep the text, never the styling.</summary>
    private static string Line(bool colored, params (string Text, SpanStyle Style)[] spans)
    {
        var line = new StyledLine();
        foreach ((string text, SpanStyle style) in spans)
        {
            line.Add(text, colored ? style : SpanStyle.Plain);
        }

        return line.ToMarkup();
    }
}
