using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

using static Lunate.Extensibility.Tests.HookFailureHandlers;

internal static class HookFailureScenarios
{
    public static void RegisterScenario(
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
}
