using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class AcpApproverTests
{
    /// <summary>
    /// Only a read-only call runs without the editor's permission request. A write is a change to
    /// the user's files, and an editor's request is never auto-approved (docs/guide.md, "Security
    /// and safety"), whatever the configured policy allows in print mode.
    /// </summary>
    [Theory]
    [InlineData(ApprovalPolicy.Ask, ToolRisk.ReadOnly, true, 0)]
    [InlineData(ApprovalPolicy.Ask, ToolRisk.Write, true, 1)]
    [InlineData(ApprovalPolicy.Ask, ToolRisk.Execute, true, 1)]
    [InlineData(ApprovalPolicy.AutoEdit, ToolRisk.ReadOnly, true, 0)]
    [InlineData(ApprovalPolicy.AutoEdit, ToolRisk.Write, true, 1)]
    [InlineData(ApprovalPolicy.AutoEdit, ToolRisk.Execute, true, 1)]
    public async Task Only_a_read_only_call_bypasses_the_client(
        ApprovalPolicy policy,
        ToolRisk risk,
        bool expected,
        int expectedClientCalls
    )
    {
        var client = new CountingApprover(result: true);
        var approver = new AcpApprover(policy, client);

        bool approved = await approver.ApproveAsync(
            Request(new RiskTool(risk)),
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
            Request(new RiskTool(ToolRisk.Write)),
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
    }

    private static ApprovalRequest Request(ITool tool) =>
        new(tool, JsonDocument.Parse("{}").RootElement.Clone(), "run_1", null, 0);

    private sealed class CountingApprover(bool result) : IToolApprover
    {
        public int Calls { get; private set; }

        public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
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
