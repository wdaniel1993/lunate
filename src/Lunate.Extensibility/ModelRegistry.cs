using System.Globalization;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility;

/// <summary>
/// The live model registry: extensions register providers, middleware and virtual models at load
/// time, manifests declare providers, and <see cref="Resolve"/> turns a model id into a client.
/// Resolution order is fixed: virtual model → programmatic provider → declared provider → session
/// catalog → teaching error listing the known ids. Registered ids are namespaced
/// <c>ext/&lt;extension-id&gt;/&lt;id&gt;</c> and an extension-registered model can never shadow a
/// catalog model id (collisions are refused at registration). Declared providers are instantiated
/// lazily into OpenAI-compatible clients; the host reads the secret value and it only ever reaches
/// the client's request headers (hygiene, not a security boundary — ADR-0017).
/// </summary>
public sealed partial class ModelRegistry : IModelRegistry
{
    internal const int MaxProvidersPerExtension = 8;
    internal const int MaxVirtualModelsPerExtension = 8;
    internal const int MaxMiddlewaresPerExtension = 16;
    internal const int MaxVirtualDepth = 8;
    internal const int KnownIdsShown = 20;

    private readonly object _gate = new();
    private readonly ModelCatalog? _catalog;
    private readonly IChatClientFactory? _clientFactory;
    private readonly List<RegisteredModelProvider> _declared = [];
    private readonly List<DeclaredEntry> _declaredEntries = [];
    private readonly List<ProgrammaticEntry> _programmatic = [];
    private readonly List<VirtualEntry> _virtuals = [];
    private readonly List<MiddlewareEntry> _middlewares = [];
    private readonly Dictionary<string, object> _entriesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _providerIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IChatClient> _clients = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _clientGates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _extensionOrder = new(StringComparer.Ordinal);
    private readonly HashSet<string> _validatedVirtuals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Capabilities> _capabilities = new(StringComparer.Ordinal);
    private int _extensionCounter;
    private int _registrationCounter;

    public ModelRegistry(ModelCatalog? catalog = null, IChatClientFactory? clientFactory = null)
    {
        _catalog = catalog;
        _clientFactory = clientFactory;
    }

    /// <summary>The declared model providers of the loaded extensions, in registration order.</summary>
    public IReadOnlyList<RegisteredModelProvider> List()
    {
        lock (_gate)
        {
            return [.. _declared];
        }
    }

    /// <summary>
    /// The capabilities of an extension-registered model id (virtual, programmatic or declared);
    /// null when the id is not extension-registered (catalog models included). A context window of
    /// 0 means unknown.
    /// </summary>
    public ModelCapabilities? FindCapabilities(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        return Find(modelId) switch
        {
            VirtualEntry virtualEntry => ToPublic(ValidateVirtualChain(virtualEntry)),
            DeclaredEntry declared => ToPublic(
                new Capabilities(
                    declared.Descriptor.ContextWindow,
                    declared.Descriptor.SupportsTools
                )
            ),
            ProgrammaticEntry => new ModelCapabilities(0, true),
            _ => null,
        };
    }

    /// <summary>Registers the providers an extension's manifest declares.</summary>
    public void RegisterDeclared(
        string extensionId,
        IReadOnlyList<ModelProviderDescriptor> providers,
        IExtensionSecrets secrets
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(secrets);

        lock (_gate)
        {
            ConfirmProviderCap(extensionId, providers.Count);
            ConfirmDeclaredBatch(extensionId, providers);
            foreach (ModelProviderDescriptor descriptor in providers)
            {
                var entry = new DeclaredEntry(extensionId, descriptor, secrets);
                _providerIds[descriptor.Id] = extensionId;
                _declared.Add(
                    new RegisteredModelProvider(
                        descriptor.Id,
                        $"ext/{extensionId}/{descriptor.Id}",
                        extensionId,
                        descriptor.DisplayName,
                        descriptor.Endpoint,
                        descriptor.SecretName,
                        descriptor.ModelIds,
                        descriptor.ContextWindow,
                        descriptor.SupportsTools
                    )
                );
                _declaredEntries.Add(entry);
                IndexModels(entry, descriptor.ModelIds);
            }
        }
    }

