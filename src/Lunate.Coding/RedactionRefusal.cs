using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>
/// The hard guard behind the context mask: the read that carried a credential left the model
/// holding <c>[redacted:&lt;name&gt;]</c>, and a result the model writes back would replace the
/// real value with the placeholder and destroy the credential. Both guards return null when no
/// redactor is bound, so hosts without a session behave exactly as before.
/// </summary>
internal static class RedactionRefusal
{
    /// <summary>
    /// A refused write: content carries a known placeholder while its target file currently holds
    /// one of that name's known values. Anything else — no placeholder, an unknown name, a fresh
    /// file — writes normally (null).
    /// </summary>
    internal static ToolResult? ForWrite(
        string content,
        string? fileText,
        string relativePath,
        CredentialRedactor? redactor
    )
    {
        if (redactor is null)
        {
            return null;
        }

        List<string> held = [];
        foreach (string name in CredentialRedactor.PlaceholderNamesIn(content))
        {
            IReadOnlyList<string> values = redactor.ValuesFor(name);
            if (values.Count == 0 || fileText is null || !HoldsAny(fileText, values))
            {
                continue;
            }

            held.Add(name);
        }

        return held is { Count: > 0 } ? Refuse(held[0], relativePath) : null;
    }

    /// <summary>
    /// A refused edit, deliberately blunt for v1: a placeholder on either side of the edit refuses,
    /// without attempting to map it back into the file, because an edit that travels through a
    /// marker can never address the text the marker replaced.
    /// </summary>
    internal static ToolResult? ForEdit(
        string oldText,
        string newText,
        CredentialRedactor? redactor
    )
    {
        if (redactor is null)
        {
            return null;
        }

        string? name = FirstPlaceholderName(oldText) ?? FirstPlaceholderName(newText);
        if (name is null)
        {
            return null;
        }

        bool known = redactor.ValuesFor(name).Count > 0;
        string marker = CredentialRedactor.Marker(name);
        return new ToolResult(
            known
                ? $"Refused: the edit carries the placeholder \"{marker}\" for the {name} credential; "
                    + "placeholders cannot be mapped back to the real value. Edit around the "
                    + "credential line instead of through it."
                : $"Refused: the edit carries the placeholder \"{marker}\", which is not a credential "
                    + "this session knows. Placeholders must not travel through edits; use the real "
                    + "text or leave that part out.",
            IsError: true
        );
    }

    private static string? FirstPlaceholderName(string text)
    {
        IReadOnlyList<string> names = CredentialRedactor.PlaceholderNamesIn(text);
        return names.Count > 0 ? names[0] : null;
    }

    private static bool HoldsAny(string fileText, IReadOnlyList<string> values) =>
        values.Any(value => fileText.Contains(value, StringComparison.Ordinal));

    private static ToolResult Refuse(string name, string relativePath) =>
        new(WriteMessage(name, relativePath), IsError: true);

    private static string WriteMessage(string name, string relativePath) =>
        $"Refused: {relativePath} currently holds the real {name} credential and the content would "
        + $"place \"{CredentialRedactor.Marker(name)}\" in that position. Writing this would "
        + "destroy the credential — edit around that line, or leave this write out; the file "
        + "already has the value the model cannot see.";
}
