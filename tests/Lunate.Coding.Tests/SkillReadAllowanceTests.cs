using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// The read tool's per-run skill allowance (ADR-0016 boundary): discovered skill directories,
/// bundled files included, resolve read-only even outside the workspace; runs compute their own
/// allowance.
/// </summary>
public sealed class SkillReadAllowanceTests
{
    private static Task<ToolResult> Read(ReadTool tool, string path) =>
        tool.ExecuteAsync(
            JsonDocument.Parse($$"""{"path": {{JsonSerializer.Serialize(path)}}}""").RootElement,
            new ToolContext("unused", new NullAgentEvents()),
            TestContext.Current.CancellationToken
        );

    [Fact]
    public async Task A_user_scoped_skill_and_its_bundled_file_read_with_the_allowance()
    {
        using var temp = new TempDirectory();
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(worktree);
        string skill = Path.Combine(temp.Root, "home", ".agents", "skills", "greek");
        Directory.CreateDirectory(Path.Combine(skill, "references"));
        File.WriteAllText(Path.Combine(skill, "SKILL.md"), "skill body");
        File.WriteAllText(Path.Combine(skill, "references", "long.md"), "bundled content");
        var workspace = new Workspace(worktree);

        var without = new ReadTool(workspace);
        ToolResult refused = await Read(without, Path.Combine(skill, "references", "long.md"));
        Assert.True(refused.IsError);
        Assert.Contains("outside the workspace", refused.Output, StringComparison.Ordinal);

        var allowed = new ReadTool(
            workspace,
            files: null,
            allowance: new SkillReadAllowance([skill])
        );
        ToolResult skillFile = await Read(allowed, Path.Combine(skill, "SKILL.md"));
        Assert.False(skillFile.IsError);
        Assert.Contains("skill body", skillFile.Output, StringComparison.Ordinal);
        ToolResult bundled = await Read(allowed, Path.Combine(skill, "references", "long.md"));
        Assert.False(bundled.IsError);
        Assert.Contains("bundled content", bundled.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_later_allowance_update_is_seen_in_place()
    {
        using var temp = new TempDirectory();
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(worktree);
        string skill = Path.Combine(temp.Root, "skill");
        Directory.CreateDirectory(skill);
        File.WriteAllText(Path.Combine(skill, "SKILL.md"), "body");
        var allowance = new SkillReadAllowance();
        var tool = new ReadTool(new Workspace(worktree), files: null, allowance);

        Assert.True((await Read(tool, Path.Combine(skill, "SKILL.md"))).IsError);

        allowance.Set([skill]);
        Assert.False((await Read(tool, Path.Combine(skill, "SKILL.md"))).IsError);
    }

    [Fact]
    public async Task A_child_run_in_another_worktree_resolves_its_own_allowance()
    {
        using var temp = new TempDirectory();
        string worktree = Path.Combine(temp.Root, "worktree");
        string childWorktree = Path.Combine(temp.Root, "child-worktree");
        Directory.CreateDirectory(worktree);
        Directory.CreateDirectory(childWorktree);
        string userSkill = Path.Combine(temp.Root, "home", ".agents", "skills", "greek");
        Directory.CreateDirectory(userSkill);
        File.WriteAllText(Path.Combine(userSkill, "SKILL.md"), "user skill");
        string childSkill = Path.Combine(childWorktree, ".agents", "skills", "local");
        Directory.CreateDirectory(childSkill);
        File.WriteAllText(Path.Combine(childSkill, "SKILL.md"), "child skill");
        string home = Path.Combine(temp.Root, "home");

        SkillCatalog catalog = SkillCatalog.Discover(
            new SkillScanRequest(worktree, null, home, [])
        );

        // The child worktree's own discovery adds its project skill directory; the user scope
        // stays resolvable.
        IReadOnlyList<string> roots = catalog.ReadRootsFor(childWorktree);
        Assert.Contains(Workspace.Canonicalize(userSkill), roots.Select(Workspace.Canonicalize));
        Assert.Contains(Workspace.Canonicalize(childSkill), roots.Select(Workspace.Canonicalize));

        Func<string, ToolRegistry> factory = HarnessFactory.CreateWorkspaceTools(
            new WorkspaceServices(1),
            [],
            root => catalog.ReadRootsFor(root)
        );
        ReadTool childRead = Assert.IsType<ReadTool>(
            factory(Workspace.Canonicalize(childWorktree)).Find("read")
        );
        ToolResult skill = await Read(childRead, Path.Combine(userSkill, "SKILL.md"));
        Assert.False(skill.IsError);
        ToolResult local = await Read(childRead, Path.Combine(childSkill, "SKILL.md"));
        Assert.False(local.IsError);
    }
}
