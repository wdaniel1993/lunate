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
    internal static readonly SpanStyle Question = new(Color: "yellow");
    internal static readonly SpanStyle Accent = new(Color: "cyan");

    private readonly bool _unicode;

    /// <summary>The Unicode defaults; the interactive session passes its detected capabilities.</summary>
    public ApprovalPromptRenderer()
        : this(new TerminalCapabilities(ColorSystemSupport.Detect, Unicode: true)) { }

    internal ApprovalPromptRenderer(TerminalCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
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
                    [.. line.Spans.Select(span => span with { Text = Styled(span, color) })],
                    line.Part
                )),
        ];
    }

    private static SpanStyle Style(ApprovalTone tone) =>
        tone switch
        {
            ApprovalTone.Warning => Question,
            ApprovalTone.Muted => SpanStyle.Dim,
            ApprovalTone.Accent => Accent,
            _ => SpanStyle.Plain,
        };

    private static string Styled(ApprovalSpan span, ColorSystemSupport color) =>
        span.Tone switch
        {
            ApprovalTone.Warning => LivePalette.Warning(span.Text, color),
            ApprovalTone.Muted => LivePalette.Muted(span.Text, color),
            ApprovalTone.Accent => LivePalette.Accent(span.Text, color),
            _ => span.Text,
        };
}
