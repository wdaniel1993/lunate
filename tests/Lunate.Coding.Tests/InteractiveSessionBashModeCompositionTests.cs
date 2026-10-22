using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The production-composition proof for bash mode: the real interactive host takes the typed
/// <c>!echo hi</c>, the approval prompt decides, and the command commits one block without any
/// model request.
/// </summary>
public sealed class InteractiveSessionBashModeCompositionTests
{
    [Fact]
    public async Task The_real_host_runs_a_typed_bash_line_without_a_model_request()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("!echo hi");
        await host.WaitUntilAsync(() => host.Session.IsBashMode);
        host.Console.SendEnter();

        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        Assert.Equal("bash", host.Session.PendingApproval!.ToolName);
        Assert.Contains(
            "echo hi",
            host.Session.PendingApproval.Detail![0],
            StringComparison.Ordinal
        );

        host.Console.SendText("y");
        await host.WaitUntilAsync(() =>
            !host.Session.IsBashRunning
            && host.ScrollbackText.Contains("echo hi", StringComparison.Ordinal)
        );

        Assert.Contains(
            "\u2713 bash  echo hi \u00b7 exit 0 \u00b7 0.0s",
            host.ScrollbackText,
            StringComparison.Ordinal
        );
        Assert.Empty(host.Client.Requests);
        Assert.DoesNotContain(host.Events, agentEvent => agentEvent is RunStarted);

        host.Console.Complete();
        await run;
    }
}
