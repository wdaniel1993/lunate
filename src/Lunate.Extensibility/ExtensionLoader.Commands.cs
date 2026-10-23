using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed partial class ExtensionLoader
{
    private readonly Dictionary<string, IReadOnlyList<string>> _declaredNotProvidedCommands = new(
        StringComparer.Ordinal
    );

    /// <summary>
    /// The manifest commands of one loaded extension that no registered source provided. They are
    /// not runnable; the <c>/extensions</c> view reports them as <c>declared, not provided</c>.
    /// </summary>
    public IReadOnlyList<string> DeclaredNotProvidedCommands(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        return _declaredNotProvidedCommands.TryGetValue(
            extensionId,
            out IReadOnlyList<string>? commands
        )
            ? commands
            : Array.Empty<string>();
    }

    /// <summary>
    /// Enforces the manifest command rules after the factory ran: every provided command must be
    /// declared, no provided name may collide with a reserved (built-in) name, and every declared
    /// command no source provided is logged and recorded for the view. Descriptions are cached on
    /// the registration so composition never asks an extension twice.
    /// </summary>
    private async Task CrossCheckManifestCommandsAsync(
        ExtensionDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        HashSet<string> declared = new(
            descriptor.Manifest.Commands.Select(name => name.TrimStart('/')),
            StringComparer.Ordinal
        );
        HashSet<string> reserved = new(
            _options.ReservedCommands.Select(name => name.TrimStart('/')),
            StringComparer.Ordinal
        );
        HashSet<string> provided = new(StringComparer.Ordinal);
        List<string> notProvided = [];

        foreach (
            RegisteredCommandSource registered in _commandSources
                .List()
                .Where(registration =>
                    string.Equals(registration.ExtensionId, descriptor.Id, StringComparison.Ordinal)
                )
        )
        {
            IReadOnlyList<CommandDescriptor> described;
            try
            {
                described = await registered
                    .Source.DescribeAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ExtensionLoadException(
                    $"extension '{descriptor.Id}': command source '{registered.Source.SourceId}' failed to describe its commands: {exception.Message}",
                    exception
                );
            }

            List<CommandDescriptor> stamped = [];
            foreach (CommandDescriptor command in described)
            {
                if (
                    string.IsNullOrWhiteSpace(command.Name)
                    || command.Name.StartsWith('/')
                    || command.Name.Any(char.IsWhiteSpace)
                )
                {
                    throw new ExtensionLoadException(
                        $"extension '{descriptor.Id}': command name '{command.Name}' is invalid; use a bare name without a slash or whitespace."
                    );
                }

                if (reserved.Contains(command.Name))
                {
                    throw new ExtensionLoadException(
                        $"extension '{descriptor.Id}': command '/{command.Name}' collides with the built-in command '/{command.Name}'; rename the extension command or remove it from the manifest 'commands' list."
                    );
                }

                if (!declared.Contains(command.Name))
                {
                    throw new ExtensionLoadException(
                        $"extension '{descriptor.Id}': command '/{command.Name}' is provided by code but not declared in the manifest 'commands' list; the manifest must cover everything the code contributes. Declare it or remove the registration."
                    );
                }

                if (!provided.Add(command.Name))
                {
                    (_options.Log ?? NullExtensionLog.Instance).Warn(
                        $"extension '{descriptor.Id}': command '/{command.Name}' is provided by more than one source; the first source wins."
                    );
                }

                stamped.Add(
                    command with
                    {
                        Name = command.Name,
                        Description = command.Description ?? string.Empty,
                        SourceId = descriptor.Id,
                        Kind = CommandKind.Extension,
                    }
                );
            }

            _commandSources.SetDescriptors(registered.Source, stamped);
        }

        foreach (string name in declared)
        {
            if (provided.Contains(name))
            {
                continue;
            }

            (_options.Log ?? NullExtensionLog.Instance).Warn(
                $"extension '{descriptor.Id}': manifest declares command '/{name}' but no command source provided it; the command is not runnable."
            );
            notProvided.Add(name);
        }

        _declaredNotProvidedCommands[descriptor.Id] =
            notProvided.Count == 0 ? Array.Empty<string>() : notProvided;
    }
}
