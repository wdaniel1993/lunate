namespace S5.Harness;

/// <summary>
/// Plain text live-area renderer shared by all variants. The real TUI draws
/// ANSI in the bottom rows; the spike renders the same content as lines into
/// the fake console so every variant can be diffed and asserted.
/// </summary>
public static class LiveAreaRenderer
{
    public static IReadOnlyList<string> Render(LiveAreaView view, int width)
    {
        var lines = new List<string>();
        foreach (var line in view.Tail)
        {
            lines.Add(Clip(line, width));
        }

        foreach (var steering in view.QueuedSteering)
        {
            lines.Add(Clip($"queued: {steering}", width));
        }

        var status = new List<string>();
        if (view.SpinnerGlyph is not null)
        {
            status.Add(view.SpinnerGlyph);
        }

        if (view.Footer.Length > 0)
        {
            status.Add(view.Footer);
        }

        if (status.Count > 0)
        {
            lines.Add(Clip(string.Join("  ", status), width));
        }

        if (view.Status is not null)
        {
            lines.Add(Clip(view.Status, width));
        }

        if (view.ApprovalPrompt is not null)
        {
            lines.Add(Clip(view.ApprovalPrompt, width));
        }

        lines.Add(Clip($"> {view.Input}", width));
        return lines;
    }

    private static string Clip(string value, int width) =>
        value.Length <= width || width <= 0 ? value : value[..width];
}
