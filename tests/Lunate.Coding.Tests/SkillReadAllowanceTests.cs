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
        WriteSkill(skill, name: "greek");
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
        Assert.Contains("Body of greek.", skillFile.Output, StringComparison.Ordinal);
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
        WriteSkill(skill, name: "skill");
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
        WriteSkill(userSkill, name: "greek");
        string childSkill = Path.Combine(childWorktree, ".agents", "skills", "local");
        WriteSkill(childSkill, name: "local");
        string home = Path.Combine(temp.Root, "home");

        SkillCatalog catalog = SkillCatalog.Discover(
            new SkillScanRequest(worktree, null, home, [])
        );

        // The child worktree's own discovery adds its project skill directory; the user scope
        // stays resolvable.
        IReadOnlyList<string> roots = catalog.ReadRootsFor(childWorktree);
        Assert.Contains(Workspace.Canonicalize(userSkill), roots.Select(Workspace.Canonicalize));
        Assert.Contains(Workspace.Canonicalize(childSkill), roots.Select(Workspace.Canonicalize));

        Func<string, ToolRegistry> factory = RoslynTestHarness.CreateWorkspaceTools(
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

    [Fact]
    public async Task An_invalid_skill_directory_is_not_a_read_root()
    {
        using var temp = new TempDirectory();
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(worktree);
        string broken = Path.Combine(temp.Root, "home", ".agents", "skills", "broken");
        // The name disagrees with the directory: discovery rejects the skill, so its directory must
        // not be an allowance root either.
        WriteSkill(broken, name: "other");
        var catalog = SkillCatalog.Discover(
            new SkillScanRequest(worktree, null, Path.Combine(temp.Root, "home"), [])
        );
        catalog.SetProjectTrusted(true);

        var tool = new ReadTool(
            new Workspace(worktree),
            files: null,
            allowance: new SkillReadAllowance(catalog.ReadRoots)
        );

        ToolResult refused = await Read(tool, Path.Combine(broken, "SKILL.md"));
        Assert.True(refused.IsError);
        Assert.Contains("outside the workspace", refused.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_untrusted_project_skill_directory_is_not_a_read_root_before_approval()
    {
        using var temp = new TempDirectory();
        string repo = Path.Combine(temp.Root, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string worktree = Path.Combine(repo, "src", "inner");
        Directory.CreateDirectory(worktree);
        // An ancestor level's skill: outside the session's worktree, so only the allowance can
        // license a read of it.
        string skill = Path.Combine(repo, ".agents", "skills", "greek");
        WriteSkill(skill, name: "greek");
        string home = Path.Combine(temp.Root, "home");
        var catalog = SkillCatalog.Discover(new SkillScanRequest(worktree, repo, home, []));

        catalog.SetProjectTrusted(false);
        var allowance = new SkillReadAllowance(catalog.ReadRoots);
        var tool = new ReadTool(new Workspace(worktree), files: null, allowance);

        Assert.DoesNotContain(Workspace.Canonicalize(skill), allowance.Roots);
        Assert.DoesNotContain(
            Workspace.Canonicalize(skill),
            catalog.ReadRootsFor(worktree).Select(Workspace.Canonicalize)
        );
        ToolResult refused = await Read(tool, Path.Combine(skill, "SKILL.md"));
        Assert.True(refused.IsError);
        Assert.Contains("outside the workspace", refused.Output, StringComparison.Ordinal);

        // Approval is what makes it readable.
        catalog.SetProjectTrusted(true);
        allowance.Set(catalog.ReadRoots);
        ToolResult allowed = await Read(tool, Path.Combine(skill, "SKILL.md"));
        Assert.False(allowed.IsError);
        Assert.Contains("Body of greek.", allowed.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_run_in_another_worktree_keeps_the_sessions_untrusted_project_skills_out()
    {
        using var temp = new TempDirectory();
        string repo = Path.Combine(temp.Root, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string worktree = Path.Combine(repo, "main-tree");
        string childWorktree = Path.Combine(repo, "child-tree");
        Directory.CreateDirectory(worktree);
        Directory.CreateDirectory(childWorktree);
        string skill = Path.Combine(worktree, ".agents", "skills", "greek");
        WriteSkill(skill, name: "greek");
        string home = Path.Combine(temp.Root, "home");
        var catalog = SkillCatalog.Discover(new SkillScanRequest(worktree, repo, home, []));
        catalog.SetProjectTrusted(false);

        IReadOnlyList<string> roots = catalog.ReadRootsFor(childWorktree);

        Assert.DoesNotContain(Workspace.Canonicalize(skill), roots.Select(Workspace.Canonicalize));
    }

    private static void WriteSkill(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: {name}\ndescription: D\n---\n\nBody of {name}.\n"
        );
    }
}
