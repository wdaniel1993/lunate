using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

using static Lunate.Extensibility.Tests.HookSemanticsSupport;

public sealed class HookRunnerContextSemanticsTests
{
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
}
