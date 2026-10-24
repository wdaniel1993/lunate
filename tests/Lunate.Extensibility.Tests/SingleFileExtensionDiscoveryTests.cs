using System.Text;
using System.Text.Json;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// Single-file discovery (spec: "Single-file extension discovery" and the trust requirement's
/// state half): every <c>*.cs</c> directly under an extensions root is a candidate reported by its
/// declared id, a file with no header is listed as <c>invalid (no #:extension)</c>, duplicate ids
/// are refused naming both paths, and a trusted file whose bytes changed is <c>stale</c> before
/// anything could be built.
/// </summary>
public sealed class SingleFileExtensionDiscoveryTests
{
    [Fact]
    public void A_file_with_a_header_is_a_candidate_reported_by_its_declared_id()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string path = fixture.WriteGlobal("acme.cs", SingleFileFixture.Valid);
        ExtensionLoader loader = fixture.Loader();

        loader.Discover(fixture.Worktree, "repo");

        Assert.Empty(loader.Descriptors);
        Assert.Empty(loader.DiscoveryErrors);
        SingleFileExtension candidate = Assert.Single(loader.SingleFileExtensions);
        Assert.Equal("acme-tools", candidate.Id);
        Assert.Equal("1.0.0", candidate.Version);
        Assert.Equal(ExtensionScope.Global, candidate.Scope);
        Assert.Equal(path, candidate.Path);
        Assert.Equal("acme.cs", candidate.FileName);
        Assert.Equal(SingleFileExtensionState.NotBuilt, candidate.State);
        Assert.Null(candidate.Reason);
        Assert.Equal(64, candidate.Digest.Length);
        // Nothing was built, restored or trusted: no cache directory and no trust record exist.
        Assert.False(Directory.Exists(Path.Combine(fixture.Store, "cache")));
        Assert.False(File.Exists(fixture.TrustFile));
    }

    [Fact]
    public void A_file_without_the_extension_directive_is_listed_invalid_and_writes_nothing()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        fixture.WriteProject("broken.cs", "public static class Broken { }\n");
        ExtensionLoader loader = fixture.Loader();

        loader.Discover(fixture.Worktree, "repo");

        Assert.Empty(loader.Descriptors);
        Assert.Empty(loader.DiscoveryErrors);
        SingleFileExtension candidate = Assert.Single(loader.SingleFileExtensions);
        Assert.Equal(ExtensionScope.Project, candidate.Scope);
        Assert.Null(candidate.Id);
        Assert.Equal(SingleFileExtensionState.Invalid, candidate.State);
        Assert.Equal("no #:extension", candidate.Reason);
        Assert.Equal(string.Empty, candidate.Digest);
        Assert.False(File.Exists(fixture.TrustFile));
    }

    [Fact]
    public void A_refused_directive_lists_the_file_invalid_naming_its_line()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        fixture.WriteGlobal(
            "sdk.cs",
            "#:extension acme-tools@1.0.0\n#:sdk Microsoft.NET.Sdk\n\npublic sealed class Acme;\n"
        );

        SingleFileExtension candidate = Assert.Single(fixture.Discover());

        Assert.Equal(SingleFileExtensionState.Invalid, candidate.State);
        Assert.Equal("acme-tools", candidate.Id);
        Assert.StartsWith("line 2:", candidate.Reason, StringComparison.Ordinal);
        Assert.Contains("#:sdk", candidate.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_files_declaring_one_id_are_both_refused_naming_both_paths()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string global = fixture.WriteGlobal("first.cs", SingleFileFixture.Header("acme-tools"));
        string project = fixture.WriteProject("second.cs", SingleFileFixture.Header("acme-tools"));
        ExtensionLoader loader = fixture.Loader();

        IReadOnlyList<SingleFileExtension> candidates = fixture.Discover(loader);

        Assert.Empty(loader.Descriptors);
        Assert.Equal(2, candidates.Count);
        foreach (SingleFileExtension candidate in candidates)
        {
            Assert.Equal(SingleFileExtensionState.Invalid, candidate.State);
            Assert.Equal("acme-tools", candidate.Id);
            Assert.Contains(global, candidate.Reason, StringComparison.Ordinal);
            Assert.Contains(project, candidate.Reason, StringComparison.Ordinal);
        }

        Assert.Equal(2, loader.DiscoveryErrors.Count);
        foreach (ExtensionDiscoveryError error in loader.DiscoveryErrors)
        {
            Assert.Contains("acme-tools", error.Message, StringComparison.Ordinal);
            Assert.Contains(global, error.Message, StringComparison.Ordinal);
            Assert.Contains(project, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_file_colliding_with_a_packaged_extension_is_refused_naming_both_paths()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string packaged = TestExtensions.GlobalExtensionDirectory(temp, "acme");
        TestExtensions.WriteManifest(packaged, "acme");
        string file = fixture.WriteGlobal("acme.cs", SingleFileFixture.Header("acme"));

        ExtensionLoader loader = fixture.Loader();
        IReadOnlyList<SingleFileExtension> candidates = fixture.Discover(loader);

        Assert.Equal(["acme"], loader.Descriptors.Select(descriptor => descriptor.Id));
        SingleFileExtension candidate = Assert.Single(candidates);
        Assert.Equal(SingleFileExtensionState.Invalid, candidate.State);
        Assert.Contains(packaged, candidate.Reason, StringComparison.Ordinal);
        Assert.Contains(file, candidate.Reason, StringComparison.Ordinal);
        ExtensionDiscoveryError error = Assert.Single(loader.DiscoveryErrors);
        Assert.Contains(packaged, error.Message, StringComparison.Ordinal);
        Assert.Contains(file, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_files_directly_under_the_root_are_candidates()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string packaged = TestExtensions.GlobalExtensionDirectory(temp, "hello");
        TestExtensions.WriteManifest(packaged, "hello");
        File.WriteAllText(Path.Combine(packaged, "Helper.cs"), SingleFileFixture.Header("nested"));
        File.WriteAllText(
            Path.Combine(fixture.Store, "loose.cs"),
            SingleFileFixture.Header("loose")
        );

        Assert.Empty(fixture.Discover());
    }

    [Fact]
    public void Global_files_are_listed_before_project_files_and_each_group_by_file_name()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        fixture.WriteGlobal("z-global.cs", SingleFileFixture.Header("z-tools"));
        fixture.WriteGlobal("a-global.cs", SingleFileFixture.Header("a-tools"));
        fixture.WriteProject("p-project.cs", SingleFileFixture.Header("p-tools"));

        IReadOnlyList<SingleFileExtension> candidates = fixture.Discover();

        Assert.Equal(["a-tools", "z-tools", "p-tools"], candidates.Select(entry => entry.Id));
        Assert.Equal(
            [ExtensionScope.Global, ExtensionScope.Global, ExtensionScope.Project],
            candidates.Select(entry => entry.Scope)
        );
    }

    [Fact]
    public void A_sidecar_manifest_beside_the_file_is_ignored()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        fixture.WriteGlobal("acme.cs", SingleFileFixture.Valid);
        File.WriteAllText(
            Path.Combine(fixture.GlobalRoot, "acme.json"),
            """
            {"id":"sidecar.tools","version":"9.9.9","apiVersion":"^1.0.0","entryAssembly":"acme.dll","tools":["other"]}
            """
        );
        File.WriteAllText(
            Path.Combine(fixture.GlobalRoot, "extension.json"),
            """
            {"id":"root.tools","version":"9.9.9","apiVersion":"^1.0.0","entryAssembly":"acme.dll"}
            """
        );
        ExtensionLoader loader = fixture.Loader();

        IReadOnlyList<SingleFileExtension> candidates = fixture.Discover(loader);

        Assert.Empty(loader.Descriptors);
        SingleFileExtension candidate = Assert.Single(candidates);
        Assert.Equal("acme-tools", candidate.Id);
        Assert.Equal(["fetch_issue"], candidate.Header.Tools);
    }

    [Fact]
    public void A_trusted_file_that_changed_since_is_stale()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string path = fixture.WriteGlobal("acme.cs", SingleFileFixture.Valid);
        SingleFileExtension trusted = Assert.Single(fixture.Discover());
        fixture.Trust().RecordSingleFileTrust(path, trusted.Digest);

        Assert.Equal(SingleFileExtensionState.NotBuilt, Assert.Single(fixture.Discover()).State);

        File.AppendAllText(path, "\n// one byte changed\n");
        SingleFileExtension stale = Assert.Single(fixture.Discover());

        Assert.Equal(SingleFileExtensionState.Stale, stale.State);
        Assert.Contains("changed since it was trusted", stale.Reason, StringComparison.Ordinal);
        Assert.NotEqual(trusted.Digest, stale.Digest);
    }

    [Fact]
    public void A_trust_record_this_version_cannot_read_reads_failed_trust()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        string path = fixture.WriteGlobal("acme.cs", SingleFileFixture.Valid);
        Directory.CreateDirectory(fixture.Store);
        File.WriteAllText(
            fixture.TrustFile,
            JsonSerializer.Serialize(
                new
                {
                    singleFile = new Dictionary<string, object>
                    {
                        [path] = new { digest = "not-the-shape-this-version-writes" },
                    },
                }
            )
        );

        SingleFileExtension candidate = Assert.Single(fixture.Discover());

        Assert.Equal(SingleFileExtensionState.TrustFailed, candidate.State);
        Assert.Contains("trust.json", candidate.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_directive_warns_on_the_log_and_leaves_the_file_usable()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        fixture.WriteGlobal(
            "acme.cs",
            "#:extension acme-tools@1.0.0\n#:whatever acme\n\npublic sealed class Acme;\n"
        );
        var log = new RecordingExtensionLog();

        SingleFileExtension candidate = Assert.Single(fixture.Discover(log: log));

        Assert.Equal(SingleFileExtensionState.NotBuilt, candidate.State);
        Assert.True(
            log.Contains("#:whatever"),
            "the unknown directive was not warned about; log:\n" + string.Join("\n", log.Messages)
        );
    }

    [Fact]
    public void Discover_replaces_the_previous_result()
    {
        using var temp = new TempDirectory();
        var fixture = new SingleFileFixture(temp);
        fixture.WriteGlobal("first.cs", SingleFileFixture.Header("first.tools"));
        ExtensionLoader loader = fixture.Loader();
        Assert.Single(fixture.Discover(loader));

        File.Delete(Path.Combine(fixture.GlobalRoot, "first.cs"));
        fixture.WriteGlobal("second.cs", SingleFileFixture.Header("second-tools"));

        Assert.Equal(["second-tools"], fixture.Discover(loader).Select(entry => entry.Id));
    }

    [Fact]
    public void The_digest_covers_the_id_the_packages_as_written_and_the_source_bytes()
    {
        byte[] source = Encoding.UTF8.GetBytes(
            "#:extension acme-tools@1.0.0\n#:package Octokit@14.0.0\n"
        );
        ExtensionPackageReference octokit = new("Octokit", "14.0.0", "Octokit@14.0.0");
        ExtensionPackageReference ranged = new(
            "Octokit",
            "[14.0.0,15.0.0)",
            "Octokit@[14.0.0,15.0.0)"
        );
        byte[] edited = Encoding.UTF8.GetBytes(
            "#:extension acme-tools@1.0.0\n#:package Octokit@14.0.0\n// edited\n"
        );

        string baseline = SingleFileExtensionDigest.Compute("acme-tools", [octokit], source);

        // Pinned: the format is a contract, so a silent change to it must fail here.
        Assert.Equal("8948713b04319850831da7187667bdff46b6fa2bab41d781712bdbb35185e47c", baseline);
        Assert.Equal(baseline, SingleFileExtensionDigest.Compute("acme-tools", [octokit], source));
        Assert.NotEqual(
            baseline,
            SingleFileExtensionDigest.Compute("other-tools", [octokit], source)
        );
        Assert.NotEqual(
            baseline,
            SingleFileExtensionDigest.Compute("acme-tools", [ranged], source)
        );
        Assert.NotEqual(
            baseline,
            SingleFileExtensionDigest.Compute("acme-tools", [octokit], edited)
        );
        Assert.NotEqual(baseline, SingleFileExtensionDigest.Compute("acme-tools", [], source));
    }
}
