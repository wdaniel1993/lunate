using System.Text.RegularExpressions;

namespace Lunate.Coding;

/// <summary>
/// Discovery and validation of portable skills: project directories from the worktree root upward
/// to the repository root (never above it), the user scope and extension-contributed directories,
/// each directory holding a <c>SKILL.md</c>. Resolved collisions warn once and keep one winner
/// (project &gt; user &gt; extension; within the project scope nearest level first, then
/// <c>.lunate</c> &gt; <c>.agents</c> &gt; <c>.claude</c>); malformed skills warn and never load;
/// discovery is bounded at 256 scanned skills per scope and unreadable directories warn, never fail.
/// </summary>
internal static partial class SkillScanner
{
    internal const int MaxSkillsPerScope = 256;
    internal const string SkillFileName = "SKILL.md";

    private static readonly string[] ProjectKinds = [".lunate", ".agents", ".claude"];

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SkillNamePattern();

    internal static SkillScanResult Scan(SkillScanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var warnings = new List<string>();
        List<SkillDescriptor> candidates = [];
        List<SkillDescriptor> invalid = [];
        var readRoots = new List<string>();
        var readSeen = new HashSet<string>(PathComparer());
        var scan = new ScanState(candidates, invalid, warnings, readRoots, readSeen);

        scan.Roots(
            ProjectRoots(request.WorktreeRoot, request.RepoRoot),
            SkillScope.Project,
            extensionId: null,
            projectBoundary: request.RepoRoot ?? request.WorktreeRoot
        );

        scan.Roots(
            [
                (Path.Combine(request.HomeDirectory, ".lunate", "skills"), 0, 0),
                (Path.Combine(request.HomeDirectory, ".agents", "skills"), 0, 1),
            ],
            SkillScope.User,
            extensionId: null,
            projectBoundary: null
        );

        foreach (ExtensionSkillSource source in request.ExtensionSources)
        {
            scan.ExtensionSource(source);
        }

        List<SkillDescriptor> skills = Resolve(candidates, warnings);
        skills.AddRange(invalid);
        return new SkillScanResult(skills, warnings, readRoots);
    }

    /// <summary>
    /// The project-scope discovery alone (the trust inventory's needs): the same walk and
    /// validation, without user or extension roots.
    /// </summary>
    internal static SkillScanResult ScanProject(SkillScanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var warnings = new List<string>();
        List<SkillDescriptor> candidates = [];
        List<SkillDescriptor> invalid = [];
        var scan = new ScanState(candidates, invalid, warnings, [], []);
        scan.Roots(
            ProjectRoots(request.WorktreeRoot, request.RepoRoot),
            SkillScope.Project,
            extensionId: null,
            projectBoundary: request.RepoRoot ?? request.WorktreeRoot
        );
        List<SkillDescriptor> skills = Resolve(candidates, warnings);
        skills.AddRange(invalid);
        return new SkillScanResult(skills, warnings, []);
    }

    /// <summary>
    /// The project skill roots in ranking order: the worktree root first, then each ancestor up to
    /// (and including) the repository root — never above it. Within one level the earlier kind wins.
    /// </summary>
    private static List<(string Root, int Depth, int Rank)> ProjectRoots(
        string worktreeRoot,
        string? repoRoot
    )
    {
        string stop = Workspace.Canonicalize(repoRoot ?? worktreeRoot);
        string current = Workspace.Canonicalize(worktreeRoot);
        List<(string Root, int Depth, int Rank)> roots = [];
        var depth = 0;
        while (true)
        {
            for (var rank = 0; rank < ProjectKinds.Length; rank++)
            {
                roots.Add((Path.Combine(current, ProjectKinds[rank], "skills"), depth, rank));
            }

            if (PathsEqual(current, stop))
            {
                break;
            }

            string? parent = Path.GetDirectoryName(current);
            if (parent is null || PathsEqual(parent, current))
            {
                break;
            }

            current = parent;
            depth++;
        }

        return roots;
    }

