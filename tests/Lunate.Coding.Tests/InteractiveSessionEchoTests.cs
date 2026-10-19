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

        Assert.Contains("> first question", host.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain("answer", host.ScrollbackText, StringComparison.Ordinal);

        host.Client.Release(1);
        await host.WaitUntilAsync(() =>
            host.ScrollbackText.Contains("answer", StringComparison.Ordinal)
        );
        Assert.True(
            host.ScrollbackText.IndexOf("> first question", StringComparison.Ordinal)
                < host.ScrollbackText.IndexOf("answer", StringComparison.Ordinal)
        );

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
        Assert.Contains("> HI", host.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain("> hi", host.ScrollbackText, StringComparison.Ordinal);

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
