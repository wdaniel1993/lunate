using System.Text.Json;
using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>A spawn probe that runs one child in a fixed workspace root.</summary>
internal sealed class SpawnProbe(string name, string workspaceRoot) : ITool
{
    public string Name { get; } = name;

    public string Description => "Spawns a subagent in a worktree.";

    public JsonElement ParametersSchema { get; } =
        JsonSerializer.SerializeToElement(
            new { type = "object", properties = new { prompt = new { type = "string" } } }
        );

    public ToolConcurrency Concurrency => ToolConcurrency.Sequential;

    public ToolRisk Risk => ToolRisk.Write;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        SubagentResult result = await ctx.Subagents!.RunAsync(
            new SubagentRequest("child prompt", WorkspaceRoot: workspaceRoot),
            ct
        );
        return new ToolResult(result.Output, result.IsError);
    }
}
