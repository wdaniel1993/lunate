using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed partial class HookRunner
{
    private static readonly Meter TimingMeter = new("Lunate.Extensibility");

    private static readonly Histogram<double> DurationHistogram =
        TimingMeter.CreateHistogram<double>("hook.duration", unit: "ms");

    private readonly ConcurrentDictionary<TimingKey, TimingEntry> _timings = new();

    /// <summary>
    /// Raised once per (extension, hook) when a dispatch first crosses the slow-hook threshold;
    /// later crossings only increment the aggregate's slow count, and a frontend with a
    /// subscriber renders the event as one notice (with no subscriber the warning stays in the
    /// log).
    /// </summary>
    public event Action<SlowHookEvent>? SlowHook;

    /// <summary>
    /// The per-hook timing snapshot of one extension, ordered by hook name; empty when the
    /// extension dispatched nothing.
    /// </summary>
    public IReadOnlyList<HookTimings> Snapshot(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        return
        [
            .. _timings
                .Where(pair =>
                    string.Equals(pair.Key.ExtensionId, extensionId, StringComparison.Ordinal)
                )
                .Select(pair => pair.Value.Snapshot())
                .OrderBy(timings => timings.Hook, StringComparer.Ordinal),
        ];
    }

    private void RecordTiming(
        string hook,
        Registration registration,
        TimeSpan measured,
        HandlerStatus status
    )
    {
        TimingEntry entry = Entry(hook, registration.ExtensionId);
        TimeSpan elapsed = SubtractPreparation(registration, measured);
        double milliseconds = elapsed.TotalMilliseconds;
        Interlocked.Increment(ref entry.Calls);
        Interlocked.Add(ref entry.TotalTicks, elapsed.Ticks);
        Interlocked.Exchange(ref entry.LastTicks, elapsed.Ticks);
        DurationHistogram.Record(milliseconds, entry.Tags);
        if (status is HandlerStatus.Completed)
        {
            Interlocked.Increment(ref entry.Completed);
        }
        else if (status is HandlerStatus.TimedOut)
        {
            Interlocked.Increment(ref entry.TimedOut);
        }

        UpdateMaximum(entry, elapsed.Ticks);
        if (milliseconds > _options.SlowHookThreshold.TotalMilliseconds)
        {
            Interlocked.Increment(ref entry.SlowCount);
            if (Interlocked.Exchange(ref entry.Warned, 1) == 0)
            {
                LogSlowHook(hook, registration, elapsed);
                RaiseSlowHook(registration.ExtensionId, hook, elapsed);
            }
        }
    }

    private void RecordCancelled(string hook, Registration registration, TimeSpan measured)
    {
        TimingEntry entry = Entry(hook, registration.ExtensionId);
        TimeSpan elapsed = SubtractPreparation(registration, measured);
        Interlocked.Increment(ref entry.Calls);
        Interlocked.Increment(ref entry.Cancelled);
        Interlocked.Add(ref entry.TotalTicks, elapsed.Ticks);
        Interlocked.Exchange(ref entry.LastTicks, elapsed.Ticks);
        DurationHistogram.Record(elapsed.TotalMilliseconds, entry.Tags);
    }

    private static TimeSpan SubtractPreparation(Registration registration, TimeSpan measured)
    {
        if (registration.Handler is not IPreparationTimeReporter reporter)
        {
            return measured;
        }

        TimeSpan elapsed = measured - reporter.ConsumePreparationTime();
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    private static void UpdateMaximum(TimingEntry entry, long ticks)
    {
        long current = Volatile.Read(ref entry.MaxTicks);
        while (ticks > current)
        {
            long seen = Interlocked.CompareExchange(ref entry.MaxTicks, ticks, current);
            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }

    private void LogSlowHook(string hook, Registration registration, TimeSpan elapsed)
    {
        _log.Warn(
            $"hook {hook}: the handler from extension '{registration.ExtensionId}' took {Milliseconds(elapsed)} ms, above the slow-hook threshold of {Milliseconds(_options.SlowHookThreshold)} ms; see /extensions for the full picture — later slow dispatches are counted, not logged."
        );
    }

    private void RaiseSlowHook(string extensionId, string hook, TimeSpan elapsed)
    {
        if (SlowHook is not { } notify)
        {
            return;
        }

        try
        {
            notify(new SlowHookEvent(extensionId, hook, elapsed, _options.SlowHookThreshold));
        }
        catch (Exception exception)
        {
            _log.Error(
                $"hook {hook}: the slow-hook notice failed: {exception.GetType().FullName}: {exception.Message}"
            );
        }
    }

    private TimingEntry Entry(string hook, string extensionId) =>
        _timings.GetOrAdd(
            new TimingKey(extensionId, hook),
            static (key, _) => new TimingEntry(key.Hook, CreateTags(key)),
            0
        );

    private static KeyValuePair<string, object?>[] CreateTags(TimingKey key) =>
        [new("extension", key.ExtensionId), new("hook", key.Hook)];

    private static string Milliseconds(TimeSpan span) =>
        span.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);

    private readonly record struct TimingKey(string ExtensionId, string Hook);

    private sealed class TimingEntry(string hook, KeyValuePair<string, object?>[] tags)
    {
        public string Hook { get; } = hook;

        public KeyValuePair<string, object?>[] Tags { get; } = tags;

        public long Calls;

        public long TotalTicks;

        public long LastTicks;

        public long MaxTicks;

        public long SlowCount;

        public long Completed;

        public long TimedOut;

        public long Cancelled;

        public int Warned;

        public HookTimings Snapshot() =>
            new(
                Hook,
                Interlocked.Read(ref Calls),
                ToMilliseconds(Interlocked.Read(ref TotalTicks)),
                ToMilliseconds(Interlocked.Read(ref LastTicks)),
                ToMilliseconds(Interlocked.Read(ref MaxTicks)),
                Interlocked.Read(ref SlowCount),
                Interlocked.Read(ref Completed),
                Interlocked.Read(ref TimedOut),
                Interlocked.Read(ref Cancelled)
            );

        private static double ToMilliseconds(long ticks) =>
            ticks / (double)TimeSpan.TicksPerMillisecond;
    }
}
