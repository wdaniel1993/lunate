namespace Lunate.Tui;

/// <summary>
/// The Mermaid <c>sequenceDiagram</c> entry point: parse, then draw. The parse fails soft —
/// every refusal becomes the fence's one-line fallback note — and the drawing never throws:
/// an unsupported statement, an unterminated note or an unreadable message leaves the source
/// with the reason instead of a rendering.
/// </summary>
internal static class MermaidSequence
{
    internal static MermaidResult TryDraw(IReadOnlyList<string> source, bool unicode)
    {
        return MermaidSequenceParser.TryParse(
            source,
            out MermaidSequenceModel? model,
            out MermaidFailure? failure
        )
            ? Draw(model!, unicode)
            : MermaidResult.Fail(failure!.Reason);
    }

    private static MermaidResult Draw(MermaidSequenceModel model, bool unicode)
    {
        var canvas = new MermaidCanvas(unicode);
        IReadOnlyList<(int X, int W)> lanes = Participants(model, canvas, out List<int> centers);
        int bottom = Place(model, out List<int> starts);
        DrawLifelines(canvas, centers, bottom);
        for (var index = 0; index < model.Speech.Count; index++)
        {
            int row = starts[index];
            MermaidSpeech statement = model.Speech[index];
            if (statement is MermaidMessage message)
            {
                DrawMessage(canvas, message, lanes, row);
            }
            else if (statement is MermaidNote note)
            {
                DrawNote(canvas, centers, note, row);
            }
        }

        return MermaidResult.Ok(canvas);
    }

    /// <summary>
    /// The row layout: a message takes its arrow row, one label row and one blank row; a note
    /// takes its box rows and a blank row after it. Returns the last content row and each
    /// statement's start row, in statement order.
    /// </summary>
    private static int Place(MermaidSequenceModel model, out List<int> starts)
    {
        starts = [];
        int row = 4;
        int bottom = 3;
        foreach (MermaidSpeech statement in model.Speech)
        {
            if (statement is MermaidMessage)
            {
                starts.Add(row);
                bottom = row + 1;
                row += 3;
            }
            else
            {
                starts.Add(row);
                int box = NoteHeights(((MermaidNote)statement).Content);
                bottom = row + box - 1;
                row += box + 1;
            }
        }

        return bottom;
    }

    private static int NoteHeights(string content) => MermaidDiagram.LabelLines(content).Count + 2;

    /// <summary>
    /// Participant boxes on one row, their lifeline columns the centers; empty diagrams still
    /// paint their (empty) row of header boxes, which the width gate then may refuse.
    /// </summary>
    private static IReadOnlyList<(int X, int W)> Participants(
        MermaidSequenceModel model,
        MermaidCanvas canvas,
        out List<int> centers
    )
    {
        var lanes = new List<(int X, int W)>();
        centers = [];
        int x = 0;
        foreach (MermaidParticipant participant in model.Participants)
        {
            int width = participant.Name.Length + 4;
            lanes.Add((x, width));
            canvas.Box(x, 0, width, 3, [participant.Name]);
            int center = x + width / 2;
            centers.Add(center);
            x += width + 5;
        }

        return lanes;
    }

    /// <summary>One lifeline per participant, from below the header row to the last content row.</summary>
    private static void DrawLifelines(MermaidCanvas canvas, List<int> centers, int bottom)
    {
        for (int row = 3; row <= bottom; row++)
        {
            foreach (int center in centers)
            {
                canvas.Link(center, row, canvas.Glyphs.Vertical);
            }
        }
    }

    /// <summary>
    /// An arrow between two lifelines, its head on the target lifeline; the text sits on the row
    /// below the arrow, clipped to the lanes between the two endpoints.
    /// </summary>
    private static void DrawMessage(
        MermaidCanvas canvas,
        MermaidMessage message,
        IReadOnlyList<(int X, int W)> lanes,
        int row
    )
    {
        MermaidGlyphs glyphs = canvas.Glyphs;
        int from = LaneCenter(lanes, message.From);
        int to = LaneCenter(lanes, message.To);
        char run = message.Dashed ? glyphs.DashedHorizontal : glyphs.Horizontal;
        char head = from < to ? glyphs.HeadRight : glyphs.HeadLeft;
        for (int column = Math.Min(from, to) + 1; column <= Math.Max(from, to) - 1; column++)
        {
            canvas.Link(column, row, run);
        }

        canvas.Force(to, row, head);
        if (message.Content.Length > 0)
        {
            LayOverLanes(
                canvas,
                message.Content,
                Math.Min(from, to) + 1,
                Math.Max(from, to) - 1,
                row + 1
            );
        }
    }

    /// <summary>A note: a box across the named participants' lifelines, its text inside.</summary>
    private static void DrawNote(MermaidCanvas canvas, List<int> centers, MermaidNote note, int row)
    {
        IReadOnlyList<string> lines = MermaidDiagram.LabelLines(note.Content);
        int widest = 1;
        foreach (string line in lines)
        {
            widest = Math.Max(widest, line.Length);
        }

        int low = int.MaxValue;
        int high = int.MinValue;
        foreach (int seat in note.Over)
        {
            if (seat < centers.Count)
            {
                low = Math.Min(low, centers[seat]);
                high = Math.Max(high, centers[seat]);
            }
        }

        if (low == int.MaxValue)
        {
            return;
        }

        int width = Math.Max(widest + 4, high - low + 4);
        int center = (low + high) / 2;
        canvas.Box(Math.Max(0, center - width / 2), row, width, lines.Count + 2, lines);
    }

    private static int LaneCenter(IReadOnlyList<(int X, int W)> lanes, int seat)
    {
        (int X, int W) lane = lanes[Math.Min(Math.Max(seat, 0), lanes.Count - 1)];
        return lane.X + lane.W / 2;
    }

    private static void LayOverLanes(
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
}
