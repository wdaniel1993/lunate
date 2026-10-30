namespace Lunate.Tui;

/// <summary>
/// The drawing half of <see cref="MermaidFlowchart"/>: layered placement — one row (or column)
/// per layer, boxes in first-appearance order, edges routed in the inter-layer gaps with the
/// box-drawing set, links first so boxes erase them, labels over their line, boxes last. Layers
/// come from the parse: every edge points a whole layer up the page, so routing never loops.
/// </summary>
internal static partial class MermaidFlowchart
{
    private const int BetweenColumns = 4;
    private const int BetweenRows = 2;
    private const int BetweenLayers = 3;

    private static MermaidResult Draw(MermaidFlowchartModel flowchart, bool unicode)
    {
        var canvas = new MermaidCanvas(unicode);
        var rects = Spread(flowchart, flowchart.Horizontal);
        var corners = new Dictionary<(int X, int Y), CornerFlags>();
        foreach (MermaidEdge edge in flowchart.Edges)
        {
            if (flowchart.Horizontal)
            {
                HorizontalEdge(canvas, edge, rects, corners);
            }
            else
            {
                VerticalEdge(canvas, edge, rects, corners);
            }
        }

        foreach (((int X, int Y) at, CornerFlags flags) in corners)
        {
            canvas.Force(at.X, at.Y, Junction(canvas, flags));
        }

        foreach (MermaidNode node in flowchart.Nodes)
        {
            (int X, int Y, int W, int H) = rects[node.Id];
            canvas.Box(X, Y, W, H, node.LabelLines);
        }

        return MermaidResult.Ok(canvas);
    }

    private static Dictionary<string, (int X, int Y, int W, int H)> Spread(
        MermaidFlowchartModel flowchart,
        bool horizontal
    )
    {
        var rects = new Dictionary<string, (int X, int Y, int W, int H)>(StringComparer.Ordinal);
        var layers = new SortedDictionary<int, List<int>>();
        for (var index = 0; index < flowchart.Nodes.Count; index++)
        {
            int layer = flowchart.Layers[index];
            if (!layers.TryGetValue(layer, out List<int>? bucket))
            {
                bucket = [];
                layers[layer] = bucket;
            }

            bucket.Add(index);
        }

        int cursor = 0;
        foreach (List<int> bucket in layers.Values)
        {
            int span = 1;
            foreach (int index in bucket)
            {
                (int W, int H) = Box(flowchart.Nodes[index]);
                span = Math.Max(span, horizontal ? W : H);
            }

            int at = 0;
            foreach (int index in bucket)
            {
                (int W, int H) = Box(flowchart.Nodes[index]);
                rects[flowchart.Nodes[index].Id] = horizontal
                    ? (cursor, at, W, H)
                    : (at, cursor, W, H);
                at += horizontal ? H + BetweenRows : W + BetweenColumns;
            }

            cursor += span + (horizontal ? ColumnGap(flowchart) : BetweenLayers);
        }

        return rects;
    }

    /// <summary>
    /// The columns between horizontal layers: enough for the diagram's longest edge label to
    /// ride on the bend row, four columns when no edge carries a label.
    /// </summary>
    private static int ColumnGap(MermaidFlowchartModel flowchart)
    {
        int label = 0;
        foreach (MermaidEdge edge in flowchart.Edges)
        {
            label = Math.Max(label, edge.Label?.Length ?? 0);
        }

        return Math.Max(BetweenColumns, label + 4);
    }

    private static (int W, int H) Box(MermaidNode node)
    {
        int widest = 1;
        foreach (string line in node.LabelLines)
        {
            widest = Math.Max(widest, line.Length);
        }

        return (widest + 4, node.LabelLines.Count + 2);
    }

    /// <summary>
    /// An edge in a vertical diagram: down from the source box's bottom into the target's two
    /// routing rows, a labelled edge keeps its text on the bend row, the head sits one row above
    /// the target box so its top border stays whole.
    /// </summary>
    private static void VerticalEdge(
        MermaidCanvas canvas,
        MermaidEdge edge,
        Dictionary<string, (int X, int Y, int W, int H)> rects,
        Dictionary<(int X, int Y), CornerFlags> corners
    )
    {
        (int sourceX, int sourceY, int sourceW, int sourceH) = rects[edge.From];
        (int targetX, int targetY, int targetW, _) = rects[edge.To];
        MermaidGlyphs glyphs = canvas.Glyphs;
        char line = edge.Dashed ? glyphs.DashedVertical : glyphs.Vertical;
        int from = sourceX + sourceW / 2;
        int to = targetX + targetW / 2;
        int bottom = sourceY + sourceH - 1;
        int top = targetY;
        if (from == to)
        {
            for (int row = bottom + 1; row <= top - 2; row++)
            {
                canvas.Link(from, row, line);
            }

            canvas.Force(to, top - 1, edge.Arrowhead ? glyphs.HeadDown : line);
        }
        else
        {
            int bend = top - 2;
            for (int row = bottom + 1; row < bend; row++)
            {
                canvas.Link(from, row, line);
            }

            Merge(corners, from, bend, up: true, left: to < from, right: to > from);
            for (int column = Math.Min(from, to) + 1; column < Math.Max(from, to); column++)
            {
                canvas.Link(column, bend, OverLineGlyph(edge, canvas));
            }

            Merge(corners, to, bend, down: true, left: to < from, right: to > from);
            for (int row = bend + 1; row <= top - 2; row++)
            {
                canvas.Link(to, row, line);
            }

            canvas.Force(to, top - 1, edge.Arrowhead ? glyphs.HeadDown : line);
        }

        if (edge.Label is { Length: > 0 } label)
        {
            if (from == to)
            {
                int mid = bottom + 1 + (top - 1 - (bottom + 1)) / 2;
                canvas.Text(to + 1, mid, label);
            }
            else
            {
                // The bend's junction glyphs own the `from` and `to` cells; a label whose run
                // reaches them moves right of the bend so the box-drawing never erases a letter.
                int bend = top - 2;
                int low = Math.Min(from, to);
                int high = Math.Max(from, to);
                int start = Math.Max(low + 1, (low + high) / 2 - label.Length / 2);
                bool touches =
                    (start <= from && from < start + label.Length)
                    || (start <= to && to < start + label.Length);
                start = touches ? high + 2 : start;
                canvas.Text(start, bend, label);
            }
        }
    }

