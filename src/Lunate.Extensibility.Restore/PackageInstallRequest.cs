namespace Lunate.Extensibility.Restore;

/// <summary>
/// One install request (design D1): an exact package id and version, and where it writes. The id is
/// the canonical spelling; the version is exact — a range or a wildcard belongs to a dependency's
/// declaration, not to an install.
/// </summary>
public sealed record PackageInstallRequest
{
    public required string Id { get; init; }

    /// <summary>The exact version; empty resolves the latest stable version once (design D1).</summary>
    public required string Version { get; init; }

    public required PackageInstallTarget Target { get; init; }

    /// <summary>
    /// The worktree the install is judged against for cross-scope collisions (design D10): the
    /// current one for a user install, and implied by <see cref="Target"/> for a project install.
    /// </summary>
    public string? Worktree { get; init; }

    /// <summary>
    /// Rewrites the recorded paths of an installed version that are missing or changed, from that
    /// version's own archive (spec: "Failure, offline and private feeds"). Repairs an install; it
    /// never turns a refusal into a replacement.
    /// </summary>
    public bool Force { get; init; }
}

public enum PackageInstallStatus
{
    /// <summary>The content and the record were written by this run.</summary>
    Installed,

    /// <summary>The same version was already installed and verified as recorded; nothing moved.</summary>
    AlreadyInstalled,

    /// <summary>
    /// A different exact version replaced the installed one: the old content went through the old
    /// record, and the new content and record are this run's (spec: "Pinning and upgrade").
    /// </summary>
    Upgraded,

    /// <summary>
    /// <c>--force</c> rewrote the recorded paths that were missing or changed from the recorded
    /// version's nupkg; the record itself is unchanged (spec: "Failure, offline and private feeds").
    /// </summary>
    Repaired,
}

/// <summary>
/// What an install did: the record it wrote or found, where that record lives, and the paths it
/// wrote relative to the scope root. <see cref="PackageInstallStatus.AlreadyInstalled"/> writes
/// nothing, so <see cref="WrittenPaths"/> is empty.
/// </summary>
public sealed record PackageInstallResult
{
    public required PackageInstallStatus Status { get; init; }

    public required PackageInstallRecord Record { get; init; }

    public required string RecordPath { get; init; }

    public IReadOnlyList<string> WrittenPaths { get; init; } = [];

    /// <summary>
    /// The version an upgrade replaced, so the command can say what changed; null when this run
    /// installed a package that was not installed before (spec: "Pinning and upgrade").
    /// </summary>
    public string? PreviousVersion { get; init; }

    /// <summary>The signs of the old version an upgrade took back, relative to the scope root.</summary>
    public IReadOnlyList<string> RemovedPaths { get; init; } = [];

    /// <summary>
    /// Paths the old version left behind because the file could not be deleted — Windows keeps a
    /// loaded assembly mapped. The new record does not name them, so the command says so instead
    /// of claiming the upgrade removed everything; a later reinstall or <c>uninstall --force</c>
    /// clears them once nothing holds the file.
    /// </summary>
    public IReadOnlyList<string> LeftBehind { get; init; } = [];

    /// <summary>
    /// What the install noticed without refusing (design D10): a same-named skill in another scope
    /// that the existing shadowing rule resolves. Empty when there is nothing to say.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// The package as the archive described it, for the command's identity lines; null when nothing
    /// was downloaded (an already-installed version answers from its record).
    /// </summary>
    public PackageInfo? Resolved { get; init; }

    /// <summary>
    /// The dependency closure in the versions it resolved to, ordered by id (spec: "Install output
    /// and <c>--dry-run</c>"). An already-installed version answers from the lock its record names,
    /// so the same command reports the same closure without touching a feed.
    /// </summary>
    public IReadOnlyList<ResolvedDependency> Dependencies { get; init; } = [];
}
