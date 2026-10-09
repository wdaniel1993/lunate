using Markdig;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

public sealed class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

    public IRenderable Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return new Markup(MarkdownAstMapper.Map(Markdown.Parse(markdown, Pipeline)));
    }
}
