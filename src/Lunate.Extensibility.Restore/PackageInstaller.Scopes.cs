using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// Where skills land and what they collide with (design D10, spec: "Scope, precedence and
/// collisions"). A skill directory that already exists where this install would write it is never
/// written into. A same-named skill somewhere else — the other scope, or another project folder in
/// this one — is the existing shadowing rule, so the install proceeds and warns once, naming who
/// wins and that the index keeps one.
/// </summary>
internal sealed partial class PackageInstaller
{
    /// <summary>
    /// The project kinds the skill scanner reads, in its precedence order: the install's own target
    /// and the two folders that shadow or are shadowed by it.
    /// </summary>
    private static readonly string[] ProjectSkillDirectories = ProjectScope.SkillDirectories;

    /// <summary>
    /// Refuses a skill directory that already exists where this install would write it, and warns
    /// once per skill name that the other scope (or another kind in the same scope) already
    /// provides — the existing shadowing rule decides, and the index keeps one winner.
    /// </summary>
    private static List<string> PlaceSkills(
        PackageInfo package,
        PackageInstallTarget target,
        string? worktree,
        PackageInstallRecord? replacing,
        bool replacingUnreadable
    )
    {
        List<string> warnings = [];
        Placement own = TargetPlacement(target);
        foreach (PackageSkill skill in package.Skills)
        {
            string destination = Path.Combine(
                target.ScopeRoot,
                target.SkillsDirectory.Replace('/', Path.DirectorySeparatorChar),
                skill.Name
            );
            if (
                Directory.Exists(destination)
                && !Owns(replacing, target, destination)
                && !(
                    replacingUnreadable
                    && IsInstallPath(target, target.SkillsDirectory, skill.Name, destination)
                )
            )
            {
                throw new InvalidOperationException(
                    $"{Label(package, null, null)}: the skill directory '{destination}' already "
                        + $"exists{Owner(target, destination)}. An install never writes into another "
                        + "skill's directory, so nothing was written — remove it first if this "
                        + "package should provide that skill."
                );
            }

            foreach (Placement other in OtherPlacements(target, worktree, skill.Name))
            {
                if (!Directory.Exists(other.Path))
                {
                    continue;
                }

                bool ownWins =
                    own.ScopeRank < other.ScopeRank
                    || (own.ScopeRank == other.ScopeRank && own.KindRank <= other.KindRank);
                (string winner, string loser) = ownWins
                    ? (own.Name(other), other.Name(own))
                    : (other.Name(own), own.Name(other));
                warnings.Add(
                    ownWins
                        ? $"skill '{skill.Name}' also exists at '{other.Path}'; {winner} wins over "
                            + $"{loser}, so the index keeps one '{skill.Name}'."
                        : $"skill '{skill.Name}' at '{destination}' will be shadowed by "
                            + $"'{other.Path}'; {winner} wins over {loser}, so the index keeps one "
                            + $"'{skill.Name}'."
                );
            }
        }

        return warnings;
    }

    /// <summary>
    /// One place a skill of a given name lives, with the ranks the scanner resolves by. Two
    /// placements in different scopes are named by their scope, two in one scope by their folder —
    /// that is what the warning has to say for the user to know which copy wins.
    /// </summary>
    private sealed record Placement(
        string Path,
        int ScopeRank,
        int KindRank,
        string Scope,
        string Kind
    )
    {
        internal string Name(Placement other) => ScopeRank == other.ScopeRank ? Kind : Scope;
    }

    /// <summary>The target's own scope and kind, with the ranks the scanner resolves by.</summary>
    private static Placement TargetPlacement(PackageInstallTarget target) =>
        target.Scope == PackageScope.Project
            ? new Placement(
                string.Empty,
                0,
                Array.IndexOf(ProjectSkillDirectories, ProjectScope.SkillsDirectory),
                "project",
                ProjectScope.Kind(ProjectScope.SkillsDirectory)
            )
            : new Placement(string.Empty, 1, 0, "user", "user");

