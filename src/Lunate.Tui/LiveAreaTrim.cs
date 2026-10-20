namespace Lunate.Tui;

/// <summary>
/// The live frame's height cap: when a short terminal cannot show the whole frame, trim the
/// transient lines above the input first and, inside an approval panel, the detail before the
/// headline. The question always renders, because it is the one line the user must answer.
/// </summary>
internal static class LiveAreaTrim
{
    public static void Apply(
        List<string> lines,
        int rows,
        int transientLines,
        int approvalStart,
        IReadOnlyList<ApprovalPanelLine> approval,
        ref int cursorRow
    )
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(approval);

        if (rows <= 0 || lines.Count <= rows)
        {
            return;
        }

        int overflow = lines.Count - rows;
        if (approval.Count == 0)
        {
            int trim = Math.Min(overflow, transientLines);
            lines.RemoveRange(0, trim);
            cursorRow -= trim;
            return;
        }

        // The tail and the other status lines sit above the panel; consume them first.
        int prefix = Math.Min(overflow, Math.Max(0, approvalStart));
        if (prefix > 0)
        {
            lines.RemoveRange(0, prefix);
            cursorRow -= prefix;
            overflow -= prefix;
        }

        if (overflow <= 0)
        {
            return;
        }

        int panelStart = Math.Max(0, approvalStart) - prefix;
        int questionLines = approval.Count(line => line.Part == ApprovalPart.Question);
        int detailLines = approval.Count - 1 - questionLines;
        // The detail block follows the headline; drop its bottom lines so the first detail stays.
        int trimDetail = Math.Min(overflow, detailLines);
        if (trimDetail > 0)
        {
            lines.RemoveRange(panelStart + 1 + (detailLines - trimDetail), trimDetail);
            cursorRow -= trimDetail;
            overflow -= trimDetail;
        }

        if (overflow > 0)
        {
            lines.RemoveAt(panelStart);
            cursorRow--;
        }
    }
}
