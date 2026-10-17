using System.Globalization;
using System.Text.Json;
using Lunate.Extensibility;
using static Lunate.Coding.Tests.ExtensionsViewTestData;

namespace Lunate.Coding.Tests;

/// <summary>
/// The hook-timing half of the <c>/extensions</c> view (design D3/D5): the slowest crossed hook
/// suffix, the remote start suffix, the <c>--json</c> <c>hookTimings</c>/<c>startMs</c> contract
/// and invariant rendering.
/// </summary>
public sealed class ExtensionsViewHookTimingTests
{
    [Fact]
    public void The_slowest_crossed_hook_appends_to_the_loaded_segment()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("ext-one")];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("ext-one", milliseconds: 12)];
        IReadOnlyList<HookTimings> timings =
        [
            new HookTimings("ContextBuilding", 5, 1900, 40, 1900, 0, 5, 0, 0),
            new HookTimings("ToolCalling", 120, 6000, 812.4, 812.4, 3, 117, 1, 2),
            new HookTimings("RunStarting", 8, 900, 900, 900, 1, 8, 0, 0),
        ];

        string segment = Assert.Single(
            ExtensionsView.Segments(
                descriptors,
                results,
                _ => new ExtensionCounts(2, 0, 3, 1),
                hookTimings: _ => timings
            )
        );

        Assert.Equal(
            "ext-one 0.1.0 (global) loaded in 12 ms, tools 2 (0 declared), commands 0, hooks 3 (0 declared), services 1 (0 declared), hook RunStarting max 900 ms, 1/8 slow",
            segment
        );
    }

    [Fact]
    public void A_quiet_extension_gets_no_hook_suffix()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("ext-one")];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("ext-one")];
        IReadOnlyList<HookTimings> timings =
        [
            new HookTimings("ToolCalling", 120, 600, 40, 60, 0, 96, 0, 24),
        ];

        string segment = Assert.Single(
            ExtensionsView.Segments(descriptors, results, NoCounts, hookTimings: _ => timings)
        );

        Assert.DoesNotContain("hook ", segment, StringComparison.Ordinal);
    }

    [Fact]
    public void A_remote_start_appends_to_the_loaded_segment()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("ext-remote")];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("ext-remote", milliseconds: 4)];

        string segment = Assert.Single(
            ExtensionsView.Segments(
                descriptors,
                results,
                NoCounts,
                remoteStart: _ => TimeSpan.FromMilliseconds(640.5)
            )
        );

        Assert.EndsWith(", start 641 ms", segment, StringComparison.Ordinal);
        Assert.DoesNotContain("hook ", segment, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_carries_hook_timings_and_start_ms()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("ext-remote")];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("ext-remote", milliseconds: 4)];
        IReadOnlyList<HookTimings> timings =
        [
            new HookTimings("ToolCalling", 120, 6000.5, 40.4, 812.6, 3, 117, 1, 2),
        ];

        using JsonDocument document = JsonDocument.Parse(
            ExtensionsView.Json(
                descriptors,
                results,
                NoCounts,
                hookTimings: _ => timings,
                remoteStart: _ => TimeSpan.FromMilliseconds(640.4)
            )
        );

        JsonElement node = document.RootElement[0];
        Assert.Equal(640, node.GetProperty("startMs").GetInt64());
        JsonElement entry = Assert.Single(node.GetProperty("hookTimings").EnumerateArray());
        Assert.Equal("ToolCalling", entry.GetProperty("hook").GetString());
        Assert.Equal(120, entry.GetProperty("calls").GetInt64());
        Assert.Equal(6001, entry.GetProperty("totalMs").GetInt64());
        Assert.Equal(40, entry.GetProperty("lastMs").GetInt64());
        Assert.Equal(813, entry.GetProperty("maxMs").GetInt64());
        Assert.Equal(3, entry.GetProperty("slowCount").GetInt64());
        Assert.Equal(117, entry.GetProperty("completed").GetInt64());
        Assert.Equal(1, entry.GetProperty("timedOut").GetInt64());
        Assert.Equal(2, entry.GetProperty("cancelled").GetInt64());
    }

    [Fact]
    public void Json_omits_hook_timings_when_nothing_dispatched_and_start_ms_when_unknown()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("ext-one")];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("ext-one")];

        using JsonDocument document = JsonDocument.Parse(
            ExtensionsView.Json(descriptors, results, NoCounts, _ => [], _ => null)
        );

        JsonElement node = document.RootElement[0];
        Assert.False(node.TryGetProperty("hookTimings", out _));
        Assert.False(node.TryGetProperty("startMs", out _));
    }

    [Fact]
    public void Hook_timing_numbers_render_invariantly_under_de_AT()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
            IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("ext-remote")];
            IReadOnlyList<ExtensionLoadResult> results = [Loaded("ext-remote")];
            IReadOnlyList<HookTimings> timings =
            [
                new HookTimings("ToolCalling", 120, 6000.5, 40.4, 812.4, 3, 120, 0, 0),
            ];

            string segment = Assert.Single(
                ExtensionsView.Segments(
                    descriptors,
                    results,
                    NoCounts,
                    hookTimings: _ => timings,
                    remoteStart: _ => TimeSpan.FromMilliseconds(640.4)
                )
            );
            string json = ExtensionsView.Json(
                descriptors,
                results,
                NoCounts,
                hookTimings: _ => timings,
                remoteStart: _ => TimeSpan.FromMilliseconds(640.4)
            );

            Assert.Contains("max 812 ms, 3/120 slow", segment, StringComparison.Ordinal);
            Assert.Contains("start 640 ms", segment, StringComparison.Ordinal);
            Assert.DoesNotContain("812,", segment, StringComparison.Ordinal);
            Assert.Contains("\"maxMs\": 812", json, StringComparison.Ordinal);
            Assert.Contains("\"startMs\": 640", json, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
