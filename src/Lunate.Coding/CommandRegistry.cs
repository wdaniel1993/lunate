using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// The session's one command registry: it asks every source what it offers once per composition,
/// orders the entries deterministically (built-ins, skills, extensions, templates; then source
/// order, then descriptor order), keeps the first entry for a duplicate name and is the only input
/// to <c>/</c> autocomplete, dispatch and <c>/help</c>. Every entry carries one source-qualified id
/// (<see cref="CommandId"/>) that the diagnostics, log lines and failure notices use.
/// </summary>
internal sealed class CommandRegistry
{
    private readonly IReadOnlyList<CommandEntry> _entries;
    private readonly Dictionary<string, CommandEntry> _byName;
    private readonly IExtensionLog? _log;

    private CommandRegistry(
        IReadOnlyList<CommandEntry> entries,
        Dictionary<string, CommandEntry> byName,
        IReadOnlyList<CommandShadow> shadowed,
        IExtensionLog? log
    )
    {
        _entries = entries;
        _byName = byName;
        Shadowed = shadowed;
        _log = log;
        Descriptors = [.. entries.Select(entry => entry.Descriptor)];
        CompletionItems = CompletionRegistry.Compose(Descriptors);
    }

    /// <summary>An empty registry; a session replaces it during composition.</summary>
    public static CommandRegistry Empty { get; } = new([], new(StringComparer.Ordinal), [], null);

    /// <summary>The merged descriptors, in their pinned order.</summary>
    public IReadOnlyList<CommandDescriptor> Descriptors { get; }

    /// <summary>The autocomplete rows composed from the merged descriptors.</summary>
    public IReadOnlyList<CompletionItem> CompletionItems { get; }

    /// <summary>The live entries, in their pinned order; each carries its source-qualified id.</summary>
    internal IReadOnlyList<CommandEntry> Entries => _entries;

    /// <summary>
    /// The commands a duplicate name dropped, in composition order: the shadowed id and the id
    /// that owns the name. They are not live — never listed or dispatched — and the extension view
    /// reports them as <c>shadowed by &lt;id&gt;</c>.
    /// </summary>
    internal IReadOnlyList<CommandShadow> Shadowed { get; }

    /// <summary>
    /// Composes the registry. A source that throws while describing is logged and skipped; a
    /// duplicate name keeps the first entry after ordering and is logged once, naming both ids.
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
        List<CommandShadow> shadowed = [];
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
            // The id is computed before the duplicate check, so the losing candidate can name
            // itself in the diagnostic even though it never becomes an entry.
            string id = CommandId.For(descriptor);
            if (byName.TryGetValue(name, out CommandEntry? existing))
            {
                shadowed.Add(new CommandShadow(name, descriptor.SourceId, id, existing.Id));
                resolvedLog?.Warn(
                    $"command '/{name}' from source '{descriptor.SourceId}' is skipped: '{existing.Id}' (source '{existing.Descriptor.SourceId}') already owns the name; '{id}' is shadowed (first source wins)."
                );
                continue;
            }

            var entry = new CommandEntry(id, descriptor, candidate.Source);
            byName[name] = entry;
            entries.Add(entry);
        }

        return new CommandRegistry(entries, byName, shadowed, resolvedLog);
    }

    /// <summary>Composes the registry with the default log.</summary>
    public static ValueTask<CommandRegistry> ComposeAsync(
        IReadOnlyList<ICommandSource> sources,
        CancellationToken cancellationToken
    ) => ComposeAsync(sources, null, cancellationToken);

    /// <summary>
    /// Invokes the source that owns <paramref name="invocation"/>. An unknown name is a notice; a
    /// handler exception is logged and returned as a teaching error notice naming the command's
    /// id, source, reason and recovery step; caller cancellation propagates.
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
                $"command {entry.Id} from source '{entry.Descriptor.SourceId}' failed: {exception.Message}"
            );
            return CommandResult.Error(FailureNotice(entry, exception));
        }
    }

    /// <summary>
    /// The teaching error notice (design D4): the command's id, the reason and one recovery step
    /// chosen by kind. A built-in keeps the shorter form — there is no external source to inspect —
    /// and a template has no user-facing index to point at. Never a stack trace: the log entry
    /// carries the detail.
    /// </summary>
    private static string FailureNotice(CommandEntry entry, Exception exception) =>
        entry.Descriptor.Kind switch
        {
            CommandKind.Extension => FormattableString.Invariant(
                $"command {entry.Id} failed: {exception.Message} — see /extensions for the extension's state"
            ),
            CommandKind.Skill => FormattableString.Invariant(
                $"command {entry.Id} failed: {exception.Message} — see /skills for the skill's state"
            ),
            CommandKind.Template => FormattableString.Invariant(
                $"command {entry.Id} failed: {exception.Message}"
            ),
            _ => FormattableString.Invariant(
                $"command /{entry.Descriptor.Name} failed: {exception.Message}"
            ),
        };

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
}

/// <summary>One live registry entry: its source-qualified id, its descriptor and its source.</summary>
internal sealed record CommandEntry(string Id, CommandDescriptor Descriptor, ICommandSource Source);

/// <summary>
/// One command a duplicate name dropped: the name, the losing source, the losing id and the id
/// that owns the name (design D2; the extension view reports it as <c>shadowed by &lt;id&gt;</c>).
/// </summary>
internal sealed record CommandShadow(
    string Name,
    string SourceId,
    string ShadowedId,
    string OwnerId
);
