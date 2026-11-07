using System.Globalization;
using System.Text;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// The <c>skills</c> prompt section: a lead line naming the read tool plus one line per
/// model-visible skill (<c>- name: description [path]</c>, the description whole up to the Agent
/// Skills spec limit of 1024 characters, overflow names at 400, never splitting a surrogate
/// pair). Order is project, then user, then extension, each group by name; extension lines
/// are enforced per extension first (over-budget lines drop and log), then the resolved hard
/// ceiling cuts strictly from the end and appends a names-only footer the model can act on —
/// skills named in <c>AGENTS.md</c> are cut last (see <see cref="SkillsBudget"/>). One token is
/// estimated as four characters, rounded up.
/// </summary>
internal static class SkillIndex
{
    /// <summary>
    /// The Agent Skills specification's limit for a description. The index carries the description
    /// whole up to it: the description is the trigger the model matches a task against.
    /// </summary>
    internal const int MaxDescriptionLength = 1024;
    internal const int MaxOverflowNamesLength = 400;
    internal const string LeadLine =
        "Skills — load a skill's full SKILL.md with the read tool before using it:";

    /// <summary>The composed index and what it references.</summary>
    internal sealed record Composed(
        string Text,
        int IndexedCount,
        int EstimatedTokens,
        IReadOnlyList<string> NotIndexed,
        int OverflowCount = 0,
        int SoftTokens = int.MaxValue,
        int HardTokens = int.MaxValue
    );

    internal static int EstimateTokens(string text) => (text.Length + 3) / 4;

    internal static Composed Compose(
        IReadOnlyList<SkillDescriptor> skills,
        string worktreeRoot,
        int perExtensionBudgetTokens,
        SkillsBudget budget,
        IReadOnlySet<string> protectedNames,
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
            return new Composed(
                string.Empty,
                0,
                0,
                [.. notIndexed.Select(skill => skill.Name)],
                0,
                budget.SoftTokens,
                budget.HardTokens
            );
        }

        List<(SkillDescriptor Skill, string Line)> kept = [.. eligible];
        List<SkillDescriptor> overflow = [];
        string index = Render(kept, overflow);
        while (kept.Count > 0 && EstimateTokens(index) > budget.HardTokens)
        {
            int cut = CutIndex(kept, protectedNames);
            if (cut < 0)
            {
                break;
            }

            overflow.Insert(0, kept[cut].Skill);
            kept.RemoveAt(cut);
            index = Render(kept, overflow);
        }

