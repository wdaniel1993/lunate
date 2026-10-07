using Lunate.Extensibility.Abstractions;

namespace TemplateExtension;

/// <summary>
/// The one public factory the loader discovers in the entry assembly. It reads the extension's
/// namespaced setting and secret, registers the hook and service, and returns the extension.
/// </summary>
public sealed class TemplateExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool hasGreeting = context.Settings.TryGet("template.greeting", out _);
        bool hasSecret = context.Secrets.TryGet("template-api-key", out _);
        context.Log.Info(
            $"template[{context.Id}]: created (greeting setting present: {hasGreeting}, secret present: {hasSecret})"
        );

        context.Register(new TemplateToolCallingHandler(context.Id, context.Log));
        context.RegisterService(
            $"ext/{context.Id}/watcher",
            new TemplateBackgroundService(context.Id, context.Log)
        );

        return new TemplateExtension(context.Id);
    }
}

/// <summary>The extension instance; replace this with whatever state your extension needs.</summary>
public sealed class TemplateExtension(string id) : IExtension
{
    public string Id { get; } = id;
}
