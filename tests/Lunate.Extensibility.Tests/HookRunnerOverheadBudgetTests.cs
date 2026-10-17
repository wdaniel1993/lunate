using System.Diagnostics;

namespace Lunate.Extensibility.Tests;

using static Lunate.Extensibility.Tests.HookSemanticsSupport;

/// <summary>
/// The zero-extension dispatch budget (trait <c>Category=Perf</c>, excluded from the main test
/// passes and run as its own gate step): with no registrations the timing funnel must add no
/// measurable overhead — nothing is measured and nothing is aggregated.
/// </summary>
public sealed class HookRunnerOverheadBudgetTests(ITestOutputHelper output)
{
    private const int Dispatches = 20_000;
    private const double BudgetMs = 2000;

    [Fact]
    [Trait("Category", "Perf")]
    public async Task Dispatching_with_zero_extensions_stays_under_the_budget()
    {
        var runner = new HookRunner();

        var stopwatch = Stopwatch.StartNew();
        for (var dispatch = 0; dispatch < Dispatches; dispatch++)
        {
            await runner.RunRunSettledAsync(TestHookPayloads.RunSettled, Ct);
        }

        stopwatch.Stop();
        double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
        output.WriteLine(
            FormattableString.Invariant(
                $"{Dispatches} zero-extension dispatches in {elapsedMs:F0} ms (budget {BudgetMs:F0} ms)"
            )
        );

        Assert.True(
            elapsedMs < BudgetMs,
            FormattableString.Invariant(
                $"{Dispatches} zero-extension dispatches took {elapsedMs:F0} ms, budget is {BudgetMs:F0} ms"
            )
        );
        Assert.Empty(runner.Snapshot("ext-absent"));
    }
}
