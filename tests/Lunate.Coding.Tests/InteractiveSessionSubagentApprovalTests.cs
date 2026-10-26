using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>The interactive approver: a child's ask is labelled, and "always" covers the whole run tree.</summary>
public sealed class InteractiveSessionSubagentApprovalTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_childs_ask_is_labelled_and_always_covers_the_whole_tree()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        IToolApprover approver = host.Session.Approver!;
        ITool tool = new WriteRiskTool();
        ApprovalRequest child = new(
            tool,
            JsonDocument.Parse("{}").RootElement.Clone(),
            RunId: "run_child",
            ParentRunId: "run_parent",
            Depth: 1
        );

        ValueTask<bool> decision = approver.ApproveAsync(child, Ct);
        await host.WaitForApprovalAsync();
        Assert.Equal("subagent (depth 1)", host.Session.PendingApproval!.Asker);
        host.Console.SendText("a");

        Assert.True(await decision);
        Assert.Null(host.Session.PendingApproval);

        ApprovalRequest parent = child with { RunId = "run_parent", ParentRunId = null, Depth = 0 };
        ApprovalRequest sibling = child with { RunId = "run_sibling" };
        Assert.True(await approver.ApproveAsync(parent, Ct));
        Assert.True(await approver.ApproveAsync(sibling, Ct));
        Assert.Null(host.Session.PendingApproval);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_child_in_another_root_has_a_workspace_root_line()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        IToolApprover approver = host.Session.Approver!;
        string childRoot = Path.Combine(host.Temp.Root, "worktree-b");
        Directory.CreateDirectory(childRoot);
        ApprovalRequest child = new(
            new WriteRiskTool(),
            JsonDocument.Parse("{}").RootElement.Clone(),
            RunId: "run_child",
            ParentRunId: "run_parent",
            Depth: 1,
            WorkspaceRoot: childRoot
        );

        ValueTask<bool> decision = approver.ApproveAsync(child, Ct);
        await host.WaitForApprovalAsync();
        // The model carries the request's root verbatim; the renderer abbreviates home paths.
        Assert.Equal(childRoot, host.Session.PendingApproval!.WorkspaceRoot);
        Assert.Equal("subagent (depth 1)", host.Session.PendingApproval.Asker);
        host.Console.SendText("n");

        Assert.False(await decision);
        Assert.Null(host.Session.PendingApproval);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_child_in_the_sessions_own_root_has_no_workspace_root_line()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        IToolApprover approver = host.Session.Approver!;
        // The session canonicalizes its working directory ("/var" vs "/private/var" on macOS),
        // so the request must carry the canonical root to look like a child's own worktree.
        string ownRoot = new Workspace(host.Temp.Root).WorktreeRoot;
        ApprovalRequest child = new(
            new WriteRiskTool(),
            JsonDocument.Parse("{}").RootElement.Clone(),
            RunId: "run_child",
            ParentRunId: "run_parent",
            Depth: 1,
            WorkspaceRoot: ownRoot
        );

        ValueTask<bool> decision = approver.ApproveAsync(child, Ct);
        await host.WaitForApprovalAsync();
        Assert.Null(host.Session.PendingApproval!.WorkspaceRoot);
        host.Console.SendText("n");

        Assert.False(await decision);
        Assert.Null(host.Session.PendingApproval);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_top_level_ask_in_another_root_has_a_workspace_root_line()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        IToolApprover approver = host.Session.Approver!;
        string childRoot = Path.Combine(host.Temp.Root, "worktree-b");
        Directory.CreateDirectory(childRoot);
        ApprovalRequest topLevel = new(
            new WriteRiskTool(),
            JsonDocument.Parse("{}").RootElement.Clone(),
            RunId: "run_top",
            ParentRunId: null,
            Depth: 0,
            WorkspaceRoot: childRoot
        );

        ValueTask<bool> decision = approver.ApproveAsync(topLevel, Ct);
        await host.WaitForApprovalAsync();
        Assert.Equal(childRoot, host.Session.PendingApproval!.WorkspaceRoot);
        Assert.Null(host.Session.PendingApproval.Asker);
        host.Console.SendText("n");

        Assert.False(await decision);
        Assert.Null(host.Session.PendingApproval);

        host.Console.Complete();
        await run;
    }

    private sealed class WriteRiskTool : ITool
    {
        public string Name => "write-risk";

        public string Description => "A write-risk tool for approval tests.";

        public JsonElement ParametersSchema => default;

        public ToolRisk Risk => ToolRisk.Write;

        public Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        ) => Task.FromResult(new ToolResult("ok", IsError: false));
    }
}
