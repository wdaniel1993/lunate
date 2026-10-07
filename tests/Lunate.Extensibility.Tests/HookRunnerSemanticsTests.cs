using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class HookRunnerSemanticsTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

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

    [Fact]
    public async Task Tool_result_ready_composes_output_and_last_attached_data_wins()
    {
        var runner = new HookRunner();
        JsonElement first = JsonDocument.Parse("""{"from":"a"}""").RootElement.Clone();
        runner.Register(
            "a",
            new TestToolResultReadyHandler(
                (payload, _) =>
                {
                    Assert.Equal("original", payload.Output);
                    return ValueTask.FromResult(new ToolResultReadyResult("redacted", first));
                }
            )
        );
        runner.Register(
            "b",
            new TestToolResultReadyHandler(
                (payload, _) =>
                {
                    Assert.Equal("redacted", payload.Output);
                    return ValueTask.FromResult(new ToolResultReadyResult("redacted twice", null));
                }
            )
        );

        ToolResultReadyDispatch result = await runner.RunToolResultReadyAsync(
            TestHookPayloads.ToolResultReady,
            Ct
        );

        Assert.Equal("redacted twice", result.Output);
        Assert.Equal("a", result.Data!.Value.GetProperty("from").GetString());
    }

    [Fact]
    public async Task Input_received_chains_transforms_and_consume_stops_the_chain()
    {
        var runner = new HookRunner();
        bool afterConsume = false;
        runner.Register(
            "a",
            new TestInputReceivedHandler(
                (_, _) =>
                    ValueTask.FromResult<InputReceivedResult>(
                        new InputReceivedResult.Transform("one")
                    )
            )
        );
        runner.Register(
            "b",
            new TestInputReceivedHandler(
                (payload, _) =>
                {
                    Assert.Equal("one", payload.Text);
                    return ValueTask.FromResult<InputReceivedResult>(
                        new InputReceivedResult.PassThrough()
                    );
                }
            )
        );

        InputReceivedResult result = await runner.RunInputReceivedAsync(TestHookPayloads.Input, Ct);

        Assert.Equal("one", Assert.IsType<InputReceivedResult.Transform>(result).Text);

        runner.Register(
            "c",
            new TestInputReceivedHandler(
                (_, _) =>
                    ValueTask.FromResult<InputReceivedResult>(new InputReceivedResult.Consume())
            )
        );
        runner.Register(
            "d",
            new TestInputReceivedHandler(
                (_, _) =>
                {
                    afterConsume = true;
                    return ValueTask.FromResult<InputReceivedResult>(
                        new InputReceivedResult.PassThrough()
                    );
                }
            )
        );

        result = await runner.RunInputReceivedAsync(TestHookPayloads.Input, Ct);

        Assert.IsType<InputReceivedResult.Consume>(result);
        Assert.False(afterConsume);
    }

    [Fact]
    public async Task Run_starting_composes_section_edits_and_last_tool_selection_wins()
    {
        var runner = new HookRunner();
        runner.Register(
            "a",
            new TestRunStartingHandler(
                (_, _) =>
                    ValueTask.FromResult<RunStartingResult>(
                        new RunStartingResult.Apply([new PromptSectionEdit("memory", "one")], null)
                    )
            )
        );
        runner.Register(
            "b",
            new TestRunStartingHandler(
                (payload, _) =>
                {
                    Assert.Equal("one", payload.Sections.Single(s => s.Name == "memory").Text);
                    return ValueTask.FromResult<RunStartingResult>(
                        new RunStartingResult.Apply(
                            [new PromptSectionEdit("memory", "two")],
                            ["read"]
                        )
                    );
                }
            )
        );
        runner.Register(
            "c",
            new TestRunStartingHandler(
                (_, _) =>
                    ValueTask.FromResult<RunStartingResult>(new RunStartingResult.Apply([], null))
            )
        );

        RunStartingDispatch result = await runner.RunRunStartingAsync(
            TestHookPayloads.RunStarting,
            Ct
        );

        Assert.Equal(["one", "two"], result.SectionEdits.Select(edit => edit.Text));
        Assert.Equal("a", result.SectionEdits[0].Source);
        Assert.Equal("b", result.SectionEdits[1].Source);
        Assert.Equal(["read"], result.ActiveTools);
    }

    [Fact]
    public async Task Context_building_appends_additions_for_the_next_handler_and_stamps_sources()
    {
        var runner = new HookRunner();
        runner.Register(
            "a",
            new TestContextBuildingHandler(
                (_, _) =>
                    ValueTask.FromResult(
                        new ContextBuildingResult([new ContextMessage("user", "one")])
                    )
            )
        );
        runner.Register(
            "b",
            new TestContextBuildingHandler(
                (payload, _) =>
                {
                    Assert.Equal("one", payload.Messages.Last().Text);
                    Assert.Equal("a", payload.Messages.Last().Source);
                    return ValueTask.FromResult(
                        new ContextBuildingResult([new ContextMessage("system", "two")])
                    );
                }
            )
        );

        ContextBuildingDispatch result = await runner.RunContextBuildingAsync(
            TestHookPayloads.ContextBuilding,
            Ct
        );

        Assert.Equal(["one", "two"], result.AddedMessages.Select(message => message.Text));
        Assert.Equal(["a", "b"], result.AddedMessages.Select(message => message.Source));
    }

    [Fact]
    public async Task Turn_ended_collects_entries_with_their_extension_and_requests_one_continuation()
    {
        var runner = new HookRunner();
        runner.Register(
            "a",
            new TestTurnEndedHandler(
                (_, _) =>
                    ValueTask.FromResult<TurnEndedResult>(
                        new TurnEndedResult.TurnEnded([Entry("one")], true)
                    )
            )
        );
        runner.Register(
            "b",
            new TestTurnEndedHandler(
                (_, _) => ValueTask.FromResult<TurnEndedResult>(new TurnEndedResult.None())
            )
        );
        runner.Register(
            "c",
            new TestTurnEndedHandler(
                (_, _) =>
                    ValueTask.FromResult<TurnEndedResult>(
                        new TurnEndedResult.TurnEnded([Entry("two")], false)
                    )
            )
        );

        TurnEndedDispatch result = await runner.RunTurnEndedAsync(TestHookPayloads.TurnEnded, Ct);

        Assert.True(result.RequestContinuation);
        Assert.Equal(["one", "two"], result.Entries.Select(e => e.Type));
        Assert.Equal(["a", "c"], result.Entries.Select(e => e.ExtensionId));
    }

    [Fact]
    public async Task Compacting_uses_the_last_supplied_summary_and_otherwise_the_default()
    {
        var runner = new HookRunner();

        CompactingResult result = await runner.RunCompactingAsync(TestHookPayloads.Compacting, Ct);
        Assert.IsType<CompactingResult.UseDefault>(result);

        runner.Register("a", Provide("one"));
        runner.Register(
            "b",
            new TestCompactingHandler(
                (_, _) => ValueTask.FromResult<CompactingResult>(new CompactingResult.UseDefault())
            )
        );
        runner.Register("c", Provide("two"));

        result = await runner.RunCompactingAsync(TestHookPayloads.Compacting, Ct);
        Assert.Equal("two", Assert.IsType<CompactingResult.Provide>(result).Summary);

        static TestCompactingHandler Provide(string summary) =>
            new(
                (_, _) =>
                    ValueTask.FromResult<CompactingResult>(new CompactingResult.Provide(summary))
            );
    }

    private static ToolCallingPayload Payload(string path) =>
        new("r1", "c1", "read", Arguments(path));

    private static JsonElement Arguments(string path) =>
        JsonDocument.Parse($$"""{"path":"{{path}}"}""").RootElement.Clone();

    private static TurnEndedEntry Entry(string type) =>
        new(type, JsonDocument.Parse("{}").RootElement.Clone());
}
