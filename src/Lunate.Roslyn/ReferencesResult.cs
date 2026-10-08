namespace Lunate.Roslyn;

/// <summary>
/// References found for an exactly resolved symbol. <see cref="References"/> is capped (200) while
/// <see cref="TotalReferenceCount"/> reports every usage; the declaration site is reported once in
/// <see cref="Resolved"/> and never among the references. <see cref="Candidates"/> carries the
/// matching definitions when the name is ambiguous instead of guessing.
/// </summary>
public sealed record ReferencesResult(
    SymbolSearchStatus Status,
    string Message,
    SymbolMatch? Resolved,
    IReadOnlyList<ReferenceLocation> References,
    int TotalReferenceCount
)
{
    /// <summary>True when the reference cap dropped usages; the retained ones come first in order.</summary>
    public bool Truncated { get; init; }

    /// <summary>Definitions the name matched when resolution was ambiguous; empty otherwise.</summary>
    public IReadOnlyList<SymbolMatch> Candidates { get; init; } = [];

    /// <summary>Per-document failures observed while refreshing changed files; bound to the first 20.</summary>
    public IReadOnlyList<WorkspaceFailure> Failures { get; init; } = [];

    /// <summary>Total per-document failures observed; <see cref="Failures"/> holds at most the first 20.</summary>
    public int TotalFailureCount { get; init; }
}
