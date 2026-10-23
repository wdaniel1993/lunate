using System.Globalization;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The <c>/help</c> listing over the whole registry: the built-in entries grouped (session, model,
/// extensions, files, quit, design D3) — one command per line with its padded name and its
/// description — then one section per non-built-in kind present (<c>skills</c>,
/// <c>extensions</c>, <c>templates</c>), in the registry's kind order and sorted by name. A group
/// or section with no commands is not printed, so the listing never shows an empty heading, and a
/// registry holding nothing but built-ins prints exactly the built-in listing.
/// </summary>
internal static class HelpView
{
    /// <summary>The group order of the listing; a group without commands is omitted.</summary>
    private static readonly CommandGroup[] Groups =
    [
        CommandGroup.Session,
        CommandGroup.Model,
        CommandGroup.Extensions,
        CommandGroup.Files,
        CommandGroup.Quit,
    ];

    /// <summary>
    /// The non-built-in sections in the registry's kind order, with the heading each prints. The
    /// headings are the kinds' names, not the built-in groups' — the built-in <c>extensions</c>
    /// group (the commands that inspect extensions) and the <c>extensions</c> section (the
    /// extension commands themselves) are different things and both may print.
    /// </summary>
    private static readonly (CommandKind Kind, string Title)[] Sections =
    [
        (CommandKind.Skill, "skills"),
        (CommandKind.Extension, "extensions"),
        (CommandKind.Template, "templates"),
    ];

    /// <summary>
    /// The cap on the skills section: a workspace can carry dozens of skills, and <c>/help</c>
    /// lists names while <c>/skills</c> explains their scope and state. Extension and template
    /// sections are not capped (sign-off Q3).
    /// </summary>
    private const int SkillsCap = 20;

    /// <summary>The <c>/help</c> lines from the registry's descriptors, built-in groups first.</summary>
    internal static IReadOnlyList<string> Lines(IReadOnlyList<CommandDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        List<string> lines = [];
        foreach (CommandGroup group in Groups)
        {
            CommandDescriptor[] commands =
            [
                .. descriptors
                    .Where(descriptor =>
                        descriptor.Kind is CommandKind.Builtin
                        && Commands.GroupOf(descriptor.Name) == group
                    )
                    .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal),
            ];
            if (commands.Length == 0)
            {
                continue;
            }

            lines.Add(Title(group));
            AddRows(lines, commands, cap: 0);
        }

        foreach ((CommandKind kind, string title) in Sections)
        {
            CommandDescriptor[] commands =
            [
                .. descriptors
                    .Where(descriptor => descriptor.Kind == kind)
                    .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal),
            ];
            if (commands.Length == 0)
            {
                continue;
            }

            lines.Add(title);
            AddRows(lines, commands, kind is CommandKind.Skill ? SkillsCap : 0);
        }

        return lines;
    }

    /// <summary>
    /// The section's rows: the typed token padded to the widest row of its own section, then the
    /// description. A positive <paramref name="cap"/> stops at that many rows and appends one
    /// overflow line pointing at the detailed index.
    /// </summary>
    private static void AddRows(List<string> lines, CommandDescriptor[] commands, int cap)
    {
        int shown = cap > 0 ? Math.Min(commands.Length, cap) : commands.Length;
        int width = commands.Take(shown).Max(descriptor => descriptor.Name.Length + 1);
        foreach (CommandDescriptor descriptor in commands.Take(shown))
        {
            string command = "/" + descriptor.Name;
            lines.Add(
                FormattableString.Invariant(
                    $"  {command.PadRight(width)} — {descriptor.Description}"
                )
            );
        }

        if (commands.Length > shown)
        {
            lines.Add(
                FormattableString.Invariant($"  +{commands.Length - shown} more — see /skills")
            );
        }
    }

    /// <summary>The lowercase group heading (<c>session</c>, <c>model</c>, ...).</summary>
    private static string Title(CommandGroup group) =>
        group.ToString().ToLower(CultureInfo.InvariantCulture);
}
