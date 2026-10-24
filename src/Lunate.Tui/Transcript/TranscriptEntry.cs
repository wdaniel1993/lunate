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

/// <summary>
/// One row of the session tree as <c>/tree</c> paints it: an entry of any branch, its depth from the
/// root and whether it sits on the active path. The row carries its own id and text, so the picker's
/// filter and the caller's pick index stay a function of what the person sees — and the id in the
/// gutter is the argument <c>/fork</c> takes.
/// </summary>
internal sealed record TranscriptTreeRowEntry(
    string Id,
    string Text,
    int Depth,
    bool OnActivePath,
    bool IsUserMessage
) : TranscriptEntry;