        List<SkillDescriptor> notIndexedFinal = [.. notIndexed, .. overflow];
        return new Composed(
            index,
            kept.Count,
            EstimateTokens(index),
            [.. notIndexedFinal.Select(skill => skill.Name)],
            overflow.Count,
            budget.SoftTokens,
            budget.HardTokens
        );
    }

    /// <summary>
    /// The one-time session-start notice for a complete index measuring above the soft threshold,
    /// or null when the index dropped nothing or stays under the threshold.
    /// </summary>
    internal static string? SoftNotice(Composed index) =>
        index.NotIndexed.Count > 0 || index.EstimatedTokens <= index.SoftTokens
            ? null
            : FormattableString.Invariant(
                $"skills: the skills index measures ~{index.EstimatedTokens} estimated tokens (soft threshold ~{index.SoftTokens}); it stays fully indexed. Disable unused skills or packages to shrink it."
            );

    /// <summary>
    /// The hard-ceiling warning: names how many skills lost their description, or null when
    /// nothing was cut.
    /// </summary>
    internal static string? HardCeilingWarning(Composed index) =>
        index.OverflowCount == 0
            ? null
            : FormattableString.Invariant(
                $"skills: the skills index passed its hard ceiling (~{index.HardTokens} estimated tokens); {index.OverflowCount} of {index.IndexedCount + index.OverflowCount} skills are listed in the footer without their description. Disable unused skills or packages, or set skills.indexBudget."
            );

    /// <summary>
    /// The resolved index budgets from the <c>skills.indexBudget</c> setting and the model's
    /// context window: the explicit literal <c>unlimited</c> disables both, a positive integer
    /// applies one budget (the soft threshold follows it so a complete pack above it could not
    /// still pass unnoticed), and unset derives soft = 2% and hard = 5% of the window — the
    /// 128,000-token default when the window is unknown.
    /// </summary>
    internal sealed record SkillsBudget(int SoftTokens, int HardTokens)
    {
        internal const double SoftFraction = 0.02;
        internal const double HardFraction = 0.05;

        internal static SkillsBudget Unlimited { get; } = new(int.MaxValue, int.MaxValue);

        internal static SkillsBudget FromSettings(SkillsSettings settings, int contextWindow)
        {
            if (settings.Unlimited)
            {
                return Unlimited;
            }

            if (settings.IndexBudget is { } budget)
            {
                return new SkillsBudget(budget, budget);
            }

            int window = contextWindow > 0 ? contextWindow : CompactionReducer.DefaultContextWindow;
            return new SkillsBudget((int)(window * SoftFraction), (int)(window * HardFraction));
        }
    }

    /// <summary>
    /// The discovered skills whose name appears as a whole name in the <c>AGENTS.md</c> chain's
    /// text: the ceiling cut never drops them.
    /// </summary>
    internal static IReadOnlySet<string> NamesInInstructions(
        IReadOnlyList<SkillDescriptor> skills,
        string instructionsText
    )
    {
        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (SkillDescriptor skill in skills.Where(skill => skill.IndexEligible))
        {
            if (Mentions(instructionsText, skill.Name))
            {
                matched.Add(skill.Name);
            }
        }

        return matched;
    }

    private static bool Mentions(string text, string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        for (
            int index = text.IndexOf(name, StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf(name, index + 1, StringComparison.Ordinal)
        )
        {
            bool left = index == 0 || !IsNameChar(text[index - 1]);
            bool right =
                index + name.Length >= text.Length || !IsNameChar(text[index + name.Length]);
            if (left && right)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNameChar(char character) =>
        char.IsAsciiLetterOrDigit(character) || character == '-';

    /// <summary>
    /// The strict-from-the-end cut position: the last unprotected skill, or -1 when every kept
    /// skill is protected — the explicit <c>AGENTS.md</c> naming wins over flood protection.
    /// </summary>
    private static int CutIndex(
        IReadOnlyList<(SkillDescriptor Skill, string Line)> kept,
        IReadOnlySet<string> protectedNames
    )
    {
        for (int index = kept.Count - 1; index >= 0; index--)
        {
            if (!protectedNames.Contains(kept[index].Skill.Name))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>The per-line shape: <c>- name: description [path]</c>.</summary>
    private static string Line(SkillDescriptor skill, string worktreeRoot) =>
        $"- {skill.Name}: {TruncateDescription(skill.Description)} [{SkillPaths.Display(skill.SkillFile, worktreeRoot)}]";

    /// <summary>
    /// The description whole, up to the Agent Skills spec limit. It is the trigger the model matches
    /// a task against, so it is carried in full: the "use when …" part is often the second
    /// sentence, and cutting at the first one hid exactly the text that decides a load.
    /// </summary>
    private static string TruncateDescription(string description) =>
        description.Length <= MaxDescriptionLength
            ? description
            : SafeSlice(description, MaxDescriptionLength) + "…";

    /// <summary>
    /// Slices to <paramref name="length"/> UTF-16 code units, dropping a trailing high surrogate so
    /// a boundary cut can never emit half of a surrogate pair.
    /// </summary>
    private static string SafeSlice(string value, int length)
    {
        int end = length;
        if (char.IsHighSurrogate(value[end - 1]))
        {
            end--;
        }

        return value[..end];
    }

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
            names = SafeSlice(names, MaxOverflowNamesLength) + "…";
        }

        return $"+{overflow.Count} more skills (not indexed): {names}";
    }
}
