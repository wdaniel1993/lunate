using System.Text;

namespace Lunate.Coding;

/// <summary>
/// The <c>/skills</c> notice: one segment per discovered skill — <c>name (scope[, marker…]) —
/// description</c> with the <c>command-only</c>, <c>not indexed</c>, <c>shadows &lt;scope&gt;</c>
/// and <c>untrusted</c> markers — and invalid entries as <c>name (invalid) — first sentence</c>.
/// Nothing discovered reads <c>skills: none found</c>. The view never prints file contents.
/// </summary>
internal static class SkillsView
{
    internal const string NoneText = "skills: none found";

    internal static string Text(IReadOnlyList<SkillDescriptor> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        if (skills.Count == 0)
        {
            return NoneText;
        }

        List<string> segments = [];
        foreach (SkillDescriptor skill in skills.Where(skill => skill.Valid))
        {
            List<string> marks = [skill.ScopeLabel];
            if (skill.CommandOnly)
            {
                marks.Add("command-only");
            }

            if (skill.NotIndexed)
            {
                marks.Add("not indexed");
            }

            if (skill.Shadows is { } shadows)
            {
                marks.Add($"shadows {shadows}");
            }

            if (skill.Untrusted)
            {
                marks.Add("untrusted");
            }

            if (skill.FromPackage is { Length: > 0 } fromPackage)
            {
                marks.Add($"from {fromPackage}");
            }

            segments.Add(
                $"{skill.Name} ({string.Join(", ", marks)}) — {Collapse(skill.Description)}"
            );
        }

        foreach (
            SkillDescriptor skill in skills
                .Where(skill => !skill.Valid)
                .OrderBy(skill => skill.Name, StringComparer.Ordinal)
        )
        {
            segments.Add($"{skill.Name} (invalid) — {FirstSentence(skill.InvalidReason)}");
        }

        return "skills: " + string.Join("; ", segments);
    }

    private static string FirstSentence(string? text)
    {
        string collapsed = Collapse(text ?? string.Empty);
        int stop = collapsed.IndexOf(". ", StringComparison.Ordinal);
        return stop >= 0 ? collapsed[..stop] : collapsed;
    }

    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
