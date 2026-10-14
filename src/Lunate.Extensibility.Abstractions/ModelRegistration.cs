using Microsoft.Extensions.AI;

namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// One model provider an extension registers at load time. <see cref="Id"/> is a local
/// <c>[a-z0-9-]+</c> name; the live registry publishes it namespaced as
/// <c>ext/&lt;extension-id&gt;/&lt;id&gt;</c> and refuses duplicates across extensions.
/// <see cref="ModelIds"/> are the model ids the provider serves: they are addressable directly
/// (they are not rewritten) and a duplicate — across extensions or with a catalog model id — is
/// refused, so an extension can never shadow a catalog model. Registration is registration only:
/// it must not start processes, sockets or timers.
/// </summary>
/// <param name="Id">The provider's local id; published namespaced by the registry.</param>
/// <param name="ModelIds">The model ids the provider serves.</param>
/// <param name="Create">
/// Creates the client for the provider. Called at most once per served model id, lazily, at first
/// use; the call itself is the provider's first use, so it may start resources (ADR-0017).
/// </param>
public sealed record ExtensionModelProvider(
    string Id,
    IReadOnlyList<string> ModelIds,
    Func<IChatClient> Create
);

/// <summary>Where a middleware wraps the model pipeline.</summary>
public enum ModelMiddlewareSlot
{
    /// <summary>Wraps the client the caller uses; sees every call, including virtual-model calls.</summary>
    BeforeRouting,

    /// <summary>Wraps each resolved backend client; sees only calls that reach a backend.</summary>
    AroundBackend,
}

/// <summary>
/// One middleware an extension registers at load time. Composition is deterministic: extension load
/// order, then <see cref="Priority"/> ascending, then registration order; first in the chain is the
/// outermost wrapper. A middleware exception fails the request closed with an error naming the
/// owning extension — never a silent skip. Registration is registration only: it must not start
/// processes, sockets or timers.
/// </summary>
/// <param name="Slot">The pipeline slot the wrapper applies at.</param>
/// <param name="Priority">Lower runs outer within the same slot; ties keep registration order.</param>
/// <param name="Wrap">Wraps a client; the returned client is the one the pipeline uses.</param>
public sealed record ExtensionModelMiddleware(
    ModelMiddlewareSlot Slot,
    int Priority,
    Func<IChatClient, IChatClient> Wrap
);

/// <summary>
/// One virtual model an extension registers at load time: a model id that routes per request over
/// <see cref="Backends"/>. <see cref="Id"/> is a local <c>[a-z0-9-]+</c> name; the registry
/// publishes it namespaced as <c>ext/&lt;extension-id&gt;/&lt;id&gt;</c> and resolves through the
/// same order as every model id (virtual model → provider → declared provider → catalog).
/// <see cref="Backends"/> may themselves name virtual models; chains are checked at first resolve
/// for cycles and a bounded depth. Registration is registration only: it must not start processes,
/// sockets or timers.
/// </summary>
/// <param name="Id">The virtual model's local id; published namespaced by the registry.</param>
/// <param name="Backends">The model ids the router may select, resolved per request.</param>
/// <param name="Select">Evaluates the request and returns one of <see cref="Backends"/>.</param>
/// <param name="ContextWindow">
/// Optional declared context window; defaults to the weakest backend's and may only narrow.
/// </param>
/// <param name="SupportsTools">
/// Optional tool-support declaration; defaults to every backend supporting tools and may only
/// narrow (declaring true while a backend does not support tools is refused at first resolve).
/// </param>
public sealed record VirtualModelRegistration(
    string Id,
    IReadOnlyList<string> Backends,
    Func<ModelRouteContext, string> Select,
    int? ContextWindow = null,
    bool? SupportsTools = null
);

/// <summary>
/// One routing decision's request context, handed to a virtual model's selector per request.
/// </summary>
public sealed class ModelRouteContext
{
    public ModelRouteContext(
        IReadOnlyList<ChatMessage> messages,
        string virtualModelId,
        ChatOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualModelId);
        Messages = messages;
        VirtualModelId = virtualModelId;
        Options = options;
    }

    /// <summary>The request's messages, read-only.</summary>
    public IReadOnlyList<ChatMessage> Messages { get; }

    /// <summary>The virtual model's namespaced id.</summary>
    public string VirtualModelId { get; }

    /// <summary>The request's options; null when the caller sent none.</summary>
    public ChatOptions? Options { get; }
}
