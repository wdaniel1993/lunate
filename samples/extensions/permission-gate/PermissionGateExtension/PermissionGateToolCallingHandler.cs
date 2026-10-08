using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace PermissionGateExtension;

/// <summary>
/// Enforces a settings-driven policy on destructive tools before approval: <c>block</c> (the
/// default) refuses the call with a reason; <c>confirm</c> lets it fall through to the normal
/// approval prompt. Calls without the <c>destructive</c> annotation always proceed unchanged.
/// </summary>
public sealed class PermissionGateToolCallingHandler(
    string extensionId,
    IExtensionSettings settings,
    IExtensionLog log
) : IToolCallingHandler
{
    private const string BlockMode = "block";
    private const string ConfirmMode = "confirm";
    private const string DestructiveAnnotation = "destructive";

    public int Priority => 0;

    public ValueTask<ToolCallingResult> HandleAsync(
        ToolCallingPayload payload,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.Annotations.Contains(DestructiveAnnotation, StringComparer.Ordinal))
        {
            return ValueTask.FromResult<ToolCallingResult>(new ToolCallingResult.Proceed(null));
        }

        if (Mode() == ConfirmMode)
        {
            log.Info(
                $"permission-gate[{extensionId}]: destructive tool '{payload.ToolName}' falls through to the approval prompt (mode=confirm)"
            );
            return ValueTask.FromResult<ToolCallingResult>(new ToolCallingResult.Proceed(null));
        }

        string reason =
            $"permission-gate: destructive tool '{payload.ToolName}' blocked - set mode=confirm to approve interactively";
        log.Warn(
            $"permission-gate[{extensionId}]: blocked destructive tool '{payload.ToolName}' (mode=block)"
        );
        return ValueTask.FromResult<ToolCallingResult>(new ToolCallingResult.Block(reason));
    }

    private string Mode()
    {
        if (
            settings.TryGet("mode", out JsonElement value)
            && value.ValueKind == JsonValueKind.String
        )
        {
            string? mode = value.GetString();
            if (string.Equals(mode, ConfirmMode, StringComparison.Ordinal))
            {
                return ConfirmMode;
            }

            if (string.Equals(mode, BlockMode, StringComparison.Ordinal))
            {
                return BlockMode;
            }
        }

        return BlockMode;
    }
}
