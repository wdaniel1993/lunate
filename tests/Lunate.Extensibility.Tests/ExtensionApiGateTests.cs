using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// Task 3.2 at the gate itself: an extension whose build saw a newer API than the host implements is
/// refused with the version it requires and the version it is running, and the packaged declaration
/// is held to the same wording. The evidence is the referenced assembly version — read from a real
/// fixture DLL here — or the version a build recorded, never a guess.
/// </summary>
public sealed class ExtensionApiGateTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void A_declared_range_above_the_host_is_refused_naming_both_versions()
    {
        string above = AboveCurrent();

        string? refusal = ExtensionApiGate.RefuseRange("acme", "^" + above);

        Assert.NotNull(refusal);
        Assert.Contains(
            FormattableString.Invariant($"requires Lunate ≥ {above}"),
            refusal,
            StringComparison.Ordinal
        );
        Assert.Contains(ExtensionApi.Current, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_range_at_the_host_is_accepted()
    {
        Assert.Null(ExtensionApiGate.RefuseRange("acme", "^" + ExtensionApi.Current));
        Assert.Null(ExtensionApiGate.RefuseRange("acme", ExtensionApi.Current));
    }

    [Fact]
    public void A_version_at_or_below_the_host_is_accepted_and_above_is_refused()
    {
        Assert.Null(ExtensionApiGate.Refuse("acme", ExtensionApi.Current));
        Assert.Null(ExtensionApiGate.Refuse("acme", "1.0.0"));
        string above = AboveCurrent();
        string? refusal = ExtensionApiGate.Refuse("acme", above);
        Assert.NotNull(refusal);
        Assert.Contains($"requires Lunate ≥ {above}", refusal, StringComparison.Ordinal);
        Assert.Contains(ExtensionApi.Current, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_that_cannot_be_read_is_reported_as_a_declaration_not_as_a_requirement()
    {
        string? refusal = ExtensionApiGate.Refuse("acme", "latest");

        Assert.NotNull(refusal);
        Assert.Contains("declares apiVersion 'latest'", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("requires Lunate", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void The_version_a_packaged_dll_references_is_read_from_its_metadata()
    {
        string fixture = TestExtensions.FixtureFile("HelloExtension.dll");

        Assert.Equal(
            typeof(ExtensionApi).Assembly.GetName().Version!.ToString(3),
            ExtensionApiGate.ReferencedVersion(fixture)
        );
    }

    [Fact]
    public void An_assembly_that_is_not_a_dll_has_no_referenced_version() =>
        Assert.Null(
            ExtensionApiGate.ReferencedVersion(
                TestExtensions.FixtureFile("HelloExtension.dll") + ".missing"
            )
        );

    [Fact]
    public async Task Discovery_refuses_a_packaged_extension_declaring_a_newer_api()
    {
        string above = AboveCurrent();
        using var temp = new TempDirectory();
        string directory = TestExtensions.GlobalExtensionDirectory(temp, "hello");
        TestExtensions.CopyFixture(directory, "HelloExtension.dll", "HelloExtension.Support.dll");
        TestExtensions.WriteManifest(directory, "hello", apiVersion: above);
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");

        ExtensionDiscoveryError error = Assert.Single(loader.DiscoveryErrors);
        Assert.Contains(
            FormattableString.Invariant($"requires Lunate ≥ {above}"),
            error.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(ExtensionApi.Current, error.Message, StringComparison.Ordinal);
        Assert.Empty(loader.Descriptors);
    }

    [Fact]
    public async Task A_packaged_extension_referencing_the_hosts_contract_still_loads()
    {
        // The accept side of the same gate, with a real fixture DLL whose AssemblyRef is the
        // contract this build resolved.
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");

        LoadedExtension loaded = await loader.Load(
            "hello",
            temp.Root,
            "repo",
            new FakeExtensionTrustPrompt(approve: true),
            Ct
        );

        Assert.Equal("hello", loaded.Id);
        Assert.Empty(loader.DiscoveryErrors);
    }

    /// <summary>The next minor above the host's API — derived, never a literal a bump would stale.</summary>
    private static string AboveCurrent()
    {
        Version version = Version.Parse(ExtensionApi.Current);
        return FormattableString.Invariant($"{version.Major}.{version.Minor + 1}.0");
    }
}
