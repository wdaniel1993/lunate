using System.Diagnostics;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class HookRunnerLimitsTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Defaults_match_the_design()
    {
        var options = new HookRunnerOptions();

        Assert.Equal(TimeSpan.FromSeconds(5), options.HandlerTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.ProviderStreamEventTimeout);
        Assert.Equal(3, options.MaxContinuations);
        Assert.Equal(2000, options.ContextBudgetPerExtension);
    }

    [Fact]
    public async Task Continuation_requests_are_capped_per_run_and_logged()
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(new HookRunnerOptions { MaxContinuations = 2 }, log);
        runner.Register("ext", Requesting());

        TurnEndedDispatch first = await runner.RunTurnEndedAsync(new TurnEndedPayload("run-1"), Ct);
        TurnEndedDispatch second = await runner.RunTurnEndedAsync(
            new TurnEndedPayload("run-1"),
            Ct
        );
        TurnEndedDispatch third = await runner.RunTurnEndedAsync(new TurnEndedPayload("run-1"), Ct);
        TurnEndedDispatch otherRun = await runner.RunTurnEndedAsync(
            new TurnEndedPayload("run-2"),
            Ct
        );

        Assert.True(first.RequestContinuation);
        Assert.True(second.RequestContinuation);
        Assert.False(third.RequestContinuation);
        Assert.True(otherRun.RequestContinuation);
        Assert.Contains(
            log.Messages,
            message => message.Contains("run-1") && message.Contains("continuation")
        );

        static TestTurnEndedHandler Requesting() =>
            new(
                (_, _) =>
                    ValueTask.FromResult<TurnEndedResult>(new TurnEndedResult.TurnEnded([], true))
            );
    }

    [Fact]
    public async Task Run_starting_resets_the_continuation_count_for_the_run()
    {
        var runner = new HookRunner(new HookRunnerOptions { MaxContinuations = 1 });
        runner.Register("ext", Requesting());

        Assert.True(
            (await runner.RunTurnEndedAsync(new TurnEndedPayload("run-1"), Ct)).RequestContinuation
        );
        Assert.False(
            (await runner.RunTurnEndedAsync(new TurnEndedPayload("run-1"), Ct)).RequestContinuation
        );

        await runner.RunRunStartingAsync(new RunStartingPayload("run-1", [], []), Ct);

        Assert.True(
            (await runner.RunTurnEndedAsync(new TurnEndedPayload("run-1"), Ct)).RequestContinuation
        );

        static TestTurnEndedHandler Requesting() =>
            new(
                (_, _) =>
                    ValueTask.FromResult<TurnEndedResult>(new TurnEndedResult.TurnEnded([], true))
            );
    }

    [Fact]
    public async Task Context_additions_beyond_the_per_extension_budget_are_dropped_and_logged()
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(new HookRunnerOptions { ContextBudgetPerExtension = 5 }, log);
        runner.Register("big", Add(new string('x', 21)));
        runner.Register("small", Add(new string('x', 20)));

        ContextBuildingDispatch result = await runner.RunContextBuildingAsync(
            new ContextBuildingPayload("r1", []),
            Ct
        );

        Assert.Equal(20, Assert.Single(result.AddedMessages).Text.Length);
        Assert.Equal("small", result.AddedMessages[0].Source);
        Assert.Contains(
            log.Messages,
            message => message.Contains("big") && message.Contains("budget")
        );

        static TestContextBuildingHandler Add(string text) =>
            new(
                (_, _) =>
                    ValueTask.FromResult(
                        new ContextBuildingResult([new ContextMessage("system", text)])
                    )
            );
    }

    [Fact]
    public async Task One_extension_cannot_exceed_its_budget_across_handlers()
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(new HookRunnerOptions { ContextBudgetPerExtension = 5 }, log);
        runner.Register("ext", Add("123456789012"));
        runner.Register("ext", Add("210987654321"));

        ContextBuildingDispatch result = await runner.RunContextBuildingAsync(
            new ContextBuildingPayload("r1", []),
            Ct
        );

        Assert.Equal("123456789012", Assert.Single(result.AddedMessages).Text);
        Assert.Contains(
            log.Messages,
            message => message.Contains("ext") && message.Contains("budget")
        );

        static TestContextBuildingHandler Add(string text) =>
            new(
                (_, _) =>
                    ValueTask.FromResult(
                        new ContextBuildingResult([new ContextMessage("system", text)])
                    )
            );
    }

    [Fact]
    public async Task Run_starting_drops_over_budget_added_sections_but_keeps_edits()
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(new HookRunnerOptions { ContextBudgetPerExtension = 5 }, log);
        runner.Register(
            "big",
            new TestRunStartingHandler(
                (_, _) =>
                    ValueTask.FromResult<RunStartingResult>(
                        new RunStartingResult.Apply(
                            [new PromptSectionEdit("extra", new string('x', 21))],
                            null
                        )
                    )
            )
        );
        runner.Register(
            "small",
            new TestRunStartingHandler(
                (_, _) =>
                    ValueTask.FromResult<RunStartingResult>(
                        new RunStartingResult.Apply(
                            [new PromptSectionEdit("system", "short")],
                            null
                        )
                    )
            )
        );

        RunStartingDispatch result = await runner.RunRunStartingAsync(
            new RunStartingPayload("r1", [new PromptSection("system", "base")], []),
            Ct
        );

        PromptSectionEdit edit = Assert.Single(result.SectionEdits);
        Assert.Equal("system", edit.Name);
        Assert.Contains(
            log.Messages,
            message => message.Contains("big") && message.Contains("budget")
        );
    }

    [Fact]
    public async Task Provider_stream_events_use_the_shorter_fast_path_timeout()
    {
        var runner = new HookRunner(
            new HookRunnerOptions
            {
                HandlerTimeout = TimeSpan.FromSeconds(5),
                ProviderStreamEventTimeout = TimeSpan.FromMilliseconds(20),
            }
        );
        runner.Register(
            "ext",
            new TestProviderStreamEventHandler(
                (_, _) => new ValueTask(new TaskCompletionSource<bool>().Task)
            )
        );

        long started = Stopwatch.GetTimestamp();
        await runner.RunProviderStreamEventAsync(TestHookPayloads.StreamEvent, Ct);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

        Assert.True(
            elapsed < TimeSpan.FromMilliseconds(200),
            $"fast path took {elapsed.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} ms"
        );
    }

    [Fact]
    public void Invalid_options_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HookRunner(new HookRunnerOptions { HandlerTimeout = TimeSpan.Zero })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HookRunner(new HookRunnerOptions { MaxContinuations = -1 })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HookRunner(new HookRunnerOptions { ContextBudgetPerExtension = -1 })
        );
    }
}
