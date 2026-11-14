using Markdig;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

public sealed class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

    private TerminalCapabilities _capabilities;

    /// <summary>The Unicode defaults; the interactive session passes its detected capabilities.</summary>
    public MarkdownRenderer()
        : this(new TerminalCapabilities(ColorSystemSupport.Detect, Unicode: true)) { }

    internal MarkdownRenderer(TerminalCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities = capabilities;
    }

    /// <summary>
    /// Applies the session's resolved capabilities — the theme's tokens and the colour depth they
    /// downgrade for — so inline code and the syntax tones resolve the way the console paints.
    /// </summary>
    internal void SetCapabilities(TerminalCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities = capabilities;
    }

    public IRenderable Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return new Markup(MarkdownAstMapper.Map(Markdown.Parse(markdown, Pipeline), _capabilities));
    }

    /// <summary>
    /// The run's rendering with diagrams gated to <paramref name="maxWidth"/> columns: an inline
    /// session passes its terminal's width here.
    /// </summary>
    internal IRenderable Render(string markdown, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return new Markup(
            MarkdownAstMapper.Map(Markdown.Parse(markdown, Pipeline), _capabilities, maxWidth)
        );
    }

    /// <summary>The rendered lines, for the transcript viewer, which wraps them to its width.</summary>
    internal IReadOnlyList<StyledLine> Lines(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return MarkdownAstMapper.MapLines(Markdown.Parse(markdown, Pipeline), _capabilities);
    }

    /// <summary>
    /// The rendered lines with diagrams gated to <paramref name="maxWidth"/> columns: the viewer
    /// passes the width its frame is wrapped to, so a diagram either fits it or falls back.
    /// </summary>
    internal IReadOnlyList<StyledLine> Lines(string markdown, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return MarkdownAstMapper.MapLines(
            Markdown.Parse(markdown, Pipeline),
            _capabilities,
            maxWidth
        );
    }
}
