using Lunate.Extensibility;

namespace Lunate.Extensibility.Testing.Tests;

public sealed class ScriptedTrustPromptTests
{
    [Fact]
    public async Task Queued_decisions_are_returned_in_order_and_counted()
    {
        var prompt = new ScriptedTrustPrompt(true, false);

        Assert.True(await prompt.ApproveAsync(Descriptor("first"), CancellationToken.None));
        Assert.False(await prompt.ApproveAsync(Descriptor("second"), CancellationToken.None));

        Assert.Equal(2, prompt.Calls);
        Assert.Equal(1, prompt.Approvals);
        Assert.Equal(1, prompt.Denials);
    }

    [Fact]
    public async Task Exhaustion_throws_and_never_approves_implicitly()
    {
        var prompt = new ScriptedTrustPrompt(true);
        _ = await prompt.ApproveAsync(Descriptor("first"), CancellationToken.None);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await prompt.ApproveAsync(Descriptor("second"), CancellationToken.None)
        );

        Assert.Contains("second", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no decision", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, prompt.Calls);
        Assert.Equal(1, prompt.Approvals);
        Assert.Equal(0, prompt.Denials);
    }

    private static ExtensionDescriptor Descriptor(string id) =>
        new(id, "0.1.0", ExtensionScope.Project, ".", Manifest(id));

    private static Lunate.Extensibility.Abstractions.ExtensionManifest Manifest(string id) =>
        new()
        {
            Id = id,
            Version = "0.1.0",
            ApiVersion = "^1.0.0",
            EntryAssembly = "Test.dll",
        };
}
