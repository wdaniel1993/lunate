namespace Lunate.Tui;

internal sealed record LiveAreaState
{
    public InputLineState Input { get; init; } = InputLine.Empty;

    public string TailText { get; init; } = string.Empty;

    public string? ToolName { get; init; }

    public int FrameNumber { get; init; }

    public string? Model { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public double ContextPercent { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? GitBranch { get; init; }

    public ConsoleSize Size { get; init; } = new(80, 24);
}
