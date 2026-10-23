using System.Globalization;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The <c>/help</c> listing: the registry's built-in entries grouped (session, model, extensions,
/// files, quit, design D3), one command per line with its padded name and its description. A group
/// with no commands is not printed, so the listing never shows an empty heading.
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

    /// <summary>The <c>/help</c> lines from the registry's built-in descriptors.</summary>
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
            int width = commands.Max(descriptor => descriptor.Name.Length + 1);
            lines.AddRange(
                commands.Select(descriptor =>
                {
                    string command = "/" + descriptor.Name;
                    return FormattableString.Invariant(
                        $"  {command.PadRight(width)} — {descriptor.Description}"
                    );
                })
            );
        }

        return lines;
    }

    /// <summary>The lowercase group heading (<c>session</c>, <c>model</c>, ...).</summary>
    private static string Title(CommandGroup group) =>
        group.ToString().ToLower(CultureInfo.InvariantCulture);
}
