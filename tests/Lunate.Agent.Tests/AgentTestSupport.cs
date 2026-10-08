using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

internal static class AgentTestSupport
{
    internal static async Task<List<AgentEvent>> Run(AgentHarness harness) =>
        await harness
            .RunAsync("go", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

    internal static ScriptedTool ReadTool(
        string output,
        string name = "read",
        ToolAnnotations? annotations = null
    ) =>
        new(name, "Reads a file.", """{"type":"object","properties":{"path":{"type":"string"}}}""")
        {
            Annotations = annotations,
            OnExecute = (_, _) => new ToolResult(output, IsError: false),
        };

    internal static ToolRegistry Registry(params ITool[] tools)
    {
        var registry = new ToolRegistry();
        foreach (ITool tool in tools)
        {
            registry.Add(tool);
        }

        return registry;
    }
}
