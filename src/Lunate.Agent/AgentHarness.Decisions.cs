using System.Text.Json;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    private const double DecisionProbabilityTolerance = 0.000001d;

    private async Task<ApprovalDecisionResult> EvaluateApprovalDecisionAsync(
        string runId,
        ApprovalRequest request,
        AgentEventChannel channel,
        CancellationToken ct
    )
    {
        UsageModelClient? model = ResolveUsageModel("decision");
        if (model is null)
        {
            channel.Emit(new DecisionUnavailable(runId));
            return new ApprovalDecisionResult(null, null);
        }

        RequireCapability(
            model.Model,
            model.Model.Capabilities?.Features,
            "structured-output",
            "feature"
        );
        double threshold = model.DecisionThreshold ?? 0.5d;
        string modelId = model.Model.Id;
        string method = model.Model.ApiType == "native" ? "native" : "adapter";
        string? failure = null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            ChatResponse response;
            try
            {
                response = await model.Client.GetResponseAsync(
                    DecisionPrompt(request),
                    new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
                    ct
                );
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failure = "provider request failed";
                break;
            }

            string resolvedModelId = response.ModelId ?? modelId;
            bool valid = TryReadDecision(
                response.Text,
                out DecisionContract? contract,
                out failure
            );
            if (response.Usage is { } usage)
            {
                AccumulateUsage(
                    runId,
                    channel,
                    usage,
                    resolvedModelId,
                    UsageAttribution(
                        "decision",
                        model,
                        valid ? method : null,
                        valid ? contract!.Probability : null
                    )
                );
            }

            if (valid)
            {
                var evaluated = new DecisionEvaluated(
                    runId,
                    resolvedModelId,
                    method,
                    contract!.Option,
                    contract.Probability
                );
                channel.Emit(evaluated);
                bool approve = contract.Option == "approve" && contract.Probability >= threshold;
                return new ApprovalDecisionResult(evaluated, null, approve);
            }
        }

        string reason = failure ?? "response did not match the decision contract";
        channel.Emit(new DecisionFailed(runId, modelId, reason));
        return new ApprovalDecisionResult(
            null,
            $"Decision for model '{modelId}' failed after retry: {reason}."
        );
    }

    private static List<ChatMessage> DecisionPrompt(ApprovalRequest request) =>
        [
            new(
                ChatRole.System,
                "Assess this proposed tool call against the user's approval rules. Return JSON with exactly an option ('approve' or 'deny') and probabilities containing one finite probability from 0 to 1 for each option; probabilities must sum to 1. This is a recommendation only and cannot grant permission."
            ),
            new(
                ChatRole.User,
                $"Tool: {request.Tool.Name}\nRisk: {request.Tool.Risk}\nDescription: {request.Tool.Description}\nWorkspace: {request.WorkspaceRoot ?? "unknown"}\nArguments: {request.Args.GetRawText()}"
            ),
        ];

    private static bool TryReadDecision(
        string? text,
        out DecisionContract? contract,
        out string? error
    )
    {
        contract = null;
        error = "response must contain one defined option and its probabilities";
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("option", out JsonElement optionElement)
                || optionElement.ValueKind != JsonValueKind.String
                || optionElement.GetString() is not ("approve" or "deny")
                || !root.TryGetProperty("probabilities", out JsonElement probabilities)
                || probabilities.ValueKind != JsonValueKind.Object
            )
            {
                return false;
            }

            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (JsonProperty property in probabilities.EnumerateObject())
            {
                if (
                    property.Name is not ("approve" or "deny")
                    || !property.Value.TryGetDouble(out double value)
                    || !double.IsFinite(value)
                    || value is < 0d or > 1d
                    || !values.TryAdd(property.Name, value)
                )
                {
                    return false;
                }
            }

            if (
                values.Count != 2
                || Math.Abs(values["approve"] + values["deny"] - 1d) > DecisionProbabilityTolerance
            )
            {
                return false;
            }

            string option = optionElement.GetString()!;
            contract = new DecisionContract(option, values[option]);
            error = null;
            return true;
        }
        catch (JsonException)
        {
            error = "response is not valid JSON";
            return false;
        }
    }

    private sealed record DecisionContract(string Option, double Probability);

    private sealed record ApprovalDecisionResult(
        DecisionEvaluated? Evaluation,
        string? Failure,
        bool Approve = false
    );
}
