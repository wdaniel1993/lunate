using Lunate.Extensibility;
using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// The session's one autocomplete registry (design D2): the built-in command table, the discovered
/// skills (<c>/skill:&lt;name&gt;</c>) and the loaded extensions' manifest commands, in that natural
/// order. Composed once per session from in-memory data — never a second hard-coded list — and
/// never carrying files: those stay in the lazy <see cref="FileIndex"/> and are queried per
/// <c>@</c> token.
/// </summary>
internal sealed class CompletionRegistry
{
    private CompletionRegistry(IReadOnlyList<CompletionItem> items)
    {
        Items = items;
    }

    /// <summary>The rows in natural order: commands, skills, extensions.</summary>
    public IReadOnlyList<CompletionItem> Items { get; }

    /// <summary>Composes the registry; the first source to claim a text wins.</summary>
    public static CompletionRegistry Compose(
        IReadOnlyList<SkillDescriptor> skills,
        IReadOnlyList<ExtensionDescriptor> extensions
    )
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(extensions);

        List<CompletionItem> items = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        void Add(CompletionItem item)
        {
            if (seen.Add(item.Text))
            {
                items.Add(item);
            }
        }

        foreach (CommandDescriptor descriptor in Commands.Descriptors)
        {
            Add(
                new CompletionItem(
                    descriptor.Name,
                    descriptor.Description,
                    CompletionKind.Command,
                    null
                )
            );
        }

        foreach (SkillDescriptor skill in skills)
        {
            if (!skill.Valid || skill.Untrusted)
            {
                continue;
            }

            string? description = skill.Description.Length > 0 ? skill.Description : null;
            Add(
                new CompletionItem(
                    Commands.SkillPrefix + skill.Name,
                    description,
                    CompletionKind.Skill,
                    null
                )
            );
        }

        foreach (ExtensionDescriptor extension in extensions)
        {
            foreach (string command in extension.Manifest.Commands)
            {
                Add(new CompletionItem(command, extension.Id, CompletionKind.Extension, null));
            }
        }

        return new CompletionRegistry(items);
    }
}
