using System.Globalization;
using System.Text.Json;
using Lunate.Ai;
using Lunate.Contracts;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    private async Task<DecisionResponse> EvaluateToolDecisionAsync(
        string runId,
        DecisionRequest request,
        AgentEventChannel channel,
        CancellationToken ct
    )
    {
        UsageModelClient? model = ResolveUsageModel("decision");
        if (model is null)
        {
            throw new InvalidOperationException(
                "The decision profile is unavailable. Continue without this advice or configure a decision profile."
            );
        }

        bool native =
            model.Model.ApiType == "native" && DecisionClientResolver.HasNativeClient(model.Client);
        if (!native)
        {
            RequireCapability(
                model.Model,
                model.Model.Capabilities?.Features,
                "structured-output",
                "feature"
            );
        }

        string modelId = model.Model.Id;
        string method = native ? "native" : "adapter";
        try
        {
            DecisionResponse response = await DecisionEngine.EvaluateAsync(
                request,
                model.Client,
                modelId,
                method,
                ct,
                (usage, resolvedModelId, decisionMethod, probability) =>
                    AccumulateUsage(
                        runId,
                        channel,
                        usage,
                        resolvedModelId,
                        UsageAttribution("decision", model, decisionMethod, probability)
                    ),
                supportsImages: DecisionClientResolver.SupportsImageInput(model.Model)
            );

            string? stateWarning = DecisionStateSizeWarning(request.State);
            foreach ((string questionId, DecisionAnswer answer) in response.Results)
            {
                channel.Emit(
                    new DecisionEvaluated(
                        runId,
                        answer.ModelId,
                        answer.Method,
                        answer.Answer,
                        answer.Probability
                    )
                    {
                        QuestionId = questionId,
                        Answer = answer.Answer,
                        Warning = stateWarning,
                    }
                );
                stateWarning = null;
            }

            return response;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            foreach (DecisionQuestion question in request.Questions)
            {
                channel.Emit(
                    new DecisionFailed(runId, modelId, exception.Message)
                    {
                        QuestionId = question.Id,
                    }
                );
            }

            throw;
        }
    }

    private static string? DecisionStateSizeWarning(JsonElement state)
    {
        long estimatedTokens = state.GetRawText().Length / 4L;
        if (estimatedTokens <= 28_000)
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Decision state is estimated above {estimatedTokens:N0} tokens (4 characters per token); it exceeds the default 32,000-token per-question budget. Long context is opt-in and can be slower and less accurate."
        );
    }
}
