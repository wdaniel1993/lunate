namespace Lunate.Coding;

/// <summary>The <c>/help</c> group a command belongs to (design D3).</summary>
internal enum CommandGroup
{
    Session,
    Model,
    Extensions,
    Files,
    Quit,
}

/// <summary>
/// One built-in command's metadata: the name as typed, its <c>/help</c> group and its one-line
/// description (no trailing period). The single source for the <c>/help</c> listing; the dispatch
/// switch stays control flow.
/// </summary>
internal sealed record CommandDescriptor(string Name, CommandGroup Group, string Description);

/// <summary>
/// The client-side slash commands: classification, parsing and the built-in table used by the
/// dispatcher, Tab completion and <c>/help</c>. A submitted line whose first non-space character is
/// <c>/</c> is a command; it never reaches the model and skips the input pipeline.
/// </summary>
internal static class Commands
{
    /// <summary>The skill invocation's typed prefix; the name after it selects the skill.</summary>
    public const string SkillPrefix = "/skill:";

    /// <summary>The skill invocation's registry name (its argument form, not a completion word).</summary>
    public const string SkillName = SkillPrefix + "<name>";

    /// <summary>
    /// Every built-in command's metadata in listing order; the <c>/help</c> listing groups it and a
    /// command that is dispatchable without an entry here fails <c>CommandRegistryTests</c>.
    /// </summary>
    public static readonly IReadOnlyList<CommandDescriptor> Descriptors =
    [
        new("/compact", CommandGroup.Session, "compact the conversation now"),
        new("/context", CommandGroup.Session, "show the context window and subagent budgets"),
        new("/help", CommandGroup.Session, "list the commands"),
        new("/new", CommandGroup.Session, "start a fresh session"),
        new("/resume", CommandGroup.Session, "open the session picker"),
        new("/thinking", CommandGroup.Session, "set the reasoning level (off|low|medium|high)"),
        new("/model", CommandGroup.Model, "pick a model or switch to one by id"),
        new(
            "/extensions",
            CommandGroup.Extensions,
            "list the discovered extensions with their state"
        ),
        new("/mcp", CommandGroup.Extensions, "list the MCP servers with their status and source"),
        new(SkillName, CommandGroup.Extensions, "invoke a discovered skill by name"),
        new(
            "/skills",
            CommandGroup.Extensions,
            "list the discovered skills with their scope and state"
        ),
        new("/trust", CommandGroup.Extensions, "show or clear this repository's trust records"),
        new("/quit", CommandGroup.Quit, "quit"),
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
