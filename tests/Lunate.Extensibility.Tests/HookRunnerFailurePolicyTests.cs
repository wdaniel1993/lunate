using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

using static Lunate.Extensibility.Tests.HookFailureDispatch;
using static Lunate.Extensibility.Tests.HookFailureHandlers;
using static Lunate.Extensibility.Tests.HookFailureScenarios;

public sealed class HookRunnerFailurePolicyTests
{
    public static TheoryData<string> Hooks() =>
        new()
        {
            "ProjectTrust",
            "SessionStarted",
            "SessionEnding",
            "InputReceived",
            "RunStarting",
            "ContextBuilding",
            "ProviderStreamEvent",
            "MessageCompleted",
            "ToolCalling",
            "ToolResultReady",
            "TurnEnded",
            "RunSettled",
            "Compacting",
            "ModelChanged",
            "ToolsChanged",
        };

    [Theory]
    [MemberData(nameof(Hooks))]
    public async Task A_throwing_handler_applies_the_declared_policy(string hook)
    {
        HookFailureHandlers.PolicyObservation observation = await RunScenario(hook, hanging: false);

        AssertPolicy(hook, observation);
        AssertFailureLogged(hook, observation, "System.InvalidOperationException: boom");
    }

    [Theory]
    [MemberData(nameof(Hooks))]
    public async Task A_hanging_handler_is_cut_off_and_applies_the_declared_policy(string hook)
    {
        HookFailureHandlers.PolicyObservation observation = await RunScenario(hook, hanging: true);

        AssertPolicy(hook, observation);
        AssertFailureLogged(hook, observation, "timed out");
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var runner = new HookRunner();
        using var source = new CancellationTokenSource();
        runner.Register(
            "ext",
            new TestRunSettledHandler(
                async (_, ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
            )
        );

        Task dispatch = runner
            .RunRunSettledAsync(TestHookPayloads.RunSettled, source.Token)
            .AsTask();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch);
    }

    private static HookRunnerOptions FastOptions() =>
        new()
        {
            HandlerTimeout = TimeSpan.FromMilliseconds(30),
            ProviderStreamEventTimeout = TimeSpan.FromMilliseconds(20),
        };

    private static async Task<HookFailureHandlers.PolicyObservation> RunScenario(
        string hook,
        bool hanging
    )
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(FastOptions(), log);
        var observation = new HookFailureHandlers.PolicyObservation();

        RegisterScenario(runner, hook, hanging, observation);
        observation.Result = await Dispatch(runner, hook);
        observation.Log = log.Messages;

        return observation;
    }

    private static void AssertPolicy(string hook, HookFailureHandlers.PolicyObservation observation)
    {
        switch (hook)
        {
            case "ProjectTrust":
                var trust = Assert.IsType<ProjectTrustDispatch>(observation.Result);
                Assert.IsType<ProjectTrustResult.Deny>(trust.Result);
                Assert.False(observation.HealthyRan);
                break;
            case "ToolCalling":
                Assert.IsType<ToolCallingResult.Block>(observation.Result);
                Assert.False(observation.HealthyRan);
                break;
            case "Compacting":
                Assert.IsType<CompactingResult.UseDefault>(observation.Result);
                Assert.False(observation.HealthyRan);
                break;
            default:
                Assert.True(
                    observation.HealthyRan,
                    $"hook {hook} should report the failure and continue"
                );
                break;
        }
    }

    private static void AssertFailureLogged(
        string hook,
        HookFailureHandlers.PolicyObservation observation,
        string reason
    )
    {
        Assert.Contains(
            observation.Log,
            message =>
                message.Contains("error:", StringComparison.Ordinal)
                && message.Contains(hook, StringComparison.Ordinal)
                && message.Contains("bad", StringComparison.Ordinal)
                && message.Contains(reason, StringComparison.Ordinal)
        );
    }
}
