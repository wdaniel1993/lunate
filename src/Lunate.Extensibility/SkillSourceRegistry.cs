using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The skill sources extensions registered, keyed by source id. Registration is registration only;
/// a duplicate id resolves to the last registration, with the conflict logged.
/// </summary>
public sealed class SkillSourceRegistry
{
    private readonly IExtensionLog _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, Registration> _sources = new(StringComparer.Ordinal);

    public SkillSourceRegistry(IExtensionLog? log = null) =>
        _log = log ?? NullExtensionLog.Instance;

    public void Register(string extensionId, ISkillSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Id);
        Registration registration = new(extensionId, source);
        Registration? replaced;
        lock (_gate)
        {
            _sources.TryGetValue(source.Id, out replaced);
            _sources[source.Id] = registration;
        }

        if (replaced is not null)
        {
            _log.Warn(
                $"skills: source '{source.Id}' from extension '{extensionId}' replaces the source from extension '{replaced.ExtensionId}'; the last registration wins."
            );
        }
    }

    /// <summary>The registered sources, in registration order (replaced ids keep their slot).</summary>
    public IReadOnlyList<RegisteredSkillSource> List()
    {
        lock (_gate)
        {
            return
            [
                .. _sources.Values.Select(registration => new RegisteredSkillSource(
                    registration.ExtensionId,
                    registration.Source
                )),
            ];
        }
    }

    /// <summary>Removes every source the extension registered.</summary>
    public void Unregister(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        lock (_gate)
        {
            string[] dropped =
            [
                .. _sources
                    .Where(pair =>
                        string.Equals(pair.Value.ExtensionId, extensionId, StringComparison.Ordinal)
                    )
                    .Select(pair => pair.Key),
            ];
            foreach (string id in dropped)
            {
                _sources.Remove(id);
            }
        }
    }

    private sealed record Registration(string ExtensionId, ISkillSource Source);
}

/// <summary>One registered source and the extension that registered it.</summary>
public sealed record RegisteredSkillSource(string ExtensionId, ISkillSource Source);
