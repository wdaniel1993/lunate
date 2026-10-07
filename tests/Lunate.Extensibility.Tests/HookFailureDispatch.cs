using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

using static Lunate.Extensibility.Tests.HookFailureHandlers;

public static class HookFailureDispatch
{
    public static async Task<object?> Dispatch(HookRunner runner, string hook) =>
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

    public static async ValueTask<object?> Observe(ValueTask dispatch)
    {
        await dispatch;
        return null;
    }
}
