using System.Security.Cryptography;
using System.Text;
using Lunate.Extensibility.Abstractions;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// Reads a nupkg as a Lunate package without extracting it (design D2, D12): the nuspec's identity,
/// the <c>lunate.package.json</c> at the root, the extension and skills it declares, and the folder
/// bounds — every refusal naming the package, the entry and what to change. No feed, no download, no
/// dependency walk and no write: this is the reader the installer and the dry run share.
/// </summary>
public static class PackageReader
{
    public const string ManifestFileName = "lunate.package.json";

    public static PackageInfo Read(string nupkgPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nupkgPath);

        string fileName = Path.GetFileName(nupkgPath);
        string fileLabel = $"package file '{fileName}'";
        using PackageArchive archive = PackageArchive.Open(nupkgPath, fileLabel);

        (string id, string version, IReadOnlyList<PackageDependency> dependencies) = Identity(
            archive,
            fileLabel
        );
        string label = FormattableString.Invariant($"package '{id}' {version}");

        string? manifestJson = archive.ReadText(ManifestFileName);
        if (manifestJson is null)
        {
            throw new InvalidDataException(
                $"{label} has no {ManifestFileName} at its root; that file is how a Lunate package "
                    + "declares what it installs (an 'extension' folder, a 'skills' folder or "
                    + "both). Add it to the package."
            );
        }

        PackageManifest manifest = PackageManifest.Parse(manifestJson, ManifestFileName);
        RefuseDependencies(dependencies, label);

        PackageExtension? extension = manifest.Extension is { } extensionFolder
            ? PackageContentReader.ReadExtension(archive, extensionFolder, label)
            : null;
        IReadOnlyList<PackageSkill> skills = manifest.Skills is { } skillsFolder
            ? PackageContentReader.ReadSkills(archive, skillsFolder, label)
            : [];

        return new PackageInfo
        {
            ArchivePath = Path.GetFullPath(nupkgPath),
            Id = id,
            Version = version,
            Sha512 = Sha512(nupkgPath),
            Manifest = manifest,
            Extension = extension,
            Skills = skills,
            IgnoredFolders = PackageContentReader.IgnoredFolders(archive, manifest),
            Files = archive.Files,
            ExpandedBytes = archive.ExpandedBytes,
        };
    }

    /// <summary>
    /// The nuspec's identity and its declared dependencies. Exactly one nuspec at the root is a NuGet
    /// package; none is a zip somebody renamed.
    /// </summary>
    private static (
        string Id,
        string Version,
        IReadOnlyList<PackageDependency> Dependencies
    ) Identity(PackageArchive archive, string label)
    {
        List<string> nuspecs =
        [
            .. archive.Files.Where(file =>
                !file.Contains('/') && file.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
            ),
        ];
        if (nuspecs.Count == 0)
        {
            throw new InvalidDataException(
                $"{label}: the archive has no .nuspec at its root, so it is not a NuGet package. "
                    + "A Lunate package is a published nupkg."
            );
        }

        if (nuspecs.Count > 1)
        {
            throw new InvalidDataException(
                $"{label}: the archive carries more than one nuspec "
                    + $"({string.Join(", ", nuspecs)}); a package carries exactly one, so its "
                    + "identity is unambiguous."
            );
        }

        string? json = archive.ReadText(nuspecs[0]);
        try
        {
            using var nuspecBytes = new MemoryStream(Encoding.UTF8.GetBytes(json!));
            NuspecReader nuspec = new(nuspecBytes);
            var identity = nuspec.GetIdentity();
            List<PackageDependency> dependencies =
            [
                .. nuspec.GetDependencyGroups().SelectMany(group => group.Packages),
            ];
            return (identity.Id, identity.Version.ToNormalizedString(), dependencies);
        }
        catch (Exception exception)
            when (exception is InvalidDataException or ArgumentException or FormatException)
        {
            throw new InvalidDataException(
                $"{label}: the nuspec '{nuspecs[0]}' could not be read: {exception.Message}",
                exception
            );
        }
    }

    /// <summary>
    /// TEMPORARY (design D12): phase 1 refuses a package that declares a NuGet dependency by name,
    /// because the walk that resolves it arrives with phase 4. Phase 4 flips this refusal into the
    /// existing restore walk — the message text goes away, the manifest handling does not.
    /// </summary>
    private static void RefuseDependencies(
        IReadOnlyList<PackageDependency> dependencies,
        string label
    )
    {
        if (dependencies.Count == 0)
        {
            return;
        }

        PackageDependency first = dependencies[0];
        string more =
            dependencies.Count == 1
                ? string.Empty
                : FormattableString.Invariant($" (and {dependencies.Count - 1} more)");
        throw new InvalidDataException(
            FormattableString.Invariant(
                $"{label} declares the NuGet dependency '{first.Id}' {first.VersionRange.ToNormalizedString()}{more}; "
            )
                + "dependency-declaring packages arrive in phase 4, so this version installs "
                + "packages without dependencies. Remove the dependency and republish, or install "
                + "a Lunate version that resolves them."
        );
    }

    /// <summary>The SHA-512 of the nupkg's bytes, base64, streamed so a large package is not read twice.</summary>
    private static string Sha512(string nupkgPath)
    {
        using FileStream stream = File.OpenRead(nupkgPath);
        return Convert.ToBase64String(SHA512.HashData(stream));
    }
}
