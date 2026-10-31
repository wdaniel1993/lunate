using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// Tests touching the process-wide MSBuild locator and real workspaces run one at a time, like the
/// Roslyn integration suite.
/// </summary>
[CollectionDefinition("roslyn-workspaces", DisableParallelization = true)]
public sealed class RoslynWorkspaceCollection;

/// <summary>
/// The per-workspace Roslyn wiring end to end: the composed <c>cs_*</c> tools load one workspace
/// per root through the registry, the cap evicts the least-recently-used root, and a fresh worktree
/// without restore output gets the ADR-0006 restore hint.
/// </summary>
[Collection("roslyn-workspaces")]
public sealed class RoslynWorkspaceTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_cap_evicts_the_lru_root_and_a_fresh_worktree_gets_the_restore_hint()
    {
        using var restored = RoslynFixtureSupport.CopySolution("console-app");
        RoslynFixtureSupport.Restore(restored, "ConsoleApp.slnx");
        using var second = RoslynFixtureSupport.CopyFixture(restored);
        using var fresh = RoslynFixtureSupport.CopySolution("console-app");
        using var services = new WorkspaceServices(2);
        var events = new NullAgentEvents();

        ToolResult firstLoad = await RunDiagnosticsAsync(restored.Root, services, events);
        Assert.False(firstLoad.IsError, firstLoad.Output);
        ToolResult secondLoad = await RunDiagnosticsAsync(second.Root, services, events);
        Assert.False(secondLoad.IsError, secondLoad.Output);
        Assert.Empty(services.Evictions);
        Assert.Equal(2, services.LoadedWorkspaces.Count);

        ToolResult freshLoad = await RunDiagnosticsAsync(fresh.Root, services, events);

        Assert.True(freshLoad.IsError);
        Assert.Contains("dotnet restore", freshLoad.Output, StringComparison.Ordinal);
        Assert.Equal([GitIdentity.Canonicalize(restored.Root)], services.Evictions);
        Assert.DoesNotContain(GitIdentity.Canonicalize(restored.Root), services.LoadedWorkspaces);
        Assert.Contains(GitIdentity.Canonicalize(second.Root), services.LoadedWorkspaces);
        Assert.Contains(GitIdentity.Canonicalize(fresh.Root), services.LoadedWorkspaces);
    }

    private static async Task<ToolResult> RunDiagnosticsAsync(
        string root,
        WorkspaceServices services,
        IAgentEvents events
    )
    {
        ToolRegistry tools = RoslynTestHarness.CreateTools(new Workspace(root), services: services);
        ITool diagnostics = tools.Find("cs_diagnostics")!;
        JsonElement args = JsonSerializer.SerializeToElement(new { scope = "solution" });
        return await diagnostics
            .ExecuteAsync(args, new ToolContext(root, events), Ct)
            .WaitAsync(WaitBudgets.Diagnostics, Ct);
    }

    private sealed class NullAgentEvents : IAgentEvents
    {
        public void Emit(AgentEvent agentEvent) { }
    }
}
