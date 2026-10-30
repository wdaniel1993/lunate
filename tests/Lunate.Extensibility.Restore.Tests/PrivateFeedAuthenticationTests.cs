using System.Text;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// Private feeds (spec: "Install output and private feeds") that ask credentials of their clients.
/// A credential-protected feed answered with NuGet's own client through this engine is where issue
/// #244 lives: the failure must name the feed, the status and the file that carries the fix,
/// instead of a generic "could not be downloaded" with the 401 buried below it.
/// </summary>
public sealed class PrivateFeedAuthenticationTests
{
    [Fact]
    public async Task An_authentication_failure_names_the_feed_and_the_file_to_fix()
    {
        using var temp = new TempDirectory();
        using TestHttpFeed feed = TestHttpFeed.Refusing("Private.Lib");
        string config =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<configuration>\n"
            + "  <packageSources>\n"
            + "    <clear />\n"
            + $"    <add key=\"private\" value=\"{feed.Url}\" />\n"
            + "  </packageSources>\n"
            + "</configuration>\n";
        await File.WriteAllTextAsync(
            Path.Combine(temp.Root, "nuget.config"),
            config,
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken
        );
        var engine = new NuGetRestoreEngine(
            new NuGetRestoreEngineOptions
            {
                GlobalPackagesFolder = Path.Combine(temp.Root, "packages"),
            }
        );
        var request = new SingleFileRestoreRequest
        {
            ExtensionId = "acme-tools",
            ExtensionVersion = "1.0.0",
            SourceDigest = "digest",
            SourcePath = Path.Combine(temp.Root, "acme.cs"),
            Packages = [new ExtensionPackageReference("Private.Lib", "1.0.0", "Private.Lib@1.0.0")],
            Target = SingleFileTarget.Of(new Version(10, 0, 4), "osx-arm64"),
        };

        InvalidOperationException failure = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            async () =>
                await engine.RestoreAsync(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains("authentication failed for feed 'private' (401)", failure.Message);
        Assert.Contains("nuget.config", failure.Message);
    }

    [Fact]
    public async Task Credentials_configured_in_nuget_config_serve_a_protected_feed()
    {
        using var temp = new TempDirectory();
        using TestHttpFeed feed = TestHttpFeed.Accepting("Private.Lib", "1.0.0", "lunate-bot");
        string config =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<configuration>\n"
            + "  <packageSources>\n"
            + "    <clear />\n"
            + $"    <add key=\"private\" value=\"{feed.Url}\" />\n"
            + "  </packageSources>\n"
            + "  <packageSourceCredentials>\n"
            + "    <private>\n"
            + "      <add key=\"Username\" value=\"lunate-bot\" />\n"
            + "      <add key=\"ClearTextPassword\" value=\"unit-test-password\" />\n"
            + "    </private>\n"
            + "  </packageSourceCredentials>\n"
            + "</configuration>\n";
        await File.WriteAllTextAsync(
            Path.Combine(temp.Root, "nuget.config"),
            config,
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken
        );
        NuGetPackageFeed seam = NuGetPackageFeed.Create(
            temp.Root,
            [],
            observer: null,
            globalPackagesFolder: Path.Combine(temp.Root, "packages")
        );

        IReadOnlyList<NuGetVersion> versions = await seam.VersionsAsync(
            "Private.Lib",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("1.0.0", versions.Single().ToNormalizedString());
    }
}