    /// <summary>Registers one programmatic provider of a loaded extension.</summary>
    public void RegisterProvider(string extensionId, ExtensionModelProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(provider);

        lock (_gate)
        {
            ConfirmProviderCap(extensionId, 1);
            ValidateLocalId(extensionId, provider.Id, "model provider");
            ConfirmProviderId(extensionId, provider.Id);
            ConfirmModelIds(extensionId, provider.Id, provider.ModelIds);
            var entry = new ProgrammaticEntry(extensionId, provider);
            _providerIds[provider.Id] = extensionId;
            _programmatic.Add(entry);
            IndexModels(entry, provider.ModelIds);
        }
    }

    /// <summary>Registers one middleware of a loaded extension.</summary>
    public void RegisterModelMiddleware(string extensionId, ExtensionModelMiddleware middleware)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(middleware);

        lock (_gate)
        {
            CapCount(
                extensionId,
                _middlewares.Count(entry => IsOwner(entry.ExtensionId, extensionId)),
                MaxMiddlewaresPerExtension,
                "model middlewares"
            );
            _middlewares.Add(
                new MiddlewareEntry(
                    extensionId,
                    middleware,
                    ExtensionOrder(extensionId),
                    _registrationCounter++
                )
            );
        }
    }

    /// <summary>Registers one virtual model of a loaded extension.</summary>
    public void RegisterVirtualModel(string extensionId, VirtualModelRegistration model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(model);

        lock (_gate)
        {
            CapCount(
                extensionId,
                _virtuals.Count(entry => IsOwner(entry.ExtensionId, extensionId)),
                MaxVirtualModelsPerExtension,
                "virtual models"
            );
            ValidateLocalId(extensionId, model.Id, "virtual model");
            if (model.Backends.Count == 0)
            {
                throw new InvalidOperationException(
                    $"extension '{extensionId}': virtual model '{model.Id}' must name at least one backend."
                );
            }

            foreach (string backend in model.Backends)
            {
                if (string.IsNullOrWhiteSpace(backend))
                {
                    throw new InvalidOperationException(
                        $"extension '{extensionId}': virtual model '{model.Id}' names an empty backend id."
                    );
                }
            }

            if (model.ContextWindow is { } window && window <= 0)
            {
                throw new InvalidOperationException(
                    $"extension '{extensionId}': virtual model '{model.Id}' declares context window {window.ToString(CultureInfo.InvariantCulture)}; it must be positive."
                );
            }

            string fullId = $"ext/{extensionId}/{model.Id}";
            ConfirmModelIdFree(extensionId, fullId, $"virtual model '{model.Id}'");
            var entry = new VirtualEntry(extensionId, model, fullId);
            _entriesById[fullId] = entry;
            _virtuals.Add(entry);
        }
    }

    /// <summary>Removes every model registration of the extension.</summary>
    public void Unregister(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        lock (_gate)
        {
            foreach (string modelId in _entriesById.Keys.ToList())
            {
                if (
                    OwnerExtension(_entriesById[modelId]) is { } owner
                    && IsOwner(owner, extensionId)
                )
                {
                    _entriesById.Remove(modelId);
                    _clients.Remove(modelId);
                    _clientGates.Remove(modelId);
                    _capabilities.Remove(modelId);
                    _validatedVirtuals.Remove(modelId);
                }
            }

            _declaredEntries.RemoveAll(entry => IsOwner(entry.ExtensionId, extensionId));
            _programmatic.RemoveAll(entry => IsOwner(entry.ExtensionId, extensionId));
            _virtuals.RemoveAll(entry => IsOwner(entry.ExtensionId, extensionId));
            _middlewares.RemoveAll(entry => IsOwner(entry.ExtensionId, extensionId));

            // Derived capabilities and first-resolve validation may depend on backends owned by
            // other extensions: clear them wholesale so dependents revalidate on next use.
            _validatedVirtuals.Clear();
            _capabilities.Clear();
            _declared.RemoveAll(provider => IsOwner(provider.ExtensionId, extensionId));
            foreach (string id in _providerIds.Keys.ToList())
            {
                if (IsOwner(_providerIds[id], extensionId))
                {
                    _providerIds.Remove(id);
                }
            }
        }
    }

    /// <summary>The client for the model id; see the class summary for the resolution order.</summary>
    public IChatClient Resolve(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        object entry =
            Find(modelId) ?? (object?)FindCatalog(modelId) ?? throw UnknownModel(modelId);
        return Compose(ResolveEntry(modelId, entry), ModelMiddlewareSlot.BeforeRouting);
    }

    private static bool IsOwner(string owner, string extensionId) =>
        string.Equals(owner, extensionId, StringComparison.Ordinal);

    private static bool IsValidLocalId(string id) =>
        id.Length > 0
        && id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private void ValidateLocalId(string extensionId, string id, string what)
    {
        if (!IsValidLocalId(id))
        {
            throw new InvalidOperationException(
                $"extension '{extensionId}': {what} id '{id}' must match [a-z0-9-]+."
            );
        }
    }

    private void CapCount(string extensionId, int count, int cap, string what)
    {
        if (count >= cap)
        {
            throw new InvalidOperationException(
                $"extension '{extensionId}' already has {count.ToString(CultureInfo.InvariantCulture)} {what}; at most {cap.ToString(CultureInfo.InvariantCulture)} are allowed."
            );
        }
    }

    private void ConfirmProviderCap(string extensionId, int additional)
    {
        int count =
            _declaredEntries.Count(entry => IsOwner(entry.ExtensionId, extensionId))
            + _programmatic.Count(entry => IsOwner(entry.ExtensionId, extensionId))
            + additional;
        if (count > MaxProvidersPerExtension)
        {
            throw new InvalidOperationException(
                $"extension '{extensionId}' would have {count.ToString(CultureInfo.InvariantCulture)} model providers; at most {MaxProvidersPerExtension.ToString(CultureInfo.InvariantCulture)} are allowed."
            );
        }
    }

    private void ConfirmDeclaredBatch(
        string extensionId,
        IReadOnlyList<ModelProviderDescriptor> providers
    )
    {
        HashSet<string> batch = new(StringComparer.Ordinal);
        foreach (ModelProviderDescriptor descriptor in providers)
        {
            ValidateLocalId(extensionId, descriptor.Id, "model provider");
            ConfirmProviderId(extensionId, descriptor.Id);
            if (!batch.Add(descriptor.Id))
            {
                throw new InvalidOperationException(
                    $"model provider id '{descriptor.Id}' is declared twice by extension '{extensionId}'."
                );
            }

            ConfirmModelIds(extensionId, descriptor.Id, descriptor.ModelIds);
        }
    }

    private void ConfirmProviderId(string extensionId, string providerId)
    {
        if (_providerIds.TryGetValue(providerId, out string? owner))
        {
            throw new InvalidOperationException(
                $"model provider id '{providerId}' is already registered by extension '{owner}'; extension '{extensionId}' cannot register it again."
            );
        }
    }

    private void ConfirmModelIds(
        string extensionId,
        string providerId,
        IReadOnlyList<string> modelIds
    )
    {
        if (modelIds.Count == 0)
        {
            throw new InvalidOperationException(
                $"extension '{extensionId}': model provider '{providerId}' must serve at least one model id."
            );
        }

        foreach (string modelId in modelIds)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                throw new InvalidOperationException(
                    $"extension '{extensionId}': model provider '{providerId}' declares an empty model id."
                );
            }

            ConfirmModelIdFree(extensionId, modelId, $"model id of provider '{providerId}'");
        }
    }

    private void ConfirmModelIdFree(string extensionId, string modelId, string what)
    {
        if (_catalog is not null && _catalog.Find(modelId) is not null)
        {
            throw new InvalidOperationException(
                $"{what} '{modelId}' is a catalog model id; extension '{extensionId}' cannot shadow it. Rename it."
            );
        }

        if (_entriesById.TryGetValue(modelId, out object? existing))
        {
            throw new InvalidOperationException(
                $"{what} '{modelId}' is already provided by {OwnerOf(existing)}; extension '{extensionId}' cannot register it again."
            );
        }
    }

    private void IndexModels(object entry, IReadOnlyList<string> modelIds)
    {
        foreach (string modelId in modelIds)
        {
            _entriesById[modelId] = entry;
        }
    }

    private static string OwnerOf(object entry) =>
        entry switch
        {
            DeclaredEntry declared => $"extension '{declared.ExtensionId}'",
            ProgrammaticEntry programmatic => $"extension '{programmatic.ExtensionId}'",
            VirtualEntry virtualEntry => $"extension '{virtualEntry.ExtensionId}'",
            _ => "another source",
        };

    private static string? OwnerExtension(object entry) =>
        entry switch
        {
            DeclaredEntry declared => declared.ExtensionId,
            ProgrammaticEntry programmatic => programmatic.ExtensionId,
            VirtualEntry virtualEntry => virtualEntry.ExtensionId,
            _ => null,
        };

    private int ExtensionOrder(string extensionId)
    {
        if (!_extensionOrder.TryGetValue(extensionId, out int order))
        {
            _extensionOrder[extensionId] = order = _extensionCounter++;
        }

        return order;
    }

    internal string KnownModelIds()
    {
        List<string> known = [];
        lock (_gate)
        {
            known.AddRange(_entriesById.Keys);
        }

        if (_catalog is not null)
        {
            known.AddRange(_catalog.Models.Select(model => model.Id));
        }

        if (known.Count == 0)
        {
            return "none";
        }

        IEnumerable<string> shown = known
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Take(KnownIdsShown);
        return string.Join(", ", shown);
    }

    internal InvalidOperationException UnknownModel(string modelId) =>
        new(
            $"Unknown model '{modelId}'. Known models: {KnownModelIds()}. Use a known model id, or add the model to the catalog."
        );

    internal object? Find(string modelId)
    {
        lock (_gate)
        {
            return _entriesById.GetValueOrDefault(modelId);
        }
    }

    internal CatalogModel? FindCatalog(string modelId) =>
        _catalog?.Find(modelId) is { } info ? new CatalogModel(info) : null;

    internal IChatClientFactory? ClientFactory => _clientFactory;

    /// <summary>
    /// Returns the cached client for the key, creating it on first use — at most once, even under
    /// concurrency. Failures are not cached: a fixed secret or registration resolves on retry.
    /// </summary>
    internal IChatClient CachedClient(string key, Func<IChatClient> create)
    {
        lock (_gate)
        {
            if (_clients.TryGetValue(key, out IChatClient? cached))
            {
                return cached;
            }
        }

        object gate;
        lock (_gate)
        {
            if (!_clientGates.TryGetValue(key, out object? existing))
            {
                _clientGates[key] = existing = new object();
            }

            gate = existing;
        }

        lock (gate)
        {
            lock (_gate)
            {
                if (_clients.TryGetValue(key, out IChatClient? raced))
                {
                    return raced;
                }
            }

            IChatClient created = create();
            lock (_gate)
            {
                _clients[key] = created;
                _clientGates.Remove(key);
            }

            return created;
        }
    }

    internal sealed record Capabilities(int Window, bool Tools);

    internal sealed record DeclaredEntry(
        string ExtensionId,
        ModelProviderDescriptor Descriptor,
        IExtensionSecrets Secrets
    );

    internal sealed record ProgrammaticEntry(string ExtensionId, ExtensionModelProvider Provider);

    internal sealed record VirtualEntry(
        string ExtensionId,
        VirtualModelRegistration Registration,
        string FullId
    );

    internal sealed record MiddlewareEntry(
        string ExtensionId,
        ExtensionModelMiddleware Middleware,
        int ExtensionOrder,
        int Registration
    );

    internal sealed record CatalogModel(ModelInfo Info);
}

/// <summary>A declared model provider as the registry lists it; the secret is a name only.</summary>
public sealed record RegisteredModelProvider(
    string Id,
    string NamespacedId,
    string ExtensionId,
    string DisplayName,
    string Endpoint,
    string SecretName,
    IReadOnlyList<string> ModelIds,
    int ContextWindow,
    bool SupportsTools
);

/// <summary>A model's capability view: context window in tokens (0 = unknown) and tool support.</summary>
public sealed record ModelCapabilities(int ContextWindow, bool SupportsTools);
