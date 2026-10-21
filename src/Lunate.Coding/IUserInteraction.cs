using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>How urgent a transient notice is; modes may present levels differently in the future.</summary>
internal enum NoticeLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>A transient user-facing notice: retry, compaction, step limit, run failure, feedback.</summary>
internal sealed record UserNotice(NoticeLevel Level, string Message);

/// <summary>The footer's data: the core fields plus the segments status widgets contributed.</summary>
internal sealed record UserStatus(
    string Model,
    long TokensUsed,
    long ContextWindow,
    string WorkingDirectory,
    string? GitBranch,
    string? ThinkLevel,
    IReadOnlyList<TextSegment> Widgets
)
{
    /// <summary>The active model's 1-based favourites position, or null when it is not one.</summary>
    public int? FavoritePosition { get; init; }

    /// <summary>The configured favourites count; zero hides the footer segment.</summary>
    public int FavoriteCount { get; init; }

    /// <summary>The current turn's formatted cost, or null when the turn is unpriced.</summary>
    public string? TurnCost { get; init; }
}

/// <summary>One tool result's extension-contributed block: styled segments for that tool.</summary>
internal sealed record ExtensionBlock(string ToolName, IReadOnlyList<TextSegment> Segments);

/// <summary>
/// The mode-facing seam for ambient user interaction: transient notices, the status footer's data
/// and extension-contributed blocks. Approvals keep their own per-mode chain; this seam carries
/// output only. One implementation per mode: the TUI presents all three, print mode writes
/// notices to stderr and treats status/blocks as no-ops, ACP logs them (the protocol has no
/// notice frame).
/// </summary>
internal interface IUserInteraction
{
    void Notify(UserNotice notice);

    void UpdateStatus(UserStatus status);

    void RenderExtension(ExtensionBlock block);
}
