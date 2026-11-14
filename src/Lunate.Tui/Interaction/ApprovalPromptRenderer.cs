using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// Renders a pending approval as a panel: a warning headline naming the tool and its concrete
/// action, a muted bounded detail block and the question with accented key letters. A subagent
/// asker prefixes the question as <c>&lt;asker&gt; asks: ...</c>. Every user-derived string is
/// markup-escaped through <see cref="StyledLine"/>; the panel's widths and tones come from
/// <see cref="ApprovalPanelLayout"/>.
/// </summary>
public sealed class ApprovalPromptRenderer
{
    private readonly bool _unicode;
    private readonly TerminalCapabilities _capabilities;

    /// <summary>The Unicode defaults; the interactive session passes its detected capabilities.</summary>
    public ApprovalPromptRenderer()
        : this(new TerminalCapabilities(ColorSystemSupport.Detect, Unicode: true)) { }

    internal ApprovalPromptRenderer(TerminalCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities = capabilities;
        _unicode = capabilities.Unicode;
    }

    /// <summary>
    /// The panel at the standard 80-column frame. A narrow caller passes its own width to
    /// <see cref="Render(ApprovalPromptModel, int)"/>.
    /// </summary>
    public IRenderable Render(ApprovalPromptModel prompt) => Render(prompt, 80);

    /// <summary>
    /// The panel at <paramref name="width"/> cells: a question too wide for the frame splits above
    /// the choices, which are never clipped.
    /// </summary>
    public IRenderable Render(ApprovalPromptModel prompt, int width)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var builder = new StringBuilder();
        foreach (var line in ApprovalPanelLayout.Build(prompt, Math.Max(1, width), _unicode))
        {
            var styled = new StyledLine();
            foreach (var span in line.Spans)
            {
                styled.Add(span.Text, Style(span.Tone));
            }

            builder.Append(styled.ToMarkup()).Append('\n');
        }

        return new Markup(builder.ToString());
    }

    /// <summary>
    /// The panel's plain lines for the live frame: the same layout at <paramref name="width"/>
    /// cells with raw SGR tones. A question too wide for the frame splits above the choices, which
    /// are never clipped.
    /// </summary>
    internal static IReadOnlyList<ApprovalPanelLine> PlainLines(
        ApprovalPromptModel prompt,
        int width,
        TuiTheme theme,
        ColorSystemSupport color,
        bool unicode
    )
    {
        ArgumentNullException.ThrowIfNull(prompt);

        return
        [
            .. ApprovalPanelLayout
                .Build(prompt, width, unicode)
                .Select(line => new ApprovalPanelLine(
                    [.. line.Spans.Select(span => span with { Text = Styled(span, theme, color) })],
                    line.Part
                )),
        ];
    }

    private SpanStyle Style(ApprovalTone tone) =>
        tone switch
        {
            ApprovalTone.Warning => _capabilities.Span(ThemeToken.Warning),
            ApprovalTone.Muted => SpanStyle.Dim,
            ApprovalTone.Accent => _capabilities.Span(ThemeToken.Accent),
            _ => SpanStyle.Plain,
        };

    /// <summary>The question marker the trust prompt shares, in the default theme's warning tone.</summary>
    internal static SpanStyle Question { get; } =
        TuiTheme.Default.Span(ThemeToken.Warning, ColorSystemSupport.Detect);

    private static string Styled(ApprovalSpan span, TuiTheme theme, ColorSystemSupport color) =>
        span.Tone switch
        {
            ApprovalTone.Warning => theme.Wrap(ThemeToken.Warning, span.Text, color),
            ApprovalTone.Muted => theme.Wrap(ThemeToken.Muted, span.Text, color),
            ApprovalTone.Accent => theme.Wrap(ThemeToken.Accent, span.Text, color),
            _ => span.Text,
        };
}
