using System.Globalization;
using System.Text.Json;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>/extensions</c> view formatter (design D2): state-group ordering, the pinned segment
/// shapes, the declared-count mismatch note, the slow marker, first-sentence truncation with the
/// log pointer, the CLI-measured label and invariant number formatting.
/// </summary>
public sealed class ExtensionsViewTests
{
    private static readonly Func<string, ExtensionCounts> NoCounts = _ => new ExtensionCounts(
        0,
        0,
        0,
        0
    );

    [Fact]
    public void Segments_order_failed_then_denied_then_loaded_and_by_id_within_groups()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors =
        [
            Descriptor("z-loaded"),
            Descriptor("m-denied", ExtensionScope.Project),
            Descriptor("a-loaded"),
            Descriptor("y-failed"),
            Descriptor("b-denied", ExtensionScope.Project),
            Descriptor("c-failed"),
        ];
        IReadOnlyList<ExtensionLoadResult> results =
        [
            Loaded("z-loaded"),
            Denied("m-denied"),
            Loaded("a-loaded"),
            Failed("y-failed", "boom"),
            Denied("b-denied"),
            Failed("c-failed", "boom"),
        ];

        IReadOnlyList<string> segments = ExtensionsView.Segments(descriptors, results, NoCounts);

        Assert.Equal(
            ["c-failed", "y-failed", "b-denied", "m-denied", "a-loaded", "z-loaded"],
            segments.Select(segment => segment.Split(' ')[0])
        );
    }

    [Fact]
    public void A_loaded_segment_carries_the_registered_counts_and_the_load_time()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors =
        [
            Descriptor(
                "ext-one",
                tools: ["a", "b"],
                hooks: ["tool-calling"],
                services: ["greeter"]
            ),
        ];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("ext-one", milliseconds: 12)];

        string segment = Assert.Single(
            ExtensionsView.Segments(descriptors, results, _ => new ExtensionCounts(2, 0, 1, 1))
        );

        Assert.Equal(
            "ext-one 0.1.0 (global) loaded in 12 ms, tools 2, commands 0, hooks 1, services 1",
            segment
        );
    }

    [Fact]
    public void A_denied_segment_carries_the_declared_counts()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors =
        [
            Descriptor(
                "ext-two",
                ExtensionScope.Project,
                version: "0.2.0",
                tools: ["one"],
                services: ["greeter"]
            ),
        ];
        IReadOnlyList<ExtensionLoadResult> results = [Denied("ext-two")];

        string segment = Assert.Single(ExtensionsView.Segments(descriptors, results, NoCounts));

        Assert.Equal(
            "ext-two 0.2.0 (project) trust-denied, tools 1, commands 0, hooks 0, services 1",
            segment
        );
    }

    [Fact]
    public void A_field_differing_from_the_manifest_reads_the_declared_count()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("ext-one", tools: ["a", "b"])];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("ext-one")];

        // Registered tools 1 against 2 declared; registered services 1 against 0 declared.
        string segment = Assert.Single(
            ExtensionsView.Segments(descriptors, results, _ => new ExtensionCounts(1, 0, 0, 1))
        );

        Assert.Contains("tools 1 (2 declared)", segment, StringComparison.Ordinal);
        Assert.Contains("services 1 (0 declared)", segment, StringComparison.Ordinal);
    }

    [Fact]
    public void A_denied_or_failed_segment_never_carries_a_mismatch_note()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors =
        [
            Descriptor("ext-two", ExtensionScope.Project, tools: ["one", "two"]),
        ];
        IReadOnlyList<ExtensionLoadResult> results = [Denied("ext-two")];

        string segment = Assert.Single(ExtensionsView.Segments(descriptors, results, NoCounts));

        Assert.DoesNotContain("declared)", segment, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_message_truncates_to_its_first_sentence_with_the_log_pointer()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("broken")];
        IReadOnlyList<ExtensionLoadResult> results =
        [
            Failed("broken", "one thing failed. another detail follows. and more."),
        ];

        string segment = Assert.Single(ExtensionsView.Segments(descriptors, results, NoCounts));

        Assert.StartsWith(
            "broken 0.1.0 (global) failed: one thing failed. — see the log, tools 0, commands 0, hooks 0, services 0",
            segment,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("one thing failed.")]
    [InlineData("one thing failed")]
    public void A_message_not_truncated_carries_no_log_pointer(string message)
    {
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("broken")];
        IReadOnlyList<ExtensionLoadResult> results = [Failed("broken", message)];

        string segment = Assert.Single(ExtensionsView.Segments(descriptors, results, NoCounts));

        Assert.Contains("failed: " + message + ",", segment, StringComparison.Ordinal);
        Assert.DoesNotContain("see the log", segment, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(500.0, false)]
    [InlineData(501.0, true)]
    [InlineData(700.0, true)]
    public void A_load_above_the_threshold_reads_the_slow_marker(double milliseconds, bool slow)
    {
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("ext-one")];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("ext-one", milliseconds)];

        string segment = Assert.Single(ExtensionsView.Segments(descriptors, results, NoCounts));

        Assert.Contains("loaded in ", segment, StringComparison.Ordinal);
        Assert.Equal(slow, segment.Contains("(slow)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cli_measured_load_labels_the_time_and_combines_with_slow()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors =
        [
            Descriptor("ext-one"),
            Descriptor("ext-two"),
        ];
        IReadOnlyList<ExtensionLoadResult> results =
        [
            Loaded("ext-one", milliseconds: 12),
            Loaded("ext-two", milliseconds: 700),
        ];

        IReadOnlyList<string> segments = ExtensionsView.Segments(
            descriptors,
            results,
            NoCounts,
            cliMeasured: true
        );

        Assert.Contains("loaded in 12 ms (CLI-measured),", segments[0], StringComparison.Ordinal);
        Assert.Contains(
            "loaded in 700 ms (CLI-measured, slow),",
            segments[1],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void The_view_formats_invariantly_under_de_AT()
    {
        using var culture = new CultureScope("de-AT");
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("ext-one")];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("ext-one", milliseconds: 1234.5)];

        string segment = Assert.Single(
            ExtensionsView.Segments(descriptors, results, _ => new ExtensionCounts(1540, 0, 0, 0))
        );

        Assert.Contains("loaded in 1234.5 ms", segment, StringComparison.Ordinal);
        Assert.Contains("tools 1540 (0 declared)", segment, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_carries_the_ordered_fields_with_load_time_only_on_loaded_extensions()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors =
        [
            Descriptor("ext-two", ExtensionScope.Project, version: "0.2.0", services: ["greeter"]),
            Descriptor("ext-one"),
        ];
        IReadOnlyList<ExtensionLoadResult> results = [Denied("ext-two"), Loaded("ext-one", 12)];

        using JsonDocument document = JsonDocument.Parse(
            ExtensionsView.Json(descriptors, results, _ => new ExtensionCounts(2, 0, 1, 1))
        );

        JsonElement denied = document.RootElement[0];
        Assert.Equal("ext-two", denied.GetProperty("id").GetString());
        Assert.Equal("0.2.0", denied.GetProperty("version").GetString());
        Assert.Equal("project", denied.GetProperty("scope").GetString());
        Assert.Equal("trust-denied", denied.GetProperty("state").GetString());
        Assert.False(denied.TryGetProperty("loadMs", out _));
        Assert.False(denied.TryGetProperty("slow", out _));
        Assert.Equal(0, denied.GetProperty("tools").GetInt32());
        Assert.Equal(0, denied.GetProperty("commands").GetInt32());
        Assert.Equal(0, denied.GetProperty("hooks").GetInt32());
        Assert.Equal(1, denied.GetProperty("services").GetInt32());

        JsonElement loaded = document.RootElement[1];
        Assert.Equal("ext-one", loaded.GetProperty("id").GetString());
        Assert.Equal("0.1.0", loaded.GetProperty("version").GetString());
        Assert.Equal("global", loaded.GetProperty("scope").GetString());
        Assert.Equal("loaded", loaded.GetProperty("state").GetString());
        Assert.Equal(12, loaded.GetProperty("loadMs").GetInt64());
        Assert.False(loaded.TryGetProperty("slow", out _));
        Assert.Equal(2, loaded.GetProperty("tools").GetInt32());
        Assert.Equal(0, loaded.GetProperty("commands").GetInt32());
        Assert.Equal(1, loaded.GetProperty("hooks").GetInt32());
        Assert.Equal(1, loaded.GetProperty("services").GetInt32());
    }

    [Fact]
    public void Json_marks_a_slow_load()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("ext-one")];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("ext-one", milliseconds: 700)];

        using JsonDocument document = JsonDocument.Parse(
            ExtensionsView.Json(descriptors, results, NoCounts)
        );

        JsonElement node = document.RootElement[0];
        Assert.Equal(700, node.GetProperty("loadMs").GetInt64());
        Assert.True(node.GetProperty("slow").GetBoolean());
    }

    private static ExtensionDescriptor Descriptor(
        string id,
        ExtensionScope scope = ExtensionScope.Global,
        string version = "0.1.0",
        IReadOnlyList<string>? tools = null,
        IReadOnlyList<string>? commands = null,
        IReadOnlyList<string>? hooks = null,
        IReadOnlyList<string>? services = null
    ) =>
        new(
            id,
            version,
            scope,
            "/store/" + id,
            new ExtensionManifest
            {
                Id = id,
                Version = version,
                ApiVersion = "^1.5.0",
                EntryAssembly = id + ".dll",
                Tools = tools ?? [],
                Commands = commands ?? [],
                Hooks = hooks ?? [],
                Services = services ?? [],
            }
        );

    private static ExtensionLoadResult Loaded(string id, double milliseconds = 12) =>
        new(id, ExtensionLoadState.Loaded, TimeSpan.FromMilliseconds(milliseconds), null);

    private static ExtensionLoadResult Denied(string id) =>
        new(id, ExtensionLoadState.TrustDenied, TimeSpan.FromMilliseconds(1), "was not trusted.");

    private static ExtensionLoadResult Failed(string id, string message) =>
        new(id, ExtensionLoadState.Failed, TimeSpan.FromMilliseconds(1), message);

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous;

        public CultureScope(string name)
        {
            _previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
