namespace Lunate.Roslyn;

/// <summary>
/// Definitions found for a name. <see cref="Matches"/> is bounded (50) while
/// <see cref="TotalMatchCount"/> reports every match; <see cref="Truncated"/> is true when the cap
/// dropped matches. <see cref="Failures"/> reports per-document problems observed while syncing
/// changed files before the search, without discarding the loaded workspace.
/// </summary>
public sealed record SymbolSearchResult(
    SymbolSearchStatus Status,
    string Message,
    IReadOnlyList<SymbolMatch> Matches,
    int TotalMatchCount
)
{
    /// <summary>True when the match cap dropped definitions; the retained ones come first in order.</summary>
    public bool Truncated { get; init; }

    /// <summary>Per-document failures observed while refreshing changed files; bound to the first 20.</summary>
    public IReadOnlyList<WorkspaceFailure> Failures { get; init; } = [];

    /// <summary>Total per-document failures observed; <see cref="Failures"/> holds at most the first 20.</summary>
    public int TotalFailureCount { get; init; }
}
