namespace Lunate.Roslyn;

/// <summary>
/// One usage site of a symbol: <see cref="File"/> is relative to the worktree root when under it
/// (absolute otherwise), <see cref="Line"/> and <see cref="Column"/> are 1-based.
/// </summary>
public sealed record ReferenceLocation(string File, int Line, int Column);
