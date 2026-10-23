namespace Lunate.Extensibility.Abstractions;

/// <summary>What kind of source contributed a command; the registry orders by kind first.</summary>
public enum CommandKind
{
    /// <summary>A command the CLI ships (the first source, shown by <c>/help</c>).</summary>
    Builtin,

    /// <summary>A discovered skill, invoked as <c>skill:&lt;name&gt;</c>.</summary>
    Skill,

    /// <summary>A prompt-template command; the slot is reserved, no template source ships yet.</summary>
    Template,

    /// <summary>A command an extension provides.</summary>
    Extension,
}

/// <summary>
/// One command a source offers. <see cref="Name"/> is the name as typed without the leading slash
/// (a skill is <c>skill:&lt;name&gt;</c>); <see cref="ArgumentHint"/> is optional display text.
/// </summary>
public sealed record CommandDescriptor(
    string Name,
    string Description,
    string? ArgumentHint,
    string SourceId,
    CommandKind Kind
);

/// <summary>What a source is asked to run: the bare command name and the argument remainder.</summary>
public sealed record CommandInvocation(string Name, string Argument);

/// <summary>
/// The outcome of an invocation: at most one user-facing message. <see cref="IsError"/> selects the
/// error notice level; a source may act through its own host callback and return
/// <see cref="None"/> when it has nothing to say.
/// </summary>
public sealed record CommandResult(string? Message, bool IsError)
{
    /// <summary>Handled with no notice.</summary>
    public static CommandResult None { get; } = new((string?)null, false);

    /// <summary>Handled with an informational notice.</summary>
    public static CommandResult Notice(string message) => new(message, false);

    /// <summary>Failed with an error notice; the caller also logs the failure.</summary>
    public static CommandResult Error(string message) => new(message, true);
}

/// <summary>
/// One source of slash commands. The host asks every source what it offers once per composition
/// and invokes the source that owns a submitted name. Both calls are asynchronous and cancellable;
/// registration is registration only and must not start processes, sockets or timers.
/// </summary>
public interface ICommandSource
{
    /// <summary>The source's id; diagnostics name it next to the owning extension.</summary>
    string SourceId { get; }

    /// <summary>The commands this source currently offers, in display order.</summary>
    ValueTask<IReadOnlyList<CommandDescriptor>> DescribeAsync(CancellationToken cancellationToken);

    /// <summary>Runs one command this source owns.</summary>
    ValueTask<CommandResult> InvokeAsync(
        CommandInvocation invocation,
        CancellationToken cancellationToken
    );
}
