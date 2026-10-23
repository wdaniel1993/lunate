using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The key-help overlay: `?` on an empty idle prompt opens the catalog's bindings, the overlay is
/// read-only, and a `?` typed into a draft stays a character.
/// </summary>
public sealed class InteractiveSessionKeyHelpTests
{
    [Fact]
    public async Task Question_mark_opens_the_overlay_and_Esc_closes_it()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("?");
        await host.WaitForFrameAsync("Keys — here");
        Assert.Contains("Send the message", host.LastFrame, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);

        host.Console.SendEscape();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains("Keys — here", StringComparison.Ordinal);
        });

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Question_mark_inside_a_draft_is_a_literal_character()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("what?");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "what?");

        Assert.DoesNotContain("Keys — here", host.LastFrame, StringComparison.Ordinal);
        Assert.Null(host.Session.PendingApproval);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Tab_switches_to_every_binding_and_jk_moves_the_selection()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("?");
        await host.WaitForFrameAsync("Keys — here");

        host.Console.Send(new KeyEvent(KeyKind.Tab, null, false, false, false));
        await host.WaitForFrameAsync("Keys — all");
        Assert.Contains("Run the bash line", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("j");
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> ", StringComparison.Ordinal);
        });

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Filter_narrows_and_Enter_never_submits()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("?");
        await host.WaitForFrameAsync("Keys — here");

        host.Console.SendText("/");
        host.Console.SendText("stash");
        await host.WaitForFrameAsync("filter: stash");
        Assert.Contains("Stash or restore the draft", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendEnter();
        host.Advance(33);
        Assert.Empty(host.Client.Requests);
        Assert.True(host.Session.IsComposed);

        host.Console.Complete();
        await run;
    }
}
