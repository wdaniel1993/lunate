using System.Text.Json;

namespace Lunate.Extensibility.Testing.Tests;

public sealed class ScriptedApproverTests
{
    private static readonly JsonElement Args = JsonDocument.Parse("{}").RootElement.Clone();

    [Fact]
    public async Task Queued_allow_and_deny_decisions_are_returned_in_order_and_counted()
    {
        var approver = new ScriptedApprover(true, false);

        Assert.True(
            await approver.ApproveAsync(new TestTool("first", "ok"), Args, CancellationToken.None)
        );
        Assert.False(
            await approver.ApproveAsync(new TestTool("second", "ok"), Args, CancellationToken.None)
        );

        Assert.Equal(2, approver.Calls);
        Assert.Equal(1, approver.Approvals);
        Assert.Equal(1, approver.Denials);
    }

    [Fact]
    public async Task Exhaustion_throws_naming_the_tool_and_never_allows_implicitly()
    {
        var approver = new ScriptedApprover(true);
        _ = await approver.ApproveAsync(new TestTool("first", "ok"), Args, CancellationToken.None);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await approver.ApproveAsync(
                    new TestTool("second", "ok"),
                    Args,
                    CancellationToken.None
                )
        );

        Assert.Contains("second", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no decision", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, approver.Calls);
        Assert.Equal(1, approver.Approvals);
        Assert.Equal(0, approver.Denials);
    }
}
