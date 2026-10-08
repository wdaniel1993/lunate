namespace Lunate.Roslyn;

/// <summary>
/// One compiler diagnostic at a source position: <see cref="File"/> is relative to the worktree
/// root when under it (absolute otherwise), positions are 1-based.
/// </summary>
public sealed record DiagnosticsItem(
    string File,
    int Line,
    int Column,
    DiagnosticsSeverity Severity,
    string Id,
    string Message
);
