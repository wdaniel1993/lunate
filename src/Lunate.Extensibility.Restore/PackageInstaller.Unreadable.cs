using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// An install over a record that does not answer for itself (spec: "The install record"). The
/// addendum makes a reinstall the repair: an unreadable record does not refuse the install, the
/// content and the record are written as a replacement, and the run then names the bytes it could
/// not account for — because the only list of them was the record it could not read. Nothing here
/// claims a removal that did not happen.
/// </summary>
internal sealed partial class PackageInstaller
{
    /// <summary>
    /// What this run can say after replacing content whose previous record was unreadable: the
    /// reason, the files under the content roots that the new record does not name, and the fact
    /// that a previous version's skill directories cannot be known at all. Sorted, so the output is
    /// stable.
    /// </summary>
    private static IReadOnlyList<string> Unaccounted(
        string problem,
        PackageInstallTarget target,
        PackageInfo package,
        IReadOnlyList<PackageContentStager.Staged> staged
    )
    {
        List<string> notes =
        [
            FormattableString.Invariant(
                $"{problem} This run replaced the content and wrote a new record; bytes a previous version wrote are left where they are rather than removed by name."
            ),
        ];

        HashSet<string> written = new(PathComparer);
        foreach (PackageContentStager.Staged file in staged)
        {
            written.Add(FullyQualified(target.ScopeRoot, file.RelativePath));
        }

        foreach (string root in ContentRoots(package, target))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (
                string path in Directory
                    .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
            )
            {
                if (!written.Contains(Path.GetFullPath(path)))
                {
                    notes.Add(
                        FormattableString.Invariant(
                            $"left in place: '{path}' — no readable record named it, so nothing removed it."
                        )
                    );
                }
            }
        }

        if (package.Skills.Count > 0)
        {
            notes.Add(
                FormattableString.Invariant(
                    $"a previous version may have written skill directories this record cannot name; '{Path.Combine(target.ScopeRoot, target.SkillsDirectory.Replace('/', Path.DirectorySeparatorChar))}' was not swept."
                )
            );
        }

        return notes;
    }

    /// <summary>The folders this package's content lands in, at this target's scope.</summary>
    private static IEnumerable<string> ContentRoots(
        PackageInfo package,
        PackageInstallTarget target
    )
    {
        if (package.Extension is { } extension)
        {
            yield return Path.Combine(
                target.ScopeRoot,
                target.ExtensionDirectory.Replace('/', Path.DirectorySeparatorChar),
                extension.Manifest.Id
            );
        }

        foreach (PackageSkill skill in package.Skills)
        {
            yield return Path.Combine(
                target.ScopeRoot,
                target.SkillsDirectory.Replace('/', Path.DirectorySeparatorChar),
                skill.Name
            );
        }
    }

    /// <summary>
    /// The scope's one size warning for the record's content, or nothing (spec: "Install output and
    /// <c>--dry-run</c>"): a project install above the threshold says what it costs and what the
    /// largest entries are.
    /// </summary>
    private static List<string> SizeWarnings(
        PackageScope scope,
        IReadOnlyList<InstalledFile> files
    ) => PackageContentSize.Warning(scope, files) is { } warning ? [warning] : [];

    private static string FullyQualified(string scopeRoot, string relative) =>
        Path.GetFullPath(
            Path.Combine(scopeRoot, relative.Replace('/', Path.DirectorySeparatorChar))
        );

    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
}
