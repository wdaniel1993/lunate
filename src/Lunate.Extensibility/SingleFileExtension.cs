namespace Lunate.Extensibility;

/// <summary>
/// What <c>/extensions</c> reports for one discovered single-file extension in this phase: a valid
/// declaration nobody has built yet, one that was trusted and whose bytes have changed since, one
/// whose trust record cannot be read, or one whose header refuses the file.
/// </summary>
public enum SingleFileExtensionState
{
    /// <summary>Declared and usable; no build exists yet (nothing compiles a file in this phase).</summary>
    NotBuilt,

    /// <summary>The file cannot be an extension; <see cref="SingleFileExtension.Reason"/> says why.</summary>
    Invalid,

    /// <summary>Trusted before, and the id, a declared package or the source bytes changed since.</summary>
    Stale,

    /// <summary>Trust could not be decided or verified; <see cref="SingleFileExtension.Reason"/> says why.</summary>
    TrustFailed,
}

/// <summary>
/// One discovered single-file extension candidate: the <c>.cs</c> file, its scope, the header it
/// declares and the state <c>/extensions</c> shows. The digest is the first-trust form —
/// <c>sha256("lunate-extension-v1" | id | each declared package as written | source bytes)</c> —
/// computed before anything is restored or compiled and stored with the trust decision.
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

    /// <summary>The first-trust digest; empty when the file could not be read.</summary>
    public string Digest { get; init; } = string.Empty;

    /// <summary>Declarations nothing provided (a warning, shown as <c>declared, not provided</c>).</summary>
    public IReadOnlyList<ExtensionDeclaration> DeclaredNotProvided { get; init; } = [];

    /// <summary>The declared id, or null when the header declares none.</summary>
    public string? Id => Header.Id;

    /// <summary>The declared version, or null when the header declares none.</summary>
    public string? Version => Header.Version;

    /// <summary>The file name, the identity a header without an id is listed by.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}
