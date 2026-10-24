using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>
/// Task 3 of the branching-sessions change at the harness seam: one summary call on the session's
/// model with the fixed prompt, its usage reported back, and a confirmation line that names the
/// estimate and the one call.
/// </summary>
public sealed class BranchSummaryTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_summary_call_is_one_request_with_the_fixed_prompt()
    {
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("the branch built the parser"),
            LoopScripts.Usage(120, 8)
        );
        AgentHarness harness = new(client, new ToolRegistry(), new AgentHarnessOptions());

        BranchSummaryResult? result = await harness.SummarizeBranchAsync(
            [new ChatMessage(ChatRole.User, "hello from the branch")],
            Ct
        );

        Assert.NotNull(result);
        Assert.Equal("the branch built the parser", result.Summary);
        Assert.Equal(120, result.Usage?.InputTokenCount);
        Assert.Equal(8, result.Usage?.OutputTokenCount);

        ScriptedRequest request = Assert.Single(client.Requests);
        Assert.Equal(BranchSummary.Prompt, request.Messages[0].Text);
        Assert.Equal(ChatRole.System, request.Messages[0].Role);
        Assert.Contains(
            "hello from the branch",
            request.Messages[1].Text,
            StringComparison.Ordinal
        );
        Assert.Null(request.Options!.Tools);
    }

    [Fact]
    public async Task An_empty_summary_reports_nothing_so_the_switch_can_leave_the_session_untouched()
    {
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Stop());
        AgentHarness harness = new(client, new ToolRegistry(), new AgentHarnessOptions());

        Assert.Null(await harness.SummarizeBranchAsync([new ChatMessage(ChatRole.User, "x")], Ct));
    }

    [Fact]
    public void The_estimate_is_the_bytes_over_four_rule()
    {
        List<ChatMessage> transcript = [new ChatMessage(ChatRole.User, new string('x', 4000))];

        int estimate = BranchSummary.EstimateTokens(transcript);

        Assert.InRange(estimate, 900, 1100);
    }

    [Fact]
    public void The_confirmation_names_the_estimate_the_model_and_the_one_call()
    {
        string line = BranchSummary.Confirmation("e_02", 1234, "gpt-5.1", "<$0.0001");

        Assert.Contains("e_02", line, StringComparison.Ordinal);
        Assert.Contains("1234", line, StringComparison.Ordinal);
        Assert.Contains("gpt-5.1", line, StringComparison.Ordinal);
        Assert.Contains("<$0.0001", line, StringComparison.Ordinal);
        Assert.Contains("one model call will be made", line, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unpriced_model_says_so_instead_of_inventing_a_cost()
    {
        string line = BranchSummary.Confirmation("e_02", 1234, "local-model", null);

        Assert.Contains("local-model", line, StringComparison.Ordinal);
        Assert.DoesNotContain("cost", line, StringComparison.Ordinal);
    }
}
