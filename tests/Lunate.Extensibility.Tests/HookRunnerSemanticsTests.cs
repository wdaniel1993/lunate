using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

using static Lunate.Extensibility.Tests.HookSemanticsSupport;

public sealed class HookRunnerSemanticsTests
{
    [Fact]
    public async Task Observe_hooks_run_every_handler_and_ignore_results()
    {
        var runner = new HookRunner();
        int calls = 0;
        runner.Register("a", new TestProviderStreamEventHandler((_, _) => Observe()));
        runner.Register("b", new TestProviderStreamEventHandler((_, _) => Observe()));
        runner.Register("c", new TestRunSettledHandler((_, _) => Observe()));
        runner.Register("d", new TestModelChangedHandler((_, _) => Observe()));
        runner.Register("e", new TestToolsChangedHandler((_, _) => Observe()));
        runner.Register("f", new TestSessionStartedHandler((_, _) => Observe()));
        runner.Register("g", new TestSessionEndingHandler((_, _) => Observe()));

        await runner.RunProviderStreamEventAsync(TestHookPayloads.StreamEvent, Ct);
        await runner.RunRunSettledAsync(TestHookPayloads.RunSettled, Ct);
        await runner.RunModelChangedAsync(TestHookPayloads.ModelChanged, Ct);
        await runner.RunToolsChangedAsync(TestHookPayloads.ToolsChanged, Ct);
        await runner.RunSessionStartedAsync(TestHookPayloads.SessionStarted, Ct);
        await runner.RunSessionEndingAsync(TestHookPayloads.SessionEnding, Ct);

        Assert.Equal(7, calls);

        ValueTask Observe()
        {
            calls++;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Project_trust_requires_no_deny_and_reports_its_handler_count()
    {
        var runner = new HookRunner();
        var dispatch = await runner.RunProjectTrustAsync(
            new ProjectTrustPayload("ext", "/ext", "repo", "/work"),
            cancellationToken: Ct
        );

        Assert.False(dispatch.HasHandlers);
        Assert.Equal(0, dispatch.HandlerCount);
        Assert.Null(dispatch.Result);

        runner.Register("a", Allow());
        runner.Register("b", Allow());

        dispatch = await runner.RunProjectTrustAsync(
            new ProjectTrustPayload("ext", "/ext", "repo", "/work"),
            cancellationToken: Ct
        );

        Assert.True(dispatch.HasHandlers);
        Assert.Equal(2, dispatch.HandlerCount);
        Assert.IsType<ProjectTrustResult.Allow>(dispatch.Result);

        static TestProjectTrustHandler Allow() =>
            new((_, _) => ValueTask.FromResult<ProjectTrustResult>(new ProjectTrustResult.Allow()));
    }

    [Fact]
    public async Task Project_trust_stops_at_the_first_deny()
    {
        var runner = new HookRunner();
        bool reached = false;
        runner.Register("a", Allow());
        runner.Register(
            "b",
            new TestProjectTrustHandler(
                (_, _) =>
                    ValueTask.FromResult<ProjectTrustResult>(new ProjectTrustResult.Deny("blocked"))
            )
        );
        runner.Register(
            "c",
            new TestProjectTrustHandler(
                (_, _) =>
                {
                    reached = true;
                    return ValueTask.FromResult<ProjectTrustResult>(new ProjectTrustResult.Allow());
                }
            )
        );

        ProjectTrustDispatch dispatch = await runner.RunProjectTrustAsync(
            new ProjectTrustPayload("ext", "/ext", "repo", "/work"),
            cancellationToken: Ct
        );

        var deny = Assert.IsType<ProjectTrustResult.Deny>(dispatch.Result);
        Assert.Equal("blocked", deny.Reason);
        Assert.False(reached);

        static TestProjectTrustHandler Allow() =>
            new((_, _) => ValueTask.FromResult<ProjectTrustResult>(new ProjectTrustResult.Allow()));
    }

    [Fact]
    public async Task Tool_calling_chains_arguments_and_stops_at_the_first_block()
    {
        var runner = new HookRunner();
        bool afterBlock = false;
        runner.Register("a", Mutate("first"));
        runner.Register(
            "b",
            new TestToolCallingHandler(
                (payload, _) =>
                {
                    Assert.Equal("first", payload.Arguments.GetProperty("path").GetString());
                    return ValueTask.FromResult<ToolCallingResult>(
                        new ToolCallingResult.Proceed(Arguments("second"))
                    );
                }
            )
        );
        runner.Register(
            "c",
            new TestToolCallingHandler(
                (payload, _) =>
                {
                    Assert.Equal("second", payload.Arguments.GetProperty("path").GetString());
                    return ValueTask.FromResult<ToolCallingResult>(
                        new ToolCallingResult.Block("not allowed")
                    );
                }
            )
        );
        runner.Register("d", Mutate("after"));

        ToolCallingResult result = await runner.RunToolCallingAsync(
            Payload("original"),
            cancellationToken: Ct
        );

        var block = Assert.IsType<ToolCallingResult.Block>(result);
        Assert.Equal("not allowed", block.Reason);
        Assert.False(afterBlock);

        TestToolCallingHandler Mutate(string path) =>
            new(
                (_, _) =>
                {
                    afterBlock |= path == "after";
                    return ValueTask.FromResult<ToolCallingResult>(
                        new ToolCallingResult.Proceed(Arguments(path))
                    );
                }
            );
    }

    [Fact]
    public async Task Tool_calling_returns_the_final_arguments_when_nothing_blocks()
    {
        var runner = new HookRunner();
        runner.Register("a", Proceed("one"));
        runner.Register("b", Proceed("two"));

        ToolCallingResult result = await runner.RunToolCallingAsync(
            Payload("original"),
            cancellationToken: Ct
        );

        var proceed = Assert.IsType<ToolCallingResult.Proceed>(result);
        Assert.Equal("two", proceed.Arguments!.Value.GetProperty("path").GetString());

        TestToolCallingHandler Proceed(string path) =>
            new(
                (_, _) =>
                    ValueTask.FromResult<ToolCallingResult>(
                        new ToolCallingResult.Proceed(Arguments(path))
                    )
            );
    }

    [Fact]
    public async Task Message_completed_chains_replacements_and_keeps_the_final_text()
    {
        var runner = new HookRunner();
        runner.Register(
            "a",
            new TestMessageCompletedHandler(
                (payload, _) =>
                {
                    Assert.Equal("draft", payload.Text);
                    return ValueTask.FromResult<MessageCompletedResult>(
                        new MessageCompletedResult.Replace("one")
                    );
                }
            )
        );
        runner.Register(
            "b",
            new TestMessageCompletedHandler(
                (payload, _) =>
                {
                    Assert.Equal("one", payload.Text);
                    return ValueTask.FromResult<MessageCompletedResult>(
                        new MessageCompletedResult.Replace("two")
                    );
                }
            )
        );
        runner.Register(
            "c",
            new TestMessageCompletedHandler(
                (_, _) =>
                    ValueTask.FromResult<MessageCompletedResult>(new MessageCompletedResult.Keep())
            )
        );

        MessageCompletedResult result = await runner.RunMessageCompletedAsync(
            TestHookPayloads.MessageCompleted,
            Ct
        );

        Assert.Equal("two", Assert.IsType<MessageCompletedResult.Replace>(result).Text);
    }
}
