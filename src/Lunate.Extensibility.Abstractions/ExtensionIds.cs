namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// The one extension-id rule, shared by the packaged manifest and a single file's
/// <c>#:extension</c> header directive. A single file promoted to a package has to keep its id, so
/// there is exactly one grammar: lowercase letters, digits and hyphens. A dot is refused because
/// the id prefixes every tool the extension contributes (<c>&lt;id&gt;__&lt;tool&gt;</c>), and
/// provider APIs accept only <c>[A-Za-z0-9_-]</c> in a tool name; a colon is refused because a
/// command id reads <c>ext:&lt;id&gt;:&lt;name&gt;</c>.
/// </summary>
internal static class ExtensionIds
{
    /// <summary>The rule as it appears in a diagnostic.</summary>
    internal const string Pattern = "[a-z0-9-]+";

    internal static bool IsValid(string id) =>
        id.Length > 0
        && id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
