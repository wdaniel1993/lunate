namespace Lunate.Tui;

/// <summary>
/// One committed item of the session's transcript, in commit order. The store keeps the full data
/// (a tool block's whole output and diff included); excerpting stays a render concern.
/// </summary>
public abstract record TranscriptEntry;

/// <summary>A submitted user message, as the run received it (steering echoes included).</summary>
public sealed record TranscriptUserEntry(string Text) : TranscriptEntry;

/// <summary>A committed assistant paragraph: the raw markdown, re-rendered by the viewer.</summary>
public sealed record TranscriptAssistantEntry(string Markdown) : TranscriptEntry;
