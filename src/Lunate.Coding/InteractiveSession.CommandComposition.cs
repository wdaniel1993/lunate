using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>
    /// Composes the command registry for this session: built-ins first, then the discovered skills,
    /// then the loaded extensions' sources, then the template slot when a template source lands.
    /// Runs after skill discovery, at session start and on every reload, so the registry,
    /// autocomplete, dispatch and <c>/help</c> always describe the same command set.
    /// </summary>
    private async Task ComposeCommandsAsync(CancellationToken ct)
    {
        List<ICommandSource> sources =
        [
            new BuiltinCommandSource(this),
            new SkillCommandSource(
                _skills?.Skills ?? [],
                (name, arguments, token) => new ValueTask(InvokeSkillAsync(name, arguments, token))
            ),
        ];
        if (_extensions is { } host)
        {
            sources.AddRange(host.CommandSources());
        }

        _commands = await CommandRegistry
            .ComposeAsync(sources, _extensions?.Log, ct)
            .ConfigureAwait(false);
    }
}
