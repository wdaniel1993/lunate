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
        JsonElement state = JsonSerializer.SerializeToElement(
            new
            {
                input = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text,
                profiles,
            }
        );
        RoutingPlan plan = BuildRoutingPlan(
            profiles,
            _options.RoutingProfiles,
            model.Model.ChoiceOrderSensitive
        );
        var request = new DecisionRequest(state, plan.Questions);

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
                questionCriteria: plan.Criteria
            );
            RoutingSelection selection = SelectRoutingResult(response, profiles, plan);
            bool shadow = _options.RoutingMode == DecisionMode.Shadow;
            bool confident = selection.Confidence >= _options.RoutingMinConfidence;
            UsageModelClient? routedModel =
                shadow || !confident
                    ? configuredModel
                    : _options.RoutingProfiles[selection.Profile];
            string outcome =
                shadow ? "shadow"
                : confident ? "routed"
                : "confidence-below-threshold";
            string? warning = DecisionStateSizeWarning(state);
            string? descriptionWarning = MissingDescriptionWarning();
            if (descriptionWarning is not null)
            {
                warning = warning is null ? descriptionWarning : warning + " " + descriptionWarning;
            }

            channel.Emit(
                new DecisionEvaluated(
                    runId,
                    selection.ModelId,
                    selection.Method,
                    selection.Profile,
                    selection.Probability
                )
                {
                    QuestionId = questionId,
                    Answer = selection.Profile,
                    Distribution = selection.Distribution,
                    Confidence = selection.Confidence,
                    ConfidenceThreshold = _options.RoutingMinConfidence,
                    Outcome = outcome,
                    Strategy = plan.Strategy,
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

    private static RoutingPlan BuildRoutingPlan(
        string[] profiles,
        IReadOnlyDictionary<string, UsageModelClient> routingProfiles,
        bool orderSensitive
    )
    {
        Dictionary<string, IReadOnlyDictionary<string, string>> criteria = new(
            StringComparer.Ordinal
        );
        List<DecisionQuestion> questions = [];
        if (orderSensitive)
        {
            foreach (string profile in profiles)
            {
                string id = RoutingQuestionId(profile);
                questions.Add(
                    new DecisionQuestion(
                        id,
                        $"Does this request fit the '{profile}' profile?",
                        DecisionQuestionKind.YesNo
                    )
                );
                if (!string.IsNullOrWhiteSpace(routingProfiles[profile].Description))
                {
                    criteria[id] = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["true"] = routingProfiles[profile].Description!,
                        ["false"] = $"The request does not fit the '{profile}' profile.",
                    };
                }
            }

            return new RoutingPlan(questions, criteria, "noul-per-profile", NoulPerProfile: true);
        }

        string[] descriptions =
        [
            .. profiles.Where(profile =>
                !string.IsNullOrWhiteSpace(routingProfiles[profile].Description)
            ),
        ];
        if (descriptions.Length > 0)
        {
            criteria["routing"] = profiles
                .Where(profile => !string.IsNullOrWhiteSpace(routingProfiles[profile].Description))
                .ToDictionary(
                    profile => profile,
                    profile => routingProfiles[profile].Description!,
                    StringComparer.Ordinal
                );
        }

        questions.Add(
            new DecisionQuestion(
                "routing",
                "Which configured model profile best fits the user's request?",
                DecisionQuestionKind.Choice,
                profiles
            )
        );
        return new RoutingPlan(questions, criteria, "choice", NoulPerProfile: false);
    }

    private static RoutingSelection SelectRoutingResult(
        DecisionResponse response,
        string[] profiles,
        RoutingPlan plan
    )
    {
        if (!plan.NoulPerProfile)
        {
            DecisionAnswer answer = response.Results["routing"];
            DecisionProbability mostProbable = answer.Distribution.MaxBy(item => item.Probability)!;
            return new RoutingSelection(
                mostProbable.Answer,
                mostProbable.Probability,
                answer.Confidence,
                answer.Distribution,
                answer.ModelId,
                answer.Method
            );
        }

        DecisionAnswer first = response.Results[RoutingQuestionId(profiles[0])];
        DecisionProbability[] distribution =
        [
            .. profiles.Select(profile => new DecisionProbability(
                profile,
                response
                    .Results[RoutingQuestionId(profile)]
                    .Distribution.Single(item =>
                        string.Equals(item.Answer, "yes", StringComparison.Ordinal)
                    )
                    .Probability
            )),
        ];
        DecisionProbability[] ranked =
        [
            .. distribution.OrderByDescending(item => item.Probability),
        ];
        return new RoutingSelection(
            ranked[0].Answer,
            ranked[0].Probability,
            ranked[0].Probability - ranked[1].Probability,
            distribution,
            first.ModelId,
            first.Method
        );
    }

    private string? MissingDescriptionWarning()
    {
        if (_routingDescriptionWarningEmitted)
        {
            return null;
        }

        string[] missing =
        [
            .. _options
                .RoutingProfiles!.Where(profile =>
                    string.IsNullOrWhiteSpace(profile.Value.Description)
                )
                .Select(profile => profile.Key),
        ];
        if (missing.Length == 0)
        {
            return null;
        }

        _routingDescriptionWarningEmitted = true;
        return $"Routing quality depends on descriptions for profiles: {string.Join(", ", missing)}.";
    }

    private static string RoutingQuestionId(string profile) => "routing:" + profile;

    private sealed record RoutingPlan(
        IReadOnlyList<DecisionQuestion> Questions,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Criteria,
        string Strategy,
        bool NoulPerProfile
    );

    private sealed record RoutingSelection(
        string Profile,
        double Probability,
        double Confidence,
        IReadOnlyList<DecisionProbability> Distribution,
        string ModelId,
        string Method
    );
}
