namespace Lunate.Agent.Tests;

public sealed class ToolRiskResolverTests
{
    /// <summary>
    /// The resolver errs on the side of danger: only a hint that says read-only and nothing that
    /// says destructive or open-world is read-only; anything that declares danger is an execute.
    /// </summary>
    [Theory]
    [InlineData(false, false, false, false, ToolRisk.Write)]
    [InlineData(true, false, false, false, ToolRisk.ReadOnly)]
    [InlineData(true, false, true, false, ToolRisk.ReadOnly)]
    [InlineData(true, true, false, false, ToolRisk.Execute)]
    [InlineData(true, false, false, true, ToolRisk.Execute)]
    [InlineData(false, true, false, false, ToolRisk.Execute)]
    [InlineData(false, false, false, true, ToolRisk.Execute)]
    [InlineData(false, true, false, true, ToolRisk.Execute)]
    [InlineData(false, false, true, false, ToolRisk.Write)]
    public void The_annotations_resolve_to_the_safer_risk(
        bool readOnly,
        bool destructive,
        bool idempotent,
        bool openWorld,
        ToolRisk expected
    )
    {
        var annotations = new ToolAnnotations(readOnly, destructive, idempotent, openWorld);

        Assert.Equal(expected, ToolRiskResolver.FromAnnotations(annotations));
    }

    [Fact]
    public void No_annotations_resolve_to_an_execute()
    {
        Assert.Equal(ToolRisk.Execute, ToolRiskResolver.FromAnnotations(null));
    }

    [Fact]
    public void An_unannotated_tool_derives_an_execute_risk()
    {
        ITool tool = new UnannotatedTool();

        Assert.Null(tool.Annotations);
        Assert.Equal(ToolRisk.Execute, tool.Risk);
    }

    private sealed class UnannotatedTool : ITool
    {
        public string Name => "unannotated";

        public string Description => "A tool that declares no annotations.";

        public System.Text.Json.JsonElement ParametersSchema => default;

        public Task<ToolResult> ExecuteAsync(
            System.Text.Json.JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        ) => Task.FromResult(new ToolResult("ok", IsError: false));
    }
}
