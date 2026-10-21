using Lunate.Ai;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentToolModelContextTests
{
    [Fact]
    public void Tool_context_outside_a_run_has_no_model()
    {
        var context = new ToolContext("/workspace", new RecordingAgentEvents());

        Assert.Null(context.Model);
    }

    [Fact]
    public void Harness_options_default_to_no_model()
    {
        Assert.Null(new AgentHarnessOptions().Model);
    }

    [Fact]
    public async Task A_run_stamps_the_active_model_into_the_tool_context()
    {
        using var temp = new TempDirectory();
        string catalogFile = temp.File("models.json");
        File.WriteAllText(
            catalogFile,
            """
            {
              "schemaVersion": 2,
              "models": [
                {
                  "id": "vision-test",
                  "provider": "openai",
                  "contextWindow": 128000,
                  "supportsTools": true,
                  "supportsVision": true
                }
              ]
            }
            """
        );
        ModelInfo model = ModelCatalog.Load(catalogFile).Find("vision-test")!;
        ScriptedTool tool = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions { Model = model, WorkingDirectory = temp.Root }
        );

        await Run(harness);

        ModelInfo seen = tool.ReceivedContext!.Model!;
        Assert.Same(model, seen);
        Assert.Equal("vision-test", seen.Id);
        Assert.True(seen.SupportsVision);
    }
}
