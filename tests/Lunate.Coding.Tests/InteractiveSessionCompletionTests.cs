using Lunate.Tui;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionCompletionTests
{
    private static readonly KeyEvent Tab = new(KeyKind.Tab, null, false, false, false);

    [Fact]
    public async Task Tab_completes_a_single_match()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("/comp");
        host.Console.Send(Tab);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "> /compact");

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task No_progress_lists_the_sorted_candidates()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("/");
        host.Console.Send(Tab);
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "commands: /compact /model /new /quit /resume"
        );
        Assert.Contains("> /", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Tab_outside_a_slash_word_does_nothing()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("hello");
        host.Console.Send(Tab);
        host.Console.SendText("!");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "> hello!");
        Assert.DoesNotContain("commands:", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendCtrlC();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "> hello!");
        host.Console.SendText("/new tail");
        host.Console.Send(Tab);
        host.Console.SendText("!");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "> /new tail!");
        Assert.DoesNotContain("commands:", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Tab_mid_word_does_nothing()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("/compact");
        host.Console.Send(new KeyEvent(KeyKind.Left, null, false, false, false));
        host.Console.Send(Tab);
        host.Console.SendText("!");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "> /compac!t");
        Assert.DoesNotContain("commands:", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Tab_while_a_turn_runs_does_nothing()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        host.Console.SendText("/comp");
        host.Console.Send(Tab);
        host.Console.SendText("!");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "> /comp!");
        Assert.DoesNotContain("> /compact", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("commands:", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.Complete();
        await run;
    }
}
