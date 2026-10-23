using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The command sources extensions registered, in registration order. Registration is registration
/// only; descriptions are cached by the loader at load time (the manifest cross-check asks once),
/// and every registration leaves with its extension on unload.
/// </summary>
public sealed class CommandSourceRegistry
{
    private readonly IExtensionLog _log;
    private readonly object _gate = new();
    private readonly List<Registration> _registrations = [];

    public CommandSourceRegistry(IExtensionLog? log = null) =>
        _log = log ?? NullExtensionLog.Instance;

    public void Register(string extensionId, ICommandSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceId);
        lock (_gate)
        {
            if (
                _registrations.Any(registration =>
                    string.Equals(registration.ExtensionId, extensionId, StringComparison.Ordinal)
                    && string.Equals(
                        registration.Source.SourceId,
                        source.SourceId,
                        StringComparison.Ordinal
                    )
                )
            )
            {
                _log.Warn(
                    $"commands: extension '{extensionId}' registered source '{source.SourceId}' more than once; every registration contributes."
                );
            }

            _registrations.Add(new Registration(extensionId, source));
        }
    }

    /// <summary>The registered sources and their cached descriptions, in registration order.</summary>
    public IReadOnlyList<RegisteredCommandSource> List()
    {
        lock (_gate)
        {
            return
            [
                .. _registrations.Select(registration => new RegisteredCommandSource(
                    registration.ExtensionId,
                    registration.Source,
                    registration.Descriptors
                )),
            ];
        }
    }

    /// <summary>Stores the descriptors the loader cross-checked against the manifest.</summary>
    internal void SetDescriptors(
        ICommandSource source,
        IReadOnlyList<CommandDescriptor> descriptors
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(descriptors);
        lock (_gate)
        {
            for (var index = 0; index < _registrations.Count; index++)
            {
                if (ReferenceEquals(_registrations[index].Source, source))
                {
                    _registrations[index] = _registrations[index] with
                    {
                        Descriptors = descriptors,
                    };
                }
            }
        }
    }

    /// <summary>Removes every source the extension registered.</summary>
    public void Unregister(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        lock (_gate)
        {
            _registrations.RemoveAll(registration =>
                string.Equals(registration.ExtensionId, extensionId, StringComparison.Ordinal)
            );
        }
    }

    private sealed record Registration(string ExtensionId, ICommandSource Source)
    {
        public IReadOnlyList<CommandDescriptor> Descriptors { get; init; } = [];
    }
}

/// <summary>One registered command source, its owning extension and the load-time descriptors.</summary>
public sealed record RegisteredCommandSource(
    string ExtensionId,
    ICommandSource Source,
    IReadOnlyList<CommandDescriptor> Descriptors
);
