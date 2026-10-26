namespace Lunate.Extensibility.Restore;

/// <summary>
/// The scope an install wrote to (design D6). <c>user</c> content lives under the state root,
/// <c>project</c> content in the worktree; the record is under the state root in both cases, and a
/// project record names the worktree it belongs to.
/// </summary>
public enum PackageScope
{
    User,
    Project,
}

/// <summary>One written file (design D8): its path relative to the scope root, its size and its SHA-256.</summary>
public sealed record InstalledFile(string Path, long Size, string Sha256);

/// <summary>
/// The record's <c>package</c> half (design D8): what was downloaded and what it vouches for — the
/// sources it came from, the canonical id, the exact version, the nupkg's SHA-512, every entry the
/// archive held, and the extension id whose cache entry holds the dependency lock. The lock itself
/// is not copied in; the record names the extension and the lock stays the existing file.
/// </summary>
public sealed record InstalledPackage
{
    public required string Id { get; init; }

    public required string Version { get; init; }

    /// <summary>The nupkg's SHA-512, base64 — transport integrity, not a signature.</summary>
    public required string Sha512 { get; init; }

    /// <summary>The feed names the download read from, sorted.</summary>
    public required IReadOnlyList<string> Sources { get; init; }

    /// <summary>Every file entry the archive held, package-relative and sorted.</summary>
    public required IReadOnlyList<string> Files { get; init; }

    /// <summary>The packaged extension's id, or null for a skills-only package.</summary>
    public string? ExtensionId { get; init; }
}

/// <summary>
/// The record's <c>content</c> half (design D8): what landed on the machine — the extension id and
/// version when one was installed, the skill names, and every written file relative to the scope
/// root with its size and SHA-256. Directories need no entry: the extension id names its directory
/// and each skill name names one.
/// </summary>
public sealed record InstalledContent
{
    public string? ExtensionId { get; init; }

    public string? ExtensionVersion { get; init; }

    public required IReadOnlyList<string> Skills { get; init; }

    public required IReadOnlyList<InstalledFile> Paths { get; init; }
}

/// <summary>
/// One installed package's <c>install.json</c> (design D8): the two halves, the scope, the worktree a
/// project install belongs to, the install time, and an integrity hash over its own canonical bytes.
/// It is the commit point of an install — a record that exists names content that exists — and it
/// answers for itself: <see cref="PackageInstallRecordJson"/> refuses one whose hash does not match.
/// </summary>
public sealed record PackageInstallRecord
{
    public const int CurrentVersion = 1;

    public required InstalledPackage Package { get; init; }

    public required InstalledContent Content { get; init; }

    public required PackageScope Scope { get; init; }

    /// <summary>The worktree a project install belongs to; null for a user install.</summary>
    public string? Worktree { get; init; }

    /// <summary>The install time, ISO 8601 in the invariant culture.</summary>
    public required string InstalledAt { get; init; }
}
