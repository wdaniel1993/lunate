using Lunate.Extensibility.Abstractions;

namespace MemoryProviderExtension;

/// <summary>
/// The one public factory the loader discovers in the entry assembly. It creates the memory store,
/// registers it as the <c>memory-store</c> service and registers the capture/inject and commit
/// handlers.
/// </summary>
public sealed class MemoryProviderExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var store = new MemoryStore(context.Id, context.Settings, context.Log);
        context.RegisterService("memory-store", store);
        context.Register(new MemoryContextBuildingHandler(context.Id, store, context.Log));
        context.Register(new MemoryTurnEndedHandler(store));

        return new MemoryProviderExtension(context.Id);
    }
}

/// <summary>The extension instance.</summary>
public sealed class MemoryProviderExtension(string id) : IExtension
{
    public string Id { get; } = id;
}