    /// <summary>
    /// Every other skill root a same-named skill could come from: the user root and the worktree's
    /// three project kinds for a project install, the worktree's project kinds for a user install.
    /// </summary>
    private static IEnumerable<Placement> OtherPlacements(
        PackageInstallTarget target,
        string? worktree,
        string name
    )
    {
        if (target.Scope != PackageScope.User)
        {
            yield return new Placement(
                Path.Combine(target.StoreRoot, "skills", name),
                1,
                0,
                "user",
                "user"
            );
        }

        if (worktree is not { Length: > 0 } root)
        {
            yield break;
        }

        for (var rank = 0; rank < ProjectSkillDirectories.Length; rank++)
        {
            string directory = ProjectSkillDirectories[rank];
            if (
                target.Scope == PackageScope.Project
                && string.Equals(directory, ProjectScope.SkillsDirectory, StringComparison.Ordinal)
            )
            {
                // The target's own directory; the existence check upstream owns it.
                continue;
            }

            yield return new Placement(
                ProjectScope.Under(root, directory, name),
                0,
                rank,
                "project",
                ProjectScope.Kind(directory)
            );
        }
    }

    /// <summary>
    /// Who owns an existing skill directory, when a record names it: the package and its uninstall,
    /// so the refusal says whose skill this is.
    /// </summary>
    private static string Owner(PackageInstallTarget target, string directory)
    {
        foreach (PackageInstallRecord record in Records(target.StoreRoot))
        {
            foreach (InstalledFile file in record.Content.Paths)
            {
                string path = Path.Combine(
                    ScopeRoot(record, target.StoreRoot),
                    file.Path.Replace('/', Path.DirectorySeparatorChar)
                );
                if (
                    path.StartsWith(
                        Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
                        OperatingSystem.IsLinux()
                            ? StringComparison.Ordinal
                            : StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return $" (it belongs to package '{record.Package.Id}' {record.Package.Version}, "
                        + $"record '{Path.Combine(target.StoreRoot, "packages", record.Package.Id.ToLowerInvariant(), PackageInstallRecordJson.FileName)}' "
                        + $"— run 'lunate uninstall {record.Package.Id}' to remove it)";
                }
            }
        }

        return string.Empty;
    }

    /// <summary>The directory a record's relative paths resolve against.</summary>
    private static string ScopeRoot(PackageInstallRecord record, string storeRoot) =>
        record.Scope == PackageScope.Project && record.Worktree is { Length: > 0 } worktree
            ? worktree
            : storeRoot;

    /// <summary>
    /// Whether <paramref name="directory"/> is filled by the record's own content: the install an
    /// upgrade replaces, whose extension folder and skill directories are not a collision with
    /// themselves.
    /// </summary>
    private static bool Owns(
        PackageInstallRecord? record,
        PackageInstallTarget target,
        string directory
    )
    {
        if (record is null)
        {
            return false;
        }

        string root = ScopeRoot(record, target.StoreRoot);
        string prefix = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return record.Content.Paths.Any(file =>
            Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar))
                .StartsWith(prefix, comparison)
        );
    }

    /// <summary>
    /// Whether <paramref name="directory"/> is exactly where this install writes the item called
    /// <paramref name="name"/> under <paramref name="holder"/> — the extension folder or one skill
    /// folder. With a record that does not answer for itself these are the only paths a reinstall
    /// may take back, because they are the ones it is about to write.
    /// </summary>
    private static bool IsInstallPath(
        PackageInstallTarget target,
        string holder,
        string name,
        string directory
    ) =>
        SamePath(
            Path.Combine(target.ScopeRoot, holder.Replace('/', Path.DirectorySeparatorChar), name),
            directory
        );

    /// <summary>Two paths naming one directory, under the platform's case rule.</summary>
    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsLinux()
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase
        );

    /// <summary>Every record under the state root; a record that does not answer for itself is skipped.</summary>
    private static IEnumerable<PackageInstallRecord> Records(string storeRoot)
    {
        string packages = Path.Combine(storeRoot, "packages");
        if (!Directory.Exists(packages))
        {
            yield break;
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
                continue;
            }

            PackageInstallRecord? record = PackageInstallRecordJson.TryRead(path, out _);
            if (record is not null)
            {
                yield return record;
            }
        }
    }

    private static string Label(PackageInfo package, string? extensionVersion, string? extensionId)
    {
        string label = FormattableString.Invariant($"package '{package.Id}' {package.Version}");
        return extensionId is null || extensionVersion is null
            ? label
            : label
                + FormattableString.Invariant($": extension '{extensionId}' {extensionVersion}");
    }
}
