namespace Lunate.Coding;

/// <summary>
/// The client-side slash commands: classification, parsing and the built-in table used by the
/// dispatcher and Tab completion. A submitted line whose first non-space character is <c>/</c> is
/// a command; it never reaches the model and skips the input pipeline.
/// </summary>
internal static class Commands
{
    /// <summary>The built-in command names in ordinal order (completion notices sort them).</summary>
    public static readonly IReadOnlyList<string> BuiltIn =
    [
        "/compact",
        "/model",
        "/new",
        "/quit",
        "/resume",
    ];

    /// <summary>Whether the first non-space character of the submitted line is <c>/</c>.</summary>
    public static bool IsCommand(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (char character in text)
        {
            if (!char.IsWhiteSpace(character))
            {
                return character == '/';
            }
        }

        return false;
    }

    /// <summary>Splits the raw line into the command name and its optional argument.</summary>
    public static (string Name, string Argument) Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string trimmed = text.Trim();
        int separator = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return separator < 0
            ? (trimmed, string.Empty)
            : (trimmed[..separator], trimmed[(separator + 1)..].Trim());
    }
}
