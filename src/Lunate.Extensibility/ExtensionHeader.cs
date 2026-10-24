namespace Lunate.Extensibility;

/// <summary>
/// One header diagnostic: the 1-based line it was read on and what is wrong. The message never
/// repeats the line number; <see cref="ToString"/> renders <c>line N: …</c>, so every surface that
/// shows a header problem names the line the author has to open.
/// </summary>
public sealed record ExtensionHeaderIssue(int Line, string Message)
{
    public override string ToString() => FormattableString.Invariant($"line {Line}: {Message}");
}

/// <summary>
/// One <c>#:package</c> declaration: the id, the version or range and the argument exactly as the
/// author wrote it. <see cref="Written"/> is what the trust digest hashes — a range written with a
/// space (<c>[1.0.0, 2.0.0)</c>) must digest differently from one without it, because the file's
/// bytes differ and the user approved those bytes.
/// </summary>
public sealed record ExtensionPackageReference(string Id, string VersionRange, string Written);

/// <summary>
/// A single-file extension's declaration header, parsed before anything is restored or compiled.
/// <see cref="Errors"/> refusing the file are the only reason a header cannot be used;
/// <see cref="Warnings"/> records what was ignored. The declaration lists mirror
/// <see cref="Abstractions.ExtensionManifest"/>'s one to one through
/// <see cref="ExtensionDirectives"/>.
/// </summary>
public sealed record ExtensionHeader
{
    /// <summary>The declared id; null when the header declares none or the declaration was refused.</summary>
    public string? Id { get; init; }

    /// <summary>The declared exact version; null when absent or refused.</summary>
    public string? Version { get; init; }

    public IReadOnlyList<string> Tools { get; init; } = [];

    public IReadOnlyList<string> Commands { get; init; } = [];

    public IReadOnlyList<string> Hooks { get; init; } = [];

    public IReadOnlyList<string> Services { get; init; } = [];

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>The declared model-provider ids (the manifest's <c>modelProviders[].id</c>).</summary>
    public IReadOnlyList<string> Providers { get; init; } = [];

    public IReadOnlyList<ExtensionPackageReference> Packages { get; init; } = [];

    /// <summary>True when the header carries <c>#:generators on</c>.</summary>
    public bool Generators { get; init; }

    /// <summary>Problems that refuse the file; empty means the header is usable.</summary>
    public IReadOnlyList<ExtensionHeaderIssue> Errors { get; init; } = [];

    /// <summary>Ignored directives and other advisories; they never refuse the file.</summary>
    public IReadOnlyList<ExtensionHeaderIssue> Warnings { get; init; } = [];

    /// <summary>A header with nothing declared and nothing wrong (an empty file).</summary>
    public static ExtensionHeader None { get; } = new();

    /// <summary>True when the header declares an id and nothing refuses it.</summary>
    public bool IsValid => Id is not null && Errors.Count == 0;
}
