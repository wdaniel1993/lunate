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
    /// The content hash over the discovered project skills: the repo-relative directory of each
    /// skill (ordinal-sorted) plus the SHA-256 content hash of that directory, so both a rename and
    /// any file change re-prompt.
    /// </summary>
    internal static string ComputeContentHash(
        IReadOnlyList<SkillDescriptor> projectSkills,
        string repoRoot
    )
    {
        ArgumentNullException.ThrowIfNull(projectSkills);
        List<(string Relative, string Hash)> entries = [];
        foreach (SkillDescriptor skill in projectSkills)
        {
            string relative = Path.GetRelativePath(repoRoot, skill.Directory)
                .Replace(Path.DirectorySeparatorChar, '/');
            entries.Add((relative, ExtensionTrustStore.ComputeContentHash(skill.Directory)));
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

    /// <summary>The dim notice for an untrusted skill invocation, naming the fix.</summary>
    internal static string UntrustedNotice(string name) =>
        $"skill '{name}' is not trusted in this repository — approve it in the trust prompt or run 'lunate trust'";
}
