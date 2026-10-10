using System.Runtime.CompilerServices;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionStreamingTests
{
    [Fact]
    public async Task Completed_paragraphs_commit_and_only_the_tail_stays_live()
    {
        var client = new StepChatClient("one\n\ntwo");
        using var host = new InteractiveSessionHost(chat: client);
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.ScrollbackText.Contains("one"));

        string mid = host.ScrollbackText;
        Assert.Contains("one", mid, StringComparison.Ordinal);
        Assert.DoesNotContain("two", mid, StringComparison.Ordinal);
        host.Advance(33);
        Assert.Contains("two", host.LastFrame, StringComparison.Ordinal);

        client.Release();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        Assert.Contains("two", host.ScrollbackText, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_tool_result_commits_a_tool_block()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllText(host.Temp.File("a.txt"), "contents");
        host.Client.Enqueue(
            Scripts.Call("call-1", "read", Scripts.Args(("path", "a.txt"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
            host.ScrollbackText.Contains("tool read", StringComparison.Ordinal)
        );

        string scrollback = host.ScrollbackText;
        Assert.Contains("a.txt", scrollback, StringComparison.Ordinal);
        Assert.Contains("contents", scrollback, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Usage_updates_the_footer()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 })]
            ),
            Scripts.Text("done"),
            Scripts.Stop()
        );
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.Frames.Contains("15/128.0k", StringComparison.Ordinal);
        });

        Assert.Contains("15/128.0k", host.Frames, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Retrying_shows_a_notice()
    {
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                RetryBaseDelay = TimeSpan.Zero,
            }
        );
        host.Client.EnqueueFailure(new HttpRequestException("boom"));
        host.Client.Enqueue(Scripts.Text("recovered"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("retrying (attempt 1)", StringComparison.Ordinal);
        });

        await host.Client.WaitForCallAsync(2);
        Assert.True(
            host.Frames.Contains("retrying (attempt 1)", StringComparison.Ordinal),
            $"run completed={run.IsCompleted}, writes={host.Console.Writes.Count}, last={host.LastFrame.Replace('\n', '|')}, frames={host.Frames.Replace('\n', '|')}"
        );
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_step_limit_shows_a_notice()
    {
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                MaxSteps = 1,
            }
        );
        File.WriteAllText(host.Temp.File("a.txt"), "contents");
        host.Client.Enqueue(
            Scripts.Call("call-1", "read", Scripts.Args(("path", "a.txt"))),
            Scripts.ToolCalls()
        );
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.Frames.Contains("step limit reached (1 steps)", StringComparison.Ordinal);
        });

        Assert.Contains("step limit reached (1 steps)", host.Frames, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_consumed_input_starts_nothing_and_shows_a_notice()
    {
        var runner = new HookRunner();
        var consumer = new HookHandler(_ => new InputReceivedResult.Consume());
        runner.Register("ext.consume", consumer);
        using var host = new InteractiveSessionHost(hooks: runner);
        Task run = host.RunAsync();

        host.Console.SendText("secret");
        host.Console.SendEnter();
        await consumer.Invoked.Task;
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(
                "input consumed by an extension hook",
                StringComparison.Ordinal
            );
        });

        Assert.Empty(host.Client.Requests);
        Assert.Contains(
            "input consumed by an extension hook",
            host.Frames,
            StringComparison.Ordinal
        );
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_transformed_input_starts_the_run_with_the_transformed_text()
    {
        var runner = new HookRunner();
        runner.Register(
            "ext.transform",
            new HookHandler(_ => new InputReceivedResult.Transform("HI"))
        );
        using var host = new InteractiveSessionHost(hooks: runner);
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Equal("HI", host.Client.Requests[0][^1].Text);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_consumed_steering_input_queues_nothing()
    {
        var runner = new HookRunner();
        runner.Register(
            "ext.consume",
            new HookHandler(text =>
                text == "steer"
                    ? new InputReceivedResult.Consume()
                    : new InputReceivedResult.PassThrough()
            )
        );
        using var host = new InteractiveSessionHost(hooks: runner);
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("steer");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.Frames.Contains(
                "input consumed by an extension hook",
                StringComparison.Ordinal
            );
        });

        Assert.Equal(0, host.Session.QueuedSteeringCount);
        host.Console.SendEscape();
        host.Console.Complete();
        await run;
    }

    private sealed class HookHandler(Func<string, InputReceivedResult> handle)
        : IInputReceivedHandler
    {
        public int Priority => 0;

        public TaskCompletionSource Invoked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<InputReceivedResult> HandleAsync(
            InputReceivedPayload payload,
            CancellationToken cancellationToken
        )
        {
            Invoked.TrySetResult();
            return ValueTask.FromResult(handle(payload.Text));
        }
    }

    /// <summary>Streams the first fragment, blocks, and only then finishes the message.</summary>
    private sealed class StepChatClient(string fragment) : IChatClient
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public void Release() => _release.TrySetResult();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException("The session streams.");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(fragment)]);
            await _release.Task.WaitAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                FinishReason = ChatFinishReason.Stop,
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
