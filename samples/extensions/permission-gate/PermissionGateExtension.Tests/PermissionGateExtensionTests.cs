using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Testing;

namespace PermissionGateExtension.Tests;

public sealed class PermissionGateExtensionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Prompt = "go";

    [Fact]
    public async Task Block_mode_blocks_a_destructive_call_before_approval()
    {
        using var temp = new TempDirectory();
        var approver = new ScriptedApprover();
        GateRun run = await RunAsync(temp, "permission-gate-destructive-call.jsonl", approver);

        ToolCallResult result = Assert.Single(run.Events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains(
            "permission-gate: destructive tool 'wipe' blocked - set mode=confirm to approve interactively",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Equal(0, run.Destructive.Executions);
        Assert.Equal(0, approver.Calls);
        Assert.Contains(run.Logs, message => message.Contains("blocked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Block_mode_lets_a_read_only_call_proceed_through_approval()
    {
        using var temp = new TempDirectory();
        var approver = new ScriptedApprover(true);
        GateRun run = await RunAsync(temp, "permission-gate-read-only-call.jsonl", approver);

        ToolCallResult result = Assert.Single(run.Events.OfType<ToolCallResult>());
        Assert.False(result.IsError);
        Assert.Equal(1, run.ReadOnly.Executions);
        Assert.Equal(1, approver.Calls);
        Assert.Equal(1, approver.Approvals);
    }

    [Fact]
    public async Task Confirm_mode_falls_through_to_the_approver()
    {
        using var deniedTemp = new TempDirectory();
        var deny = new ScriptedApprover(false);
        GateRun denied = await RunAsync(
            deniedTemp,
            "permission-gate-destructive-call.jsonl",
            deny,
            mode: "confirm"
        );

        ToolCallResult deniedResult = Assert.Single(denied.Events.OfType<ToolCallResult>());
        Assert.True(deniedResult.IsError);
        Assert.Contains("was not run", deniedResult.Output, StringComparison.Ordinal);
        Assert.Equal(0, denied.Destructive.Executions);
        Assert.Equal(1, deny.Calls);
        Assert.Equal(1, deny.Denials);

        using var allowedTemp = new TempDirectory();
        var allow = new ScriptedApprover(true);
        GateRun allowed = await RunAsync(
            allowedTemp,
            "permission-gate-destructive-call.jsonl",
            allow,
            mode: "confirm"
        );

        ToolCallResult allowedResult = Assert.Single(allowed.Events.OfType<ToolCallResult>());
        Assert.False(allowedResult.IsError);
        Assert.Equal(1, allowed.Destructive.Executions);
        Assert.Equal(1, allow.Calls);
        Assert.Equal(1, allow.Approvals);
    }

    [Fact]
    public async Task The_persisted_setting_drives_the_mode()
    {
        using var blockTemp = new TempDirectory();
        var blockApprover = new ScriptedApprover();
        GateRun blocked = await RunAsync(
            blockTemp,
            "permission-gate-destructive-call.jsonl",
            blockApprover
        );

        Assert.Equal(0, blockApprover.Calls);
        Assert.Equal(0, blocked.Destructive.Executions);
        Assert.Contains(
            "blocked",
            Assert.Single(blocked.Events.OfType<ToolCallResult>()).Output,
            StringComparison.Ordinal
        );

        using var confirmTemp = new TempDirectory();
        var confirmApprover = new ScriptedApprover(false);
        GateRun confirmed = await RunAsync(
            confirmTemp,
            "permission-gate-destructive-call.jsonl",
            confirmApprover,
            mode: "confirm"
        );

        Assert.Equal(1, confirmApprover.Calls);
        Assert.Equal(0, confirmed.Destructive.Executions);
        Assert.Contains(
            "was not run",
            Assert.Single(confirmed.Events.OfType<ToolCallResult>()).Output,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Calls_without_annotations_proceed_in_both_modes()
    {
        using var blockTemp = new TempDirectory();
        var blockApprover = new ScriptedApprover(true);
        GateRun blockMode = await RunAsync(
            blockTemp,
            "permission-gate-unannotated-call.jsonl",
            blockApprover
        );

        Assert.Equal(1, blockApprover.Calls);
        Assert.Equal(1, blockMode.Unannotated.Executions);

        using var confirmTemp = new TempDirectory();
        var confirmApprover = new ScriptedApprover(true);
        GateRun confirmMode = await RunAsync(
            confirmTemp,
            "permission-gate-unannotated-call.jsonl",
            confirmApprover,
            mode: "confirm"
        );

        Assert.Equal(1, confirmApprover.Calls);
        Assert.Equal(1, confirmMode.Unannotated.Executions);
    }

    private static async Task<GateRun> RunAsync(
        TempDirectory temp,
        string fixture,
        IToolApprover approver,
        string? mode = null
    )
    {
        await using var host = new ExtensionTestHost(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "permission-gate",
                ExtensionDirectory = Path.Combine(
                    AppContext.BaseDirectory,
                    "PermissionGateExtension"
                ),
                Approver = approver,
                MaxSteps = 1,
            }
        );
        if (mode is not null)
        {
            WriteMode(host.StorePath, mode);
        }

        var destructive = new GateTool("wipe", new ToolAnnotations(Destructive: true));
        var readOnly = new GateTool("peek", new ToolAnnotations(ReadOnly: true));
        var unannotated = new GateTool("note", null);
        host.Tools.Add(destructive);
        host.Tools.Add(readOnly);
        host.Tools.Add(unannotated);

        ExtensionRunResult result = await host.RunAsync(
            new ReplayChatClient(FixturePath(fixture)),
            Prompt,
            Ct
        );
        await host.StopAsync(Ct);
        return new GateRun(
            result.Events,
            destructive,
            readOnly,
            unannotated,
            [.. host.Log.Messages]
        );
    }

    private static void WriteMode(string storePath, string mode)
    {
        string settings = Path.Combine(storePath, "extensions-settings");
        Directory.CreateDirectory(settings);
        File.WriteAllText(
            Path.Combine(settings, "permission-gate.json"),
            $$"""{"mode":"{{mode}}"}"""
        );
    }

    private static string FixturePath(string name) =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "fixtures", "streams", name);

    private sealed record GateRun(
        IReadOnlyList<AgentEvent> Events,
        GateTool Destructive,
        GateTool ReadOnly,
        GateTool Unannotated,
        IReadOnlyList<string> Logs
    );
}
