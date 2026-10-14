using System.Runtime.CompilerServices;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility;

public sealed partial class ModelRegistry
{
    /// <summary>Composes the client for the entry, with its slot's middleware applied.</summary>
    private IChatClient ResolveEntry(string modelId, object entry) =>
        entry switch
        {
            VirtualEntry virtualEntry => ResolveVirtual(virtualEntry),
            _ => Compose(
                CachedClient(modelId, () => CreateBaseClient(modelId)),
                ModelMiddlewareSlot.AroundBackend
            ),
        };

    private IChatClient ResolveVirtual(VirtualEntry entry)
    {
        ValidateVirtualChain(entry);
        return CachedClient(entry.FullId, () => new VirtualModelClient(this, entry));
    }

    private IChatClient ResolveBackend(object entry, string backendId) =>
        entry is VirtualEntry virtualEntry
            ? ResolveVirtual(virtualEntry)
            : Compose(
                CachedClient(backendId, () => CreateBaseClient(backendId)),
                ModelMiddlewareSlot.AroundBackend
            );

    private IChatClient CreateBaseClient(string modelId)
    {
        object entry =
            Find(modelId) ?? (object?)FindCatalog(modelId) ?? throw UnknownModel(modelId);
        return entry switch
        {
            DeclaredEntry declared => CreateDeclaredClient(declared, modelId),
            ProgrammaticEntry programmatic => CreateProgrammaticClient(programmatic),
            CatalogModel catalog => CreateCatalogClient(catalog, modelId),
            _ => throw new InvalidOperationException(
                $"Model '{modelId}' resolves to a virtual model in the base-client path; this is a registry bug."
            ),
        };
    }

