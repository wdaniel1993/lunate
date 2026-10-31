using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// A recording extension context for adapter tests: it captures hook and service registrations
/// and serves the service lookup from them, so the adapter's proxies can be driven directly.
/// </summary>
internal sealed class FakeExtensionContext : IExtensionContext
{
    public string Id { get; init; } = "remote-sample";

    public IExtensionSettings Settings { get; } = new EmptySettings();

    public IExtensionSecrets Secrets { get; } = new EmptySecrets();

    public RecordingLog Log { get; } = new();

    IExtensionLog IExtensionContext.Log => Log;

    public List<IHookHandler> Handlers { get; } = [];

    public Dictionary<string, IBackgroundService> Services { get; } = new(StringComparer.Ordinal);

    public List<IFileChangedHandler> FileChangedSubscriptions { get; } = [];

    public List<WriteVerifierRegistration> WriteVerifiers { get; } = [];

    public void Register(IHookHandler handler) => Handlers.Add(handler);

    public void RegisterWriteVerifier(WriteVerifierRegistration registration) =>
        WriteVerifiers.Add(registration);

    public void RegisterService(string name, IBackgroundService service) =>
        Services[name] = service;

    public void SubscribeFileChanged(IFileChangedHandler handler, string? pathPattern = null) =>
        FileChangedSubscriptions.Add(handler);

    public bool TryGetService(string name, out IBackgroundService? service)
    {
        service = Services.GetValueOrDefault(name);
        return service is not null;
    }

    public bool TryGetCoreService<T>(string name, out T? service)
        where T : class
    {
        service = null;
        return false;
    }

    public void RegisterToolResultRenderer(IToolResultRenderer renderer) =>
        throw new NotSupportedException();

    public void RegisterStatusWidget(IStatusWidget widget) => throw new NotSupportedException();

    public void RegisterTool(ExtensionToolRegistration registration) =>
        throw new NotSupportedException();

    public void RegisterMcpServer(McpServerRegistration registration) =>
        throw new NotSupportedException();

    public void RegisterModelProvider(ExtensionModelProvider provider) =>
        throw new NotSupportedException();

    public void RegisterModelMiddleware(ExtensionModelMiddleware middleware) =>
        throw new NotSupportedException();

    public void RegisterVirtualModel(VirtualModelRegistration model) =>
        throw new NotSupportedException();

    public void RegisterSkillSource(ISkillSource source) => throw new NotSupportedException();

    public List<ICommandSource> CommandSources { get; } = [];

    public void RegisterCommandSource(ICommandSource source) => CommandSources.Add(source);

    public void RegisterWorkspaceService<TService>(
        string name,
        IWorkspaceServiceFactory<TService> factory
    )
        where TService : notnull => throw new NotSupportedException();

    public ValueTask<IWorkspaceServiceLease<TService>> AcquireWorkspaceServiceAsync<TService>(
        string name,
        string canonicalWorktreeRoot,
        CancellationToken ct = default
    )
        where TService : notnull => throw new NotSupportedException();

    private sealed class EmptySettings : IExtensionSettings
    {
        public bool TryGet(string path, out JsonElement value)
        {
            value = default;
            return false;
        }
    }

    private sealed class EmptySecrets : IExtensionSecrets
    {
        public bool TryGet(
            string name,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value
        )
        {
            value = null;
            return false;
        }
    }
}

/// <summary>One recording log shared by the adapter tests.</summary>
internal sealed class RecordingLog : IExtensionLog
{
    public List<string> Infos { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public void Info(string message) => Infos.Add(message);

    public void Warn(string message) => Warnings.Add(message);

    public void Error(string message) => Errors.Add(message);
}
