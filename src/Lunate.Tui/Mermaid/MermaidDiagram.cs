using System.Globalization;

namespace Lunate.Tui;

/// <summary>Why a diagram could not be drawn; the phrasing becomes the one fallback line.</summary>
internal sealed record MermaidFailure(string Reason);

/// <summary>Either the drawn grid or the reason it was refused; never both, never neither.</summary>
internal readonly record struct MermaidResult(MermaidCanvas? Canvas, MermaidFailure? Failure)
{
    internal static MermaidResult Ok(MermaidCanvas canvas) => new(canvas, null);

    internal static MermaidResult Fail(string reason) => new(null, new MermaidFailure(reason));
}

/// <summary>
/// The Mermaid fence entry point: it classifies the first meaningful line, hands the statements
/// to the renderer for the one type the line names, and falls back to the source with a single
/// note line whenever the type is unsupported, the parse fails or the drawing does not fit the
/// terminal. Rendering never throws and never leaves a partial diagram: a diagram leaves whole
/// or its source leaves whole.
/// </summary>
internal static class MermaidDiagram
{
    internal const int MinimumWidth = 40;

    public static IReadOnlyList<StyledLine> RenderFence(
        IReadOnlyList<string> source,
        TerminalCapabilities capabilities,
        int terminalWidth
    )
    {
        MermaidResult result = ClassifyAndDraw(
            source,
            capabilities,
            terminalWidth,
            out string? unsupportedType
        );
        if (result.Canvas is null)
        {
            return Fallback(
                source,
                unsupportedType is null
                    ? result.Failure!.Reason
                    : $"unsupported diagram type '{unsupportedType}'"
            );
        }

        var lines = new List<StyledLine> { Label() };
        lines.AddRange(result.Canvas.ToLines());
        return lines;
    }

    private static MermaidResult ClassifyAndDraw(
        IReadOnlyList<string> source,
        TerminalCapabilities capabilities,
        int terminalWidth,
        out string? unsupportedType
    )
    {
        unsupportedType = null;
        (int Number, string Text)? first = SignificantLine(source);
        if (first is null)
        {
            return MermaidResult.Fail("the fence has no diagram");
        }

        string verb = Verb(first.Value.Text);
        MermaidResult result = verb switch
        {
            "flowchart" or "graph" => MermaidFlowchart.TryDraw(source, capabilities.Unicode, verb),
            "sequenceDiagram" => MermaidSequence.TryDraw(source, capabilities.Unicode),
            _ => MermaidResult.Ok(null!),
        };
        if (result.Canvas is not null && !Fits(result.Canvas.Width, terminalWidth))
        {
            return WidthFailure(result.Canvas.Width, terminalWidth);
        }

        unsupportedType = result.Canvas is null && result.Failure is null ? verb ?? "" : null;
        return result;
    }

    /// <summary>
    /// The width gate: the widest drawn line must fit the terminal and the terminal itself must
    /// be at least <see cref="MinimumWidth"/> columns; a call without a width (<see
    /// cref="int.MaxValue"/>) renders ungated.
    /// </summary>
    private static bool Fits(int diagramWidth, int terminalWidth) =>
        terminalWidth >= int.MaxValue
        || (terminalWidth >= MinimumWidth && diagramWidth <= Math.Max(terminalWidth, MinimumWidth));

    private static MermaidResult WidthFailure(int diagramWidth, int terminalWidth)
    {
        string reason =
            terminalWidth < MinimumWidth
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"this terminal has {terminalWidth} columns, {MinimumWidth} is the minimum"
                )
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"diagram is {diagramWidth} columns wide, this terminal has {terminalWidth}"
                );
        return MermaidResult.Fail(reason);
    }

    private static (int Number, string Text)? SignificantLine(IReadOnlyList<string> source)
    {
        for (var index = 0; index < source.Count; index++)
        {
            string text = source[index].Trim();
            if (text.Length > 0)
            {
                return (index + 1, text);
            }
        }

        return null;
    }

    private static string Verb(string firstLine)
    {
        int end = firstLine.IndexOf(' ');
        return end < 0 ? firstLine : firstLine[..end];
    }

    internal static StyledLine Label()
    {
        var label = new StyledLine();
        label.Add("mermaid", SpanStyle.Dim);
        return label;
    }

    /// <summary>
    /// A node label's lines: the <c>&lt;br/&gt;</c> breaks kind inside a node label split it, and
    /// the remaining text keeps its characters (label text is drawn verbatim, one column a rune).
    /// </summary>
    internal static IReadOnlyList<string> LabelLines(string raw)
    {
        var lines = new List<string>();
        int cursor = 0;
        while (cursor < raw.Length)
        {
            int br = FindBr(raw, cursor);
            if (br < 0)
            {
                lines.Add(raw[cursor..]);
                return lines;
            }

            lines.Add(raw[cursor..br]);
            cursor = BrEnd(raw, br);
        }

        return lines;
    }

    private static int FindBr(string text, int from)
    {
        int index = from - 1;
        while ((index = text.IndexOf('<', index + 1)) >= 0)
        {
            int close = text.IndexOf('>', index + 1);
            if (close < 0 || close - index > 6)
            {
                continue;
            }

            string tag = text[index..(close + 1)];
            if (
                tag.Equals("<br>", StringComparison.OrdinalIgnoreCase)
                || tag.Equals("<br/>", StringComparison.OrdinalIgnoreCase)
                || tag.Equals("<br />", StringComparison.OrdinalIgnoreCase)
            )
            {
                return index;
            }
        }

        return -1;
    }

    private static int BrEnd(string text, int br)
    {
        int close = text.IndexOf('>', br);
        return close < 0 ? text.Length : close + 1;
    }

    private static List<StyledLine> Fallback(IReadOnlyList<string> source, string reason)
    {
        var lines = new List<StyledLine> { Label() };
        foreach (string text in source)
        {
            var line = new StyledLine();
            line.Add(text, SpanStyle.Plain);
            lines.Add(line);
        }

        var note = new StyledLine();
        note.Add(
            string.Create(CultureInfo.InvariantCulture, $"mermaid: {reason} — showing the source"),
            SpanStyle.Plain
        );
        lines.Add(note);
        return lines;
    }
}
