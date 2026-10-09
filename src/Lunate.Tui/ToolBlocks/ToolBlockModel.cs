namespace Lunate.Tui;

/// <summary>Status of a tool call shown in a transcript block.</summary>
public enum ToolBlockStatus
{
    Running,
    Ok,
    Error,
}

/// <summary>
/// UI-only diff details of an <c>edit</c> or <c>write</c> call: primitives only, so the TUI never
/// references <c>Lunate.Coding</c>; the wiring layer maps <c>EditDetails</c>/<c>WriteDetails</c> onto it.
/// </summary>
public sealed record ToolDiffInfo(string Path, string MatchTier, string UnifiedDiff);

/// <summary>
/// A tool call rendered as a transcript block: tool name, argument summary, status, an optionally
/// bounded output excerpt and an optional unified diff.
/// </summary>
public sealed record ToolBlockModel(
    string ToolName,
    string ArgsSummary,
    ToolBlockStatus Status,
    string? Output,
    ToolDiffInfo? Diff
);
