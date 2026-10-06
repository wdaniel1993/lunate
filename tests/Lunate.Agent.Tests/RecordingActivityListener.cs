using System.Diagnostics;

namespace Lunate.Agent.Tests;

internal sealed class RecordingActivityListener : IDisposable
{
    private readonly ActivityListener _listener;

    public RecordingActivityListener(Func<ActivitySource, bool>? shouldListen = null)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = shouldListen ?? DefaultShouldListen,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = Activities.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public List<Activity> Activities { get; } = [];

    public void Dispose() => _listener.Dispose();

    private static bool DefaultShouldListen(ActivitySource source) =>
        source.Name == "Lunate.Agent"
        || source.Name.Contains("Microsoft.Extensions.AI", StringComparison.Ordinal);
}
