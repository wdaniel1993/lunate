using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// The session's one command registry: it asks every source what it offers once per composition,
/// orders the entries deterministically (built-ins, skills, extensions, templates; then source
/// order, then descriptor order), keeps the first entry for a duplicate name and is the only input
/// to <c>/</c> autocomplete, dispatch and <c>/help</c>.
/// </summary>
internal sealed class CommandRegistry
{
    private readonly IReadOnlyList<CommandEntry> _entries;
    private readonly Dictionary<string, CommandEntry> _byName;
    private readonly IExtensionLog? _log;

    private CommandRegistry(
        IReadOnlyList<CommandEntry> entries,
        Dictionary<string, CommandEntry> byName,
        IExtensionLog? log
    )
    {
        _entries = entries;
        _byName = byName;
        _log = log;
        Descriptors = [.. entries.Select(entry => entry.Descriptor)];
        CompletionItems = CompletionRegistry.Compose(Descriptors);
    }

    /// <summary>An empty registry; a session replaces it during composition.</summary>
    public static CommandRegistry Empty { get; } = new([], new(StringComparer.Ordinal), null);

    /// <summary>The merged descriptors, in their pinned order.</summary>
    public IReadOnlyList<CommandDescriptor> Descriptors { get; }

    /// <summary>The autocomplete rows composed from the merged descriptors.</summary>
    public IReadOnlyList<CompletionItem> CompletionItems { get; }

    /// <summary>
    /// Composes the registry. A source that throws while describing is logged and skipped; a
    /// duplicate name keeps the first entry after ordering and is logged once.
    /// </summary>
    public static async ValueTask<CommandRegistry> ComposeAsync(
        IReadOnlyList<ICommandSource> sources,
        IExtensionLog? log = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(sources);
        IExtensionLog? resolvedLog = log;
        List<Candidate> candidates = [];
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            ICommandSource source = sources[sourceIndex];
            IReadOnlyList<CommandDescriptor>? descriptors;
            try
            {
                descriptors = await source.DescribeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                resolvedLog?.Warn(
                    $"command source '{source.SourceId}' could not describe its commands and was skipped: {exception.Message}"
                );
                continue;
            }

            if (descriptors is null)
            {
                resolvedLog?.Warn(
                    $"command source '{source.SourceId}' returned no descriptor list and was skipped."
                );
                continue;
            }

            for (var descriptorIndex = 0; descriptorIndex < descriptors.Count; descriptorIndex++)
            {
                candidates.Add(
                    new Candidate(
                        descriptors[descriptorIndex],
                        source,
                        sourceIndex,
                        descriptorIndex
                    )
                );
            }
        }

        candidates.Sort(
            static (left, right) =>
            {
                int rank = Rank(left.Descriptor.Kind).CompareTo(Rank(right.Descriptor.Kind));
                if (rank != 0)
                {
                    return rank;
                }

                int source = left.SourceIndex.CompareTo(right.SourceIndex);
                return source != 0 ? source : left.DescriptorIndex.CompareTo(right.DescriptorIndex);
            }
        );

        List<CommandEntry> entries = [];
        Dictionary<string, CommandEntry> byName = new(StringComparer.Ordinal);
        foreach (Candidate candidate in candidates)
        {
            string name = candidate.Descriptor.Name.TrimStart('/');
            if (name.Length == 0 || name.Any(char.IsWhiteSpace))
            {
                resolvedLog?.Warn(
                    $"command source '{candidate.Source.SourceId}' offered the invalid name '{candidate.Descriptor.Name}' and it was dropped."
                );
                continue;
            }

            CommandDescriptor descriptor = candidate.Descriptor with
            {
                Name = name,
                Description = candidate.Descriptor.Description ?? string.Empty,
                SourceId = string.IsNullOrWhiteSpace(candidate.Descriptor.SourceId)
                    ? candidate.Source.SourceId
                    : candidate.Descriptor.SourceId,
            };
            var entry = new CommandEntry(descriptor, candidate.Source);
            if (byName.TryGetValue(name, out CommandEntry? existing))
            {
                resolvedLog?.Warn(
                    $"command '/{name}' from source '{descriptor.SourceId}' is skipped: source '{existing.Descriptor.SourceId}' already provides it (first source wins)."
                );
                continue;
            }

            byName[name] = entry;
            entries.Add(entry);
        }

        return new CommandRegistry(entries, byName, resolvedLog);
    }

    /// <summary>Composes the registry with the default log.</summary>
    public static ValueTask<CommandRegistry> ComposeAsync(
        IReadOnlyList<ICommandSource> sources,
        CancellationToken cancellationToken
    ) => ComposeAsync(sources, null, cancellationToken);

    /// <summary>
    /// Invokes the source that owns <paramref name="invocation"/>. An unknown name is a notice; a
    /// handler exception is logged and returned as an error result; cancellation propagates.
    /// </summary>
    public async ValueTask<CommandResult> InvokeAsync(
        CommandInvocation invocation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(invocation);
        string name = invocation.Name.TrimStart('/');
        if (!_byName.TryGetValue(name, out CommandEntry? entry))
        {
            // Known skills own their exact names; an unknown skill still falls through to the
            // built-in placeholder so it keeps today's "unknown skill" notice.
            if (
                name.StartsWith("skill:", StringComparison.Ordinal)
                && _byName.TryGetValue(Commands.SkillName.TrimStart('/'), out CommandEntry? skill)
            )
            {
                entry = skill;
            }
            else
            {
                return CommandResult.Notice("unknown command: /" + name);
            }
        }

        var normalized = new CommandInvocation(name, invocation.Argument);
        try
        {
            return await entry
                .Source.InvokeAsync(normalized, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log?.Warn(
                $"command '/{name}' from source '{entry.Source.SourceId}' failed: {exception.Message}"
            );
            return CommandResult.Error($"command /{name} failed: {exception.Message}");
        }
    }

    /// <summary>The merge order: built-ins, skills, extensions, templates.</summary>
    private static int Rank(CommandKind kind) =>
        kind switch
        {
            CommandKind.Builtin => 0,
            CommandKind.Skill => 1,
            CommandKind.Extension => 2,
            CommandKind.Template => 3,
            _ => 4,
        };

    private readonly record struct Candidate(
        CommandDescriptor Descriptor,
        ICommandSource Source,
        int SourceIndex,
        int DescriptorIndex
    );

    private sealed record CommandEntry(CommandDescriptor Descriptor, ICommandSource Source);
}
