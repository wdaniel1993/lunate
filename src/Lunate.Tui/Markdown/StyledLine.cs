using System.Text;
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
    public static readonly SpanStyle Keyword = new(Color: "blue");
    public static readonly SpanStyle String = new(Color: "green");
    public static readonly SpanStyle Comment = new(Color: "grey");
    public static readonly SpanStyle Number = new(Color: "yellow");
    public static readonly SpanStyle Key = new(Color: "blue");
    public static readonly SpanStyle Variable = new(Color: "yellow");

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
