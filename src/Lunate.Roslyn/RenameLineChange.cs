namespace Lunate.Roslyn;

/// <summary>
/// One changed line of a proposed rename: <see cref="Line"/> is 1-based, <see cref="OldText"/> and
/// <see cref="NewText"/> are the trimmed line contents.
/// </summary>
public sealed record RenameLineChange(int Line, string OldText, string NewText);
