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

    private static ExtensionToolAdapter Adapter(
        string name,
        ExtensionToolActivationRule? rule,
        JsonElement schema
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
            rule
        );
        return Assert.Single(registry.List());
    }

    private static ExtensionToolActivationRule HasFile(params string[] patterns) =>
        new(ExtensionToolActivationRule.WorkspaceRootHasFile, patterns);

    private static ExtensionToolActivationRule IsGitRepository() =>
        new(ExtensionToolActivationRule.WorkspaceIsGitRepository, []);

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