    /// <summary>
    /// Resolves name collisions: one winner per name by rank (project &gt; user &gt; extension,
    /// nearest project level first, then <c>.lunate</c> &gt; <c>.agents</c> &gt; <c>.claude</c>),
    /// one warning per shadowed candidate and the shadowed scope recorded on the winner.
    /// </summary>
    private static List<SkillDescriptor> Resolve(
        List<SkillDescriptor> candidates,
        List<string> warnings
    )
    {
        List<SkillDescriptor> winners = [];
        foreach (
            IGrouping<string, SkillDescriptor> group in candidates.GroupBy(
                entry => entry.Name,
                StringComparer.Ordinal
            )
        )
        {
            List<SkillDescriptor> ranked = [.. group.OrderBy(entry => entry, RankOrder.Instance)];
            SkillDescriptor winner = ranked[0] with
            {
                Shadows = ranked.Count > 1 ? ranked[1].ScopeLabel : null,
            };
            winners.Add(winner);
            foreach (SkillDescriptor loser in ranked.Skip(1))
            {
                warnings.Add(
                    $"skills: skill '{winner.Name}' at '{winner.Directory}' shadows '{loser.Directory}'"
                );
            }
        }

        winners.Sort(SkillDescriptor.IndexOrderComparer);
        return winners;
    }

    /// <summary>The scan bookkeeping shared by the project, user and extension roots.</summary>
    private sealed class ScanState(
        List<SkillDescriptor> candidates,
        List<SkillDescriptor> invalid,
        List<string> warnings,
        List<string> readRoots,
        HashSet<string> readSeen
    )
    {
        internal void Roots(
            IReadOnlyList<(string Root, int Depth, int Rank)> roots,
            SkillScope scope,
            string? extensionId,
            string? projectBoundary
        )
        {
            var scanned = 0;
            var truncated = false;
            foreach ((string root, int depth, int rank) in roots)
            {
                IReadOnlyList<string> directories;
                try
                {
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    directories =
                    [
                        .. Directory
                            .EnumerateDirectories(root)
                            .OrderBy(Path.GetFileName, StringComparer.Ordinal),
                    ];
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"skills: '{root}' could not be read: {exception.Message}");
                    continue;
                }

                foreach (string directory in directories)
                {
                    if (scanned == MaxSkillsPerScope)
                    {
                        truncated = true;
                        break;
                    }

                    scanned++;
                    DirectoryStep(directory, scope, extensionId, depth, rank, projectBoundary);
                }

                if (truncated)
                {
                    break;
                }
            }

            if (truncated)
            {
                string label = scope switch
                {
                    SkillScope.Project => "project",
                    SkillScope.User => "user",
                    _ => $"extension '{extensionId}'",
                };
                warnings.Add(
                    $"skills: more than {MaxSkillsPerScope} skills in the {label} scope; the extra directories were skipped"
                );
            }
        }

        internal void ExtensionSource(ExtensionSkillSource source)
        {
            string boundary;
            try
            {
                boundary = Workspace.Canonicalize(source.ExtensionDirectory);
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or ArgumentException
                            or NotSupportedException
                )
            {
                warnings.Add(
                    $"skills: extension '{source.ExtensionId}' source '{source.SourceId}' could not be resolved: {exception.Message}"
                );
                return;
            }

            var scanned = 0;
            var truncated = false;
            foreach (string directory in source.Directories)
            {
                if (scanned == MaxSkillsPerScope)
                {
                    truncated = true;
                    break;
                }

                scanned++;
                string canonical;
                try
                {
                    canonical = Workspace.Canonicalize(directory);
                }
                catch (Exception exception)
                    when (exception
                            is IOException
                                or UnauthorizedAccessException
                                or ArgumentException
                                or NotSupportedException
                    )
                {
                    warnings.Add(
                        $"skills: extension '{source.ExtensionId}' source '{source.SourceId}' named '{directory}', which could not be resolved: {exception.Message}"
                    );
                    continue;
                }

                if (!IsInside(canonical, boundary))
                {
                    warnings.Add(
                        $"skills: extension '{source.ExtensionId}' source '{source.SourceId}' named '{directory}' outside the extension's own folder; it was skipped"
                    );
                    continue;
                }

                if (!Directory.Exists(canonical))
                {
                    warnings.Add(
                        $"skills: extension '{source.ExtensionId}' source '{source.SourceId}' named '{directory}', which does not exist; it was skipped"
                    );
                    continue;
                }

                DirectoryStep(
                    canonical,
                    SkillScope.Extension,
                    source.ExtensionId,
                    depth: 0,
                    rank: 0,
                    projectBoundary: null
                );
            }

            if (truncated)
            {
                warnings.Add(
                    $"skills: more than {MaxSkillsPerScope} skills contributed by extension '{source.ExtensionId}'; the extra directories were skipped"
                );
            }
        }

