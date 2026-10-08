namespace Lunate.Roslyn;

/// <summary>
/// Diagnostics for a scope. <see cref="Items"/> is capped (200) while the counts report every
/// finding; <see cref="Truncated"/> is true when the cap dropped items. <see cref="Failures"/>
/// reports per-document problems (a file deleted or unreadable since load) without discarding the
/// loaded workspace.
/// </summary>
public sealed record DiagnosticsResult(
    IReadOnlyList<DiagnosticsItem> Items,
    int ErrorCount,
    int WarningCount,
    bool Truncated,
    string ScopeDescription
)
{
    /// <summary>Per-document failures observed while refreshing changed files; bound to the first 20.</summary>
    public IReadOnlyList<WorkspaceFailure> Failures { get; init; } = [];

    /// <summary>Total per-document failures observed; <see cref="Failures"/> holds at most the first 20.</summary>
    public int TotalFailureCount { get; init; }
}
