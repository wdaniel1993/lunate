using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// Maps the command registry's merged descriptors to the autocomplete rows: built-ins and
/// templates read as command rows, skills keep their kind, extension commands carry their source
/// id when they have no description, and the argument-bearing skill placeholder is not a row (the
/// real <c>/skill:&lt;name&gt;</c> entries come from the skill source). The first text wins.
/// </summary>
internal static class CompletionRegistry
{
    /// <summary>Composes the rows in the registry's descriptor order.</summary>
    public static IReadOnlyList<CompletionItem> Compose(
        IReadOnlyList<CommandDescriptor> descriptors
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        List<CompletionItem> items = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (CommandDescriptor descriptor in descriptors)
        {
            // The placeholder is a /help entry, not a completion row.
            if (
                descriptor.Kind is CommandKind.Builtin
                && string.Equals(
                    descriptor.Name,
                    Commands.SkillName.TrimStart('/'),
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            string text = "/" + descriptor.Name;
            if (!seen.Add(text))
            {
                continue;
            }

            CompletionKind kind = descriptor.Kind switch
            {
                CommandKind.Skill => CompletionKind.Skill,
                CommandKind.Extension => CompletionKind.Extension,
                _ => CompletionKind.Command,
            };
            string? description =
                descriptor.Description.Length > 0 ? descriptor.Description
                : descriptor.Kind is CommandKind.Extension ? descriptor.SourceId
                : null;
            items.Add(new CompletionItem(text, description, kind, null));
        }

        return items;
    }
}
