using System.Text.Json;
using Lunate.Extensibility;
using static Lunate.Coding.Tests.ExtensionsViewTestData;

namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>/extensions</c> view of single-file candidates (spec: "Extension states and reload",
/// phase 1 states): <c>not-built</c>, <c>invalid (no #:extension)</c>, <c>stale</c> and
/// <c>failed (trust)</c>, each with its reason on the same line, appended after the packaged
/// segments and problems first inside the group.
/// </summary>
public sealed class ExtensionsViewSingleFileTests
{
    [Fact]
    public void A_not_built_file_reads_not_built_with_the_counts_it_declares()
    {
        SingleFileExtension candidate = SingleFile(tools: ["fetch_issue", "close_issue"]);

        string segment = Assert.Single(
            ExtensionsView.Segments([], [], NoCounts, singleFile: [candidate])
        );

        Assert.Equal(
            "acme-tools 1.0.0 (global, single file) not-built, tools 2, commands 0, hooks 0, services 0",
            segment
        );
    }

    [Fact]
    public void A_file_without_a_header_reads_invalid_no_extension()
    {
        SingleFileExtension candidate = SingleFile(
            id: null,
            scope: ExtensionScope.Project,
            state: SingleFileExtensionState.Invalid,
            reason: "no #:extension"
        );

        string segment = Assert.Single(
            ExtensionsView.Segments([], [], NoCounts, singleFile: [candidate])
        );

        Assert.Equal("acme.cs (project, single file) invalid (no #:extension)", segment);
    }

    [Fact]
    public void A_stale_file_carries_its_reason_on_the_same_line()
    {
        SingleFileExtension candidate = SingleFile(
            state: SingleFileExtensionState.Stale,
            reason: "the file or its declared packages changed since it was trusted"
        );

        string segment = Assert.Single(
            ExtensionsView.Segments([], [], NoCounts, singleFile: [candidate])
        );

        Assert.Equal(
            "acme-tools 1.0.0 (global, single file) stale — the file or its declared packages changed since it was trusted",
            segment
        );
        Assert.DoesNotContain('\n', segment);
    }

    [Fact]
    public void A_restored_file_shows_the_resolved_graph_from_its_lock()
    {
        SingleFileExtension candidate = SingleFile(
            restored: new ExtensionLock
            {
                ExtensionId = "acme-tools",
                ExtensionVersion = "1.0.0",
                SourceDigest = "digest",
                PackagesWritten = ["Octokit@14.0.0"],
                Toolset = new ExtensionLockToolset
                {
                    Tfm = "net10.0",
                    Rid = "osx-arm64",
                    Runtime = "10.0.13",
                    ReferencePack = new ExtensionLockReferencePack
                    {
                        Id = "Microsoft.NETCore.App.Ref",
                        Version = "10.0.13",
                        Rule = ReferencePackRules.Exact,
                    },
                },
                Sources = ["nuget.org"],
                Packages =
                [
                    FakePackage("Octokit", "14.0.0"),
                    FakePackage("Octokit.Dependency", "1.2.0"),
                ],
            }
        );

        string segment = Assert.Single(
            ExtensionsView.Segments([], [], NoCounts, singleFile: [candidate])
        );

        Assert.EndsWith(
            ", resolved: Octokit 14.0.0, Octokit.Dependency 1.2.0",
            segment,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void A_restore_in_flight_reads_restoring_rather_than_a_stale_row()
    {
        SingleFileExtension candidate = SingleFile(state: SingleFileExtensionState.Restoring);

        string segment = Assert.Single(
            ExtensionsView.Segments([], [], NoCounts, singleFile: [candidate])
        );

        Assert.Equal("acme-tools 1.0.0 (global, single file) restoring", segment);
    }

    [Fact]
    public void A_restore_failure_reads_failed_restore_with_its_reason()
    {
        SingleFileExtension candidate = SingleFile(
            state: SingleFileExtensionState.RestoreFailed,
            reason: "restore failed: package 'Octokit' was not found on any configured feed (nuget.org)."
        );

        string segment = Assert.Single(
            ExtensionsView.Segments([], [], NoCounts, singleFile: [candidate])
        );

        Assert.Equal(
            "acme-tools 1.0.0 (global, single file) failed (restore) — restore failed: package 'Octokit' was not found on any configured feed (nuget.org).",
            segment
        );
    }

    [Fact]
    public void A_failed_trust_file_carries_its_reason_on_the_same_line()
    {
        SingleFileExtension candidate = SingleFile(
            state: SingleFileExtensionState.TrustFailed,
            reason: "the trust record for this file is unreadable"
        );

        string segment = Assert.Single(
            ExtensionsView.Segments([], [], NoCounts, singleFile: [candidate])
        );

        Assert.Equal(
            "acme-tools 1.0.0 (global, single file) failed (trust) — the trust record for this file is unreadable",
            segment
        );
    }

    [Fact]
    public void Single_file_segments_follow_the_packaged_ones_and_order_problems_first()
    {
        IReadOnlyList<ExtensionDescriptor> descriptors = [Descriptor("packaged")];
        IReadOnlyList<ExtensionLoadResult> results = [Loaded("packaged")];
        IReadOnlyList<SingleFileExtension> singleFile =
        [
            SingleFile(id: "z-tools", fileName: "z.cs"),
            SingleFile(
                id: "a-tools",
                fileName: "a.cs",
                state: SingleFileExtensionState.Stale,
                reason: "changed"
            ),
            SingleFile(
                id: null,
                fileName: "broken.cs",
                state: SingleFileExtensionState.Invalid,
                reason: "no #:extension"
            ),
        ];

        IReadOnlyList<string> segments = ExtensionsView.Segments(
            descriptors,
            results,
            NoCounts,
            singleFile: singleFile
        );

        Assert.Equal(4, segments.Count);
        Assert.StartsWith(
            "packaged 0.1.0 (global) loaded in ",
            segments[0],
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            "broken.cs (global, single file) invalid",
            segments[1],
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            "a-tools 1.0.0 (global, single file) stale",
            segments[2],
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            "z-tools 1.0.0 (global, single file) not-built",
            segments[3],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void A_declared_name_nothing_provided_reads_declared_not_provided()
    {
        SingleFileExtension candidate = SingleFile(
            tools: ["fetch_issue", "ghost"],
            declaredNotProvided: [new ExtensionDeclaration(ExtensionDeclarationKind.Tool, "ghost")]
        );

        string segment = Assert.Single(
            ExtensionsView.Segments([], [], NoCounts, singleFile: [candidate])
        );

        Assert.Contains(
            ", tools 2, commands 0, hooks 0, services 0, declared, not provided: ghost",
            segment,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Json_carries_the_single_file_candidate_with_its_state_and_reason()
    {
        IReadOnlyList<SingleFileExtension> singleFile =
        [
            SingleFile(tools: ["fetch_issue"]),
            SingleFile(
                id: null,
                scope: ExtensionScope.Project,
                state: SingleFileExtensionState.Invalid,
                reason: "no #:extension",
                fileName: "broken.cs"
            ),
            SingleFile(
                id: "gone-tools",
                state: SingleFileExtensionState.TrustFailed,
                reason: "the trust record for this file is unreadable",
                fileName: "gone.cs"
            ),
        ];

        using JsonDocument document = JsonDocument.Parse(
            ExtensionsView.Json([], [], NoCounts, singleFile: singleFile)
        );

        // The JSON array is in the text view's order: problems first, then by id.
        Assert.Equal(
            ["broken.cs", "gone.cs", "acme.cs"],
            document
                .RootElement.EnumerateArray()
                .Select(node => node.GetProperty("singleFile").GetString())
        );

        JsonElement notBuilt = Node(document, "acme.cs");
        Assert.Equal("acme-tools", notBuilt.GetProperty("id").GetString());
        Assert.Equal("1.0.0", notBuilt.GetProperty("version").GetString());
        Assert.Equal("global", notBuilt.GetProperty("scope").GetString());
        Assert.Equal("not-built", notBuilt.GetProperty("state").GetString());
        Assert.Equal(1, notBuilt.GetProperty("tools").GetInt32());
        Assert.False(notBuilt.TryGetProperty("reason", out _));
        Assert.False(notBuilt.TryGetProperty("failure", out _));

        JsonElement invalid = Node(document, "broken.cs");
        Assert.Equal(JsonValueKind.Null, invalid.GetProperty("id").ValueKind);
        Assert.Equal("invalid", invalid.GetProperty("state").GetString());
        Assert.Equal("no #:extension", invalid.GetProperty("reason").GetString());

        JsonElement failed = Node(document, "gone.cs");
        Assert.Equal("failed", failed.GetProperty("state").GetString());
        Assert.Equal("trust", failed.GetProperty("failure").GetString());
        Assert.Contains(
            "unreadable",
            failed.GetProperty("reason").GetString(),
            StringComparison.Ordinal
        );
    }

    private static ExtensionLockPackage FakePackage(string id, string version) =>
        new()
        {
            Id = id,
            Version = version,
            Sha512 = "ZmFrZQ==",
            Requested = FormattableString.Invariant($"{id}@{version}"),
            Direct = true,
        };

    private static JsonElement Node(JsonDocument document, string fileName) =>
        document
            .RootElement.EnumerateArray()
            .Single(node =>
                string.Equals(
                    node.GetProperty("singleFile").GetString(),
                    fileName,
                    StringComparison.Ordinal
                )
            );

    private static SingleFileExtension SingleFile(
        string? id = "acme-tools",
        string version = "1.0.0",
        ExtensionScope scope = ExtensionScope.Global,
        SingleFileExtensionState state = SingleFileExtensionState.NotBuilt,
        string? reason = null,
        IReadOnlyList<string>? tools = null,
        IReadOnlyList<ExtensionDeclaration>? declaredNotProvided = null,
        ExtensionLock? restored = null,
        string fileName = "acme.cs"
    ) =>
        new()
        {
            Path = "/store/extensions/" + fileName,
            Scope = scope,
            Header = new ExtensionHeader
            {
                Id = id,
                Version = id is null ? null : version,
                Tools = tools ?? [],
            },
            State = state,
            Reason = reason,
            Lock = restored,
            DeclaredNotProvided = declaredNotProvided ?? [],
        };
}
