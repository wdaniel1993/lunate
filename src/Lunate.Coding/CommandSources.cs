using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The discovered skills as a command source: every valid, trusted skill is described as
/// <c>skill:&lt;name&gt;</c> and invoked through the session's existing skill path. Invalid and
/// untrusted entries stay out of the descriptor list, exactly as they stayed out of completion.
/// </summary>
internal sealed class SkillCommandSource(
    IReadOnlyList<SkillDescriptor> skills,
    Func<string, string, CancellationToken, ValueTask> invoke
) : ICommandSource
{
    /// <summary>The source's registry id.</summary>
    public const string SourceIdValue = "skills";

    public string SourceId => SourceIdValue;

    public ValueTask<IReadOnlyList<CommandDescriptor>> DescribeAsync(
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<CommandDescriptor> descriptors =
        [
            .. skills
                .Where(skill => skill.Valid && !skill.Untrusted)
                .Select(skill => new CommandDescriptor(
                    Commands.SkillPrefix.TrimStart('/') + skill.Name,
                    skill.Description,
                    ArgumentHint: "[arguments]",
                    SourceId: SourceIdValue,
                    Kind: CommandKind.Skill
                )),
        ];
        return ValueTask.FromResult(descriptors);
    }

    public async ValueTask<CommandResult> InvokeAsync(
        CommandInvocation invocation,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(invocation);
        string prefix = Commands.SkillPrefix.TrimStart('/');
        string name = invocation.Name.StartsWith(prefix, StringComparison.Ordinal)
            ? invocation.Name[prefix.Length..]
            : invocation.Name;
        await invoke(name, invocation.Argument, cancellationToken).ConfigureAwait(false);
        return CommandResult.None;
    }
}

/// <summary>
/// One loaded extension's registered source, exposing the descriptors the loader already
/// cross-checked and stamped (extension id, <see cref="CommandKind.Extension"/>). Invocation goes
/// to the extension's own source.
/// </summary>
internal sealed class ExtensionCommandSource(
    string extensionId,
    ICommandSource source,
    IReadOnlyList<CommandDescriptor> descriptors
) : ICommandSource
{
    public string SourceId => extensionId;

    public ValueTask<IReadOnlyList<CommandDescriptor>> DescribeAsync(
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(descriptors);

    public ValueTask<CommandResult> InvokeAsync(
        CommandInvocation invocation,
        CancellationToken cancellationToken
    ) => source.InvokeAsync(invocation, cancellationToken);
}
