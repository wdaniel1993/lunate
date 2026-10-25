using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// Task 3.2 on the single-file path: the <c>apiVersion</c> a build recorded is the evidence a load
/// gates on. A build at the host's version loads, one recorded above it is refused by name — the
/// required version and the running one — before any of its code runs, and one built before an API
/// bump still loads because its record is at or below the host's.
/// </summary>
public sealed class SingleFileApiGateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_build_recorded_at_the_hosts_api_loads()
    {
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        SingleFileBuildOutcome built = await fixture.BuildAsync(
            SingleFileLoadFixture.Source(probe: "\"equal\""),
            cancellationToken: Ct
        );
        Assert.Equal(ExtensionApi.Current, built.Build!.Manifest.ApiVersion);

        ExtensionLoader loader = fixture.Discovered();
        LoadedExtension loaded = await loader.LoadSingleFileAsync(fixture.Id, Ct);

        Assert.Equal(fixture.Id, loaded.Id);
        Assert.False(fixture.Log.Contains("requires Lunate"));
    }

    [Fact]
    public async Task A_build_recorded_above_the_hosts_api_is_refused_naming_both_versions()
    {
        string above = VersionAboveCurrent();
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        SingleFileBuildOutcome built = await fixture.BuildAsync(
            SingleFileLoadFixture.Source(probe: "\"ahead\""),
            cancellationToken: Ct
        );
        fixture.RewriteBuildApiVersion(built.Build!.Key, above);

        ExtensionLoader loader = fixture.Discovered();
        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.LoadSingleFileAsync(fixture.Id, Ct)
        );

        Assert.Contains(
            FormattableString.Invariant($"requires Lunate ≥ {above}"),
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(ExtensionApi.Current, exception.Message, StringComparison.Ordinal);
        // Nothing of the extension ran: no tool, no command, nothing registered.
        Assert.Empty(loader.Tools.List());
        Assert.Empty(loader.CommandSources.List());
    }

    [Fact]
    public async Task A_build_recorded_before_an_api_bump_still_loads()
    {
        // Older than the host, the way a build made by the previous release is: <= is accepted, so a
        // Lunate update does not orphan the extension it just built.
        string below = VersionBelowCurrent();
        using SingleFileLoadFixture fixture = SingleFileLoadFixture.Create();
        SingleFileBuildOutcome built = await fixture.BuildAsync(
            SingleFileLoadFixture.Source(probe: "\"older\""),
            cancellationToken: Ct
        );
        fixture.RewriteBuildApiVersion(built.Build!.Key, below);

        ExtensionLoader loader = fixture.Discovered();
        LoadedExtension loaded = await loader.LoadSingleFileAsync(fixture.Id, Ct);

        Assert.Equal(fixture.Id, loaded.Id);
        Assert.False(fixture.Log.Contains("requires Lunate"));
    }

    /// <summary>The next minor above the host's API — derived, never a literal a bump would stale.</summary>
    internal static string VersionAboveCurrent()
    {
        Version version = HostVersion();
        return FormattableString.Invariant($"{version.Major}.{version.Minor + 1}.0");
    }

    /// <summary>A version below the host's: its major, if that is smaller, else a lower patch.</summary>
    internal static string VersionBelowCurrent()
    {
        Version version = HostVersion();
        return version.Minor > 0
            ? FormattableString.Invariant($"{version.Major}.{version.Minor - 1}.0")
            : FormattableString.Invariant(
                $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build - 1)}"
            );
    }

    internal static Version HostVersion() => Version.Parse(ExtensionApi.Current);
}
