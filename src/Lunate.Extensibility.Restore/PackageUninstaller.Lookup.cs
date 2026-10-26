using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// Which record an uninstall acts on (spec: "Uninstall"): every record under the state root, matched
/// by package id or by the extension id it provides. One match is the answer; none names what is
/// installed, and more than one names the candidates — an extension id must never pick a package by
/// accident of ordering.
/// </summary>
public sealed partial class PackageUninstaller
{
    /// <summary>
    /// The one record the id names. A record that does not answer for itself is named rather than
    /// believed — unless <c>--force</c> asked for it, where its paths are still confined and the
    /// user has said they accept the record's content.
    /// </summary>
    private static Found Resolve(string store, string id, bool force)
    {
        List<Found> installed = [];
        List<(string Path, string Problem)> broken = [];
        Read(store, force, installed, broken);

        List<Found> matches = [.. installed.Where(entry => Names(entry.Record, id))];
        if (matches.Count == 0)
        {
            RefuseNoMatch(store, id, broken);
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"'{id}' names an extension that {matches.Count} installed packages provide, and an "
                    + "uninstall takes one: "
                    + string.Join(
                        "; ",
                        matches.Select(entry =>
                            FormattableString.Invariant(
                                $"package '{entry.Record.Package.Id}' {entry.Record.Package.Version} (record '{entry.Path}')"
                            )
                        )
                    )
                    + ". Name the package id instead of the extension id."
            );
        }

        return matches[0];
    }

    /// <summary>Every record under <c>&lt;root&gt;/packages/</c>, readable ones and broken ones apart.</summary>
    private static void Read(
        string store,
        bool force,
        List<Found> installed,
        List<(string Path, string Problem)> broken
    )
    {
        string packages = Path.Combine(store, "packages");
        if (!Directory.Exists(packages))
        {
            return;
        }

        foreach (
            string directory in Directory
                .EnumerateDirectories(packages)
                .Order(StringComparer.Ordinal)
        )
        {
            string path = Path.Combine(directory, PackageInstallRecordJson.FileName);
            if (!File.Exists(path))
            {
                // A directory without a record names nothing, so there is nothing to delete.
                continue;
            }

            PackageInstallRecord? record = force
                ? PackageInstallRecordJson.TryReadForForce(path, out string? problem)
                : PackageInstallRecordJson.TryRead(path, out problem);
            if (record is not null)
            {
                // A record read for --force can carry the reason its integrity hash disagreed; that
                // is the one case where an uninstall says it needed --force for the record itself.
                installed.Add(new Found(record, path, problem is not null));
                continue;
            }

            broken.Add((path, problem ?? "the record could not be read"));
        }
    }

    /// <summary>No record names the id; a record that names it but cannot be read still gets named.</summary>
    private static void RefuseNoMatch(
        string store,
        string id,
        List<(string Path, string Problem)> broken
    )
    {
        foreach ((string path, string problem) in broken)
        {
            string directory = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
            if (string.Equals(directory, id.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"{problem} — nothing acts on it, so '{id}' was not uninstalled."
                );
            }
        }

        throw new InvalidOperationException(
            FormattableString.Invariant($"no installed package matches '{id}' under '{store}'; ")
                + "'lunate packages' lists what is installed, by package id and extension id."
        );
    }

    /// <summary>Whether a record's package id or extension id is the argument.</summary>
    private static bool Names(PackageInstallRecord record, string id) =>
        string.Equals(record.Package.Id, id, StringComparison.OrdinalIgnoreCase)
        || string.Equals(record.Content.ExtensionId, id, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The directories the install created for its content: the extension's folder and each skill's.
    /// Only these are candidates for removal; the roots that hold them are shared with everything
    /// else installed.
    /// </summary>
    private static IEnumerable<string> ContentRoots(PackageInstallRecord record, string scopeRoot)
    {
        bool project = record.Scope == PackageScope.Project;
        if (record.Content.ExtensionId is { Length: > 0 } extensionId)
        {
            yield return project
                ? ProjectScope.Under(scopeRoot, ProjectScope.ExtensionsDirectory, extensionId)
                : Path.Combine(scopeRoot, "extensions", extensionId);
        }

        foreach (string skill in record.Content.Skills)
        {
            yield return project
                ? ProjectScope.Under(scopeRoot, ProjectScope.SkillsDirectory, skill)
                : Path.Combine(scopeRoot, "skills", skill);
        }
    }

    /// <summary>The directory a record's relative paths resolve against.</summary>
    private static string ScopeRoot(PackageInstallRecord record, string store) =>
        record.Scope == PackageScope.Project && record.Worktree is { Length: > 0 } worktree
            ? worktree
            : store;

    /// <summary>How deep a directory sits, for removing the deepest empty directory first.</summary>
    private static int Depth(string directory) =>
        directory.Count(character => character == Path.DirectorySeparatorChar);

    private sealed record Found(PackageInstallRecord Record, string Path, bool Modified);
}
