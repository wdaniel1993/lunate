using System.Text;
using Markdig.Syntax.Inlines;

namespace Lunate.Tui;

internal static partial class MarkdownAstMapper
{
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
}
