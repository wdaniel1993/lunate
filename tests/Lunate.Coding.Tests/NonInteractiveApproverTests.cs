using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class NonInteractiveApproverTests
{
    [Theory]
    [InlineData(ApprovalPolicy.Ask, ToolRisk.ReadOnly, true)]
    [InlineData(ApprovalPolicy.Ask, ToolRisk.Write, false)]
    [InlineData(ApprovalPolicy.Ask, ToolRisk.Execute, false)]
    [InlineData(ApprovalPolicy.AutoEdit, ToolRisk.ReadOnly, true)]
    [InlineData(ApprovalPolicy.AutoEdit, ToolRisk.Write, true)]
    [InlineData(ApprovalPolicy.AutoEdit, ToolRisk.Execute, false)]
    public async Task The_policy_maps_onto_tool_risk(
        ApprovalPolicy policy,
        ToolRisk risk,
        bool expected
    )
    {
        var approver = new NonInteractiveApprover(policy, yolo: false);

        bool approved = await approver.ApproveAsync(
            new RiskTool(risk),
            default,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expected, approved);
    }

    [Theory]
    [InlineData(ApprovalPolicy.Ask)]
    [InlineData(ApprovalPolicy.AutoEdit)]
    public async Task Yolo_allows_every_risk(ApprovalPolicy policy)
    {
        var approver = new NonInteractiveApprover(policy, yolo: true);

        foreach (ToolRisk risk in Enum.GetValues<ToolRisk>())
        {
            Assert.True(
                await approver.ApproveAsync(
                    new RiskTool(risk),
                    default,
                    TestContext.Current.CancellationToken
                )
            );
        }
    }

    private sealed class RiskTool(ToolRisk risk) : ITool
    {
        public string Name => "risk";

        public string Description => "A tool with a fixed risk for approver tests.";

        public JsonElement ParametersSchema => default;

        public ToolRisk Risk => risk;

        public Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        ) => Task.FromResult(new ToolResult("ok", IsError: false));
    }
}
