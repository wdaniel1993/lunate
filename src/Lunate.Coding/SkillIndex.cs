using System.Text;

namespace Lunate.Coding;

/// <summary>
/// The <c>skills</c> prompt section: a lead line naming the read tool plus one line per
/// model-visible skill (<c>- name: description [path]</c>, descriptions truncated at 200
/// characters). Order is project, then user, then extension, each group by name; extension lines
/// are enforced per extension first (over-budget lines drop and log), then the global 1,000
/// estimated-token cap cuts strictly from the end and appends a names-only footer the model can act
/// on. One token is estimated as four characters, rounded up.
/// </summary>
internal static class SkillIndex
{
    internal const int MaxEstimatedTokens = 1000;
    internal const int MaxDescriptionLength = 200;
    internal const int MaxOverflowNamesLength = 400;
    internal const string LeadLine =
        "Skills — load a skill's full SKILL.md with the read tool before using it:";

    /// <summary>The composed index and what it references.</summary>
    internal sealed record Composed(
        string Text,
        int IndexedCount,
        int EstimatedTokens,
        IReadOnlyList<string> NotIndexed
    );

    internal static int EstimateTokens(string text) => (text.Length + 3) / 4;

    internal static Composed Compose(
        IReadOnlyList<SkillDescriptor> skills,
        string worktreeRoot,
        int perExtensionBudgetTokens,
        Action<string>? log = null
    )
    {
        ArgumentNullException.ThrowIfNull(skills);

        List<(SkillDescriptor Skill, string Line)> eligible = [];
        Dictionary<string, int> extensionUsed = new(StringComparer.Ordinal);
        List<SkillDescriptor> notIndexed = [];
        foreach (
            SkillDescriptor skill in skills
                .Where(skill => skill.IndexEligible)
                .OrderBy(skill => skill, SkillDescriptor.IndexOrderComparer)
        )
        {
            string line = Line(skill, worktreeRoot);
            if (skill.Scope is SkillScope.Extension)
            {
                string id = skill.ExtensionId ?? string.Empty;
                int used = extensionUsed.GetValueOrDefault(id);
                int cost = EstimateTokens(line);
                if (used + cost > perExtensionBudgetTokens)
                {
                    notIndexed.Add(skill);
                    log?.Invoke(
                        $"skills: the extension '{id}' exceeded its context budget ({perExtensionBudgetTokens} estimated tokens); '{skill.Name}' was dropped from the index."
                    );
                    continue;
                }

                extensionUsed[id] = used + cost;
            }

            eligible.Add((skill, line));
        }

        if (eligible.Count == 0)
        {
            // Nothing survived the per-extension budgets: there is no index to send, only names
            // the human surfaces can still show as (not indexed).
            return new Composed(string.Empty, 0, 0, [.. notIndexed.Select(skill => skill.Name)]);
        }

        List<(SkillDescriptor Skill, string Line)> kept = [.. eligible];
        List<SkillDescriptor> overflow = [];
        string text = Render(kept, overflow);
        while (kept.Count > 0 && EstimateTokens(text) > MaxEstimatedTokens)
        {
            overflow.Insert(0, kept[^1].Skill);
            kept.RemoveAt(kept.Count - 1);
            text = Render(kept, overflow);
        }

        List<SkillDescriptor> notIndexedFinal = [.. notIndexed, .. overflow];
        return new Composed(
            text,
            kept.Count,
            EstimateTokens(text),
            [.. notIndexedFinal.Select(skill => skill.Name)]
        );
    }

    /// <summary>The per-line shape: <c>- name: description [path]</c>.</summary>
    private static string Line(SkillDescriptor skill, string worktreeRoot) =>
        $"- {skill.Name}: {TruncateDescription(skill.Description)} [{SkillPaths.Display(skill.SkillFile, worktreeRoot)}]";

    private static string TruncateDescription(string description) =>
        description.Length <= MaxDescriptionLength
            ? description
            : description[..MaxDescriptionLength] + "…";

    private static string Render(
        IReadOnlyList<(SkillDescriptor Skill, string Line)> kept,
        IReadOnlyList<SkillDescriptor> overflow
    )
    {
        var text = new StringBuilder(LeadLine);
        foreach ((SkillDescriptor _, string line) in kept)
        {
            text.Append('\n').Append(line);
        }

        if (overflow.Count > 0)
        {
            text.Append('\n').Append(Footer(overflow));
        }

        return text.ToString();
    }

    private static string Footer(IReadOnlyList<SkillDescriptor> overflow)
    {
        string names = string.Join(", ", overflow.Select(skill => skill.Name));
        if (names.Length > MaxOverflowNamesLength)
        {
            names = names[..MaxOverflowNamesLength] + "…";
        }

        return $"+{overflow.Count} more skills (not indexed): {names}";
    }
}
