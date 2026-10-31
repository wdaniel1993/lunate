using System.Text.Json;
using Lunate.Ai;
using Lunate.Contracts;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
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
            channel.Emit(new DecisionUnavailable(runId) { QuestionId = "approval" });
            return new ApprovalDecisionResult(null, null);
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
        var state = JsonSerializer.SerializeToElement(
            new
            {
                tool = request.Tool.Name,
                risk = request.Tool.Risk.ToString(),
                description = request.Tool.Description,
                workspace = request.WorkspaceRoot,
                arguments = request.Args,
            }
        );
        var decisionRequest = new DecisionRequest(
            state,
            [
                new DecisionQuestion(
                    "approval",
                    "Should this tool call be approved?",
                    DecisionQuestionKind.Choice,
                    ["approve", "deny"]
                ),
            ]
        );

        try
        {
            DecisionResponse response = await DecisionEngine.EvaluateAsync(
                decisionRequest,
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
            DecisionAnswer answer = response.Results["approval"];
            var evaluated = new DecisionEvaluated(
                runId,
                answer.ModelId,
                answer.Method,
                answer.Answer,
                answer.Probability
            )
            {
                QuestionId = "approval",
                Answer = answer.Answer,
            };
            channel.Emit(evaluated);
            double threshold = model.DecisionThreshold ?? 0.5d;
            bool approve = answer.Answer == "approve" && answer.Probability >= threshold;
            return new ApprovalDecisionResult(evaluated, null, approve);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            const string questionId = "approval";
            string reason = exception.Message;
            channel.Emit(new DecisionFailed(runId, modelId, reason) { QuestionId = questionId });
            return new ApprovalDecisionResult(null, reason);
        }
        catch (Exception)
        {
            const string reason = "provider request failed";
            channel.Emit(new DecisionFailed(runId, modelId, reason) { QuestionId = "approval" });
            return new ApprovalDecisionResult(
                null,
                $"Decision for model '{modelId}' failed after retry: {reason}."
            );
        }
    }

    private sealed record ApprovalDecisionResult(
        DecisionEvaluated? Evaluation,
        string? Failure,
        bool Approve = false
    );
}
