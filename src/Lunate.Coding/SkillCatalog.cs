using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// The session's discovered skills: the scan result, the trust-gated index and the per-run read
/// allowance the read tool consults. Discovery runs at session start (and again on a wholesale
/// reload); the index is built once and never rebuilt mid-run.
/// </summary>
internal sealed class SkillCatalog
{
    private readonly SkillScanRequest _request;

    private SkillCatalog(SkillScanRequest request, SkillScanResult scan)
    {
        _request = request;
        Skills = scan.Skills;
        Warnings = scan.Warnings;
        ReadRoots = scan.ReadRoots;
    }

    /// <summary>The resolved entries (winners and invalid entries) in index order.</summary>
    internal IReadOnlyList<SkillDescriptor> Skills { get; private set; }

    /// <summary>The one-time discovery warnings (collisions, invalid entries, bounds).</summary>
    internal IReadOnlyList<string> Warnings { get; }

    /// <summary>The composed index; empty until <see cref="BuildIndex"/> ran and nothing is eligible.</summary>
    internal SkillIndex.Composed Index { get; private set; } = new(string.Empty, 0, 0, []);

    /// <summary>This run's read-only allowance roots: every discovered skill directory.</summary>
    internal IReadOnlyList<string> ReadRoots { get; private set; }

    /// <summary>Discovers the session's skills; run once per session start.</summary>
    internal static SkillCatalog Discover(SkillScanRequest request) =>
        new(request, SkillScanner.Scan(request));

    /// <summary>
    /// Applies the project trust decision: untrusted project skills stay listed but leave the
    /// model index and cannot be invoked.
    /// </summary>
    internal void SetProjectTrusted(bool trusted)
    {
        Skills =
        [
            .. Skills.Select(skill =>
                skill.Scope is SkillScope.Project ? skill with { Untrusted = !trusted } : skill
            ),
        ];
    }

    /// <summary>
    /// Builds the model index once: extension lines are enforced against the contributing
    /// extension's context budget first (dropped lines are logged), then the global cap applies.
    /// Not-indexed entries are marked for <c>/skills</c>.
    /// </summary>
    internal void BuildIndex(string worktreeRoot, int perExtensionBudgetTokens, Action<string>? log)
    {
        Index = SkillIndex.Compose(Skills, worktreeRoot, perExtensionBudgetTokens, log);
        HashSet<string> notIndexed = new(Index.NotIndexed, StringComparer.Ordinal);
        Skills =
        [
            .. Skills.Select(skill =>
                skill.Valid && notIndexed.Contains(skill.Name)
                    ? skill with
                    {
                        NotIndexed = true,
                    }
                    : skill
            ),
        ];
    }

    /// <summary>
    /// The read allowance of a run in the given root: every skill directory discovered from that
    /// root's own walk plus the user and extension directories, so child worktree runs resolve
    /// their own allowance the same way.
    /// </summary>
    internal IReadOnlyList<string> ReadRootsFor(string worktreeRoot)
    {
        SkillScanResult scan = SkillScanner.Scan(
            _request with
            {
                WorktreeRoot = worktreeRoot,
                RepoRoot = GitIdentity.Detect(worktreeRoot).RepoRoot,
            }
        );
        return [.. ReadRoots.Union(scan.ReadRoots, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Finds a resolvable skill by name: valid winners only, so an invalid directory resolves to
    /// unknown. Command-only skills stay findable (the exclusion is from the index, not the user).
    /// </summary>
    internal bool TryFind(string name, out SkillDescriptor? skill)
    {
        skill = Skills.FirstOrDefault(entry =>
            entry.Valid && string.Equals(entry.Name, name, StringComparison.Ordinal)
        );
        return skill is not null;
    }
}
