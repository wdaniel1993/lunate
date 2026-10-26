using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// One extension id, one provider (design D10, spec: "Scope, precedence and collisions"). Before a
/// package's content is written, the id it would install is checked against every other install on
/// the machine — either scope, any worktree — and against the hand-placed and single-file
/// extensions a session in this worktree would discover, so the duplicate the next session start
/// would report becomes a refusal at the moment the user can act on it.
/// </summary>
internal sealed partial class PackageInstaller
{
    /// <summary>
    /// Refuses an install whose extension id already has a provider (design D10). The set is the
    /// records under the state root, the two extensions roots and the <c>*.cs</c> candidates in
    /// them — what a session would discover, plus the project installs of other worktrees. The
    /// directory an upgrade is replacing is its own provider, so the old record's paths are not a
    /// collision.
    /// </summary>
    private static void RequireExtensionIdFree(
        PackageInfo package,
        PackageInstallTarget target,
        string? worktree,
        PackageInstallRecord? replacing,
        bool replacingUnreadable
    )
    {
        if (package.Extension is not { } extension)
        {
            return;
        }

        string id = extension.Manifest.Id;
        string label = Label(package, extension.Manifest.Version, id);

        foreach (PackageInstallRecord record in Records(target.StoreRoot))
        {
            if (
                string.Equals(record.Content.ExtensionId, id, StringComparison.Ordinal)
                && !string.Equals(record.Package.Id, package.Id, StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new InvalidOperationException(
                    $"{label}: extension '{id}' is already provided by package "
                        + $"'{record.Package.Id}' {record.Package.Version} (record "
                        + $"'{Path.Combine(target.StoreRoot, "packages", record.Package.Id.ToLowerInvariant(), PackageInstallRecordJson.FileName)}'). "
                        + $"One extension id has one provider, so nothing was written — run "
                        + $"'lunate uninstall {record.Package.Id}' first, or install a package that "
                        + "declares a different extension id."
                );
            }
        }

        foreach ((string directory, string scope) in ExtensionRoots(target, worktree))
        {
            string handPlaced = Path.Combine(directory, id);
            if (
                File.Exists(Path.Combine(handPlaced, "extension.json"))
                && !Owns(replacing, target, handPlaced)
                && !(
                    replacingUnreadable
                    && IsInstallPath(target, target.ExtensionDirectory, id, handPlaced)
                )
            )
            {
                throw new InvalidOperationException(
                    $"{label}: extension '{id}' is already provided by the {scope} extension "
                        + $"directory '{handPlaced}'. One extension id has one provider, so nothing "
                        + "was written — remove that directory first, or install a package that "
                        + "declares a different extension id."
                );
            }
        }

        foreach ((string path, string scope) in SingleFileCandidates(target, worktree, id))
        {
            throw new InvalidOperationException(
                $"{label}: extension '{id}' is already provided by the {scope} single-file "
                    + $"extension '{path}'. One extension id has one provider, so nothing was "
                    + "written — remove that file first, or install a package that declares a "
                    + "different extension id."
            );
        }
    }

    /// <summary>The two extensions roots a session in this worktree scans, with their scope labels.</summary>
    private static IEnumerable<(string Directory, string Scope)> ExtensionRoots(
        PackageInstallTarget target,
        string? worktree
    )
    {
        yield return (Path.Combine(target.StoreRoot, "extensions"), "user");
        if (worktree is { Length: > 0 })
        {
            yield return (
                ProjectScope.Under(worktree, ProjectScope.ExtensionsDirectory),
                "project"
            );
        }
    }

    /// <summary>
    /// The <c>*.cs</c> candidates either root holds whose header declares <paramref name="id"/> —
    /// read by the same reader discovery uses, because a file's name need not be its id.
    /// </summary>
    private static IEnumerable<(string Path, string Scope)> SingleFileCandidates(
        PackageInstallTarget target,
        string? worktree,
        string id
    )
    {
        var options = new ExtensionHostOptions { StorePath = target.StoreRoot };
        var reader = new SingleFileExtensionReader(options);
        foreach ((string root, string scope) in ExtensionRoots(target, worktree))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<SingleFileExtension> candidates = reader.Read(
                root,
                scope == "project" ? ExtensionScope.Project : ExtensionScope.Global,
                []
            );
            foreach (SingleFileExtension candidate in candidates)
            {
                if (
                    candidate.Header.IsValid
                    && string.Equals(candidate.Id, id, StringComparison.Ordinal)
                )
                {
                    yield return (candidate.Path, scope);
                }
            }
        }
    }
}
