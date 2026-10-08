using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility;

/// <summary>
/// Adapts the agent harness's extension seam to the hook runner: agent-native records become hook
/// DTOs and the runner's tagged, aggregated results become agent-native results.
/// </summary>
public sealed class AgentHookAdapter : IAgentHookPoints
{
    private readonly HookRunner _runner;

    public AgentHookAdapter(HookRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async ValueTask<AgentRunStartingResult> RunStartingAsync(
        AgentRunStartingContext context,
        CancellationToken cancellationToken
    )
    {
        RunStartingDispatch dispatch = await _runner.RunRunStartingAsync(
            new RunStartingPayload(
                context.RunId,
                [
                    .. context.Sections.Select(section => new PromptSection(
                        section.Name,
                        section.Text
                    )),
                ],
                context.Tools
            ),
            cancellationToken
        );
        if (dispatch.SectionEdits.Count == 0 && dispatch.ActiveTools is null)
        {
            return new AgentRunStartingResult.None();
        }

        return new AgentRunStartingResult.Apply(
            [
                .. dispatch.SectionEdits.Select(edit => new AgentPromptSectionEdit(
                    edit.Name,
                    edit.Text
                )),
            ],
            dispatch.ActiveTools
        );
    }

    public async ValueTask<AgentContextBuildingResult> ContextBuildingAsync(
        AgentContextBuildingContext context,
        CancellationToken cancellationToken
    )
    {
        ContextBuildingDispatch dispatch = await _runner.RunContextBuildingAsync(
            new ContextBuildingPayload(
                context.RunId,
                [
                    .. context.Messages.Select(message => new ContextMessage(
                        message.Role,
                        message.Text
                    )
                    {
                        Source = message.Source,
                    }),
                ]
            ),
            cancellationToken
        );
        return new AgentContextBuildingResult([
            .. dispatch.AddedMessages.Select(message => new AgentContextMessage(
                message.Role,
                message.Text,
                message.Source
            )),
        ]);
    }

    public async ValueTask ProviderStreamEventAsync(
        string runId,
        ChatResponseUpdate update,
        CancellationToken cancellationToken
    )
    {
        await _runner.RunProviderStreamEventAsync(
            new ProviderStreamEventPayload(
                runId,
                update.ModelId,
                string.Concat(
                    update.Contents.OfType<TextContent>().Select(content => content.Text)
                ),
                update.FinishReason?.Value
            ),
            cancellationToken
        );
    }

    public async ValueTask<AgentMessageCompletedResult> MessageCompletedAsync(
        AgentMessageCompletedContext context,
        CancellationToken cancellationToken
    )
    {
        MessageCompletedResult result = await _runner.RunMessageCompletedAsync(
            new MessageCompletedPayload(context.RunId, context.Role, context.Text),
            cancellationToken
        );
        return result switch
        {
            MessageCompletedResult.Replace replace => new AgentMessageCompletedResult.Replace(
                replace.Text
            ),
            _ => new AgentMessageCompletedResult.Keep(),
        };
    }

    public async ValueTask<AgentToolCallingResult> ToolCallingAsync(
        AgentToolCallingContext context,
        CancellationToken cancellationToken
    )
    {
        ToolCallingResult result = await _runner.RunToolCallingAsync(
            new ToolCallingPayload(
                context.RunId,
                context.CallId,
                context.ToolName,
                context.Arguments
            ),
            cancellationToken
        );
        return result switch
        {
            ToolCallingResult.Block block => new AgentToolCallingResult.Block(block.Reason),
            ToolCallingResult.Proceed proceed => new AgentToolCallingResult.Proceed(
                proceed.Arguments
            ),
            _ => new AgentToolCallingResult.Proceed(context.Arguments),
        };
    }

    public async ValueTask<AgentToolResultReadyResult> ToolResultReadyAsync(
        AgentToolResultReadyContext context,
        CancellationToken cancellationToken
    )
    {
        ToolResultReadyDispatch dispatch = await _runner.RunToolResultReadyAsync(
            new ToolResultReadyPayload(
                context.RunId,
                context.CallId,
                context.ToolName,
                context.Output,
                context.IsError
            ),
            cancellationToken
        );
        return new AgentToolResultReadyResult(dispatch.Output, dispatch.Data);
    }

    public async ValueTask<AgentTurnEndedResult> TurnEndedAsync(
        AgentTurnEndedContext context,
        CancellationToken cancellationToken
    )
    {
        TurnEndedDispatch dispatch = await _runner.RunTurnEndedAsync(
            new TurnEndedPayload(context.RunId),
            cancellationToken
        );
        return new AgentTurnEndedResult(
            [
                .. dispatch.Entries.Select(entry => new AgentExtensionEntry(
                    entry.ExtensionId ?? string.Empty,
                    entry.Type,
                    entry.Payload
                )),
            ],
            dispatch.RequestContinuation
        );
    }

    public async ValueTask RunSettledAsync(
        AgentRunSettledContext context,
        CancellationToken cancellationToken
    ) => await _runner.RunRunSettledAsync(new RunSettledPayload(context.RunId), cancellationToken);

    public async ValueTask<AgentCompactingResult> CompactingAsync(
        AgentCompactingContext context,
        CancellationToken cancellationToken
    )
    {
        CompactingResult result = await _runner.RunCompactingAsync(
            new CompactingPayload(
                context.RunId,
                [
                    .. context.Messages.Select(message => new ContextMessage(
                        message.Role,
                        message.Text
                    )
                    {
                        Source = message.Source,
                    }),
                ]
            ),
            cancellationToken
        );
        return result switch
        {
            CompactingResult.Provide provide => new AgentCompactingResult.Provide(provide.Summary),
            _ => new AgentCompactingResult.UseDefault(),
        };
    }
}
