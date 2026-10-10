using System.Diagnostics;

namespace Lunate.Coding.Tests;

/// <summary>
/// The large-repository budget check (trait <c>Category=Perf</c>, excluded from the main test
/// passes and run as its own gate step): a generated 20,000-file tree with a `.gitignore` must
/// build within 5 seconds and answer a warm query within 50 milliseconds.
/// </summary>
public sealed class PathIndexBudgetTests
{
    private readonly ITestOutputHelper _output;

    public PathIndexBudgetTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "Perf")]
    public void A_twenty_thousand_file_tree_builds_and_queries_within_budget()
    {
        using var temp = new TempDirectory();
        GenerateTree(temp.Root);

        var index = new FileIndex(new SystemWorkspaceFiles(temp.Root));
        var build = Stopwatch.StartNew();
        index.EnsureStarted();
        WaitReady(index);
        build.Stop();

        Assert.False(index.IsTruncated);
        Assert.Empty(index.Match("vendor", 10));
        index.Match("pkg00", 100);

        var query = Stopwatch.StartNew();
        IReadOnlyList<string> matches = index.Match("pkg10/sub3/file01", 100);
        query.Stop();

        double buildMs = build.Elapsed.TotalMilliseconds;
        double queryMs = query.Elapsed.TotalMilliseconds;
        _output.WriteLine(
            FormattableString.Invariant($"path index build: {buildMs:F0} ms (budget 5000 ms)")
        );
        _output.WriteLine(
            FormattableString.Invariant($"warm query: {queryMs:F2} ms (budget 50 ms)")
        );
        _output.WriteLine(FormattableString.Invariant($"entries: {index.Match("", 0).Count}"));

        Assert.NotEmpty(matches);
        Assert.True(
            buildMs <= 5000,
            FormattableString.Invariant($"the build took {buildMs:F0} ms (budget 5000 ms)")
        );
        Assert.True(
            queryMs <= 50,
            FormattableString.Invariant($"the query took {queryMs:F2} ms (budget 50 ms)")
        );
    }

    private static void WaitReady(FileIndex index)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!index.IsReady)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("the file index never became ready");
            }

            Thread.Sleep(1);
        }
    }

    private static void GenerateTree(string root)
    {
        File.WriteAllText(Path.Combine(root, ".gitignore"), "vendor/\n");
        for (var package = 0; package < 40; package++)
        {
            string packagePath = Path.Combine(root, $"pkg{package:D2}");
            for (var sub = 0; sub < 5; sub++)
            {
                string directory = Path.Combine(packagePath, $"sub{sub}");
                Directory.CreateDirectory(directory);
                for (var file = 0; file < 99; file++)
                {
                    File.WriteAllText(Path.Combine(directory, $"file{file:D3}.cs"), string.Empty);
                }
            }
        }

        Directory.CreateDirectory(Path.Combine(root, "vendor"));
        for (var file = 0; file < 199; file++)
        {
            File.WriteAllText(Path.Combine(root, "vendor", $"lib{file:D3}.dll"), string.Empty);
        }
    }
}
