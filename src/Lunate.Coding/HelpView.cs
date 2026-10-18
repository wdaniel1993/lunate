using System.Globalization;

namespace Lunate.Coding;

/// <summary>
/// The <c>/help</c> listing: the command registry grouped (session, model, extensions, files, quit,
/// design D3), one command per line with its padded name and its description. A group with no
/// commands is not printed, so the listing never shows an empty heading.
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

    /// <summary>The <c>/help</c> lines, printed as finished output into scrollback.</summary>
    internal static IReadOnlyList<string> Lines()
    {
        List<string> lines = [];
        foreach (CommandGroup group in Groups)
        {
            CommandDescriptor[] commands =
            [
                .. Commands
                    .Descriptors.Where(descriptor => descriptor.Group == group)
                    .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal),
            ];
            if (commands.Length == 0)
            {
                continue;
            }

            lines.Add(Title(group));
            int width = commands.Max(descriptor => descriptor.Name.Length);
            lines.AddRange(
                commands.Select(descriptor =>
                    FormattableString.Invariant(
                        $"  {descriptor.Name.PadRight(width)} — {descriptor.Description}"
                    )
                )
            );
        }

        return lines;
    }

    /// <summary>The lowercase group heading (<c>session</c>, <c>model</c>, ...).</summary>
    private static string Title(CommandGroup group) =>
        group.ToString().ToLower(CultureInfo.InvariantCulture);
}
