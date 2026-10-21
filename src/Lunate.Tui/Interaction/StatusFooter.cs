namespace Lunate.Tui;

/// <summary>
/// The session status shown as the footer line: model id, tokens used against the context window,
/// the favourites position and current turn cost when known, working directory (pre-shortened by
/// the wiring layer) and the current git branch when known. <see cref="ThinkLevel"/> carries the
/// session's thinking level literal (<c>low</c>, <c>medium</c>, <c>high</c>); null — the default
/// and the <c>off</c> level — omits the segment.
/// </summary>
public sealed record StatusFooterModel(
    string Model,
    long TokensUsed,
    long ContextWindow,
    string WorkingDirectory,
    string? GitBranch
)
{
    /// <summary>The thinking level segment's literal, or null when thinking is off.</summary>
    public string? ThinkLevel { get; init; }

    /// <summary>
    /// The active model's 1-based position in the settings favourites list, or null when it is not
    /// a favourite. The segment appears only when <see cref="FavoriteCount"/> is positive and the
    /// position is in range.
    /// </summary>
    public int? FavoritePosition { get; init; }

    /// <summary>The configured favourites count; zero hides the favourites segment.</summary>
    public int FavoriteCount { get; init; }

    /// <summary>
    /// The current turn's cost, already formatted for display by the wiring layer
    /// (<c>$0.0123</c>, <c>$1.24</c>, <c>&lt;$0.0001</c>); null or empty when the turn is unpriced.
    /// </summary>
    public string? TurnCost { get; init; }
}
