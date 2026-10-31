using System.Text.Json;
using Lunate.Ai;
using Lunate.Contracts;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    private async Task<UsageModelClient?> EvaluateRoutingDecisionAsync(
        string runId,
        IReadOnlyList<ChatMessage> messages,
        UsageModelClient? configuredModel,
        AgentEventChannel channel,
        CancellationToken ct
    )
    {
        if (_options.RoutingMode == DecisionMode.Off)
        {
            return configuredModel;
        }

        const string questionId = "routing";
        UsageModelClient? model = ResolveUsageModel("decision");
        if (model is null)
        {
            channel.Emit(new DecisionUnavailable(runId) { QuestionId = questionId });
            return configuredModel;
        }

        bool native =
            model.Model.ApiType == "native"
            && model.Client.GetService(typeof(INativeDecisionClient)) is INativeDecisionClient;
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
        string[] profiles = [.. _options.RoutingProfiles!.Keys];
        var request = new DecisionRequest(
            JsonSerializer.SerializeToElement(
                new
                {
                    input = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text,
                    profiles,
                }
            ),
            [
                new DecisionQuestion(
                    questionId,
                    "Which configured model profile best fits the user's request?",
                    DecisionQuestionKind.Choice,
                    profiles
                ),
            ]
        );

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
                    )
            );
            DecisionAnswer answer = response.Results[questionId];
            DecisionProbability mostProbable = answer.Distribution.MaxBy(item => item.Probability)!;
            channel.Emit(
                new DecisionEvaluated(
                    runId,
                    answer.ModelId,
                    answer.Method,
                    mostProbable.Answer,
                    mostProbable.Probability
                )
                {
                    QuestionId = questionId,
                    Answer = mostProbable.Answer,
                }
            );
            return _options.RoutingMode == DecisionMode.Shadow
                ? configuredModel
                : _options.RoutingProfiles[mostProbable.Answer];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            channel.Emit(
                new DecisionFailed(runId, modelId, exception.Message) { QuestionId = questionId }
            );
            return configuredModel;
        }
        catch (Exception)
        {
            channel.Emit(
                new DecisionFailed(runId, modelId, "provider request failed")
                {
                    QuestionId = questionId,
                }
            );
            return configuredModel;
        }
    }
}
