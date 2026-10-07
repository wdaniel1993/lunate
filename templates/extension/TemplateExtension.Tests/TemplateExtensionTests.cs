using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Testing;

namespace TemplateExtension.Tests;

public sealed class TemplateExtensionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Template_loads_and_replays_a_tool_call_through_the_real_loader()
    {
        using var temp = new TempDirectory();
        var approver = new ScriptedApprover(true);
        await using var host = new ExtensionTestHost(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "template",
                ExtensionDirectory = Path.Combine(AppContext.BaseDirectory, "TemplateExtension"),
                Approver = approver,
            }
        );
        host.Tools.Add(new EchoTool());

        ExtensionRunResult result = await host.RunAsync(
            new ReplayChatClient(FixturePath()),
            "call the echo tool",
            Ct
        );
        await host.StopAsync(Ct);

        Assert.Contains(
            host.Log.Entries,
            entry =>
                entry.Message.Contains("template", StringComparison.Ordinal)
                && entry.Message.Contains("tool-calling", StringComparison.Ordinal)
        );
        Assert.Single(
            host.Log.Entries,
            entry => entry.Message.Contains("service started", StringComparison.Ordinal)
        );
        Assert.Single(
            host.Log.Entries,
            entry => entry.Message.Contains("service stopped", StringComparison.Ordinal)
        );
        Assert.Equal(1, approver.Calls);
        Assert.Equal(1, approver.Approvals);
        Assert.Contains(result.Events.OfType<ToolCallResult>(), toolResult => !toolResult.IsError);
        Assert.Contains(
            result.Events.OfType<RunFinished>(),
            finished => finished.StopReason == StopReasons.Stop
        );
    }

    private static string FixturePath() =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "fixtures",
            "streams",
            "extension-template-tool-call.jsonl"
        );
}
