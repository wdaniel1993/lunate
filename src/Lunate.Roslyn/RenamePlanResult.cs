namespace Lunate.Roslyn;

/// <summary>
/// A proposed solution-wide rename. The backend never applies it: <see cref="Changes"/> lists the
/// bounded per-file line edits computed on a forked solution, the workspace and disk stay
/// untouched, and the plan is applied through the edit path. <see cref="TotalFileCount"/> and
/// <see cref="TotalChangeCount"/> report every file and changed line; <see cref="Truncated"/> is
/// true when the entry cap dropped lines.
/// </summary>
public sealed record RenamePlanResult(
    SymbolSearchStatus Status,
    string Message,
    IReadOnlyList<RenameChange> Changes,
    int TotalFileCount,
    int TotalChangeCount
)
{
    /// <summary>True when the change cap dropped line entries; the retained ones come first in order.</summary>
    public bool Truncated { get; init; }

    /// <summary>Definitions the name matched when resolution was ambiguous; empty otherwise.</summary>
    public IReadOnlyList<SymbolMatch> Candidates { get; init; } = [];

    /// <summary>Per-document failures observed while refreshing changed files; bound to the first 20.</summary>
    public IReadOnlyList<WorkspaceFailure> Failures { get; init; } = [];

    /// <summary>Total per-document failures observed; <see cref="Failures"/> holds at most the first 20.</summary>
    public int TotalFailureCount { get; init; }
}
