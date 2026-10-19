namespace Lunate.Tui;

/// <summary>
/// A finished reasoning span: the same <see cref="ThinkingCommitModel"/> the scrollback commits, so
/// the viewer can show the collapsed line or the full text.
/// </summary>
public sealed record TranscriptThinkingEntry(ThinkingCommitModel Commit) : TranscriptEntry;