    private IChatClient CreateProgrammaticClient(ProgrammaticEntry entry)
    {
        try
        {
            return entry.Provider.Create();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"The client factory of model provider 'ext/{entry.ExtensionId}/{entry.Provider.Id}' (extension '{entry.ExtensionId}') failed: {exception.Message}. Fix the provider registration.",
                exception
            );
        }
    }

    private IChatClient CreateDeclaredClient(DeclaredEntry entry, string modelId)
    {
        ModelProviderDescriptor descriptor = entry.Descriptor;
        string namespaced = $"ext/{entry.ExtensionId}/{descriptor.Id}";
        if (
            !entry.Secrets.TryGet(descriptor.SecretName, out string? value)
            || string.IsNullOrEmpty(value)
        )
        {
            throw new InvalidOperationException(
                $"Model provider '{namespaced}' (endpoint '{descriptor.Endpoint}') needs secret '{descriptor.SecretName}' from the extension's secrets store, but no value is set. Add '{descriptor.SecretName}' to the extension's secrets, then retry."
            );
        }

        if (_clientFactory is null)
        {
            throw new InvalidOperationException(
                $"Model provider '{namespaced}' cannot be instantiated: no client factory is configured in this session."
            );
        }

        var model = new ModelInfo(
            modelId,
            "openai",
            new Uri(descriptor.Endpoint),
            descriptor.ContextWindow,
            descriptor.SupportsTools
        );
        IChatClient client = _clientFactory.Create(model, value);
        return descriptor.SupportsTools ? client : new DeclaredToolsGate(client, namespaced);
    }

    private IChatClient CreateCatalogClient(CatalogModel catalog, string modelId)
    {
        if (_clientFactory is null)
        {
            throw new InvalidOperationException(
                $"Model '{modelId}' is a catalog model, but no client factory is configured in this session."
            );
        }

        return _clientFactory.Create(catalog.Info);
    }

    /// <summary>
    /// Wraps the client with the slot's middleware chain: extension load order, then priority
    /// ascending, then registration order; the first entry in the chain is the outermost wrapper.
    /// A middleware that throws fails the request closed with an error naming the extension; an
    /// exception from below (the provider) surfaces unchanged.
    /// </summary>
    private IChatClient Compose(IChatClient inner, ModelMiddlewareSlot slot)
    {
        MiddlewareEntry[] ordered;
        lock (_gate)
        {
            ordered =
            [
                .. _middlewares
                    .Where(entry => entry.Middleware.Slot == slot)
                    .OrderBy(entry => entry.ExtensionOrder)
                    .ThenBy(entry => entry.Middleware.Priority)
                    .ThenBy(entry => entry.Registration),
            ];
        }

        IChatClient client = inner;
        for (int index = ordered.Length - 1; index >= 0; index--)
        {
            MiddlewareEntry entry = ordered[index];
            IChatClient produced;
            try
            {
                produced = entry.Middleware.Wrap(new BelowMarker(client));
            }
            catch (Exception exception)
            {
                throw MiddlewareFailure(entry.ExtensionId, exception);
            }

            client = new FailureBoundary(produced, entry.ExtensionId);
        }

        return client;
    }

    internal static InvalidOperationException MiddlewareFailure(
        string extensionId,
        Exception exception
    ) =>
        new(
            $"Model middleware from extension '{extensionId}' failed: {exception.Message}. The call failed closed; fix or remove the middleware.",
            exception
        );

    /// <summary>The client a virtual model exposes: routes per request, then delegates.</summary>
    private sealed class VirtualModelClient(ModelRegistry registry, VirtualEntry entry)
        : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            List<ChatMessage> request = [.. messages];
            IChatClient backend = SelectBackend(registry, entry, request, options);
            return await backend
                .GetResponseAsync(request, options, cancellationToken)
                .ConfigureAwait(false);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            List<ChatMessage> request = [.. messages];
            IChatClient backend = SelectBackend(registry, entry, request, options);
            await foreach (
                ChatResponseUpdate update in backend
                    .GetStreamingResponseAsync(request, options, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }

        private static IChatClient SelectBackend(
            ModelRegistry registry,
            VirtualEntry entry,
            IReadOnlyList<ChatMessage> messages,
            ChatOptions? options
        )
        {
            string backend;
            try
            {
                backend = entry.Registration.Select(
                    new ModelRouteContext(messages, entry.FullId, options)
                );
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Virtual model '{entry.FullId}' (extension '{entry.ExtensionId}') failed to select a backend: {exception.Message}. Fix the selector.",
                    exception
                );
            }

            if (!entry.Registration.Backends.Contains(backend, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Virtual model '{entry.FullId}' selected backend '{backend}', which is not one of its declared backends: {string.Join(", ", entry.Registration.Backends)}. Fix the selector or declare the backend."
                );
            }

            object backendEntry =
                registry.Find(backend)
                ?? throw new InvalidOperationException(
                    $"Virtual model '{entry.FullId}' names backend '{backend}', which is not a known model. Known models: {registry.KnownModelIds()}. Fix the backend or register the model."
                );
            IChatClient client = registry.ResolveBackend(backendEntry, backend);
            ModelRouteObserver.Report(entry.FullId, backend);
            return client;
        }
    }

    /// <summary>
    /// Fails the request closed when the wrapped client throws while unmarked: the untagged
    /// exception is a middleware's own failure and is attributed to its extension. Exceptions
    /// marked by a <see cref="BelowMarker"/> (a provider call, or an inner boundary's teaching
    /// error) pass through unchanged.
    /// </summary>
    internal sealed class FailureBoundary(IChatClient inner, string extensionId)
        : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                return await base.GetResponseAsync(messages, options, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (!BelowMarker.IsMarked(exception))
            {
                throw MiddlewareFailure(extensionId, exception);
            }
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            IAsyncEnumerable<ChatResponseUpdate>? stream = null;
            try
            {
                stream = base.GetStreamingResponseAsync(messages, options, cancellationToken);
            }
            catch (Exception exception) when (!BelowMarker.IsMarked(exception))
            {
                throw MiddlewareFailure(extensionId, exception);
            }

            IAsyncEnumerator<ChatResponseUpdate> enumerator = stream!.GetAsyncEnumerator(
                cancellationToken
            );
            while (true)
            {
                bool more;
                try
                {
                    more = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (!BelowMarker.IsMarked(exception))
                {
                    throw MiddlewareFailure(extensionId, exception);
                }

                if (!more)
                {
                    break;
                }

                yield return enumerator.Current;
            }

            try
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (!BelowMarker.IsMarked(exception))
            {
                throw MiddlewareFailure(extensionId, exception);
            }
        }
    }

    /// <summary>Marks every exception crossing from below, so outer boundaries pass it unchanged.</summary>
    internal sealed class BelowMarker(IChatClient inner) : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                return await base.GetResponseAsync(messages, options, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Mark(exception);
                throw;
            }
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            IAsyncEnumerable<ChatResponseUpdate>? stream = null;
            try
            {
                stream = base.GetStreamingResponseAsync(messages, options, cancellationToken);
            }
            catch (Exception exception)
            {
                Mark(exception);
                throw;
            }

            IAsyncEnumerator<ChatResponseUpdate> enumerator = stream!.GetAsyncEnumerator(
                cancellationToken
            );
            while (true)
            {
                bool more;
                try
                {
                    more = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Mark(exception);
                    throw;
                }

                if (!more)
                {
                    break;
                }

                yield return enumerator.Current;
            }

            try
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Mark(exception);
                throw;
            }
        }

        private static readonly object Marker = new();

        internal static bool IsMarked(Exception exception)
        {
            try
            {
                return exception.Data.Contains(Marker);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Mark(Exception exception)
        {
            try
            {
                exception.Data[Marker] = true;
            }
            catch (Exception)
            {
                // Best-effort marking: an exception whose Data is not writable still surfaces.
            }
        }
    }

    /// <summary>
    /// Refuses a request offering tools to a declared provider whose declaration says it does not
    /// support them, naming the provider and the declaration so the fix is findable.
    /// </summary>
    private sealed class DeclaredToolsGate(IChatClient inner, string namespacedId)
        : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Refuse(options);
            return base.GetResponseAsync(messages, options, cancellationToken);
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Refuse(options);
            return base.GetStreamingResponseAsync(messages, options, cancellationToken);
        }

        private void Refuse(ChatOptions? options)
        {
            if (options?.Tools is { Count: > 0 })
            {
                throw new InvalidOperationException(
                    $"Model provider '{namespacedId}' declares supportsTools: false, but this request offers tools. Use the model without tools, or fix the provider's supportsTools declaration if it does support them."
                );
            }
        }
    }
}
