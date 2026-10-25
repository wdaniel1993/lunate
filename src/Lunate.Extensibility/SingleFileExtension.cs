namespace Lunate.Extensibility;

/// <summary>
/// What <c>/extensions</c> reports for one discovered single-file extension: a valid declaration
/// nobody has restored yet, a restore in progress, one whose bytes (source, lock or packages) have
/// changed since it was trusted, one whose trust record cannot be read, one whose last restore
/// failed, or one whose header refuses the file.
/// </summary>
public enum SingleFileExtensionState
{
    /// <summary>Declared and usable; no lock exists yet, so nothing has been restored or built.</summary>
    NotBuilt,

    /// <summary>A restore is running; <c>/extensions</c> shows progress rather than a stale row.</summary>
    Restoring,

    /// <summary>The file cannot be an extension; <see cref="SingleFileExtension.Reason"/> says why.</summary>
    Invalid,

    /// <summary>Trusted before, and the source, the lock or a declared package changed since.</summary>
    Stale,

    /// <summary>Trust could not be decided or verified; <see cref="SingleFileExtension.Reason"/> says why.</summary>
    TrustFailed,

    /// <summary>A restore failed; <see cref="SingleFileExtension.Reason"/> carries the first reason.</summary>
    RestoreFailed,
}

/// <summary>
/// One discovered single-file extension candidate: the <c>.cs</c> file, its scope, the header it
/// declares, the lock it is restored to (when one answers for these bytes) and the state
/// <c>/extensions</c> shows. <see cref="Digest"/> is the trust identity of the bytes on disk:
/// <c>sha256("lunate-extension-v1" | id | each package as written | source bytes)</c> before the
/// first restore, and <c>sha256("lunate-extension-v2" | id | each package as written | source bytes
/// | the lock's graph bytes)</c> afterwards. The lock's toolset half is not an input, so a .NET
/// patch never changes this digest.
/// </summary>
public sealed record SingleFileExtension
{
    /// <summary>The absolute path of the <c>.cs</c> file.</summary>
    public required string Path { get; init; }

    public required ExtensionScope Scope { get; init; }

    /// <summary>The parsed header; <see cref="ExtensionHeader.None"/> when the file was unreadable.</summary>
    public required ExtensionHeader Header { get; init; }

    public required SingleFileExtensionState State { get; init; }

    /// <summary>The same-line explanation for <see cref="State"/>; null when there is nothing to say.</summary>
    public string? Reason { get; init; }

    /// <summary>The trust digest of the bytes on disk; empty when the file could not be read.</summary>
    public string Digest { get; init; } = string.Empty;

    /// <summary>
    /// The source-level digest — id, declared packages and source bytes, no lock segment. It is
    /// what a lock records and what the first trust covers.
    /// </summary>
    public string SourceDigest { get; init; } = string.Empty;

    /// <summary>
    /// The lock that answers for these exact source bytes, or null when there is none yet (or the
    /// one on disk was resolved from other bytes).
    /// </summary>
    public ExtensionLock? Lock { get; init; }

    /// <summary>
    /// The SHA-256 of the whole lock file's bytes when <see cref="Lock"/> is set: the build cache's
    /// identity for the resolution, so a graph change and a toolset change both make a new key. It
    /// is not the trust digest, which covers the graph half only.
    /// </summary>
    public string? LockHash { get; init; }

    /// <summary>Declarations nothing provided (a warning, shown as <c>declared, not provided</c>).</summary>
    public IReadOnlyList<ExtensionDeclaration> DeclaredNotProvided { get; init; } = [];

    /// <summary>The declared id, or null when the header declares none.</summary>
    public string? Id => Header.Id;

    /// <summary>The declared version, or null when the header declares none.</summary>
    public string? Version => Header.Version;

    /// <summary>The file name, the identity a header without an id is listed by.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}
