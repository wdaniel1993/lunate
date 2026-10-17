using System.Diagnostics.Metrics;

namespace Lunate.Extensibility.Tests;

using static Lunate.Extensibility.Tests.HookSemanticsSupport;

/// <summary>
/// The hook-duration histogram: meter <c>Lunate.Extensibility</c>, unit ms, tagged with the
/// extension id and the hook (design D1).
/// </summary>
public sealed class HookRunnerMetricsTests
{
    [Fact]
    public async Task A_dispatch_records_the_histogram_with_extension_and_hook_tags()
    {
        var measurements = new List<Measurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (
                string.Equals(
                    instrument.Meter.Name,
                    "Lunate.Extensibility",
                    StringComparison.Ordinal
                )
            )
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) =>
            {
                var captured = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object?> tag in tags)
                {
                    captured[tag.Key] = tag.Value;
                }

                lock (measurements)
                {
                    measurements.Add(new Measurement(instrument.Name, value, captured));
                }
            }
        );
        listener.Start();
        var runner = new HookRunner();
        runner.Register(
            "ext-metrics",
            new TestRunSettledHandler((_, _) => ValueTask.CompletedTask)
        );

        await runner.RunRunSettledAsync(TestHookPayloads.RunSettled, Ct);

        Measurement measured;
        lock (measurements)
        {
            measured = Assert.Single(
                measurements,
                entry => entry.Tags.GetValueOrDefault("extension") is "ext-metrics"
            );
        }

        Assert.Equal("hook.duration", measured.Instrument);
        Assert.True(measured.Value >= 0);
        Assert.Equal("ext-metrics", measured.Tags["extension"]);
        Assert.Equal("RunSettled", measured.Tags["hook"]);
    }

    private sealed record Measurement(
        string Instrument,
        double Value,
        Dictionary<string, object?> Tags
    );
}
