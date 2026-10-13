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
        log.Info(
            $"hello file changed: path={payload.Path}, relative={payload.RelativePath}, workspace={payload.WorkspaceId}, run={payload.RunId ?? "-"}, kind={payload.Kind}"
        );
        return ValueTask.CompletedTask;
    }
}
