namespace Lunate.Tui;

/// <summary>
/// A finished tool call: the complete <see cref="ToolBlockModel"/> as committed, so the viewer
/// shows the full output and diff the scrollback excerpted.
/// </summary>
public sealed record TranscriptToolEntry(ToolBlockModel Block) : TranscriptEntry;
