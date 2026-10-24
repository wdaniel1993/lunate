namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// The naming of an extension tool: the local name the manifest (or a single file's header)
/// declares, and the model-facing name the provider sees, <c>&lt;extension-id&gt;__&lt;local&gt;</c>.
/// Provider APIs accept only <c>[A-Za-z0-9_-]</c> and 64 characters in a tool name, so both the
/// local-name rule and the composed limit live here and are enforced at every entry: the manifest,
/// a single-file header and the runtime registry cannot drift apart.
/// </summary>
internal static class ExtensionToolNaming
{
    /// <summary>The longest model-facing name a provider accepts.</summary>
    internal const int MaxModelFacingNameLength = 64;

    /// <summary>The separator between the extension id and the tool's local name.</summary>
    internal const string Separator = "__";

    /// <summary>The model-facing name: <c>&lt;extension-id&gt;__&lt;local&gt;</c>.</summary>
    internal static string ModelFacing(string extensionId, string localName) =>
        string.Concat(extensionId, Separator, localName);

    /// <summary>A local name's shape: non-empty and <c>[A-Za-z0-9_-]</c>.</summary>
    internal static bool IsValidLocalName(string name) =>
        name.Length > 0
        && name.All(character =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'
        );
}
