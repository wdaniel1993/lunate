using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The declaration-level model provider registry: extensions declare providers in their manifests,
/// the host registers the declarations on load and lists them namespaced as
/// <c>ext/&lt;extension-id&gt;/&lt;id&gt;</c>. Duplicate provider ids across extensions are refused.
/// Secrets are referenced by name only; values never reach the registry.
/// </summary>
public sealed class ModelProviderRegistry
{
    private readonly object _gate = new();
    private readonly List<RegisteredModelProvider> _providers = [];

    public void Register(string extensionId, IReadOnlyList<ModelProviderDescriptor> providers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(providers);
        lock (_gate)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (ModelProviderDescriptor provider in providers)
            {
                if (!IsValidId(provider.Id))
                {
                    throw new InvalidOperationException(
                        $"extension '{extensionId}': model provider id '{provider.Id}' must match [a-z0-9-]+."
                    );
                }

                RegisteredModelProvider? existing = _providers.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, provider.Id, StringComparison.Ordinal)
                );
                if (existing is not null || !seen.Add(provider.Id))
                {
                    string owner = existing?.ExtensionId ?? extensionId;
                    throw new InvalidOperationException(
                        $"model provider id '{provider.Id}' is already declared by extension '{owner}'; extension '{extensionId}' cannot declare it again."
                    );
                }
            }

            foreach (ModelProviderDescriptor provider in providers)
            {
                _providers.Add(
                    new RegisteredModelProvider(
                        provider.Id,
                        $"ext/{extensionId}/{provider.Id}",
                        extensionId,
                        provider.DisplayName,
                        provider.Endpoint,
                        provider.SecretName,
                        provider.ModelIds
                    )
                );
            }
        }
    }

    public IReadOnlyList<RegisteredModelProvider> List()
    {
        lock (_gate)
        {
            return [.. _providers];
        }
    }

    public void Unregister(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        lock (_gate)
        {
            _providers.RemoveAll(provider =>
                string.Equals(provider.ExtensionId, extensionId, StringComparison.Ordinal)
            );
        }
    }

    private static bool IsValidId(string id) =>
        id.Length > 0
        && id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}

/// <summary>A declared model provider as the registry lists it; the secret is a name only.</summary>
public sealed record RegisteredModelProvider(
    string Id,
    string NamespacedId,
    string ExtensionId,
    string DisplayName,
    string Endpoint,
    string SecretName,
    IReadOnlyList<string> ModelIds
);
