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
}

public enum PackageInstallStatus
{
    /// <summary>The content and the record were written by this run.</summary>
    Installed,

    /// <summary>The same version was already installed and verified as recorded; nothing moved.</summary>
    AlreadyInstalled,
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
    /// What the install noticed without refusing (design D10): a same-named skill in another scope
    /// that the existing shadowing rule resolves. Empty when there is nothing to say.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// The package as the archive described it, for the command's identity lines; null when nothing
    /// was downloaded (an already-installed version answers from its record).
    /// </summary>
    public PackageInfo? Resolved { get; init; }
}
