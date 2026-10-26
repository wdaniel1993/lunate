using System.Security.Cryptography;
using System.Text;
using Lunate.Extensibility;

namespace Lunate.Coding;

/// <summary>
/// Project-skill trust: one decision per repository identity and worktree, keyed by a content hash
/// over the discovered project skill directories (skill names plus their directory content) so
/// changed skills re-prompt. The question lists the discovered skill names; approving records the
/// decision like extension trust.
/// </summary>
internal static class SkillTrust
{
    /// <summary>The question's title shown by the trust prompt and the trust report.</summary>
    internal const string QuestionTitle = "Load project skills from this repository";

    /// <summary>
    /// The content hash over the discovered project skills: the directory of each skill relative
    /// to the repository root (ordinal-sorted) plus the SHA-256 content hash of that directory, so
    /// both a rename and any file change re-prompt. For skills discovered in a linked worktree the
    /// path is relative to the main checkout and may begin with `..` segments — the hash stays
    /// deterministic per worktree. A skill an install provided binds its package identity beside
    /// the directory hash (design D7), so an upgrade re-prompts exactly as an edit does.
    /// </summary>
    internal static string ComputeContentHash(
        IReadOnlyList<SkillDescriptor> projectSkills,
        string repoRoot,
        InstalledPackages? installed = null
    )
    {
        ArgumentNullException.ThrowIfNull(projectSkills);
        List<(string Relative, string Hash)> entries = [];
        foreach (SkillDescriptor skill in projectSkills)
        {
            string relative = Path.GetRelativePath(repoRoot, skill.Directory)
                .Replace(Path.DirectorySeparatorChar, '/');
            entries.Add(
                (
                    relative,
                    InstalledPackages.BindTrustDigest(
                        ExtensionTrustStore.ComputeContentHash(skill.Directory),
                        installed?.ForSkill(skill.Directory)
                    )
                )
            );
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Relative, right.Relative));
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach ((string relative, string content) in entries)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData([0]);
            hash.AppendData(Encoding.UTF8.GetBytes(content));
            hash.AppendData([0]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>The discovered project skill names, ordinal-sorted.</summary>
    internal static IReadOnlyList<string> Names(IReadOnlyList<SkillDescriptor> projectSkills) =>
        [
            .. projectSkills
                .Select(skill => skill.Name)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

    /// <summary>The question's detail lines: the discovered project skill names.</summary>
    internal static IReadOnlyList<string> QuestionDetails(IReadOnlyList<string> names) =>
        [$"skills: {string.Join(", ", names)}"];

    /// <summary>
    /// The skills question's full detail list: the discovered names, then the provenance line of
    /// each package that provided one (design D7), so the live-area question names the package too.
    /// </summary>
    internal static IReadOnlyList<string> QuestionLines(
        IReadOnlyList<SkillDescriptor> projectSkills,
        InstalledPackages? installed
    ) => [.. QuestionDetails(Names(projectSkills)), .. FromPackages(projectSkills, installed)];

    /// <summary>
    /// The provenance lines of the packages that provided the discovered skills, ordinal-sorted and
    /// deduplicated: a repository whose project skills all came from one install asks one line.
    /// </summary>
    internal static IReadOnlyList<string> FromPackages(
        IReadOnlyList<SkillDescriptor> projectSkills,
        InstalledPackages? installed
    )
    {
        ArgumentNullException.ThrowIfNull(projectSkills);
        if (installed is null)
        {
            return [];
        }

        SortedSet<string> lines = new(StringComparer.Ordinal);
        foreach (SkillDescriptor skill in projectSkills)
        {
            if (installed.ForSkill(skill.Directory) is { } provenance)
            {
                lines.Add(provenance.QuestionLine);
            }
        }

        return [.. lines];
    }

    /// <summary>The dim notice for an untrusted skill invocation, naming the fix.</summary>
    internal static string UntrustedNotice(string name) =>
        $"skill '{name}' is not trusted in this repository — approve it in the trust prompt or run 'lunate trust'";
}
