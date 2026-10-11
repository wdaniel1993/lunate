namespace Lunate.Extensibility.Abstractions;

/// <summary>How one <see cref="TextSegment"/> is emphasised; a mode maps it to its own palette.</summary>
public enum TextStyle
{
    Plain,
    Dim,
    Accent,
    Warning,
    Error,
}

/// <summary>
/// One JSON-safe run of text contributed to a user-facing UI surface. Everything crossing the
/// extension boundary is data: segments are the contract, never native renderables.
/// </summary>
public sealed record TextSegment(string Text, TextStyle Style);