        private void DirectoryStep(
            string directory,
            SkillScope scope,
            string? extensionId,
            int depth,
            int rank,
            string? projectBoundary
        )
        {
            string canonical;
            try
            {
                canonical = Workspace.Canonicalize(directory);
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or ArgumentException
                            or NotSupportedException
                )
            {
                warnings.Add($"skills: '{directory}' could not be resolved: {exception.Message}");
                return;
            }

            // A symlinked skill directory is followed only within the repository boundary.
            if (
                projectBoundary is { Length: > 0 } boundary
                && !IsInside(canonical, Workspace.Canonicalize(boundary))
            )
            {
                warnings.Add(
                    $"skills: '{directory}' resolves to '{canonical}', outside the repository; it was skipped"
                );
                return;
            }

            string skillFile = Path.Combine(canonical, SkillFileName);
            if (!File.Exists(skillFile))
            {
                return;
            }

            if (readSeen.Add(canonical))
            {
                readRoots.Add(canonical);
            }

            string directoryName = Path.GetFileName(canonical);
            string text;
            try
            {
                text = File.ReadAllText(skillFile);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                string readReason = $"could not be read: {exception.Message}";
                warnings.Add($"skills: '{skillFile}' {readReason}");
                invalid.Add(
                    Invalid(
                        directoryName,
                        canonical,
                        skillFile,
                        scope,
                        extensionId,
                        depth,
                        rank,
                        readReason
                    )
                );
                return;
            }

            SkillFrontmatter frontmatter = SkillFrontmatterParser.Parse(text);
            string? reason = frontmatter.Error;
            if (
                reason is null
                && !string.Equals(frontmatter.Name, directoryName, StringComparison.Ordinal)
            )
            {
                reason =
                    $"name '{frontmatter.Name}' does not match the directory name '{directoryName}'";
            }

            if (reason is null && !SkillNamePattern().IsMatch(directoryName))
            {
                reason = $"name '{directoryName}' must match [a-z0-9-]+";
            }

            if (reason is not null)
            {
                warnings.Add($"skills: '{canonical}': {reason}; it was not loaded");
                invalid.Add(
                    Invalid(
                        directoryName,
                        canonical,
                        skillFile,
                        scope,
                        extensionId,
                        depth,
                        rank,
                        reason
                    )
                );
                return;
            }

            candidates.Add(
                new SkillDescriptor(
                    frontmatter.Name!,
                    frontmatter.Description!,
                    frontmatter.Body,
                    canonical,
                    skillFile,
                    scope,
                    extensionId,
                    depth,
                    rank,
                    frontmatter.CommandOnly,
                    Valid: true,
                    InvalidReason: null
                )
            );
        }
    }

    private static SkillDescriptor Invalid(
        string name,
        string directory,
        string skillFile,
        SkillScope scope,
        string? extensionId,
        int depth,
        int rank,
        string reason
    ) =>
        new(
            name,
            string.Empty,
            string.Empty,
            directory,
            skillFile,
            scope,
            extensionId,
            depth,
            rank,
            CommandOnly: false,
            Valid: false,
            InvalidReason: reason
        );

    /// <summary>The collision rank: scope, then nearest project level, then kind, then extension id.</summary>
    private sealed class RankOrder : IComparer<SkillDescriptor>
    {
        internal static RankOrder Instance { get; } = new();

        public int Compare(SkillDescriptor? left, SkillDescriptor? right)
        {
            if (left is null || right is null)
            {
                return left is null ? (right is null ? 0 : -1) : 1;
            }

            int scope = left.Scope.CompareTo(right.Scope);
            if (scope != 0)
            {
                return scope;
            }

            int depth = left.ProjectDepth.CompareTo(right.ProjectDepth);
            if (depth != 0)
            {
                return depth;
            }

            int rank = left.SourceRank.CompareTo(right.SourceRank);
            if (rank != 0)
            {
                return rank;
            }

            int extension = string.CompareOrdinal(left.ExtensionId, right.ExtensionId);
            return extension != 0
                ? extension
                : string.CompareOrdinal(left.Directory, right.Directory);
        }
    }

    private static bool IsInside(string canonical, string root)
    {
        if (canonical.Equals(root, PathComparison()))
        {
            return true;
        }

        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return canonical.StartsWith(prefix, PathComparison());
    }

    private static bool PathsEqual(string left, string right) =>
        left.Equals(right, PathComparison());

    private static StringComparison PathComparison() =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static StringComparer PathComparer() =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
}
