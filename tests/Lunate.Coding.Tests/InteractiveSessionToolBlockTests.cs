using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The tool-block wiring: the header meta's duration is measured on the injected scheduler between
/// the call's start and its result — never on the wall clock — and a bash result's exit code
/// travels from its UI-only details into the block.
/// </summary>
public sealed class InteractiveSessionToolBlockTests
{
    [Fact]
    public async Task Bash_block_carries_its_exit_code_and_measured_duration()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.HandleEvent(new RunStarted("run"));
        host.Session.HandleEvent(new ToolCallStart("run", "call_1", "bash"));
        host.Session.HandleEvent(
            new ToolCallArgs("run", "call_1", """{"command":"dotnet build"}""")
        );
        host.Session.HandleEvent(new ToolCallEnd("run", "call_1"));
        host.Advance(400);
        host.Session.HandleEvent(
            new ToolCallResult("run", "call_1", "Build succeeded.", false, new BashDetails(0))
        );
        host.Advance(40);

        Assert.Contains(
            "\u2713 bash  dotnet build \u00b7 exit 0 \u00b7 0.4s",
            host.ScrollbackText,
            StringComparison.Ordinal
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_failed_bash_block_keeps_its_non_zero_exit_code()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.HandleEvent(new RunStarted("run"));
        host.Session.HandleEvent(new ToolCallStart("run", "call_1", "bash"));
        host.Session.HandleEvent(new ToolCallArgs("run", "call_1", """{"command":"false"}"""));
        host.Session.HandleEvent(
            new ToolCallResult("run", "call_1", "exit code: 1", true, new BashDetails(1))
        );
        host.Advance(40);

        Assert.Contains(
            "\u2717 bash  false \u00b7 exit 1 \u00b7 0.0s",
            host.ScrollbackText,
            StringComparison.Ordinal
        );

        host.Console.Complete();
        await run;
    }
}
