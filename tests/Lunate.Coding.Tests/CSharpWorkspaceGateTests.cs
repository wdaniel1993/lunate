using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The Roslyn built-in manifest expresses activation through the shared evaluator:
/// <c>workspace-root-has-file</c> over <c>*.sln</c>, <c>*.slnx</c> and <c>*.csproj</c>. These pins
/// hold the manifest rule to the same matching the former flagship scan had and verify that its
/// verdict agrees with the shared composition filter.
/// </summary>
public sealed class CSharpWorkspaceGateTests
{
    [Fact]
    public void The_gate_matches_a_multi_dot_solution_name_like_the_flagship_scan_did()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("App.x.sln"), "solution");

        Assert.True(ExtensionSession.RoslynIsActive(temp.Root));
    }

    [Fact]
    public void The_gate_never_takes_a_longer_extension_for_a_pattern()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("App.slnx"), "solution");

        // The flagship rule names *.slnx separately, so a solution file with it activates; but a
        // rule naming only *.sln cannot treat .slnx as its extension.
        Assert.True(ExtensionSession.RoslynIsActive(temp.Root));
        Assert.False(
            ToolActivationEvaluator.Evaluate(
                new ExtensionToolActivationRule(
                    ExtensionToolActivationRule.WorkspaceRootHasFile,
                    ["*.sln"]
                ),
                temp.Root
            )
        );
    }

    [Fact]
    public void The_gate_stays_strictly_case_sensitive()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("App.SLN"), "solution");

        Assert.False(ExtensionSession.RoslynIsActive(temp.Root));
    }

    [Fact]
    public void The_gate_and_the_filter_agree_on_a_root_with_a_direct_solution()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("App.sln"), "solution");

        AgreementPins(temp.Root, expectedLooksLike: true);
    }

    [Fact]
    public void The_gate_and_the_filter_agree_on_a_root_without_a_solution()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Program.cs"), "class C;\n");

        AgreementPins(temp.Root, expectedLooksLike: false);
    }

    [Fact]
    public void The_gate_and_the_filter_agree_on_a_nested_but_not_direct_solution()
    {
        using var temp = new TempDirectory();
        string nested = temp.File("src");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "App.sln"), "solution");

        AgreementPins(temp.Root, expectedLooksLike: false);
    }

    [Fact]
    public void The_gate_and_the_filter_agree_on_an_unreadable_root()
    {
        // A file at the root path — the cheapest cross-platform unreadable root.
        using var temp = new TempDirectory();
        string notADirectory = temp.File("plain-file");
        File.WriteAllText(notADirectory, "text");

        AgreementPins(notADirectory, expectedLooksLike: false);
    }

    /// <summary>
    /// The agreement pin itself: the gate's boolean and the shared helper's verdict on an adapter
    /// stamped with the flagship rule must be one behaviour at the reported condition.
    /// </summary>
    private static void AgreementPins(string root, bool expectedLooksLike)
    {
        ITool flagshipAdapter = FlagshipRuleAdapter();
        var workspace = new Workspace(root);
        bool kept = ExtensionToolActivationFilter
            .ActiveOnly([flagshipAdapter], workspace)
            .Any(tool => tool.Name == flagshipAdapter.Name);

        Assert.Equal(expectedLooksLike, ExtensionSession.RoslynIsActive(root));
        Assert.Equal(expectedLooksLike, kept);
    }

    /// <summary>An adapter stamped with the flagship rule through the loader's registration seam.</summary>
    private static ITool FlagshipRuleAdapter()
    {
        var registry = new ExtensionToolRegistry();
        registry.Register(
            "gate",
            new ExtensionToolRegistration
            {
                Name = "probe",
                Description = "A rule probe for the gate agreement.",
                ParametersSchema = JsonSerializer.SerializeToElement(new { type = "object" }),
                Handler = new GateProbeHandler(),
            },
            new ExtensionToolActivationRule(
                ExtensionToolActivationRule.WorkspaceRootHasFile,
                ["*.sln", "*.slnx", "*.csproj"]
            )
        );
        return Assert.Single(registry.List());
    }

    private sealed class GateProbeHandler : IExtensionToolHandler
    {
        public Task<ToolResult> InvokeAsync(
            JsonElement arguments,
            ToolContext context,
            CancellationToken ct
        ) => Task.FromResult(new ToolResult("ok", false));
    }
}
