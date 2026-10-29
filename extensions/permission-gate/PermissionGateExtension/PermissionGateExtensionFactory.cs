using Lunate.Extensibility.Abstractions;

namespace PermissionGateExtension;

/// <summary>
/// The one public factory the loader discovers in the entry assembly. It registers the ToolCalling
/// policy handler and returns the extension.
/// </summary>
public sealed class PermissionGateExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Register(
            new PermissionGateToolCallingHandler(context.Id, context.Settings, context.Log)
        );

        return new PermissionGateExtension(context.Id);
    }
}

/// <summary>The extension instance.</summary>
public sealed class PermissionGateExtension(string id) : IExtension
{
    public string Id { get; } = id;
}
