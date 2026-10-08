namespace Lunate.Roslyn;

/// <summary>
/// The proposed line changes for one file of a rename plan: <see cref="File"/> is relative to the
/// worktree root when under it (absolute otherwise).
/// </summary>
public sealed record RenameChange(string File, IReadOnlyList<RenameLineChange> Entries);
