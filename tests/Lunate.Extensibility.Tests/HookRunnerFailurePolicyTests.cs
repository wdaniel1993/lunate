using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

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
        PolicyObservation observation = await RunScenario(hook, hanging: false);

        AssertPolicy(hook, observation);
        AssertFailureLogged(hook, observation, "System.InvalidOperationException: boom");
    }

    [Theory]
    [MemberData(nameof(Hooks))]
    public async Task A_hanging_handler_is_cut_off_and_applies_the_declared_policy(string hook)
    {
        PolicyObservation observation = await RunScenario(hook, hanging: true);

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

    private static async Task<PolicyObservation> RunScenario(string hook, bool hanging)
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(FastOptions(), log);
        var observation = new PolicyObservation();

        RegisterScenario(runner, hook, hanging, observation);
        observation.Result = await Dispatch(runner, hook);
        observation.Log = log.Messages;

        return observation;
    }

    private static void RegisterScenario(
        HookRunner runner,
        string hook,
        bool hanging,
        PolicyObservation observation
    )
    {
        switch (hook)
        {
            case "ProjectTrust":
                runner.Register(
                    "bad",
                    new TestProjectTrustHandler(
                        Fail<ProjectTrustPayload, ProjectTrustResult>(hanging)
                    )
                );
                runner.Register(
                    "good",
                    new TestProjectTrustHandler(
                        (_, _) =>
                        {
                            observation.HealthyRan = true;
                            return ValueTask.FromResult<ProjectTrustResult>(
                                new ProjectTrustResult.Allow()
                            );
                        }
                    )
                );
                break;
            case "SessionStarted":
                runner.Register(
                    "bad",
                    new TestSessionStartedHandler(FailVoid<SessionStartedPayload>(hanging))
                );
                runner.Register(
                    "good",
                    new TestSessionStartedHandler((_, _) => HealthyObserve(observation))
                );
                break;
            case "SessionEnding":
                runner.Register(
                    "bad",
                    new TestSessionEndingHandler(FailVoid<SessionEndingPayload>(hanging))
                );
                runner.Register(
                    "good",
                    new TestSessionEndingHandler((_, _) => HealthyObserve(observation))
                );
                break;
            case "InputReceived":
                runner.Register(
                    "bad",
                    new TestInputReceivedHandler(
                        Fail<InputReceivedPayload, InputReceivedResult>(hanging)
                    )
                );
                runner.Register(
                    "good",
                    new TestInputReceivedHandler(
                        (_, _) =>
                        {
                            observation.HealthyRan = true;
                            return ValueTask.FromResult<InputReceivedResult>(
                                new InputReceivedResult.PassThrough()
                            );
                        }
                    )
                );
                break;
            case "RunStarting":
                runner.Register(
                    "bad",
                    new TestRunStartingHandler(Fail<RunStartingPayload, RunStartingResult>(hanging))
                );
                runner.Register(
                    "good",
                    new TestRunStartingHandler(
                        (_, _) =>
                        {
                            observation.HealthyRan = true;
                            return ValueTask.FromResult<RunStartingResult>(
                                new RunStartingResult.Apply([], null)
                            );
                        }
                    )
                );
                break;
            case "ContextBuilding":
                runner.Register(
                    "bad",
                    new TestContextBuildingHandler(
                        Fail<ContextBuildingPayload, ContextBuildingResult>(hanging)
                    )
                );
                runner.Register(
                    "good",
                    new TestContextBuildingHandler(
                        (_, _) =>
                        {
                            observation.HealthyRan = true;
                            return ValueTask.FromResult(ContextBuildingResult.None);
                        }
                    )
                );
                break;
            case "ProviderStreamEvent":
                runner.Register(
                    "bad",
                    new TestProviderStreamEventHandler(
                        FailVoid<ProviderStreamEventPayload>(hanging)
                    )
                );
                runner.Register(
                    "good",
                    new TestProviderStreamEventHandler((_, _) => HealthyObserve(observation))
                );
                break;
            case "MessageCompleted":
                runner.Register(
                    "bad",
                    new TestMessageCompletedHandler(
                        Fail<MessageCompletedPayload, MessageCompletedResult>(hanging)
                    )
                );
                runner.Register(
                    "good",
                    new TestMessageCompletedHandler(
                        (_, _) =>
                        {
                            observation.HealthyRan = true;
                            return ValueTask.FromResult<MessageCompletedResult>(
                                new MessageCompletedResult.Replace("healthy")
                            );
                        }
                    )
                );
                break;
            case "ToolCalling":
                runner.Register(
                    "bad",
                    new TestToolCallingHandler(Fail<ToolCallingPayload, ToolCallingResult>(hanging))
                );
                runner.Register(
                    "good",
                    new TestToolCallingHandler(
                        (_, _) =>
                        {
                            observation.HealthyRan = true;
                            return ValueTask.FromResult<ToolCallingResult>(
                                new ToolCallingResult.Proceed(null)
                            );
                        }
                    )
                );
                break;
            case "ToolResultReady":
                runner.Register(
                    "bad",
                    new TestToolResultReadyHandler(
                        Fail<ToolResultReadyPayload, ToolResultReadyResult>(hanging)
                    )
                );
                runner.Register(
                    "good",
                    new TestToolResultReadyHandler(
                        (payload, _) =>
                        {
                            observation.HealthyRan = true;
                            return ValueTask.FromResult(
                                new ToolResultReadyResult(payload.Output + "+healthy", null)
                            );
                        }
                    )
                );
                break;
            case "TurnEnded":
                runner.Register(
                    "bad",
                    new TestTurnEndedHandler(Fail<TurnEndedPayload, TurnEndedResult>(hanging))
                );
                runner.Register(
                    "good",
                    new TestTurnEndedHandler(
                        (_, _) =>
                        {
                            observation.HealthyRan = true;
                            return ValueTask.FromResult<TurnEndedResult>(
                                new TurnEndedResult.None()
                            );
                        }
                    )
                );
                break;
            case "RunSettled":
                runner.Register(
                    "bad",
                    new TestRunSettledHandler(FailVoid<RunSettledPayload>(hanging))
                );
                runner.Register(
                    "good",
                    new TestRunSettledHandler((_, _) => HealthyObserve(observation))
                );
                break;
            case "Compacting":
                runner.Register(
                    "bad",
                    new TestCompactingHandler(Fail<CompactingPayload, CompactingResult>(hanging))
                );
                runner.Register(
                    "good",
                    new TestCompactingHandler(
                        (_, _) =>
                        {
                            observation.HealthyRan = true;
                            return ValueTask.FromResult<CompactingResult>(
                                new CompactingResult.Provide("healthy")
                            );
                        }
                    )
                );
                break;
            case "ModelChanged":
                runner.Register(
                    "bad",
                    new TestModelChangedHandler(FailVoid<ModelChangedPayload>(hanging))
                );
                runner.Register(
                    "good",
                    new TestModelChangedHandler((_, _) => HealthyObserve(observation))
                );
                break;
            case "ToolsChanged":
                runner.Register(
                    "bad",
                    new TestToolsChangedHandler(FailVoid<ToolsChangedPayload>(hanging))
                );
                runner.Register(
                    "good",
                    new TestToolsChangedHandler((_, _) => HealthyObserve(observation))
                );
                break;
            default:
                throw new InvalidOperationException($"unknown hook '{hook}'");
        }
    }

    private static async Task<object?> Dispatch(HookRunner runner, string hook) =>
        hook switch
        {
            "ProjectTrust" => await runner.RunProjectTrustAsync(
                new ProjectTrustPayload("ext", "/ext", "repo", "/work"),
                cancellationToken: CancellationToken.None
            ),
            "SessionStarted" => await Observe(
                runner.RunSessionStartedAsync(
                    TestHookPayloads.SessionStarted,
                    CancellationToken.None
                )
            ),
            "SessionEnding" => await Observe(
                runner.RunSessionEndingAsync(TestHookPayloads.SessionEnding, CancellationToken.None)
            ),
            "InputReceived" => await runner.RunInputReceivedAsync(
                TestHookPayloads.Input,
                CancellationToken.None
            ),
            "RunStarting" => await runner.RunRunStartingAsync(
                TestHookPayloads.RunStarting,
                CancellationToken.None
            ),
            "ContextBuilding" => await runner.RunContextBuildingAsync(
                TestHookPayloads.ContextBuilding,
                CancellationToken.None
            ),
            "ProviderStreamEvent" => await Observe(
                runner.RunProviderStreamEventAsync(
                    TestHookPayloads.StreamEvent,
                    CancellationToken.None
                )
            ),
            "MessageCompleted" => await runner.RunMessageCompletedAsync(
                TestHookPayloads.MessageCompleted,
                CancellationToken.None
            ),
            "ToolCalling" => await runner.RunToolCallingAsync(
                TestHookPayloads.ToolCalling,
                CancellationToken.None
            ),
            "ToolResultReady" => await runner.RunToolResultReadyAsync(
                TestHookPayloads.ToolResultReady,
                CancellationToken.None
            ),
            "TurnEnded" => await runner.RunTurnEndedAsync(
                TestHookPayloads.TurnEnded,
                CancellationToken.None
            ),
            "RunSettled" => await Observe(
                runner.RunRunSettledAsync(TestHookPayloads.RunSettled, CancellationToken.None)
            ),
            "Compacting" => await runner.RunCompactingAsync(
                TestHookPayloads.Compacting,
                CancellationToken.None
            ),
            "ModelChanged" => await Observe(
                runner.RunModelChangedAsync(TestHookPayloads.ModelChanged, CancellationToken.None)
            ),
            "ToolsChanged" => await Observe(
                runner.RunToolsChangedAsync(TestHookPayloads.ToolsChanged, CancellationToken.None)
            ),
            _ => throw new InvalidOperationException($"unknown hook '{hook}'"),
        };

    private static async ValueTask<object?> Observe(ValueTask dispatch)
    {
        await dispatch;
        return null;
    }

    private static void AssertPolicy(string hook, PolicyObservation observation)
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
        PolicyObservation observation,
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

    private static Func<TPayload, CancellationToken, ValueTask> FailVoid<TPayload>(bool hanging) =>
        hanging
            ? (_, _) => new ValueTask(new TaskCompletionSource<bool>().Task)
            : (_, _) => throw new InvalidOperationException("boom");

    private static Func<TPayload, CancellationToken, ValueTask<TResult>> Fail<TPayload, TResult>(
        bool hanging
    ) =>
        hanging
            ? (_, _) => new ValueTask<TResult>(new TaskCompletionSource<TResult>().Task)
            : (_, _) => throw new InvalidOperationException("boom");

    private static ValueTask HealthyObserve(PolicyObservation observation)
    {
        observation.HealthyRan = true;
        return ValueTask.CompletedTask;
    }

    private sealed class PolicyObservation
    {
        public bool HealthyRan { get; set; }

        public object? Result { get; set; }

        public IReadOnlyList<string> Log { get; set; } = [];
    }
}
