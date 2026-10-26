using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The approval arm seam, probed directly. The harness's <c>WaitForApprovalAsync</c> waits for the
/// panel to be *visible* and then ages the 300 ms in-flight window with
/// <c>ExpireApprovalArmWindow()</c> — a plain write into the session's own arm stamp from another
/// thread. When the session published the panel before stamping the arm, an expire that landed in
/// between was overwritten by the stamp: the window was open again with the virtual clock frozen,
/// every key the test sent was dropped, and the test waited out its whole budget for a decision no
/// key could deliver ("The scrollback never showed … (running=False writes=2)", last frame blank).
///
/// These probes assert the seam's contract rather than the symptom, so the failure is a named
/// invariant instead of a ten-second timeout.
/// </summary>
public sealed class ApprovalArmSeamProbe
{
    /// <summary>
    /// The product's publication contract: by the time another thread can see the panel, the arm
    /// is stamped, so an expire issued afterwards cannot be overwritten. Polled tightly — the
    /// harness's 1 ms delay makes the window rare, not absent.
    /// </summary>
    [Fact]
    public async Task The_panel_is_never_visible_before_its_arm_is_stamped()
    {
        for (var i = 0; i < 200; i++)
        {
            var (host, run) = await OpenApprovalAsync();
            using (host)
            {
                await WaitForPanelAsync(host);

                host.Session.ExpireApprovalArmWindow();
                bool armed = host.Session.ApprovalArmedForNextKey;
                string snapshot = host.Snapshot();
                if (!armed)
                {
                    Assert.Fail(
                        $"iteration {i}: the panel became visible before the arm was stamped, so "
                            + "the expire was overwritten and the next key would be dropped by a "
                            + "300 ms window that a virtual clock never closes."
                            + Environment.NewLine
                            + snapshot
                    );
                }

                host.Console.Complete();
                await run.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken
                );
            }
        }
    }

    /// <summary>
    /// The harness's own guarantee: even when an expire is lost (the raw sequence above, raced),
    /// <c>WaitForApprovalAsync</c> returns only once the key it is about to send will decide the
    /// prompt — and that key really does decide it.
    /// </summary>
    [Fact]
    public async Task The_waiter_recovers_an_expire_the_session_overwrote()
    {
        for (var i = 0; i < 200; i++)
        {
            var (host, run) = await OpenApprovalAsync();
            using (host)
            {
                await WaitForPanelAsync(host);

                // The dangerous sequence, unguarded: this expire may land before the arm stamp.
                host.Session.ExpireApprovalArmWindow();

                await host.WaitForApprovalAsync();
                Assert.True(
                    host.Session.ApprovalArmedForNextKey,
                    $"iteration {i}: the waiter returned an approval the next key cannot decide."
                );

                host.Console.SendText("y");
                await host.WaitUntilAsync(() => host.Session.PendingApproval is null);
                host.Console.Complete();
                await run.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken
                );
            }
        }
    }

    /// <summary>
    /// Waits for the panel with as tight a poll as the thread allows: the harness's 1 ms delay is
    /// what makes the window rare, so the probe must not inherit it.
    /// </summary>
    private static Task WaitForPanelAsync(InteractiveSessionHost host)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (host.Session.PendingApproval is null)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail(
                    "the approval panel never appeared." + Environment.NewLine + host.Snapshot()
                );
            }

            Thread.SpinWait(64);
        }

        return Task.CompletedTask;
    }

    /// <summary>A composed session whose first turn asks to write a file.</summary>
    private static async Task<(InteractiveSessionHost Host, Task Run)> OpenApprovalAsync()
    {
        var host = new InteractiveSessionHost();
        host.Client.Enqueue(
            Scripts.Call("call-1", "write", Scripts.Args(("path", "note.txt"), ("content", "hi"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("go");
        host.Console.SendEnter();
        return (host, run);
    }
}
