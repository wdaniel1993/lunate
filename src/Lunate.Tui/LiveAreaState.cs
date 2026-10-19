using Spectre.Console;

namespace Lunate.Tui;

internal sealed record LiveAreaState
{
    public InputLineState Input { get; init; } = InputLine.Empty;

    public string TailText { get; init; } = string.Empty;

    public string? ToolName { get; init; }

    /// <summary>True while a child (subagent) run of the current run is live.</summary>
    public bool SubagentRunning { get; init; }

    public int FrameNumber { get; init; }

    public string? Notice { get; init; }

    public ApprovalPromptModel? Approval { get; init; }

    public TrustPromptModel? Trust { get; init; }

    public SelectListModel? Picker { get; init; }

    /// <summary>The open autocomplete list; null while closed (design D4).</summary>
    public AutocompleteState? Autocomplete { get; init; }

    public string? Loading { get; init; }

    /// <summary>The live thinking block; null while no reasoning streams.</summary>
    public ThinkingState? Thinking { get; init; }

    /// <summary>The colour system the raw live frame is painted for (NO_COLOR drops SGR).</summary>
    public ColorSystemSupport Color { get; init; } = ColorSystemSupport.NoColors;

    public StatusFooterModel? Footer { get; init; }

    public IReadOnlyList<string> FooterWidgets { get; init; } = [];

    public ConsoleSize Size { get; init; } = new(80, 24);
}

/// <summary>
/// The live thinking block: the accumulating reasoning text and how long it has streamed. The
/// elapsed seconds are carried by the state (not read as a clock by the renderer), so rendering
/// stays a pure function of state.
/// </summary>
internal sealed record ThinkingState
{
    public required string Text { get; init; }

    /// <summary>When the first reasoning content arrived, on the scheduler's clock.</summary>
    public DateTimeOffset StartedAt { get; init; }

    public int ElapsedSeconds { get; init; }
}
