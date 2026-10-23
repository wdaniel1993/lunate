using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// A command's host-side identity (design D2): <c>builtin:&lt;name&gt;</c>,
/// <c>skill:&lt;name&gt;</c>, <c>template:&lt;name&gt;</c> or <c>ext:&lt;extension id&gt;:&lt;name&gt;</c>,
/// computed from the descriptor's kind, source and bare name. It is host internals — a source
/// cannot set or spoof it — and it never changes the typed token or an autocomplete row; it exists
/// so every diagnostic, log line and failure notice can say which command it means when two
/// sources offer one name. Skill ids and skill names coincide (<c>skill:&lt;name&gt;</c>), which is
/// why the skill path needs no separate form.
/// </summary>
internal static class CommandId
{
    /// <summary>The id of one descriptor, as the registry computes it before the duplicate check.</summary>
    internal static string For(CommandDescriptor descriptor) =>
        ForSource(descriptor.SourceId, descriptor.Name, descriptor.Kind);

    /// <summary>
    /// The id from a source id, a bare name and a kind. The extension form is delimited by
    /// <c>:</c>, which is why manifest validation refuses a colon in an extension id.
    /// </summary>
    internal static string ForSource(string sourceId, string name, CommandKind kind)
    {
        string bare = name.TrimStart('/');
        return kind switch
        {
            CommandKind.Builtin => "builtin:" + bare,
            CommandKind.Skill => bare.StartsWith("skill:", StringComparison.Ordinal)
                ? bare
                : "skill:" + bare,
            CommandKind.Template => "template:" + bare,
            CommandKind.Extension => FormattableString.Invariant($"ext:{sourceId}:{bare}"),
            _ => bare,
        };
    }
}
