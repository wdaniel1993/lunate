using Lunate.Extensibility.Abstractions;

namespace TemplateExtension;

/// <summary>
/// Logs every tool call before approval and lets it proceed with the arguments unchanged.
/// Return <see cref="ToolCallingResult.Block"/> to refuse a call, or
/// <see cref="ToolCallingResult.Proceed"/> with new arguments to mutate them.
/// </summary>
public sealed class TemplateToolCallingHandler(string extensionId, IExtensionLog log)
    : IToolCallingHandler
{
    public int Priority => 0;

    public ValueTask<ToolCallingResult> HandleAsync(
        ToolCallingPayload payload,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(payload);
        log.Info(
            $"template[{extensionId}]: tool-calling hook saw '{payload.ToolName}' (call {payload.CallId})"
        );
        return ValueTask.FromResult<ToolCallingResult>(new ToolCallingResult.Proceed(null));
    }
}
