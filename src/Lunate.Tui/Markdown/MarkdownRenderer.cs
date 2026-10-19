using Markdig;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

public sealed class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

    private readonly TerminalCapabilities _capabilities;

    /// <summary>The Unicode defaults; the interactive session passes its detected capabilities.</summary>
    public MarkdownRenderer()
        : this(new TerminalCapabilities(ColorSystemSupport.Detect, Unicode: true)) { }

    internal MarkdownRenderer(TerminalCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities = capabilities;
    }

    public IRenderable Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return new Markup(MarkdownAstMapper.Map(Markdown.Parse(markdown, Pipeline), _capabilities));
    }

    /// <summary>The rendered lines, for the transcript viewer, which wraps them to its width.</summary>
    internal IReadOnlyList<StyledLine> Lines(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return MarkdownAstMapper.MapLines(Markdown.Parse(markdown, Pipeline), _capabilities);
    }
}
