using System.Text.Json;
using Lunate.Ai;
using Lunate.Contracts;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class DecideToolTests
{
    [Fact]
    public async Task One_call_returns_three_results_without_approving_a_later_tool()
    {
        using JsonDocument state = JsonDocument.Parse("{\"tool\":\"write\"}");
        var chat = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call(
                    "decide-call",
                    "decide",
                    LoopScripts.Args(
                        ("state", state.RootElement),
                        (
                            "questions",
                            new object[]
                            {
                                new
                                {
                                    id = "safe",
                                    text = "Is it safe?",
                                    kind = "yes/no",
                                },
                                new
                                {
                                    id = "risk",
                                    text = "What is the risk?",
                                    kind = "choice",
                                    answers = new[] { "low", "high" },
                                },
                                new
                                {
                                    id = "quality",
                                    text = "How is the quality?",
                                    kind = "score",
                                    answers = new[] { "weak", "strong" },
                                },
                            }
                        )
                    )
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("write-call", "write", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var decision = new ScriptedChatClient().Enqueue(
            LoopScripts.Text(
                """{"results":{"safe":{"answer":"yes","probabilities":{"yes":0.8,"no":0.2}},"risk":{"answer":"low","probabilities":{"low":0.7,"high":0.3}},"quality":{"answer":"strong","probabilities":{"weak":0.1,"strong":0.9}}}}"""
            ),
            LoopScripts.Stop()
        );
        var approver = new ToolNameApprover("decide");
        int writes = 0;
        var tools = new ToolRegistry();
        var write = new ScriptedTool(
            "write",
            "Writes a file.",
            """{"type":"object"}""",
            ToolRisk.Write
        )
        {
            OnExecute = (_, _) =>
            {
                writes++;
                return new ToolResult("written", IsError: false);
            },
        };
        tools.Add(write);
        var harness = new AgentHarness(
            chat,
            tools,
            new AgentHarnessOptions
            {
                Approver = approver,
                UsageModels = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    ["chat"] = new(
                        chat,
                        "chat-profile",
                        "openai",
                        new ModelInfo("chat-model", "openai", null, 64000, SupportsTools: true)
                    ),
                    ["decision"] = new(
                        decision,
                        "decision-profile",
                        "openai",
                        new ModelInfo("decision-model", "openai", null, 64000, SupportsTools: false)
                    ),
                },
            }
        );

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Contains(
            chat.Requests[0].Options!.Tools!,
            declaration => declaration.Name == "decide"
        );
        Assert.Single(decision.Requests);
        Assert.Equal(["decide", "write"], approver.ToolNames);
        Assert.Equal(0, writes);

        ToolCallResult decideResult = Assert.Single(
            events.OfType<ToolCallResult>().Where(result => result.CallId == "decide-call")
        );
        using JsonDocument response = JsonDocument.Parse(decideResult.Output);
        JsonElement results = response.RootElement.GetProperty("results");
        Assert.Equal(["safe", "risk", "quality"], results.EnumerateObject().Select(p => p.Name));
        Assert.Equal("yes", results.GetProperty("safe").GetProperty("answer").GetString());
        Assert.Equal(0.8, results.GetProperty("safe").GetProperty("probability").GetDouble());
        Assert.Equal(
            2,
            results.GetProperty("risk").GetProperty("distribution").GetArrayLength()
        );
        Assert.Contains(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "write-call" && result.IsError
        );
    }

    [Fact]
    public async Task Decide_tool_is_not_offered_without_a_decision_profile()
    {
        var chat = new ScriptedChatClient().Enqueue(LoopScripts.Stop());
        var harness = new AgentHarness(
            chat,
            new ToolRegistry(),
            new AgentHarnessOptions
            {
                UsageModels = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    ["chat"] = new(
                        chat,
                        "chat-profile",
                        "openai",
                        new ModelInfo("chat-model", "openai", null, 64000, SupportsTools: true)
                    ),
                },
            }
        );

        await AgentTestSupport.Run(harness);

        Assert.DoesNotContain(
            chat.Requests[0].Options!.Tools!,
            declaration => declaration.Name == "decide"
        );
    }

    private sealed class ToolNameApprover(params string[] allowedTools) : IToolApprover
    {
        public List<string> ToolNames { get; } = [];

        public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
        {
            ToolNames.Add(request.Tool.Name);
            return ValueTask.FromResult(allowedTools.Contains(request.Tool.Name, StringComparer.Ordinal));
        }
    }
}
