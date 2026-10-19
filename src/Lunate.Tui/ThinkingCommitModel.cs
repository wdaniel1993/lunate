namespace Lunate.Tui;

/// <summary>
/// A finished reasoning span committing to the scrollback: the whole seconds from its first to
/// its last event, and the text the <c>thinkingFull</c> setting commits instead of the collapsed
/// line.
/// </summary>
public sealed record ThinkingCommitModel(string Text, int ElapsedSeconds, bool Full);
