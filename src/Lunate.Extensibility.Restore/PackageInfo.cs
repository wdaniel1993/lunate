using Lunate.Extensibility.Abstractions;
using NuGet.Packaging;

namespace Lunate.Extensibility.Restore;

/// <summary>One packaged extension: the folder it lives in and the manifest the existing parser read.</summary>
internal sealed record PackageExtension(string Directory, ExtensionManifest Manifest);

/// <summary>One packaged skill: its folder, the name its frontmatter declares and its description.</summary>
internal sealed record PackageSkill(string Directory, string Name, string Description);

/// <summary>
/// A package read without extracting anything (phase 1): its identity from the nuspec, the manifest,
/// the content the manifest declares, the folders the package carries that no kind names, and the
/// archive facts a dry run prints. Writing files is not this type's job — the installer that lands
/// in a later phase writes exactly what this describes.
/// </summary>
internal sealed record PackageInfo
{
    /// <summary>The nupkg the facts came from.</summary>
    public required string ArchivePath { get; init; }

    /// <summary>The package id in its canonical spelling, from the nuspec.</summary>
    public required string Id { get; init; }

    /// <summary>The exact version, from the nuspec.</summary>
    public required string Version { get; init; }

    /// <summary>The SHA-512 of the nupkg's bytes, base64 — transport integrity, not a signature.</summary>
    public required string Sha512 { get; init; }

    public required PackageManifest Manifest { get; init; }

    /// <summary>The packaged extension, or null for a skills-only package.</summary>
    public PackageExtension? Extension { get; init; }

    public IReadOnlyList<PackageSkill> Skills { get; init; } = [];

    /// <summary>Top-level folders that are neither NuGet metadata nor a declared kind, sorted.</summary>
    public IReadOnlyList<string> IgnoredFolders { get; init; } = [];

    /// <summary>
    /// The NuGet dependencies the nuspec declares, across every group, sorted by id: what the walk
    /// resolves (design D3). The group that applies to a framework is chosen when the walk runs, not
    /// here, because the reader has no target framework.
    /// </summary>
    public IReadOnlyList<ExtensionLockDependency> DeclaredDependencies { get; init; } = [];

    /// <summary>
    /// The nuspec's dependency groups as NuGet read them, for the walk's nearest-framework choice.
    /// Internal because the group type is NuGet's, and this assembly is the only place that may know
    /// it: a caller outside gets <see cref="DeclaredDependencies"/>.
    /// </summary>
    internal IReadOnlyList<PackageDependencyGroup> DependencyGroups { get; init; } = [];

    /// <summary>Every file entry in the archive, package-relative and sorted.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>The archive's expanded size in bytes, from the zip's own directory.</summary>
    public long ExpandedBytes { get; init; }
}
