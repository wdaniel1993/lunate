using System.Diagnostics;
using XenoAtom.Terminal;

namespace S6.Harness;

public static class SpikeProgram
{
    public static async Task<int> Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--scenario";
        return mode switch
        {
            "--startup" => Startup(),
            "--idle" => Idle(),
            "--burst" => await BurstAsync(),
            "--interactive" => await InteractiveAsync(),
            _ => await ScenarioAsync(),
        };
    }

    private static async Task<int> ScenarioAsync()
    {
        using var app = XenoLiveApp.CreateHeadless(Scenario.InitialWidth, Scenario.InitialHeight);
        var result = await ScenarioReplay.RunAsync(app);
        var rawPath = Environment.GetEnvironmentVariable("S6_RAW_OUT");
        if (!string.IsNullOrEmpty(rawPath))
        {
            File.WriteAllText(rawPath, result.RawOutput);
        }

        Console.Write(ScenarioReplay.ToTranscript(result));
        return result.QuitRequested ? 0 : 1;
    }

    private static int Startup()
    {
        var watch = Stopwatch.StartNew();
        using var app = XenoLiveApp.CreateHeadless(Scenario.InitialWidth, Scenario.InitialHeight);
        using var cts = new CancellationTokenSource();
        var run = Task.Run(() => app.RunAsync(cts.Token));
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
        app.PostEvent(new AgentEvent.TextDelta("hello"));
        app.WaitForTicksAsync(2, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        app.RequestStop();
        run.GetAwaiter().GetResult();
        watch.Stop();

        Console.WriteLine(
            $"startup_ms={watch.Elapsed.TotalMilliseconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} "
                + $"ticks={app.Ticks} "
                + $"heap_bytes={GC.GetTotalMemory(forceFullCollection: false)}"
        );
        return app.Ticks > 0 ? 0 : 1;
    }

    private static int Idle()
    {
        using var app = XenoLiveApp.CreateHeadless(Scenario.InitialWidth, Scenario.InitialHeight);
        using var cts = new CancellationTokenSource();
        var run = Task.Run(() => app.RunAsync(cts.Token));
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
        app.WaitForTicksAsync(1, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        Console.WriteLine($"ready heap_bytes={GC.GetTotalMemory(forceFullCollection: false)}");
        Console.Out.Flush();
        Thread.Sleep(TimeSpan.FromSeconds(4));
        app.RequestStop();
        run.GetAwaiter().GetResult();
        Console.WriteLine("done");
        return 0;
    }

    private static async Task<int> BurstAsync()
    {
        using var app = XenoLiveApp.CreateHeadless(Scenario.InitialWidth, Scenario.InitialHeight);
        var run = Task.Run(() => app.RunAsync());
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));

        var expected = new System.Text.StringBuilder();
        for (var i = 0; i < 10_000; i++)
        {
            var token = $"{i};";
            expected.Append(token);
            app.PostEvent(new AgentEvent.TextDelta(token));
        }

        await app.WaitForAsync(
            () => app.Model.TailText.Length >= expected.Length,
            TimeSpan.FromSeconds(30)
        );

        var exact = app.Model.TailText == expected.ToString();
        app.SendKey(Keys.Escape);
        await app.WaitForAsync(() => app.Model.CancelRequested, TimeSpan.FromSeconds(5));
        app.RequestStop();
        await run;

        Console.WriteLine(
            $"burst_exact={exact.ToString().ToLowerInvariant()} "
                + $"tail_len={app.Model.TailText.Length} "
                + $"ticks={app.Ticks} "
                + $"cancel={app.Model.CancelRequested.ToString().ToLowerInvariant()}"
        );
        return exact && app.Model.CancelRequested ? 0 : 1;
    }

    private static async Task<int> InteractiveAsync()
    {
        using var app = XenoLiveApp.CreateInteractive();
        Terminal.WriteMarkupLine(
            "[bold]S-6 interactive check[/] - live area on XenoAtom.Terminal.UI. "
                + "Type steering, answer approvals with y/n/a, Esc cancels, Ctrl+C clears, twice quits."
        );
        Terminal.WriteLine();

        var producer = Task.Run(async () =>
        {
            await Task.Delay(500);
            app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
            for (var i = 0; i < 20; i++)
            {
                app.PostEvent(new AgentEvent.TextDelta($"chunk{i} "));
                await Task.Delay(60);
            }

            app.PostEvent(new AgentEvent.ApprovalRequested("bash", "dotnet test"));
            await Task.Delay(4000);
            app.PostEvent(new AgentEvent.ToolStarted("bash"));
            app.PostEvent(new AgentEvent.Usage(1200, 340, 12.5));
            await Task.Delay(3000);
            app.PostEvent(new AgentEvent.ToolFinished("bash", true, "48 passed"));
        });

        await app.RunAsync();
        await producer;
        return 0;
    }
}
