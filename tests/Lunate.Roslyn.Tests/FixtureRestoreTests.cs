using Lunate.TestSupport;

namespace Lunate.Roslyn.Tests;

/// <summary>
/// The fixture restore the gate depends on: a failing restore has to say what the child said (twice
/// the gate reported an empty "dotnet restore failed with exit code 1:"), and the suite must not run
/// one restore per test on a loaded runner.
/// </summary>
[Collection(RoslynIntegrationCollection.Name)]
public sealed class FixtureRestoreTests
{
    [Fact]
    public void A_failed_restore_carries_what_the_child_printed()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        // A project file no restore can load: the child exits 1 and says why, and that reason has to
        // reach the test output.
        File.WriteAllText(
            Path.Combine(fixture.Root, "src", "ConsoleApp", "ConsoleApp.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup></Project>\n"
        );

        var failure = Assert.Throws<InvalidOperationException>(() =>
            Fixtures.Restore(fixture, "ConsoleApp.slnx")
        );

        Assert.Contains("exit code: 1", failure.Message, StringComparison.Ordinal);
        Assert.Contains("MSB4025", failure.Message, StringComparison.Ordinal);
        Assert.Contains("stdout", failure.Message, StringComparison.Ordinal);
        Assert.Contains("stderr", failure.Message, StringComparison.Ordinal);
        Assert.Contains("directory:", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_child_that_printed_nothing_is_reported_as_such()
    {
        // The twice-repeated CI signature was a message that ended at the colon. A silent child is
        // the interesting case: it says the tool died before it wrote anything.
        var result = new ChildProcessResult(
            Command: "dotnet restore thing.slnx",
            WorkingDirectory: "/tmp/fixture",
            ExitCode: 1,
            Duration: TimeSpan.FromSeconds(1.4),
            StandardOutput: string.Empty,
            StandardError: "  \n"
        );

        var message = result.Describe("dotnet restore failed");

        Assert.Contains("printed nothing", message, StringComparison.Ordinal);
        Assert.Contains("exit code: 1", message, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet restore failed:", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Restores_of_one_solution_do_not_fan_out()
    {
        var fixtures = Enumerable
            .Range(0, 6)
            .Select(_ => Fixtures.CopySolution("console-app"))
            .ToArray();
        try
        {
            int before = FixtureRestore.Restores;
            Parallel.ForEach(fixtures, fixture => Fixtures.Restore(fixture, "ConsoleApp.slnx"));
            int children = FixtureRestore.Restores - before;

            Assert.True(
                children <= 1,
                $"six restores of one solution started {children} child processes"
            );
            Assert.All(
                fixtures,
                fixture =>
                    Assert.True(
                        File.Exists(
                            Path.Combine(
                                fixture.Root,
                                "src",
                                "ConsoleApp",
                                "obj",
                                "project.assets.json"
                            )
                        ),
                        $"{fixture.Root} was not restored"
                    )
            );
        }
        finally
        {
            foreach (var fixture in fixtures)
            {
                fixture.Dispose();
            }
        }
    }

    [Fact]
    public void A_tree_that_differs_is_restored_on_its_own()
    {
        // The cache is a content address, not a solution name: a test that edits its fixture before
        // restoring has to get a restore of the tree it wrote, not the artifacts of the clean one.
        using var clean = Fixtures.CopySolution("console-app");
        using var edited = Fixtures.CopySolution("console-app");
        string extra = Path.Combine(edited.Root, "src", "Extra");
        Directory.CreateDirectory(extra);
        File.WriteAllText(
            Path.Combine(extra, "Extra.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
        );
        File.WriteAllText(
            Path.Combine(extra, "Marker.cs"),
            "namespace Extra; public static class Marker { }\n"
        );
        string project = Path.Combine(edited.Root, "src", "ConsoleApp", "ConsoleApp.csproj");
        File.WriteAllText(
            project,
            File.ReadAllText(project)
                .Replace(
                    "</Project>",
                    """
                      <ItemGroup>
                        <ProjectReference Include="..\Extra\Extra.csproj" />
                      </ItemGroup>
                    </Project>
                    """,
                    StringComparison.Ordinal
                )
        );

        Fixtures.Restore(clean, "ConsoleApp.slnx");
        Fixtures.Restore(edited, "ConsoleApp.slnx");

        string cleanAssets = File.ReadAllText(
            Path.Combine(clean.Root, "src", "ConsoleApp", "obj", "project.assets.json")
        );
        string editedAssets = File.ReadAllText(
            Path.Combine(edited.Root, "src", "ConsoleApp", "obj", "project.assets.json")
        );
        Assert.DoesNotContain("Extra", cleanAssets, StringComparison.Ordinal);
        Assert.Contains("\"Extra >= ", editedAssets, StringComparison.Ordinal);
    }
}
