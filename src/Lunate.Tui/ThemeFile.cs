using System.Text.Json;

namespace Lunate.Tui;

/// <summary>
/// A user theme file's outcome: the tokens it maps, and the one line naming why it could not be
/// applied. A file that fails anywhere is discarded whole — the selected built-in keeps every
/// token, never a half-applied file — so the session starts with a theme it can name.
/// </summary>
internal sealed record ThemeOutcome(
    IReadOnlyDictionary<ThemeToken, ThemeColor> Tokens,
    string? Error
)
{
    private static readonly IReadOnlyDictionary<ThemeToken, ThemeColor> NoTokens =
        new Dictionary<ThemeToken, ThemeColor>();

    /// <summary>No file, or a file that was applied: nothing to report.</summary>
    internal static ThemeOutcome Applied(IReadOnlyDictionary<ThemeToken, ThemeColor> tokens) =>
        new(tokens, Error: null);

    /// <summary>The file's tokens do not apply; <paramref name="error"/> is the one line to show.</summary>
    internal static ThemeOutcome Failed(string error) => new(NoTokens, error);
}

/// <summary>
/// The user theme file — <c>~/.lunate/theme.json</c>, a map of semantic tokens to colours. The path
/// is chosen once: <c>--theme &lt;path&gt;</c> when the flag names a file rather than a built-in,
/// otherwise <c>theme.json</c> in the Lunate home (the store <c>$LUNATE_HOME</c> moves, like every
/// other state file, so a test or a portable run cannot read the user's real configuration).
/// A missing file leaves the selected theme alone; a file that cannot be read, parsed or applied is
/// discarded whole with one line, and the selected built-in stands.
/// </summary>
internal static class ThemeFile
{
    internal const string FileName = "theme.json";

    /// <summary>The file this run reads; <paramref name="flagPath"/> is <c>--theme</c>'s value.</summary>
    internal static string ResolvePath(string? flagPath, string home) =>
        string.IsNullOrWhiteSpace(flagPath) ? Path.Combine(home, FileName) : flagPath;

    /// <summary>
    /// Reads and applies <paramref name="path"/>. An unknown token fails the whole load with the
    /// valid tokens listed, and a malformed colour names the token and the accepted forms; in both
    /// cases nothing of the file is applied.
    /// </summary>
    internal static ThemeOutcome Read(string path)
    {
        if (!File.Exists(path))
        {
            // A path that exists but is not a file — a directory, typically — was selected on
            // purpose, so saying "no theme file" would leave the user believing one applies.
            return Path.Exists(path)
                ? ThemeOutcome.Failed(
                    $"theme: {path} is not a file, so no token can be read from it"
                )
                : ThemeOutcome.Applied(new Dictionary<ThemeToken, ThemeColor>());
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ThemeOutcome.Failed($"theme: {path}: {exception.Message}");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return ThemeOutcome.Failed($"theme: {path} is not readable JSON: {exception.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ThemeOutcome.Failed(
                    $"theme: {path} must be an object of tokens; the valid tokens are "
                        + ThemeTokens.List
                );
            }

            var tokens = new Dictionary<ThemeToken, ThemeColor>();
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!ThemeTokens.TryParse(property.Name, out ThemeToken token))
                {
                    return ThemeOutcome.Failed(
                        $"theme: {path} names the unknown token '{property.Name}'; "
                            + $"the valid tokens are {ThemeTokens.List}"
                    );
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return ThemeOutcome.Failed(
                        $"theme: {path}: token '{property.Name}' must be a string colour"
                    );
                }

                if (
                    !ThemeColor.TryParse(
                        property.Value.GetString() ?? string.Empty,
                        out ThemeColor color,
                        out string? problem
                    )
                )
                {
                    return ThemeOutcome.Failed($"theme: {path}: token '{property.Name}' {problem}");
                }

                tokens[token] = color;
            }

            return ThemeOutcome.Applied(tokens);
        }
    }
}
