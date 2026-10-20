using System.Text;
using Lunate.Coding;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class AgentPrefixStabilityTests
{
    [Fact]
    public async Task Consecutive_runs_reuse_the_same_cacheable_prefix()
    {
        using var temp = new TempDirectory();
        using var environment = new EnvironmentScope(("LUNATE_DATE", "2026-10-20"));
        string prompt = SystemPrompt.Compose(new Workspace(temp.Root), ["read"]);
        ScriptedChatClient client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Stop())
            .Enqueue(LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            AgentTestSupport.Registry(AgentTestSupport.ReadTool("contents")),
            new AgentHarnessOptions { SystemPrompt = prompt, WorkingDirectory = temp.Root }
        );

        await AgentTestSupport.Run(harness);
        await AgentTestSupport.Run(harness);

        // Prefix stability covers the core prompt and the core tool list. A RunStarting
        // SectionEdits rewrite is extension-owned and deliberately starts a new prefix; this test
        // runs without extensions, so it measures the core guarantee.
        Assert.Equal(2, client.Requests.Count);
        string firstSystem = SystemText(client.Requests[0]);
        string secondSystem = SystemText(client.Requests[1]);
        Assert.Equal(Encoding.UTF8.GetBytes(firstSystem), Encoding.UTF8.GetBytes(secondSystem));
        Assert.Contains("date (UTC): 2026-10-20", firstSystem, StringComparison.Ordinal);
        Assert.Contains("date (UTC): 2026-10-20", secondSystem, StringComparison.Ordinal);

        string firstTools = ToolDeclarations(client.Requests[0]);
        Assert.Equal(firstTools, ToolDeclarations(client.Requests[1]));
        Assert.NotEmpty(firstTools);
    }

    private static string SystemText(ScriptedRequest request) =>
        Assert.Single(request.Messages, message => message.Role == ChatRole.System).Text;

    private static string ToolDeclarations(ScriptedRequest request)
    {
        IList<AITool> tools =
            request.Options?.Tools ?? throw new InvalidOperationException("No tools were sent.");
        return string.Join(
            "\n",
            tools.Select(tool =>
            {
                AIFunction function = Assert.IsAssignableFrom<AIFunction>(tool);
                return string.Join(
                    "\u001f",
                    function.Name,
                    function.Description,
                    function.JsonSchema.GetRawText()
                );
            })
        );
    }
}
