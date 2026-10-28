using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>
/// The environment a fixture restore hands its child: self-contained, so a runner whose home is
/// missing or read-only cannot fail a fixture with an error that names nothing, and a child's
/// failure can never be reported as silence.
/// </summary>
public sealed class FixtureRestoreEnvironmentTests
{
    [Fact]
    public void The_child_gets_its_own_cli_home_and_package_folders()
    {
        using var temp = new TempDirectory();

        Dictionary<string, string?> environment = FixtureRestore.ChildEnvironment(temp.Root);

        Assert.Equal(Path.Combine(temp.Root, ".dotnet-home"), environment["DOTNET_CLI_HOME"]);
        Assert.Equal(Path.Combine(temp.Root, ".packages"), environment["NUGET_PACKAGES"]);
        Assert.Equal(
            Path.Combine(temp.Root, ".nuget-http-cache"),
            environment["NUGET_HTTP_CACHE_PATH"]
        );
        Assert.DoesNotContain("HOME", environment.Keys);
    }

    [Fact]
    public void A_failing_child_reports_what_it_printed()
    {
        using var temp = new TempDirectory();

        ChildProcessResult result = DotnetCli.Run(
            temp.Root,
            ["restore", "--definitely-not-a-real-option"],
            new Dictionary<string, string?>(),
            TimeSpan.FromSeconds(60)
        );

        Assert.False(result.Succeeded);
        // dotnet writes an MSBuild error on stdout here, so the contract is "something survived",
        // not "stderr survived" — what must never happen is a failure that reads as silence.
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput + result.StandardError));
        Assert.DoesNotContain(
            "printed nothing",
            result.Describe("dotnet restore failed"),
            StringComparison.Ordinal
        );
    }
}
