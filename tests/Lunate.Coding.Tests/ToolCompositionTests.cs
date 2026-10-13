using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The composition guard (review of add-extension-tools, finding 2): a model-facing name already
/// taken is skipped with one single-line diagnostic naming both sources — first composition wins —
/// instead of the registry's raw duplicate throw.
/// </summary>
public sealed class ToolCompositionTests
{
    [Fact]
    public void A_taken_name_is_skipped_in_a_registry_with_one_diagnostic()
    {
        var tools = new ToolRegistry();
        tools.Add(Stub("a__b", "ext"));
        var warns = new List<string>();

        ToolComposition.AddSkippingTaken(
            tools,
            [Stub("a__b", "srv"), Stub("c__d", "srv")],
            "mcp.json",
            warns.Add
        );

        Assert.Equal(["a__b", "c__d"], tools.Tools.Select(tool => tool.Name));
        string warn = Assert.Single(warns);
        Assert.Contains("'a__b'", warn, StringComparison.Ordinal);
        Assert.Contains("skipped", warn, StringComparison.Ordinal);
        Assert.Contains("(ext)", warn, StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_overload_keeps_first_composition_wins()
    {
        var tools = new List<ITool> { Stub("x__y", "ext") };
        var warns = new List<string>();

        ToolComposition.AddSkippingTaken(
            tools,
            [Stub("x__y", "srv"), Stub("z__w", "srv")],
            "an extension-registered MCP server",
            warns.Add
        );

        Assert.Equal(["x__y", "z__w"], tools.Select(tool => tool.Name));
        Assert.Single(warns);
    }

    private static ITool Stub(string name, string? group) => new StubTool(name, group);

    private sealed class StubTool : ITool
    {
        public StubTool(string name, string? group)
        {
            Name = name;
            Namespace = group is null ? null : new ToolNamespace(group, "Stub group.", null);
        }

        public string Name { get; }

        public string Description => "Stub.";

        public JsonElement ParametersSchema { get; } =
            JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

        public ToolNamespace? Namespace { get; }

        public Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        ) => Task.FromResult(new ToolResult("ok", false));
    }
}
