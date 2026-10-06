using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

internal static class AgentTestSupport
{
    internal static async Task<List<AgentEvent>> Run(AgentHarness harness) =>
        await harness
            .RunAsync("go", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

    internal static ScriptedTool ReadTool(string output) =>
        new(
            "read",
            "Reads a file.",
            """{"type":"object","properties":{"path":{"type":"string"}}}"""
        )
        {
            OnExecute = (_, _) => new ToolResult(output, IsError: false),
        };

    internal static ToolRegistry Registry(ITool tool)
    {
        var registry = new ToolRegistry();
        registry.Add(tool);
        return registry;
    }
}
