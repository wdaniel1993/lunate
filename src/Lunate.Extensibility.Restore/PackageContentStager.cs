using System.Security.Cryptography;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// Extracts a package's declared content into staging (design D9) before anything is committed: the
/// extension folder becomes <c>&lt;extension root&gt;/&lt;extension-id&gt;/</c>, each skill folder
/// becomes <c>&lt;skills root&gt;/&lt;name&gt;/</c>, and nothing outside the declared kinds is
/// written. Every staged file is hashed here, so the record is written from what is on disk rather
/// than from what the archive claimed.
/// </summary>
internal static class PackageContentStager
{
    internal sealed record Staged(string RelativePath, string Path, long Size, string Sha256);

    internal static IReadOnlyList<Staged> Stage(
        PackageInfo package,
        PackageInstallTarget target,
        string stagingDirectory
    )
    {
        string label = FormattableString.Invariant($"package '{package.Id}' {package.Version}");
        using PackageArchive archive = PackageArchive.Open(package.ArchivePath, label);

        List<(string Folder, string Destination)> roots = [];
        if (package.Extension is { } extension)
        {
            roots.Add(
                (extension.Directory, $"{target.ExtensionDirectory}/{extension.Manifest.Id}")
            );
        }

        foreach (PackageSkill skill in package.Skills)
        {
            roots.Add((skill.Directory, $"{target.SkillsDirectory}/{skill.Name}"));
        }

        List<Staged> staged = [];
        HashSet<string> written = new(StringComparer.Ordinal);
        foreach ((string folder, string destination) in roots)
        {
            foreach (string entry in archive.FilesUnder(folder))
            {
                string relative = entry[(folder.Length + 1)..];
                if (
                    PackageInstallRecordJson.TryConfine(
                        $"{destination}/{relative}",
                        out string targetRelative,
                        out string? reason
                    )
                    is false
                )
                {
                    throw new InvalidDataException(
                        $"{label}: the content path '{destination}/{relative}' would not stay inside "
                            + $"the install root ({reason}); republish the package."
                    );
                }

                if (!written.Add(targetRelative))
                {
                    throw new InvalidDataException(
                        $"{label}: two archive entries would be written to '{targetRelative}', so "
                            + "which bytes land there is ambiguous; republish the package."
                    );
                }

                string path = System.IO.Path.Combine(
                    stagingDirectory,
                    targetRelative.Replace('/', System.IO.Path.DirectorySeparatorChar)
                );
                long size = archive.CopyTo(entry, path);
                staged.Add(new Staged(targetRelative, path, size, Sha256(path)));
            }
        }

        staged.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return staged;
    }

    private static string Sha256(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
