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
        string[] profiles = [.. _options.RoutingProfiles!.Keys];
        var profileCriteria = _options
            .RoutingProfiles!.Where(profile =>
                !string.IsNullOrWhiteSpace(profile.Value.Description)
            )
            .ToDictionary(
                profile => profile.Key,
                profile => profile.Value.Description!,
                StringComparer.Ordinal
            );
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? criteria =
            profileCriteria.Count == 0
                ? null
                : new Dictionary<string, IReadOnlyDictionary<string, string>>(
                    StringComparer.Ordinal
                )
                {
                    [questionId] = profileCriteria,
                };
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
                    ),
                questionCriteria: criteria
            );
            DecisionAnswer answer = response.Results[questionId];
            DecisionProbability mostProbable = answer.Distribution.MaxBy(item => item.Probability)!;
            bool shadow = _options.RoutingMode == DecisionMode.Shadow;
            bool confident = answer.Confidence >= _options.RoutingMinConfidence;
            UsageModelClient? routedModel =
                shadow || !confident
                    ? configuredModel
                    : _options.RoutingProfiles[mostProbable.Answer];
            string outcome =
                shadow ? "shadow"
                : confident ? "routed"
                : "confidence-below-threshold";
            string? warning = null;
            if (!_routingDescriptionWarningEmitted)
            {
                string[] undescribed =
                [
                    .. _options
                        .RoutingProfiles!.Where(profile =>
                            string.IsNullOrWhiteSpace(profile.Value.Description)
                        )
                        .Select(profile => profile.Key),
                ];
                if (undescribed.Length > 0)
                {
                    warning =
                        $"Routing quality depends on descriptions for profiles: {string.Join(", ", undescribed)}.";
                    _routingDescriptionWarningEmitted = true;
                }
            }

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
                    Distribution = answer.Distribution,
                    Confidence = answer.Confidence,
                    ConfidenceThreshold = _options.RoutingMinConfidence,
                    Outcome = outcome,
                    Warning = warning,
                }
            );
            return routedModel;
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
