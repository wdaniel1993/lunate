using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The status widgets extensions registered. Registration is registration only; widgets contribute
/// in registration order on each status update. A throwing widget is omitted for that update,
/// reported through the log and retried on the next update.
/// </summary>
public sealed class StatusWidgetRegistry
{
    private readonly IExtensionLog _log;
    private readonly object _gate = new();
    private readonly List<Registration> _widgets = [];

    public StatusWidgetRegistry(IExtensionLog? log = null) =>
        _log = log ?? NullExtensionLog.Instance;

    public void Register(string extensionId, IStatusWidget widget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(widget);
        lock (_gate)
        {
            _widgets.Add(new Registration(extensionId, widget));
        }
    }

    /// <summary>Invokes every widget in registration order, concatenating their segments.</summary>
    public IReadOnlyList<TextSegment> Segments(StatusView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Registration[] registrations;
        lock (_gate)
        {
            registrations = [.. _widgets];
        }

        List<TextSegment> segments = [];
        foreach (Registration registration in registrations)
        {
            try
            {
                IReadOnlyList<TextSegment>? contributed = registration.Widget.Segments(view);
                if (contributed is not null)
                {
                    segments.AddRange(contributed);
                }
            }
            catch (Exception exception)
            {
                _log.Error(
                    $"extension UI: status widget {registration.Widget.GetType().Name} from extension '{registration.ExtensionId}' failed: {exception.GetType().FullName}: {exception.Message}; the contribution is omitted for this update and retried on the next one."
                );
            }
        }

        return segments;
    }

    /// <summary>Removes every widget the extension registered.</summary>
    public void Unregister(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        lock (_gate)
        {
            _widgets.RemoveAll(registration =>
                string.Equals(registration.ExtensionId, extensionId, StringComparison.Ordinal)
            );
        }
    }

    private sealed record Registration(string ExtensionId, IStatusWidget Widget);
}
