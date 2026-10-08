namespace Lunate.Roslyn;

/// <summary>
/// A file's declarations in source order. <see cref="Items"/> is capped (200) while
/// <see cref="TotalItemCount"/> reports every declaration; <see cref="Truncated"/> is true when
/// the cap dropped items. Outlining is syntax-level: it never requires a loaded solution.
/// </summary>
public sealed record OutlineResult(
    string Message,
    IReadOnlyList<OutlineItem> Items,
    int TotalItemCount
)
{
    /// <summary>True when the item cap dropped declarations; the retained ones come first in order.</summary>
    public bool Truncated { get; init; }
}
