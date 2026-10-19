namespace Lunate.Tui;

/// <summary>
/// The viewer's transcript: every committed item in commit order. The session owns the list; this
/// model is the read view the renderer consumes.
/// </summary>
public sealed record TranscriptModel(IReadOnlyList<TranscriptEntry> Entries);
