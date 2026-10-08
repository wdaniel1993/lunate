namespace Lunate.Roslyn;

/// <summary>
/// Diagnostics for a scope. <see cref="Items"/> is capped (200) while the counts report every
/// finding; <see cref="Truncated"/> is true when the cap dropped items.
/// </summary>
public sealed record DiagnosticsResult(
    IReadOnlyList<DiagnosticsItem> Items,
    int ErrorCount,
    int WarningCount,
    bool Truncated,
    string ScopeDescription
);
