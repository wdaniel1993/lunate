using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The tool-result renderers extensions registered. Registration is registration only; matching is
/// ordinal by tool name and several renderers for one name resolve to the last registration, with
/// the conflict logged. A throwing renderer is omitted for that result, reported through the log
/// and retried on the next result.
/// </summary>
public sealed class ToolResultRendererRegistry
{
    private readonly IExtensionLog _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, Registration> _renderers = new(StringComparer.Ordinal);

    public ToolResultRendererRegistry(IExtensionLog? log = null) =>
        _log = log ?? NullExtensionLog.Instance;

    public void Register(string extensionId, IToolResultRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentException.ThrowIfNullOrWhiteSpace(renderer.ToolName);
        Registration registration = new(extensionId, renderer);
        Registration? replaced;
        lock (_gate)
        {
            _renderers.TryGetValue(renderer.ToolName, out replaced);
            _renderers[renderer.ToolName] = registration;
        }

        if (replaced is not null)
        {
            _log.Warn(
                $"extension UI: tool result renderer for '{renderer.ToolName}' from extension '{extensionId}' replaces the renderer from extension '{replaced.ExtensionId}'; the last registration wins."
            );
        }
    }

    /// <summary>
    /// Renders the tool result with the renderer registered for the tool name. False means no
    /// renderer matched or the renderer failed; either way the caller renders nothing extra.
    /// </summary>
    public bool TryRender(
        string toolName,
        ToolResultView view,
        out IReadOnlyList<TextSegment> segments
    )
    {
        ArgumentNullException.ThrowIfNull(toolName);
        ArgumentNullException.ThrowIfNull(view);
        Registration? registration;
        lock (_gate)
        {
            _renderers.TryGetValue(toolName, out registration);
        }

        if (registration is null)
        {
            segments = [];
            return false;
        }

        try
        {
            segments = registration.Renderer.Render(view) ?? [];
            return true;
        }
        catch (Exception exception)
        {
            _log.Error(
                $"extension UI: tool result renderer {registration.Renderer.GetType().Name} from extension '{registration.ExtensionId}' failed: {exception.GetType().FullName}: {exception.Message}; the contribution is omitted for this result and retried on the next one."
            );
            segments = [];
            return false;
        }
    }

    /// <summary>Removes every renderer the extension registered.</summary>
    public void Unregister(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        lock (_gate)
        {
            string[] dropped =
            [
                .. _renderers
                    .Where(pair =>
                        string.Equals(pair.Value.ExtensionId, extensionId, StringComparison.Ordinal)
                    )
                    .Select(pair => pair.Key),
            ];
            foreach (string toolName in dropped)
            {
                _renderers.Remove(toolName);
            }
        }
    }

    private sealed record Registration(string ExtensionId, IToolResultRenderer Renderer);
}
