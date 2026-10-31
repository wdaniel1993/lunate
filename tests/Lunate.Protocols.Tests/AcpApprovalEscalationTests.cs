using System.Text.Json;
using Acp.Schema;
using Lunate.Agent;
using Lunate.Protocols.Acp;

namespace Lunate.Protocols.Tests;

public sealed class AcpApprovalEscalationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Escalation_bypasses_cached_allow_and_shows_the_decision_reason()
    {
        await using RawAcpRuntime runtime = AcpTestSupport.StartRaw();
        int answers = 0;
        runtime.State.PermissionHandler = (request, _) =>
            Task.FromResult(
                Answer(
                    request,
                    Interlocked.Increment(ref answers) == 1
                        ? PermissionOptionKind.AllowAlways
                        : PermissionOptionKind.AllowOnce
                )
            );
        var approver = new ClientApprover(runtime.Server, new SessionId("decision-session"));
        var tool = new RecordingRiskTool("write-probe", ToolRisk.Write);
        ApprovalRequest request = new(
            tool,
            JsonDocument.Parse("""{"path":"a.cs"}""").RootElement.Clone(),
            "run_1",
            null,
            Depth: 0
        );

        Assert.True(await approver.ApproveAsync(request, Ct));
        Assert.True(
            await approver.PromptAsync(
                request with
                {
                    EscalationRequested = true,
                    DecisionRecommendation = "deny",
                    DecisionProbability = 0.87d,
                    DecisionModelId = "decision-model",
                    DecisionMethod = "adapter",
                },
                Ct
            )
        );

        RequestPermissionRequest[] prompts = runtime.State.PermissionRequests.ToArray();
        Assert.Equal(2, prompts.Length);
        Assert.Contains(
            "decision model recommends deny (0.87, decision-model)",
            prompts[1].ToolCall.Title,
            StringComparison.Ordinal
        );
    }

    private static RequestPermissionResponse Answer(
        RequestPermissionRequest request,
        PermissionOptionKind kind
    ) =>
        new()
        {
            Outcome = new SelectedPermissionOutcome
            {
                OptionId = request.Options.First(option => option.Kind == kind).OptionId,
            },
        };
}
