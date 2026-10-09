using System.Globalization;
using System.Text;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Spectre.Console;

namespace Lunate.Tui;

[Flags]
internal enum TextStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Dim = 4,
    Underline = 8,
}

internal readonly record struct SpanStyle(TextStyle Text = TextStyle.None, string? Color = null)
{
    public static readonly SpanStyle Plain = new();
    public static readonly SpanStyle Bold = new(TextStyle.Bold);
    public static readonly SpanStyle Dim = new(TextStyle.Dim);
    public static readonly SpanStyle Heading1 = new(TextStyle.Bold | TextStyle.Underline);
    public static readonly SpanStyle InlineCode = new(Color: "aqua");

    public static string Attributes(SpanStyle style)
    {
        var parts = new List<string>(5);
        if ((style.Text & TextStyle.Bold) != 0)
        {
            parts.Add("bold");
        }

        if ((style.Text & TextStyle.Italic) != 0)
        {
            parts.Add("italic");
        }

        if ((style.Text & TextStyle.Underline) != 0)
        {
            parts.Add("underline");
        }

        if ((style.Text & TextStyle.Dim) != 0)
        {
            parts.Add("dim");
        }

        if (style.Color is not null)
        {
            parts.Add(style.Color);
        }

        return string.Join(' ', parts);
    }
}

internal sealed class StyledLine
{
    private readonly List<StyledSpan> _spans = [];

    public bool IsBlank => _spans.All(static span => string.IsNullOrWhiteSpace(span.Text));

    public void Add(string text, SpanStyle style)
    {
        if (text.Length > 0)
        {
            _spans.Add(new StyledSpan(text, style));
        }
    }

    public void Append(StyledLine other) => _spans.AddRange(other._spans);

    public string ToMarkup()
    {
        var builder = new StringBuilder();
        foreach (var (text, style) in TrimmedTrailingWhitespace())
        {
            string escaped = Markup.Escape(text);
            string attributes = SpanStyle.Attributes(style);
            if (attributes.Length == 0)
            {
                builder.Append(escaped);
            }
            else
            {
                builder.Append('[').Append(attributes).Append(']').Append(escaped).Append("[/]");
            }
        }

        return builder.ToString();
    }

    private List<StyledSpan> TrimmedTrailingWhitespace()
    {
        var spans = new List<StyledSpan>(_spans);
        while (spans.Count > 0)
        {
            var last = spans[^1];
            string text = last.Text.TrimEnd();
            if (text.Length == 0)
            {
                spans.RemoveAt(spans.Count - 1);
                continue;
            }

            if (text.Length != last.Text.Length)
            {
                spans[^1] = last with { Text = text };
            }

            break;
        }

        return spans;
    }

    private readonly record struct StyledSpan(string Text, SpanStyle Style);
}

internal static class MarkdownAstMapper
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

        return lines;
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

        for (var i = 0; i < content.Count; i++)
        {
            AppendPlainLine(lines, content.Lines[i].Slice.ToString());
        }
    }

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

    private static void AppendInlines(
        ContainerInline container,
        InlineWriter writer,
        SpanStyle style
    )
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    writer.Write(literal.Content.ToString(), style);
                    break;
                case CodeInline code:
                    writer.Write(code.Content, SpanStyle.InlineCode);
                    break;
                case EmphasisInline emphasis:
                    AppendInlines(emphasis, writer, Emphasis(style, emphasis.DelimiterCount));
                    break;
                case LinkInline link:
                    writer.Write($"{TextOf(link)} ({link.Url ?? string.Empty})", SpanStyle.Plain);
                    break;
                case HtmlInline html:
                    writer.Write(html.Tag, SpanStyle.Plain);
                    break;
                case HtmlEntityInline entity:
                    writer.Write(entity.Transcoded.ToString(), style);
                    break;
                case AutolinkInline autolink:
                    writer.Write(autolink.Url, SpanStyle.Plain);
                    break;
                case LineBreakInline:
                    writer.Break();
                    break;
                case ContainerInline nested:
                    AppendInlines(nested, writer, style);
                    break;
            }
        }
    }

    private static SpanStyle Emphasis(SpanStyle style, int delimiterCount)
    {
        TextStyle added = delimiterCount switch
        {
            1 => TextStyle.Italic,
            2 => TextStyle.Bold,
            _ => TextStyle.Bold | TextStyle.Italic,
        };
        return style with { Text = style.Text | added };
    }

    private static string TextOf(Inline inline)
    {
        var builder = new StringBuilder();
        CollectText(inline, builder);
        return builder.ToString();
    }

    private static void CollectText(Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.ToString());
                break;
            case CodeInline code:
                builder.Append(code.Content);
                break;
            case LineBreakInline:
                builder.Append(' ');
                break;
            case HtmlInline html:
                builder.Append(html.Tag);
                break;
            case HtmlEntityInline entity:
                builder.Append(entity.Transcoded.ToString());
                break;
            case AutolinkInline autolink:
                builder.Append(autolink.Url);
                break;
            case ContainerInline container:
                foreach (var child in container)
                {
                    CollectText(child, builder);
                }

                break;
        }
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
