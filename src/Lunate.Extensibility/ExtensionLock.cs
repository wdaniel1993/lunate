namespace Lunate.Extensibility;

/// <summary>
/// One resolved package in <c>extension.lock.json</c>: the exact version, the nupkg's SHA-512 and
/// the file list that hash covers, the assets the compile will use, whether the package ships
/// analyzers or source generators, and the dependency edges the resolution walked. Everything is
/// package-relative with forward slashes, so the lock does not depend on the machine it was
/// written on.
/// </summary>
public sealed record ExtensionLockPackage
{
    public required string Id { get; init; }

    public required string Version { get; init; }

    /// <summary>Base64 SHA-512 of the nupkg, the same value NuGet records for a restored package.</summary>
    public required string Sha512 { get; init; }

    /// <summary>The declaration or dependency range this package was resolved from.</summary>
    public required string Requested { get; init; }

    /// <summary>True when a <c>#:package</c> directive asked for it; false for a transitive dependency.</summary>
    public bool Direct { get; init; }

    public IReadOnlyList<ExtensionLockDependency> Dependencies { get; init; } = [];

    /// <summary>Every file in the package, sorted; the list the SHA-512 covers.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    public IReadOnlyList<string> Compile { get; init; } = [];

    public IReadOnlyList<string> Runtime { get; init; } = [];

    public IReadOnlyList<string> Native { get; init; } = [];

    /// <summary>Analyzer and source-generator assemblies; recorded whether or not they run.</summary>
    public IReadOnlyList<string> Analyzers { get; init; } = [];

    /// <summary>True when the package ships build-time code, so the file's opt-in decides.</summary>
    public bool Generators { get; init; }
}

/// <summary>One dependency edge of a locked package: the id and the range the package declared.</summary>
public sealed record ExtensionLockDependency(string Id, string Range);

/// <summary>
/// The reference pack a compile resolves against: its exact version, how it was chosen and the
/// hash of the nupkg it came from. The hash is what makes the toolset half answerable for itself:
/// a pack at the same version with different bits is a different toolset.
/// </summary>
public sealed record ExtensionLockReferencePack
{
    public required string Id { get; init; }

    public required string Version { get; init; }

    /// <summary><see cref="ReferencePackRules.Exact"/> or <see cref="ReferencePackRules.NearestAtLeast"/>.</summary>
    public required string Rule { get; init; }

    /// <summary>
    /// Base64 SHA-512 of the pack's nupkg — the exact bytes its reference assemblies came from —
    /// when the packages folder still holds it (or NuGet's <c>.nupkg.sha512</c> sidecar). Null for a
    /// pack folder that kept only the assemblies: the version still names the toolset, and the block
    /// still answers for itself through its integrity hash.
    /// </summary>
    public string? Sha512 { get; init; }

    public IReadOnlyList<string> Assets { get; init; } = [];
}

/// <summary>
/// The toolset a lock was resolved for: the framework, RID and exact runtime, and the reference
/// pack with its hash. This half is machine-local. A .NET patch moves the runtime and the pack
/// without changing what the user approved, so it is integrity-checked and it keys the build cache,
/// but it is <b>not</b> part of the trust digest and a change to it rebuilds without a question.
/// </summary>
public sealed record ExtensionLockToolset
{
    public required string Tfm { get; init; }

    public required string Rid { get; init; }

    public required string Runtime { get; init; }

    public required ExtensionLockReferencePack ReferencePack { get; init; }
}

/// <summary>The two rules a reference pack can be chosen by, recorded in the lock verbatim.</summary>
public static class ReferencePackRules
{
    /// <summary>The pack whose version equals the running runtime exactly.</summary>
    public const string Exact = "exact";

    /// <summary>The lowest published 10.0.x pack at or above the running runtime.</summary>
    public const string NearestAtLeast = "nearest-at-least";

    /// <summary>
    /// The highest published 10.0.x pack below the running runtime, chosen only when the runtime's
    /// own pack is not published at all (a staged patch the feed does not serve yet).
    /// </summary>
    public const string NearestBelow = "nearest-below-runtime";

    /// <summary>
    /// No pack was resolved because nothing compiles: a package install writes a lock for its
    /// dependency graph and publishes output, so the toolset half records the runtime the graph was
    /// resolved for and says plainly that no reference pack was needed. Downloading a pack nobody
    /// will compile against would make an install depend on a feed the closure does not.
    /// </summary>
    public const string NotCompiled = "not-compiled";
}

/// <summary>
/// <c>extension.lock.json</c>: the resolution an extension was restored or installed to, in two
/// halves. A single-file extension's compile resolves it; a package's graph is resolved by the same
/// walk at install time, so both write this one file, in the same cache entry. The <b>graph</b> —
/// <see cref="Sources"/> and <see cref="Packages"/> — is what a trust decision covers: it records the
/// exact versions, hashes and assets, so a later restore is a file read rather than a feed query, and
/// so a change to it is a change to what the user approved. The <b>toolset</b> is the machine's half
/// (framework, RID, runtime, reference pack and its hash): it is integrity-checked, it keys the build
/// cache, and it is deliberately outside the trust digest, because a .NET patch must never ask the
/// user about bytes they already approved. A package install writes the same file and resolves no
/// reference pack, because it compiles nothing. The file lives in the extension's cache entry, never
/// beside the user's source: a single file stays one file.
/// </summary>
public sealed record ExtensionLock
{
    /// <summary>The lock layout this version writes and reads; another value is refused, not guessed at.</summary>
    public const int CurrentVersion = 2;

    public const string FileName = "extension.lock.json";

    public required string ExtensionId { get; init; }

    public required string ExtensionVersion { get; init; }

    /// <summary>The source-level digest this resolution was computed from.</summary>
    public required string SourceDigest { get; init; }

    /// <summary>Every <c>#:package</c> argument exactly as the header wrote it, in header order.</summary>
    public required IReadOnlyList<string> PackagesWritten { get; init; }

    public required ExtensionLockToolset Toolset { get; init; }

    /// <summary>The feeds the resolution used, by source name, sorted. Part of the graph.</summary>
    public required IReadOnlyList<string> Sources { get; init; }

    /// <summary>The resolved packages, sorted by id then version. Part of the graph.</summary>
    public required IReadOnlyList<ExtensionLockPackage> Packages { get; init; }

    /// <summary>
    /// True when this lock answers for exactly these source bytes: the digest it was resolved
    /// from is the digest of the file as it is now. A lock that says otherwise belongs to an
    /// earlier version of the file and is not reused.
    /// </summary>
    public bool IsCurrentFor(string sourceDigest) =>
        string.Equals(SourceDigest, sourceDigest, StringComparison.Ordinal);

    /// <summary>True when this lock was resolved for the same toolset (TFM, RID, runtime, pack).</summary>
    public bool IsCurrentFor(ExtensionLockToolset toolset)
    {
        ArgumentNullException.ThrowIfNull(toolset);

        return string.Equals(Toolset.Tfm, toolset.Tfm, StringComparison.Ordinal)
            && string.Equals(Toolset.Rid, toolset.Rid, StringComparison.Ordinal)
            && string.Equals(Toolset.Runtime, toolset.Runtime, StringComparison.Ordinal);
    }
}