    /// <summary>
    /// An edge in a horizontal diagram: right from the source box's right border, bending one
    /// column into the gap, the head one column left of the target box.
    /// </summary>
    private static void HorizontalEdge(
        MermaidCanvas canvas,
        MermaidEdge edge,
        Dictionary<string, (int X, int Y, int W, int H)> rects,
        Dictionary<(int X, int Y), CornerFlags> corners
    )
    {
        (int sourceX, int sourceY, int sourceW, int sourceH) = rects[edge.From];
        (int targetX, int targetY, _, int targetH) = rects[edge.To];
        MermaidGlyphs glyphs = canvas.Glyphs;
        char run = edge.Dashed ? glyphs.DashedHorizontal : glyphs.Horizontal;
        char line = edge.Dashed ? glyphs.DashedVertical : glyphs.Vertical;
        int startColumn = sourceX + sourceW;
        int endColumn = targetX;
        int fromRow = sourceY + sourceH / 2;
        int toRow = targetY + targetH / 2;
        if (fromRow == toRow)
        {
            for (int column = startColumn; column <= endColumn - 2; column++)
            {
                canvas.Link(column, fromRow, run);
            }

            canvas.Force(endColumn - 1, fromRow, edge.Arrowhead ? glyphs.HeadRight : run);
        }
        else
        {
            int bend = startColumn + 1;
            canvas.Link(startColumn, fromRow, run);
            Merge(
                corners,
                bend,
                fromRow,
                down: toRow > fromRow,
                up: toRow < fromRow,
                left: true,
                right: toRow == fromRow
            );
            for (int row = Math.Min(fromRow, toRow) + 1; row < Math.Max(fromRow, toRow); row++)
            {
                canvas.Link(bend, row, line);
            }

            Merge(
                corners,
                bend,
                toRow,
                down: toRow > fromRow,
                up: toRow < fromRow,
                left: true,
                right: true
            );
            for (int column = bend + 1; column <= endColumn - 2; column++)
            {
                canvas.Link(column, toRow, run);
            }

            canvas.Force(endColumn - 1, toRow, edge.Arrowhead ? glyphs.HeadRight : run);
        }

        if (edge.Label is { Length: > 0 } label)
        {
            if (fromRow == toRow)
            {
                LayOverLine(canvas, label, startColumn + 1, endColumn - 2, fromRow);
            }
            else
            {
                LayOverLine(canvas, label, startColumn + 2, endColumn - 2, toRow);
            }
        }
    }

    private static char OverLineGlyph(MermaidEdge edge, MermaidCanvas canvas) =>
        edge.Dashed ? canvas.Glyphs.DashedHorizontal : canvas.Glyphs.Horizontal;

    /// <summary>Label text over a link run, ellipsed when it outgrows the run's own columns.</summary>
    private static void LayOverLine(
        MermaidCanvas canvas,
        string label,
        int first,
        int last,
        int row
    )
    {
        if (first > last)
        {
            return;
        }

        int available = last - first + 1;
        string printed =
            label.Length <= available
                ? label
                : label[..Math.Max(1, available - 1)] + canvas.Glyphs.Ellipsis;
        canvas.Text(first, row, printed);
    }

    /// <summary>Incoming into a junction: which directions the corner must answer to.</summary>
    private static void Merge(
        Dictionary<(int X, int Y), CornerFlags> corners,
        int x,
        int y,
        bool up = false,
        bool down = false,
        bool left = false,
        bool right = false
    )
    {
        (int X, int Y) at = (x, y);
        CornerFlags merged = corners.TryGetValue(at, out CornerFlags existing)
            ? new(
                existing.Up || up,
                existing.Down || down,
                existing.Left || left,
                existing.Right || right
            )
            : new(up, down, left, right);
        corners[at] = merged;
    }

    private static char Junction(MermaidCanvas canvas, CornerFlags flags)
    {
        MermaidGlyphs glyphs = canvas.Glyphs;
        return (flags.Up, flags.Down, flags.Left, flags.Right) switch
        {
            (true, true, true, true) => glyphs.Horizontal,
            (false, true, true, true) => glyphs.BracedTop,
            (true, false, true, true) => glyphs.BracedBottom,
            (true, true, false, true) => glyphs.BracedLeft,
            (true, true, true, false) => glyphs.BracedRight,
            (false, true, false, true) => glyphs.CornerTopLeft,
            (false, true, true, false) => glyphs.CornerTopRight,
            (true, false, false, true) => glyphs.CornerBottomLeft,
            (true, false, true, false) => glyphs.CornerBottomRight,
            (false, false, true, true) => glyphs.Horizontal,
            _ => glyphs.Vertical,
        };
    }
}

/// <summary>Junction flags and their glyphs.</summary>
internal readonly record struct CornerFlags(bool Up, bool Down, bool Left, bool Right);
