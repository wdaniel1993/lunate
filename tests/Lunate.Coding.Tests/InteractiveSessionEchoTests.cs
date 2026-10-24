using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The real host's prompt echo: the submitted message commits to scrollback as the first block of
/// the turn, before any assistant output, and carries exactly the processed text the transcript
/// entry holds.
/// </summary>
public sealed class InteractiveSessionEchoTests
{
    [Fact]
    public async Task The_prompt_block_commits_before_any_assistant_output()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("first question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        // The echo commits at submit; the answer is still gated, so its absence is asserted on the
        // frame that carries the echo.
        string prompt = await host.WaitForScrollbackAsync("> first question");
        Assert.DoesNotContain("answer", prompt, StringComparison.Ordinal);

        host.Client.Release(1);
        string answered = await host.WaitForScrollbackAsync("answer");
        Assert.True(
            answered.IndexOf("> first question", StringComparison.Ordinal)
                < answered.IndexOf("answer", StringComparison.Ordinal)
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task One_blank_line_opens_every_turn_after_the_first()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first answer"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("second answer"), Scripts.Stop());
        host.Client.Gate(1);
        host.Client.Gate(2);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("first question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Client.Release(1);
        await host.WaitForScrollbackAsync(
            text =>
                text.Contains("first answer", StringComparison.Ordinal) && !host.Session.IsRunning,
            "the first answer once the turn finishes"
        );

        host.Console.SendText("second question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);

        // The second echo is the marker: the blank-line structure is only complete once it is in.
        string text = await host.WaitForScrollbackAsync("> second question");
        Assert.Contains("\n> first question", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n> first question", text, StringComparison.Ordinal);
        Assert.Contains("first answer\n\n> second question", text, StringComparison.Ordinal);

        host.Client.Release(2);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_prompt_block_and_the_transcript_carry_the_processed_text()
    {
        var runner = new HookRunner();
        runner.Register(
            "ext.transform",
            new TransformingHandler(_ => new InputReceivedResult.Transform("HI"))
        );
        using var host = new InteractiveSessionHost(hooks: runner);
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Equal("HI", Assert.IsType<TranscriptUserEntry>(host.Session.Transcript[0]).Text);
        // The transformed echo is the marker; the raw one must not appear on that frame.
        string scrollback = await host.WaitForScrollbackAsync("> HI");
        Assert.DoesNotContain("> hi", scrollback, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    private sealed class TransformingHandler(Func<string, InputReceivedResult> handle)
        : IInputReceivedHandler
    {
        public int Priority => 0;

        public ValueTask<InputReceivedResult> HandleAsync(
            InputReceivedPayload payload,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(handle(payload.Text));
    }
}
