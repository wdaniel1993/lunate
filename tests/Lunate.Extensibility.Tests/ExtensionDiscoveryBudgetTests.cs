using System.Diagnostics;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class ExtensionDiscoveryBudgetTests(ITestOutputHelper output)
{
    private const double BudgetMs = 250;

    [Fact]
    public void Discovering_ten_installed_extensions_stays_under_the_budget()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string workingDirectory = temp.Subdirectory("worktree");
        for (int index = 0; index < 5; index++)
        {
            TestExtensions.WriteManifest(
                TestExtensions.GlobalExtensionDirectory(temp, $"global-{index}"),
                $"global-{index}"
            );
            TestExtensions.WriteManifest(
                TestExtensions.ProjectExtensionDirectory(workingDirectory, $"project-{index}"),
                $"project-{index}"
            );
        }

        var loader = new ExtensionLoader(TestExtensions.Options(store));
        loader.Discover(workingDirectory, "repo");

        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<ExtensionDescriptor> descriptors = loader.Discover(workingDirectory, "repo");
        stopwatch.Stop();

        double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
        output.WriteLine(
            FormattableString.Invariant(
                $"discovered {descriptors.Count} extensions in {elapsedMs:F1} ms (budget {BudgetMs:F0} ms)"
            )
        );

        Assert.Empty(loader.DiscoveryErrors);
        Assert.Equal(10, descriptors.Count);
        Assert.True(
            elapsedMs < BudgetMs,
            FormattableString.Invariant(
                $"discovering 10 extensions took {elapsedMs:F1} ms, budget is {BudgetMs:F0} ms"
            )
        );
    }
}
