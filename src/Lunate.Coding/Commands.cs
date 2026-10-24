using Lunate.Extensibility.Abstractions;

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
/// description (no trailing period). This is the built-in source's table; the registry is what
/// dispatch, completion and <c>/help</c> consume.
/// </summary>
internal sealed record BuiltinCommandDescriptor(
    string Name,
    CommandGroup Group,
    string Description
);

/// <summary>
/// The client-side slash commands: classification, parsing and the built-in table. A submitted
/// line whose first non-space character is <c>/</c> is a command; it never reaches the model and
/// skips the input pipeline.
/// </summary>
internal static class Commands
{
    /// <summary>The skill invocation's typed prefix; the name after it selects the skill.</summary>
    public const string SkillPrefix = "/skill:";

    /// <summary>The skill invocation's registry name (its argument form, not a completion word).</summary>
    public const string SkillName = SkillPrefix + "<name>";

    /// <summary>The built-in source's registry id.</summary>
    public const string BuiltinSourceId = "builtin";

    /// <summary>
    /// Every built-in command's metadata in listing order. The built-in source maps it to the
    /// public descriptors; a command that is dispatchable without an entry here fails
    /// <c>CommandRegistryTests</c>.
    /// </summary>
    public static readonly IReadOnlyList<BuiltinCommandDescriptor> Descriptors =
    [
        new(
            "/compact",
            CommandGroup.Session,
            "compact the conversation now, optionally focusing on <text>"
        ),
        new("/clone", CommandGroup.Session, "copy the whole session into a new session file"),
        new("/context", CommandGroup.Session, "show the context window and subagent budgets"),
        new("/fork", CommandGroup.Session, "start a new session file at a picked entry"),
        new("/help", CommandGroup.Session, "list the commands"),
        new("/name", CommandGroup.Session, "set or show the session title"),
        new("/new", CommandGroup.Session, "start a fresh session"),
        new("/resume", CommandGroup.Session, "open the session picker"),
        new("/rewind", CommandGroup.Session, "move the active leaf to a picked entry"),
        new("/session", CommandGroup.Session, "show the session file, messages and tokens"),
        new("/thinking", CommandGroup.Session, "set the reasoning level (off|low|medium|high)"),
        new("/tree", CommandGroup.Session, "browse the session tree and pick a message"),
        new("/favorite", CommandGroup.Model, "add the active or named model to favorites"),
        new("/login", CommandGroup.Model, "store an API key for a provider preset"),
        new("/logout", CommandGroup.Model, "remove the stored API key for a provider preset"),
        new("/model", CommandGroup.Model, "pick a model or switch to one by id"),
        new("/unfavorite", CommandGroup.Model, "remove the active or named model from favorites"),
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

    /// <summary>The built-in source's public descriptors, names without the slash, in table order.</summary>
    public static IReadOnlyList<CommandDescriptor> PublicDescriptors { get; } =
    [
        .. Descriptors.Select(descriptor => new CommandDescriptor(
            descriptor.Name.TrimStart('/'),
            descriptor.Description,
            ArgumentHint: null,
            SourceId: BuiltinSourceId,
            Kind: CommandKind.Builtin
        )),
    ];

    /// <summary>The bare built-in names the loader reserves against extension collisions.</summary>
    public static IReadOnlyList<string> BuiltinNames { get; } =
    [.. PublicDescriptors.Select(descriptor => descriptor.Name)];

    /// <summary>The <c>/help</c> group of a built-in bare name.</summary>
    public static CommandGroup GroupOf(string bareName) =>
        Descriptors
            .FirstOrDefault(descriptor =>
                string.Equals(descriptor.Name, "/" + bareName, StringComparison.Ordinal)
            )
            ?.Group
        ?? CommandGroup.Extensions;

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
