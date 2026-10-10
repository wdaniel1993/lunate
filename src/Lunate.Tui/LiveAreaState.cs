namespace Lunate.Tui;

internal sealed record LiveAreaState
{
    public InputLineState Input { get; init; } = InputLine.Empty;

    public string TailText { get; init; } = string.Empty;

    public string? ToolName { get; init; }

    public int FrameNumber { get; init; }

    public string? Notice { get; init; }

    public ApprovalPromptModel? Approval { get; init; }

    public SelectListModel? Picker { get; init; }

    public StatusFooterModel? Footer { get; init; }

    public ConsoleSize Size { get; init; } = new(80, 24);
}
