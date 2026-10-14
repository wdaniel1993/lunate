using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// The run-bound model access a tool receives through <see cref="ToolContext.Models"/>: every
/// client it hands out is wired into the owning run, so nested calls emit <c>UsageUpdated</c> with
/// the resolved model id, count into the run's token budget and feed the inherited tree accounting
/// (shared with the run's own model-call path), and routed calls emit <c>ModelRouted</c>.
/// </summary>
internal sealed class RunModelAccess(
    IModelRegistry registry,
    AgentHarness harness,
    AgentEventChannel channel,
    string runId
) : IModelAccess
{
    public IChatClient For(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        IChatClient client = registry.Resolve(modelId);
        return new RunBoundChatClient(client, harness, channel, runId, modelId);
    }
}

/// <summary>
/// One nested call's wrapper: it scopes the route observer around the call, emits <c>ModelRouted</c>
/// per resolution, and charges every reported usage to the owning run with the resolved model id.
/// </summary>
internal sealed class RunBoundChatClient(
    IChatClient inner,
    AgentHarness harness,
    AgentEventChannel channel,
    string runId,
    string requestedModel
) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        string? routed = null;
        ChatResponse response;
        using (
            ModelRouteObserver.Scope((requested, resolved) => routed = Report(requested, resolved))
        )
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);
        }

        if (response.Usage is { } usage)
        {
            harness.AccumulateUsage(runId, channel, usage, routed ?? requestedModel);
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        string? routed = null;
        using (
            ModelRouteObserver.Scope((requested, resolved) => routed = Report(requested, resolved))
        )
        {
            await foreach (
                ChatResponseUpdate update in base.GetStreamingResponseAsync(
                        messages,
                        options,
                        cancellationToken
                    )
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                foreach (AIContent content in update.Contents)
                {
                    if (content is UsageContent usage)
                    {
                        harness.AccumulateUsage(
                            runId,
                            channel,
                            usage.Details,
                            routed ?? requestedModel
                        );
                    }
                }

                yield return update;
            }
        }
    }

    private string? Report(string requested, string resolved)
    {
        channel.Emit(new ModelRouted(runId, requested, resolved));
        return resolved;
    }
}
