using System.Text.Json;
using Lunate.Extensibility;
using static Lunate.Coding.Tests.ExtensionsViewTestData;

namespace Lunate.Coding.Tests;

/// <summary>
/// The inactive-tool report of the <c>/extensions</c> view (tasks 5.4): a loaded extension whose
/// manifest declared a false activation rule reads <c>inactive (&lt;condition&gt;): &lt;reason&gt;</c>
/// after the tool counts, an active rule changes no counts, and the registered/declared counts stay
/// equal — the rule filters availability, not registration.
/// </summary>
public sealed class ExtensionsViewInactiveTests
{
    [Fact]
    public void A_false_rule_is_reported_after_the_tool_counts_with_its_condition_and_reason()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors =
        [
            Descriptor("acme-tools", tools: ["one", "two"]),
        ];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("acme-tools")];
        var reports = new List<ExtensionInactiveTool>
        {
            new(
                "acme-tools",
                "acme-tools__deactivate",
                "workspace-root-has-file",
                "no file matching '*.sln' at the workspace root"
            ),
        };

        string segment = Assert.Single(
            ExtensionsView.Segments(
                descriptors,
                results,
                registeredCounts: _ => new ExtensionCounts(2, 0, 0, 0),
                inactiveTools: id => reports
            )
        );

        Assert.StartsWith(
            "acme-tools 0.1.0 (global) loaded in 12 ms",
            segment,
            StringComparison.Ordinal
        );
        Assert.Contains(
            ", tools 2, inactive 'acme-tools__deactivate' (workspace-root-has-file): no file matching '*.sln' at the workspace root, commands 0, hooks 0, services 0",
            segment,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Registered_and_declared_counts_stay_equal_when_rules_filter()
    {
        // Two tools registered and declared, one inactive: the tools count is unchanged and the
        // equality marker ((N declared)) must not appear.
        IReadOnlyList<ExtensionDescriptor> descriptors =
        [
            Descriptor("acme-tools", tools: ["one", "two"]),
        ];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("acme-tools")];
        var reports = new List<ExtensionInactiveTool>
        {
            new(
                "acme-tools",
                "acme-tools__deactivate",
                "workspace-is-git-repository",
                "the workspace root is not a git repository"
            ),
        };

        string segment = Assert.Single(
            ExtensionsView.Segments(
                descriptors,
                results,
                registeredCounts: _ => new ExtensionCounts(2, 0, 0, 0),
                inactiveTools: id => reports
            )
        );

        Assert.Contains("tools 2,", segment, StringComparison.Ordinal);
        Assert.Contains(
            "inactive 'acme-tools__deactivate' (workspace-is-git-repository): the workspace root is not a git repository,",
            segment,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("(2 declared)", segment, StringComparison.Ordinal);
    }

    [Fact]
    public void Active_rules_and_no_reports_change_nothing()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors =
        [
            Descriptor("acme-tools", tools: ["one", "two"]),
        ];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("acme-tools")];

        string segment = Assert.Single(
            ExtensionsView.Segments(
                descriptors,
                results,
                registeredCounts: _ => new ExtensionCounts(2, 0, 0, 0),
                inactiveTools: _ => []
            )
        );

        Assert.Contains(
            ", tools 2, commands 0, hooks 0, services 0",
            segment,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("inactive", segment, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_inactive_tools_are_listed_in_report_order()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors =
        [
            Descriptor("acme-tools", tools: ["one", "two"]),
        ];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("acme-tools")];
        List<ExtensionInactiveTool> reports =
        [
            new(
                "acme-tools",
                "acme-tools__first",
                "workspace-root-has-file",
                "no file matching '*.sln' at the workspace root"
            ),
            new(
                "acme-tools",
                "acme-tools__second",
                "workspace-is-git-repository",
                "the workspace root is not a git repository"
            ),
        ];

        string segment = Assert.Single(
            ExtensionsView.Segments(
                descriptors,
                results,
                registeredCounts: _ => new ExtensionCounts(2, 0, 0, 0),
                inactiveTools: id => reports
            )
        );

        int first = segment.IndexOf(
            "inactive 'acme-tools__first' (workspace-root-has-file): no file matching '*.sln' at the workspace root, inactive 'acme-tools__second' (workspace-is-git-repository): the workspace root is not a git repository,",
            StringComparison.Ordinal
        );
        Assert.True(first >= 0, $"the report is missing from the segment: {segment}");
        Assert.True(
            first > segment.IndexOf(", tools 2,", StringComparison.Ordinal),
            "the report must follow the tool counts"
        );
    }

    [Fact]
    public void Json_carries_the_inactive_entries_only_when_they_exist()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors =
        [
            Descriptor("acme-tools"),
            Descriptor("quiet-tools"),
        ];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("acme-tools"), Loaded("quiet-tools")];
        var reports = new List<ExtensionInactiveTool>
        {
            new(
                "acme-tools",
                "acme-tools__deactivate",
                "workspace-root-has-file",
                "no file matching '*.sln' at the workspace root"
            ),
        };

        using JsonDocument document = JsonDocument.Parse(
            ExtensionsView.Json(
                descriptors,
                results,
                _ => new ExtensionCounts(2, 0, 0, 0),
                inactiveTools: id =>
                    string.Equals(id, "acme-tools", StringComparison.Ordinal) ? reports : []
            )
        );

        JsonElement reported = document.RootElement[0];
        JsonElement inactive = reported.GetProperty("inactive");
        JsonElement entry = Assert.Single(inactive.EnumerateArray());
        Assert.Equal("acme-tools__deactivate", entry.GetProperty("tool").GetString());
        Assert.Equal("workspace-root-has-file", entry.GetProperty("condition").GetString());
        Assert.Equal(
            "no file matching '*.sln' at the workspace root",
            entry.GetProperty("reason").GetString()
        );

        Assert.False(
            document.RootElement[1].TryGetProperty("inactive", out _),
            "an extension with no inactive tool must not carry the field"
        );
    }
}
