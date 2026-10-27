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
            Request(new RiskTool(risk), "{}"),
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
                    Request(new RiskTool(risk), "{}"),
                    TestContext.Current.CancellationToken
                )
            );
        }
    }

    /// <summary>
    /// No policy can allow a command: print mode has no panel, so a policy that allowed Execute
    /// would run a model-chosen process silently. Only the explicit --yolo flag does that.
    /// </summary>
    [Fact]
    public void No_policy_allows_execute()
    {
        foreach (ApprovalPolicy policy in Enum.GetValues<ApprovalPolicy>())
        {
            Assert.False(NonInteractiveApprover.IsAllowed(policy, ToolRisk.Execute));
        }

        Assert.False(NonInteractiveApprover.IsAllowed((ApprovalPolicy)99, ToolRisk.Execute));
        Assert.False(NonInteractiveApprover.IsAllowed((ApprovalPolicy)99, ToolRisk.ReadOnly));
    }

    [Theory]
    [InlineData(ApprovalPolicy.Ask)]
    [InlineData(ApprovalPolicy.AutoEdit)]
    public async Task A_state_changing_lunate_command_is_declined_even_under_auto_edit(
        ApprovalPolicy policy
    )
    {
        var approver = new NonInteractiveApprover(policy, yolo: false);

        bool approved = await approver.ApproveAsync(
            Request(
                new RiskTool(ToolRisk.Execute),
                """{"command":"lunate trust --yes --digest abc"}"""
            ),
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
    }

    [Fact]
    public async Task An_ordinary_write_still_runs_under_auto_edit()
    {
        var approver = new NonInteractiveApprover(ApprovalPolicy.AutoEdit, yolo: false);

        bool approved = await approver.ApproveAsync(
            Request(new RiskTool(ToolRisk.Write), """{"path":"src/app.cs","content":"x"}"""),
            TestContext.Current.CancellationToken
        );

        Assert.True(approved);
    }

    private static ApprovalRequest Request(ITool tool, string args) =>
        new(tool, JsonDocument.Parse(args).RootElement.Clone(), "run_1", null, 0);

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
