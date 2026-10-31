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

    /// <summary>
    /// The active state root when <c>LUNATE_HOME</c> moved it, in the one spelling both surfaces use
    /// (<c>home /tmp/state (LUNATE_HOME)</c>); null when the root is the default.
    /// </summary>
    public string? Home { get; init; }

    public BashGuardStatus? GuardStatus { get; init; }

    public WelcomeHeaderCounts? Counts { get; init; }

    public string? Fix { get; init; }

    /// <summary>The newer release's version without a leading <c>v</c>; null when none is known.</summary>
    public string? UpdateVersion { get; init; }

    /// <summary>The changelog URL the update line links; null without a known release.</summary>
    public string? UpdateUrl { get; init; }
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
    private const string Hint =
        "/help · /model · /skills · /extensions · ctrl+o transcript · Esc cancels · /quit";
    private const string HelpHint = "/help for commands";
    private const string CSharp = "C# workspace · Roslyn";
    private const string CSharpSegment = "(C# · Roslyn)";
    private const string AsciiCSharpSegment = "(C# - Roslyn)";
    private const string Separator = " — ";
    private const string AsciiSeparator = " - ";

    private static readonly SpanStyle MarkStyle = new(Color: LivePalette.AccentHex);

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
            AddHome(lines, state, colored);
            AddGuardStatus(lines, state, colored);
            return lines;
        }

        if (state.ModelId.Length > 0)
        {
            lines.Add(Line(colored, (ModelLine(state), SpanStyle.Dim)));
        }

        lines.Add(Line(colored, (state.Workspace, SpanStyle.Dim)));
        AddHome(lines, state, colored);
        if (state.CSharpWorkspace)
        {
            lines.Add(Line(colored, (CSharp, SpanStyle.Dim)));
        }

        AddGuardStatus(lines, state, colored);
        lines.Add(CountsLine(state.Counts, capabilities));
        if (state.UpdateVersion is { Length: > 0 } updateVersion)
        {
            lines.Add(
                Line(
                    colored,
                    (
                        string.Concat("v", updateVersion, " available · changelog"),
                        new SpanStyle(TextStyle.Dim, Link: state.UpdateUrl)
                    )
                )
            );
        }

        lines.Add(Line(colored, (Hint, SpanStyle.Dim)));
        return lines;
    }

    private static string SingleLine(WelcomeHeaderState state, TerminalCapabilities capabilities)
    {
        // A terminal without Unicode gets the same line in ASCII punctuation: LUNATE_ASCII and
        // TERM=dumb mean the session's output stays printable on a legacy console.
        bool unicode = capabilities.Unicode;
        var head = new System.Text.StringBuilder();
        head.Append(' ').Append('v').Append(state.Version);
        if (state.CSharpWorkspace)
        {
            head.Append(' ').Append(unicode ? CSharpSegment : AsciiCSharpSegment);
        }

        // A redirected or sandboxed run must never be invisible, even on the one-line form.
        if (state.Home is { Length: > 0 } home)
        {
            head.Append(unicode ? " · " : " - ").Append(home);
        }

        if (state.GuardStatus is { } guardStatus)
        {
            head.Append(unicode ? " · " : " - ").Append(guardStatus.HeaderText(unicode));
        }

        string action = state.Fix is { Length: > 0 } fix ? fix : HelpHint;
        string separator = unicode ? Separator : AsciiSeparator;
        if (state.UpdateVersion is { Length: > 0 } updateVersion)
        {
            return Line(
                Colored(capabilities),
                ("lunate", SpanStyle.Bold),
                (head.ToString(), SpanStyle.Dim),
                (
                    string.Concat(unicode ? " · " : " - ", "v", updateVersion, " available"),
                    new SpanStyle(TextStyle.Dim, Link: state.UpdateUrl)
                ),
                (separator + action, SpanStyle.Dim)
            );
        }

        head.Append(separator);
        head.Append(action);
        return Line(
            Colored(capabilities),
            ("lunate", SpanStyle.Bold),
            (head.ToString(), SpanStyle.Dim)
        );
    }

    /// <summary>
    /// The one dim linked line that names a newer release: used both under the header when a
    /// background check finds it and at the end of <c>/help</c>. The link survives NO_COLOR.
    /// </summary>
    internal static string UpdateLine(
        string version,
        string? url,
        TerminalCapabilities capabilities
    )
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(capabilities);
        string separator = capabilities.Unicode ? " · " : " - ";
        return Line(
            Colored(capabilities),
            (
                string.Concat("v", version, " available", separator, "changelog"),
                new SpanStyle(TextStyle.Dim, Link: url)
            )
        );
    }

    private static string ModelLine(WelcomeHeaderState state) =>
        state.ContextWindow > 0
            ? FormattableString.Invariant($"{state.ModelId} · {state.ContextWindow:N0} ctx")
            : state.ModelId;

    /// <summary>The active home line, when <c>LUNATE_HOME</c> moved the root; nothing otherwise.</summary>
    private static void AddHome(List<string> lines, WelcomeHeaderState state, bool colored)
    {
        if (state.Home is { Length: > 0 } home)
        {
            lines.Add(Line(colored, (home, SpanStyle.Dim)));
        }
    }

    private static void AddGuardStatus(List<string> lines, WelcomeHeaderState state, bool colored)
    {
        if (state.GuardStatus is { } guardStatus)
        {
            lines.Add(Line(colored, (guardStatus.HeaderText(unicode: true), SpanStyle.Dim)));
        }
    }

    private static string CountsText(WelcomeHeaderCounts? counts) =>
        counts is { } known
            ? FormattableString.Invariant(
                $"extensions {known.Extensions} · MCP {known.McpServers} · skills {known.Skills}"
            )
            : CountsPlaceholder;

    /// <summary>Whether the capabilities carry color (<c>NO_COLOR</c> and a non-interactive console do not).</summary>
    private static bool Colored(TerminalCapabilities capabilities) =>
        capabilities.Color != ColorSystemSupport.NoColors;

    /// <summary>
    /// One line of markup; colourless capabilities keep the text, never the styling, but the
    /// update line's link stays so the changelog is still clickable under NO_COLOR.
    /// </summary>
    private static string Line(bool colored, params (string Text, SpanStyle Style)[] spans)
    {
        var line = new StyledLine();
        foreach ((string text, SpanStyle style) in spans)
        {
            line.Add(text, colored ? style : new SpanStyle(Link: style.Link));
        }

        return line.ToMarkup();
    }
}
