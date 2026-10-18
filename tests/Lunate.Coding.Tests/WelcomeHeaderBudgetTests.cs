using System.Diagnostics;

namespace Lunate.Coding.Tests;

/// <summary>
/// The welcome header's startup budget (design D6, trait <c>Category=Perf</c>, excluded from the
/// main passes and run by the gate's own perf step): the first frame reaches the header well inside
/// the budget, so the render never becomes the reason an interactive start feels slow. The check is
/// deliberately generous — it fails on a header that started doing I/O, not on a slow runner.
/// </summary>
public sealed class WelcomeHeaderBudgetTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);

    [Fact]
    [Trait("Category", "Perf")]
    public async Task The_first_frame_header_stays_within_the_startup_budget()
    {
        using var host = new InteractiveSessionHost(environment: name =>
            name switch
            {
                "LUNATE_UNICODE" => "1",
                "OPENAI_API_KEY" => "test-key",
                _ => PrintModeTestSupport.Environment()(name),
            }
        );
        var stopwatch = Stopwatch.StartNew();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() =>
            host.ScrollbackText.Contains("lunate  v0.1.0", StringComparison.Ordinal)
        );
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < Budget,
            $"the welcome header took {stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} ms, above the {Budget.TotalMilliseconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} ms budget"
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}
