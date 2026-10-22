using Spectre.Console;

namespace Lunate.Tui;

internal sealed record LiveAreaState
{
    public InputLineState Input { get; init; } = InputLine.Empty;

    public string TailText { get; init; } = string.Empty;

    public string? ToolName { get; init; }

    /// <summary>The active tool call's one-line argument summary; empty when the args are unknown.</summary>
    public string? ToolSummary { get; init; }

    /// <summary>When the active tool started, on the scheduler's clock.</summary>
    public DateTimeOffset ToolStartedAt { get; init; }

    public int ToolElapsedSeconds { get; init; }

    /// <summary>True while a run is in flight, even before its first event.</summary>
    public bool Running { get; init; }

    /// <summary>When the current run started, on the scheduler's clock.</summary>
    public DateTimeOffset RunStartedAt { get; init; }

    public int RunElapsedSeconds { get; init; }

    /// <summary>Whether the run can be interrupted (the interrupt hint shows only then).</summary>
    public bool Cancellable { get; init; }

    /// <summary>The current run's input-token delta; null before its first usage event.</summary>
    public long? RunInputTokens { get; init; }

    /// <summary>The current run's output-token delta; null before its first usage event.</summary>
    public long? RunOutputTokens { get; init; }

    /// <summary>True while a child (subagent) run of the current run is live.</summary>
    public bool SubagentRunning { get; init; }

    public int FrameNumber { get; init; }

    public string? Notice { get; init; }

    /// <summary>The open masked secret prompt; null while none is open.</summary>
    public SecretPromptState? SecretPrompt { get; init; }

    public ApprovalPromptModel? Approval { get; init; }

    public TrustPromptModel? Trust { get; init; }

    public SelectListModel? Picker { get; init; }

    /// <summary>The open autocomplete list; null while closed (design D4).</summary>
    public AutocompleteState? Autocomplete { get; init; }

    /// <summary>Whether the input is in bash mode and renders the <c>! </c> prompt.</summary>
    public bool BashMode { get; init; }

    public string? Loading { get; init; }

    /// <summary>The live thinking block; null while no reasoning streams.</summary>
    public ThinkingState? Thinking { get; init; }

    /// <summary>The colour system the raw live frame is painted for (NO_COLOR drops SGR).</summary>
    public ColorSystemSupport Color { get; init; } = ColorSystemSupport.NoColors;

    /// <summary>Whether the terminal can show box characters; false falls back to ASCII.</summary>
    public bool Unicode { get; init; } = true;

    public StatusFooterModel? Footer { get; init; }

    public IReadOnlyList<string> FooterWidgets { get; init; } = [];

    public ConsoleSize Size { get; init; } = new(80, 24);
}

/// <summary>
/// The open masked secret prompt: the label and the mask (one asterisk per typed character). The
/// plaintext never reaches the live state or a rendered frame.
/// </summary>
internal sealed record SecretPromptState(string Label, string Mask);

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
