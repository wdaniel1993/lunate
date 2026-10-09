using System.Globalization;
using System.Text;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Lunate.Tui;

internal static partial class MarkdownAstMapper
{
    public static string Map(MarkdownDocument document)
    {
        var lines = RenderBlocks(document);
        while (lines.Count > 0 && lines[0].IsBlank)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].IsBlank)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line.ToMarkup()).Append('\n');
        }

        return builder.ToString();
    }

    private static List<StyledLine> RenderBlocks(IEnumerable<Block> blocks)
    {
        var lines = new List<StyledLine>();
        var first = true;
        foreach (var block in blocks)
        {
            if (!first)
            {
                lines.Add(new StyledLine());
            }

            first = false;
            AppendBlock(block, lines);
        }

        CollapseBlankLines(lines);
        return lines;
    }

    private static void CollapseBlankLines(List<StyledLine> lines)
    {
        for (var i = lines.Count - 1; i > 0; i--)
        {
            if (lines[i].IsBlank && lines[i - 1].IsBlank)
            {
                lines.RemoveAt(i);
            }
        }
    }

    private static void AppendBlock(Block block, List<StyledLine> lines)
    {
        switch (block)
        {
            case HeadingBlock heading:
                lines.AddRange(
                    RenderBlockInlines(
                        heading.Inline,
                        heading.Level == 1 ? SpanStyle.Heading1 : SpanStyle.Bold
                    )
                );
                break;
            case ParagraphBlock paragraph:
                lines.AddRange(RenderBlockInlines(paragraph.Inline, SpanStyle.Plain));
                break;
            case ListBlock list:
                lines.AddRange(RenderList(list));
                break;
            case QuoteBlock quote:
                lines.AddRange(RenderQuote(quote));
                break;
            case FencedCodeBlock fenced:
                AppendCode(fenced.Info, fenced.Lines, lines);
                break;
            case CodeBlock code:
                AppendCode(null, code.Lines, lines);
                break;
            case ThematicBreakBlock thematic:
                AppendPlainLine(
                    lines,
                    new string(thematic.ThematicChar, thematic.ThematicCharCount)
                );
                break;
            case LinkReferenceDefinitionGroup:
                break;
            case LeafBlock leaf:
                AppendRawLines(leaf.Lines, lines);
                break;
            case ContainerBlock container:
                lines.AddRange(RenderBlocks(container));
                break;
        }
    }

    private static List<StyledLine> RenderList(ListBlock list)
    {
        var lines = new List<StyledLine>();
        int number = OrderedStart(list);
        foreach (var child in list)
        {
            var content = RenderItem((ListItemBlock)child);
            string marker = list.IsOrdered
                ? number.ToString(CultureInfo.InvariantCulture) + ". "
                : "• ";
            for (var i = 0; i < content.Count; i++)
            {
                lines.Add(PrefixLine(content[i], i == 0 ? marker : "  ", SpanStyle.Plain));
            }

            number++;
        }

        return lines;
    }

    private static List<StyledLine> RenderItem(ListItemBlock item)
    {
        var lines = new List<StyledLine>();
        var first = true;
        foreach (var child in item)
        {
            if (!first && child is not ListBlock)
            {
                lines.Add(new StyledLine());
            }

            first = false;
            AppendBlock(child, lines);
        }

        CollapseBlankLines(lines);
        return lines;
    }

    private static List<StyledLine> RenderQuote(QuoteBlock quote)
    {
        var lines = new List<StyledLine>();
        foreach (var line in RenderBlocks(quote))
        {
            lines.Add(PrefixLine(line, "│ ", SpanStyle.Dim));
        }

        return lines;
    }

    private static void AppendCode(string? info, StringLineGroup content, List<StyledLine> lines)
    {
        if (!string.IsNullOrWhiteSpace(info))
        {
            var label = new StyledLine();
            label.Add(info, SpanStyle.Dim);
            lines.Add(label);
        }

        string? language = SyntaxHighlight.LanguageKey(info);
        bool inBlockComment = false;
        for (var i = 0; i < content.Count; i++)
        {
            string text = content.Lines[i].Slice.ToString();
            var line = new StyledLine();
            if (language is null)
            {
                line.Add(text, SpanStyle.Plain);
            }
            else
            {
                foreach (var span in SyntaxHighlight.Line(language, text, ref inBlockComment))
                {
                    line.Add(span.Text, StyleFor(span.Kind));
                }
            }

            lines.Add(line);
        }
    }

    private static SpanStyle StyleFor(HighlightKind kind) =>
        kind switch
        {
            HighlightKind.Keyword => SpanStyle.Keyword,
            HighlightKind.String => SpanStyle.String,
            HighlightKind.Comment => SpanStyle.Comment,
            HighlightKind.Number => SpanStyle.Number,
            HighlightKind.Key => SpanStyle.Key,
            HighlightKind.Variable => SpanStyle.Variable,
            _ => SpanStyle.Plain,
        };

    private static void AppendRawLines(StringLineGroup content, List<StyledLine> lines)
    {
        for (var i = 0; i < content.Count; i++)
        {
            AppendPlainLine(lines, content.Lines[i].Slice.ToString());
        }
    }

    private static void AppendPlainLine(List<StyledLine> lines, string text)
    {
        var line = new StyledLine();
        line.Add(text, SpanStyle.Plain);
        lines.Add(line);
    }

    private static List<StyledLine> RenderBlockInlines(ContainerInline? inline, SpanStyle style)
    {
        var writer = new InlineWriter();
        if (inline is not null)
        {
            AppendInlines(inline, writer, style);
        }

        return writer.Finish();
    }

    private static StyledLine PrefixLine(StyledLine source, string prefix, SpanStyle style)
    {
        var line = new StyledLine();
        line.Add(prefix, style);
        line.Append(source);
        return line;
    }

    private static int OrderedStart(ListBlock list)
    {
        if (!list.IsOrdered)
        {
            return 1;
        }

        return int.TryParse(
            list.OrderedStart,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int start
        )
            ? start
            : 1;
    }

    private sealed class InlineWriter
    {
        private readonly List<StyledLine> _lines = [];
        private StyledLine _current = new();

        public void Write(string? text, SpanStyle style)
        {
            if (!string.IsNullOrEmpty(text))
            {
                _current.Add(text, style);
            }
        }

        public void Break()
        {
            _lines.Add(_current);
            _current = new StyledLine();
        }

        public List<StyledLine> Finish()
        {
            _lines.Add(_current);
            return _lines;
        }
    }
}
