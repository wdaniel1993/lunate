using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The one shared composition helper (tasks 5.2): per run root it filters the extension tools down
/// to the active ones — a true rule or no rule stays, a false rule is dropped before the registry
/// ever sees it, an unreadable root drops without throwing, and order is preserved.
/// </summary>
public sealed class ExtensionToolActivationFilterTests
{
    [Fact]
    public void A_tool_with_no_rule_and_a_true_rule_stay_in_order_with_a_false_rule_dropped()
    {
        using var temp = new TempDirectory();
        var schema = JsonSerializer.SerializeToElement(
            new { type = "object", properties = Array.Empty<object>() }
        );
        ITool first = Adapter("first", HasFile("first.md"), schema);
        ITool inactive = Adapter("inactive", HasFile("*.sln"), schema);
        ITool unruled = Adapter("unruled", null, schema);
        File.WriteAllText(temp.File("first.md"), "marker");
        var workspace = new Workspace(temp.Root);

        IReadOnlyList<ITool> active = ExtensionToolActivationFilter.ActiveOnly(
            [first, inactive, unruled],
            workspace
        );

        Assert.Equal(["peak__first", "peak__unruled"], active.Select(tool => tool.Name));
    }

    [Fact]
    public void A_tool_with_a_true_rule_stays()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("marker.md"), "marker");
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        ITool tool = Adapter("on-mode", HasFile("marker.md"), schema);
        var workspace = new Workspace(temp.Root);

        Assert.Equal(
            ["peak__on-mode"],
            ExtensionToolActivationFilter.ActiveOnly([tool], workspace).Select(tool => tool.Name)
        );
    }

    [Fact]
    public void A_git_repository_rule_follows_the_workspace()
    {
        using var temp = new TempDirectory();
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        ITool tool = Adapter("git-mode", IsGitRepository(), schema);

        // The temp root carries no .git entry and does not sit inside one.
        string outsideRoot = temp.File("plain");
        Directory.CreateDirectory(outsideRoot);
        var outside = new Workspace(outsideRoot);
        File.WriteAllText(Path.Combine(outside.WorktreeRoot, "keep.txt"), "");
        Assert.Empty(ExtensionToolActivationFilter.ActiveOnly([tool], outside));

        // The same rule where the workspace resolves a .git directory: active.
        string repoRoot = temp.File("repo");
        Directory.CreateDirectory(repoRoot);
        string repo = GitIdentity.Canonicalize(repoRoot);
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var inside = new Workspace(repo);
        Assert.Equal(
            ["peak__git-mode"],
            ExtensionToolActivationFilter.ActiveOnly([tool], inside).Select(tool => tool.Name)
        );
    }

    [Fact]
    public void An_unreadable_root_drops_the_tool_without_throwing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        string locked = temp.File("locked");
        Directory.CreateDirectory(locked);
        UnixFileMode original = File.GetUnixFileMode(locked);
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            var schema = JsonSerializer.SerializeToElement(new { type = "object" });
            ITool tool = Adapter("offline-mode", HasFile("*.sln"), schema);
            var workspace = new Workspace(locked);

            Assert.Empty(ExtensionToolActivationFilter.ActiveOnly([tool], workspace));
        }
        finally
        {
            File.SetUnixFileMode(locked, original);
        }
    }

    [Fact]
    public void An_inactive_alias_reports_the_canonical_tool_identity()
    {
        using var temp = new TempDirectory();
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        ITool tool = Adapter("offline-mode", HasFile("*.sln"), schema, alias: "short");
        Assert.Equal("short", tool.Name);

        ExtensionInactiveTool report = Assert.Single(
            ExtensionToolActivationFilter.Split([tool], new Workspace(temp.Root)).Inactive
        );

        Assert.Equal("peak__offline-mode", report.Tool);
    }

    private static ExtensionToolAdapter Adapter(
        string name,
        ExtensionToolActivationRule? rule,
        JsonElement schema,
        string? alias = null
    )
    {
        var registry = new ExtensionToolRegistry();
        registry.Register(
            "peak",
            new ExtensionToolRegistration
            {
                Name = name,
                Description = "Does a thing.",
                ParametersSchema = schema,
                Handler = new TestToolHandler(),
            },
            rule,
            alias
        );
        return Assert.Single(registry.List());
    }

    private static ExtensionToolActivationRule HasFile(params string[] patterns) =>
        new(ExtensionToolActivationRule.WorkspaceRootHasFile, patterns);

    private static ExtensionToolActivationRule IsGitRepository() =>
        new(ExtensionToolActivationRule.WorkspaceIsGitRepository, []);

    [Fact]
    public void Split_reports_each_inactive_tool_with_its_condition_and_reason()
    {
        using var temp = new TempDirectory();
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        ITool first = Adapter("first", HasFile("first.md"), schema);
        ITool gitOnly = Adapter("git-only", IsGitRepository(), schema);
        ITool unruled = Adapter("unruled", null, schema);
        // The temp root carries neither 'first.md' nor a .git entry, so both rules are false.
        var workspace = new Workspace(temp.Root);

        (IReadOnlyList<ITool> active, IReadOnlyList<ExtensionInactiveTool> inactive) =
            ExtensionToolActivationFilter.Split([first, gitOnly, unruled], workspace);

        Assert.Equal(["peak__unruled"], active.Select(tool => tool.Name));
        ExtensionInactiveTool hasFile = Assert.Single(
            inactive,
            entry => entry.Tool == "peak__first"
        );
        Assert.Equal("peak", hasFile.ExtensionId);
        Assert.Equal("workspace-root-has-file", hasFile.Condition);
        Assert.Equal("no file matching 'first.md' at the workspace root", hasFile.Reason);
        ExtensionInactiveTool gitOnlyReport = Assert.Single(
            inactive,
            entry => entry.Tool == "peak__git-only"
        );
        Assert.Equal("workspace-is-git-repository", gitOnlyReport.Condition);
        Assert.Equal("the workspace root is not a git repository", gitOnlyReport.Reason);
    }

    [Fact]
    public void Split_keeps_registration_order_in_both_halves_and_agrees_with_active_only()
    {
        using var temp = new TempDirectory();
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        ITool active = Adapter("active-has", HasFile("keep.txt"), schema);
        ITool inactive = Adapter("z-last", HasFile("*.sln"), schema);
        ITool alsoActive = Adapter("active-two", null, schema);
        File.WriteAllText(temp.File("keep.txt"), "marker");
        var workspace = new Workspace(temp.Root);

        (IReadOnlyList<ITool> activeHalf, IReadOnlyList<ExtensionInactiveTool> inactiveHalf) =
            ExtensionToolActivationFilter.Split([active, inactive, alsoActive], workspace);

        Assert.Equal(
            ExtensionToolActivationFilter.ActiveOnly([active, inactive, alsoActive], workspace),
            activeHalf
        );
        Assert.Equal("peak__z-last", Assert.Single(inactiveHalf).Tool);
    }

    [Fact]
    public void The_has_file_reason_names_every_pattern_the_rule_looks_for()
    {
        using var temp = new TempDirectory();
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        ITool tool = Adapter("sol", HasFile("*.sln", "*.slnx"), schema);
        var workspace = new Workspace(temp.Root);

        ExtensionInactiveTool report = Assert.Single(
            ExtensionToolActivationFilter.Split([tool], workspace).Inactive
        );

        Assert.Equal("no file matching '*.sln', '*.slnx' at the workspace root", report.Reason);
    }

    /// <summary>A handler the adapter can bind; the probe never executes.</summary>
    private sealed class TestToolHandler : IExtensionToolHandler
    {
        public Task<ToolResult> InvokeAsync(
            JsonElement arguments,
            ToolContext context,
            CancellationToken ct
        ) => Task.FromResult(new ToolResult("ok", false));
    }
}
