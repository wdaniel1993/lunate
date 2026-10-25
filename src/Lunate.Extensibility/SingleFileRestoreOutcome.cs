namespace Lunate.Extensibility;

/// <summary>How far a restore got.</summary>
public enum SingleFileRestoreStatus
{
    /// <summary>A lock answers for these bytes, or a restore wrote and bound one.</summary>
    Restored,

    /// <summary>A decision is needed and no prompt could ask for one; nothing was requested from a feed.</summary>
    TrustRequired,

    /// <summary>The user (or the non-interactive prompt) refused; nothing was requested from a feed.</summary>
    Denied,

    /// <summary>The file's header refuses it, so there is nothing to restore.</summary>
    Invalid,

    /// <summary>The restore itself failed; <see cref="SingleFileRestoreOutcome.Message"/> says why.</summary>
    Failed,
}

/// <summary>
/// The outcome of <see cref="SingleFileExtensionRestorer.EnsureRestoredAsync"/>: the status, the
/// freshly read candidate with its state updated, the lock that now answers for its bytes, and the
/// lines describing what changed. <see cref="Changes"/> is the trust-relevant half — the graph — and
/// is what a question listed; <see cref="RebuildReasons"/> is why the lock was rebuilt, including a
/// toolset difference that must never become a trust question.
/// </summary>
public sealed record SingleFileRestoreOutcome(
    SingleFileRestoreStatus Status,
    SingleFileExtension Candidate,
    ExtensionLock? Lock,
    string? LockPath,
    string? LockHash,
    string? Message,
    IReadOnlyList<string> Changes
)
{
    /// <summary>
    /// Why the lock was rebuilt without asking: a toolset difference (runtime, reference pack), or a
    /// graph difference the user had already approved. Empty when the lock was reused as it was.
    /// </summary>
    public IReadOnlyList<string> RebuildReasons { get; init; } = [];
}
