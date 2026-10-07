using Lunate.Extensibility.Abstractions;

namespace HelloExtension;

public sealed class HelloFileChangedHandler(IExtensionLog log) : IFileChangedHandler
{
    public int Priority => 0;

    public ValueTask OnFileChangedAsync(
        FileChangedPayload payload,
        CancellationToken cancellationToken
    )
    {
        log.Info($"hello file changed: {payload.Path} in {payload.WorkspaceId}");
        return ValueTask.CompletedTask;
    }
}
