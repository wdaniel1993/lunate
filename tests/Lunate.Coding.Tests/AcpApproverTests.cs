using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class AcpApproverTests
{
    [Theory]
    [InlineData(ApprovalPolicy.Ask, ToolRisk.ReadOnly, true, 0)]
    [InlineData(ApprovalPolicy.Ask, ToolRisk.Write, true, 1)]
    [InlineData(ApprovalPolicy.Ask, ToolRisk.Execute, true, 1)]
    [InlineData(ApprovalPolicy.AutoEdit, ToolRisk.ReadOnly, true, 0)]
    [InlineData(ApprovalPolicy.AutoEdit, ToolRisk.Write, true, 0)]
    [InlineData(ApprovalPolicy.AutoEdit, ToolRisk.Execute, true, 1)]
    public async Task The_policy_runs_first_and_everything_else_asks_the_client(
        ApprovalPolicy policy,
        ToolRisk risk,
        bool expected,
        int expectedClientCalls
    )
    {
        var client = new CountingApprover(result: true);
        var approver = new AcpApprover(policy, client);

        bool approved = await approver.ApproveAsync(
            new RiskTool(risk),
            default,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expected, approved);
        Assert.Equal(expectedClientCalls, client.Calls);
    }

    [Fact]
    public async Task A_client_decline_reaches_the_caller()
    {
        var approver = new AcpApprover(ApprovalPolicy.Ask, new CountingApprover(result: false));

        bool approved = await approver.ApproveAsync(
            new RiskTool(ToolRisk.Write),
            default,
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
    }

    private sealed class CountingApprover(bool result) : IToolApprover
    {
        public int Calls { get; private set; }

        public ValueTask<bool> ApproveAsync(ITool tool, JsonElement args, CancellationToken ct)
        {
            Calls++;
            return ValueTask.FromResult(result);
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
