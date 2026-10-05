using System.Diagnostics;

namespace S5.Harness;

/// <summary>Shared entry point for the baseline and the three variant exes.</summary>
public static class SpikeProgram
{
    public static int Run(string[] args, Func<ISession> factory)
    {
        var mode = args.Length > 0 ? args[0] : "--scenario";
        return mode switch
        {
            "--startup" => Startup(factory),
            "--idle" => Idle(factory),
            _ => ScenarioMode(factory),
        };
    }

    private static int Startup(Func<ISession> factory)
    {
        var watch = Stopwatch.StartNew();
        using var session = factory();
        session.Post(new AgentEvent.RunStarted(Scenario.Model));
        session.Post(new AgentEvent.TextDelta("hello"));
        session.Advance(Scenario.FrameInterval * 2);
        session.DrainAsync().AsTask().GetAwaiter().GetResult();
        watch.Stop();

        Console.WriteLine(
            $"startup_ms={watch.Elapsed.TotalMilliseconds:F2} "
                + $"frames={session.Terminal.Frames.Count} "
                + $"heap_bytes={GC.GetTotalMemory(forceFullCollection: false)}"
        );
        return session.Terminal.Frames.Count > 0 ? 0 : 1;
    }

    private static int Idle(Func<ISession> factory)
    {
        using var session = factory();
        session.Post(new AgentEvent.RunStarted(Scenario.Model));
        session.Advance(Scenario.FrameInterval);
        session.DrainAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine($"ready heap_bytes={GC.GetTotalMemory(forceFullCollection: false)}");
        Console.Out.Flush();
        Thread.Sleep(TimeSpan.FromSeconds(4));
        Console.WriteLine("done");
        return 0;
    }

    private static int ScenarioMode(Func<ISession> factory)
    {
        using var session = factory();
        var result = ScenarioRunner.Run(session);
        Console.Write(ScenarioRunner.ToTranscript(result));
        return result.QuitRequested ? 0 : 1;
    }
}
