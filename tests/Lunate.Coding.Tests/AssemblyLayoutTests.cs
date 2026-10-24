namespace Lunate.Coding.Tests;

/// <summary>
/// The rule MSBL001 exists for, asserted where it is observable: no <c>Microsoft.Build.*</c>
/// assembly is copied beside the app. MSBuild comes from the located SDK at run time (ADR-0006);
/// a copy in the output directory would be loaded instead, and two MSBuild identities in one
/// process break the Roslyn workspace.
/// <para>
/// The locator's own build check is disabled repo-wide because the restore engine legitimately
/// ships <c>NuGet.Frameworks</c>, which MSBuild also carries (see <c>Directory.Build.props</c>).
/// This test is the replacement guard: it inspects the same output layout the published binary is
/// built from, so a package that starts copying MSBuild still fails the suite.
/// </para>
/// </summary>
public sealed class AssemblyLayoutTests
{
    [Theory]
    [InlineData("Microsoft.Build.Framework.dll")]
    [InlineData("Microsoft.Build.dll")]
    [InlineData("Microsoft.Build.Utilities.Core.dll")]
    [InlineData("Microsoft.Build.Tasks.Core.dll")]
    public void No_msbuild_assembly_is_copied_beside_the_app(string assembly)
    {
        string directory = Path.GetDirectoryName(
            typeof(Lunate.Coding.ExtensionsCommand).Assembly.Location
        )!;

        Assert.False(
            File.Exists(Path.Combine(directory, assembly)),
            $"{assembly} is in '{directory}': MSBuild must resolve from the located SDK at run time."
        );
    }

    [Fact]
    public void The_located_msbuild_shim_is_present_and_no_build_engine_is()
    {
        string directory = Path.GetDirectoryName(
            typeof(Lunate.Coding.ExtensionsCommand).Assembly.Location
        )!;

        Assert.True(
            File.Exists(Path.Combine(directory, "Microsoft.Build.Locator.dll")),
            "the locator shim must ship: it is what finds the SDK's MSBuild"
        );
        Assert.Empty(
            Directory.GetFiles(
                directory,
                "Microsoft.Build.Engine.dll",
                SearchOption.TopDirectoryOnly
            )
        );
    }
}
