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

    [Fact]
    public async Task Escalation_does_not_reopen_a_remembered_reject_always()
    {
        await using RawAcpRuntime runtime = AcpTestSupport.StartRaw();
        int asked = 0;
        runtime.State.PermissionHandler = (request, _) =>
            Task.FromResult(
                Answer(
                    request,
                    Interlocked.Increment(ref asked) == 1
                        ? PermissionOptionKind.RejectAlways
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

        // The user is asked once in the ordinary approval flow and answers reject-always;
        // the session-wide rejection is remembered for the tool.
        Assert.False(await approver.ApproveAsync(request, Ct));

        // A decision deny escalates the call, and the remembered rejection stands: the
        // escalation declines without asking the editor again.
        Assert.False(
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
        Assert.Single(prompts);
    }

    [Fact]
    public async Task Escalation_still_asks_after_a_remembered_reject_once()
    {
        await using RawAcpRuntime runtime = AcpTestSupport.StartRaw();
        int asked = 0;
        runtime.State.PermissionHandler = (request, _) =>
            Task.FromResult(
                Answer(
                    request,
                    Interlocked.Increment(ref asked) == 1
                        ? PermissionOptionKind.RejectOnce
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

        // A reject-once answers the one ask and is not remembered as a standing denial,
        // so the ordinary ask declines too.
        Assert.False(await approver.ApproveAsync(request, Ct));

        // The escalation is an ask the envelope does not deny outright: it prompts again
        // and the editor's answer decides.
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
