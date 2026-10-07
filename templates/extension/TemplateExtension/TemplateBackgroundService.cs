using System.Globalization;
using Lunate.Extensibility.Abstractions;

namespace TemplateExtension;

/// <summary>
/// A session-scoped background service. The host calls <see cref="StartAsync"/> once the session is
/// live and <see cref="StopAsync"/> when it ends; both may be called more than once.
/// </summary>
public sealed class TemplateBackgroundService(string extensionId, IExtensionLog log)
    : IBackgroundService
{
    public int Starts { get; private set; }

    public int Stops { get; private set; }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        Starts++;
        log.Info(
            $"template[{extensionId}]: service started (start #{Starts.ToString(CultureInfo.InvariantCulture)})"
        );
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        Stops++;
        log.Info(
            $"template[{extensionId}]: service stopped (stop #{Stops.ToString(CultureInfo.InvariantCulture)})"
        );
        return ValueTask.CompletedTask;
    }
}
