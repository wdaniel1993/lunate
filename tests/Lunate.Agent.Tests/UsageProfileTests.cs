using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class UsageProfileTests
{
    [Theory]
    [InlineData("chat", "chat-profile", "openai", "openai", "chat-model")]
    [InlineData("fast", "fast-profile", "router", "openrouter", "fast-model")]
    [InlineData("decision", "decision-profile", "router", "openrouter", "decision-model")]
    public async Task Configured_usage_client_emits_its_attribution_once(
        string usageName,
        string profileName,
        string providerName,
        string providerKind,
        string modelId
    )
    {
        var fallback = new ScriptedChatClient();
        var selected = new ScriptedChatClient().Enqueue(
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new TextContent("selected answer"),
                    new UsageContent(
                        new UsageDetails { InputTokenCount = 4, OutputTokenCount = 1 }
                    ),
                ]
            ),
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [new UsageContent(new UsageDetails { InputTokenCount = 9, OutputTokenCount = 2 })]
            ),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(
            fallback,
            new ToolRegistry(),
            new AgentHarnessOptions
            {
                Usage = usageName,
                UsageModels = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    [usageName] = new(
                        selected,
                        profileName,
                        providerName,
                        new ModelInfo(modelId, providerKind, null, 64000, SupportsTools: true)
                    ),
                },
            }
        );

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Empty(fallback.Requests);
        Assert.Single(selected.Requests);
        UsageUpdated usage = Assert.Single(events.OfType<UsageUpdated>());
        Assert.Equal(modelId, usage.ModelId);
        Assert.Equal(9L, usage.Usage.InputTokenCount);
        Assert.Equal(
            new ModelUsageAttribution(usageName, profileName, providerName, providerKind),
            usage.Attribution
        );
    }

    [Fact]
    public async Task Decision_usage_refuses_known_missing_structured_output_before_request()
    {
        var fallback = new ScriptedChatClient();
        var decision = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("unexpected"),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(
            fallback,
            new ToolRegistry(),
            new AgentHarnessOptions
            {
                Usage = "decision",
                UsageModels = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    ["decision"] = new(
                        decision,
                        "decision-profile",
                        "local",
                        new ModelInfo("decision-model", "openai", null, 64000, SupportsTools: true)
                        {
                            Capabilities = new ModelCapabilityDescriptor(
                                features: new HashSet<string>(StringComparer.Ordinal)
                            ),
                        }
                    ),
                },
            }
        );

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Empty(decision.Requests);
        Assert.Contains(
            events.OfType<RunError>(),
            error => error.Message.Contains("structured-output", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData("text")]
    [InlineData("tools")]
    public async Task Chat_usage_refuses_known_missing_requirement_before_request(
        string missingCapability
    )
    {
        var fallback = new ScriptedChatClient();
        var selected = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("unexpected"),
            LoopScripts.Stop()
        );
        var model = new ModelInfo("chat-model", "openai", null, 64000, SupportsTools: true)
        {
            Capabilities = missingCapability switch
            {
                "text" => new ModelCapabilityDescriptor(
                    inputModalities: new HashSet<string>(StringComparer.Ordinal)
                ),
                "tools" => new ModelCapabilityDescriptor(
                    features: new HashSet<string>(StringComparer.Ordinal)
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(missingCapability)),
            },
        };
        var harness = new AgentHarness(
            fallback,
            new ToolRegistry(),
            new AgentHarnessOptions
            {
                UsageModels = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    ["chat"] = new(selected, "chat-profile", "openai", model),
                },
            }
        );

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Empty(selected.Requests);
        Assert.Contains(
            events.OfType<RunError>(),
            error => error.Message.Contains(missingCapability, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Configured_subagent_profile_is_used_for_child_model_calls()
    {
        var parent = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var child = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("child answer"),
            LoopScripts.Usage(input: 7, output: 2),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(
            parent,
            Registry(SpawnTool()),
            new AgentHarnessOptions
            {
                UsageModels = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    ["chat"] = new(
                        parent,
                        "chat-profile",
                        "openai",
                        new ModelInfo("chat-model", "openai", null, 64000, SupportsTools: true)
                    ),
                    ["subagents"] = new(
                        child,
                        "subagent-profile",
                        "router",
                        new ModelInfo(
                            "subagent-model",
                            "openrouter",
                            null,
                            64000,
                            SupportsTools: true
                        )
                    ),
                },
            }
        );

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Equal(2, parent.Requests.Count);
        Assert.Single(child.Requests);
        UsageUpdated usage = Assert.Single(events.OfType<UsageUpdated>());
        Assert.Equal("subagent-model", usage.ModelId);
        Assert.Equal(
            new ModelUsageAttribution("subagents", "subagent-profile", "router", "openrouter"),
            usage.Attribution
        );
    }

    [Fact]
    public async Task Missing_subagent_assignment_uses_chat_client_with_subagent_attribution()
    {
        var shared = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Text("child answer"),
                LoopScripts.Usage(input: 7, output: 2),
                LoopScripts.Stop()
            )
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            shared,
            Registry(SpawnTool()),
            new AgentHarnessOptions
            {
                UsageModels = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    ["chat"] = new(
                        shared,
                        "chat-profile",
                        "openai",
                        new ModelInfo("chat-model", "openai", null, 64000, SupportsTools: true)
                    ),
                },
            }
        );

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Equal(3, shared.Requests.Count);
        UsageUpdated usage = Assert.Single(events.OfType<UsageUpdated>());
        Assert.Equal("chat-model", usage.ModelId);
        Assert.Equal(
            new ModelUsageAttribution("subagents", "chat-profile", "openai", "openai"),
            usage.Attribution
        );
    }

    private static ToolRegistry Registry(ScriptedTool tool)
    {
        var registry = new ToolRegistry();
        registry.Add(tool);
        return registry;
    }

    private static ScriptedTool SpawnTool()
    {
        var tool = new ScriptedTool(
            "spawn",
            "Spawns a subagent.",
            """{"type":"object","properties":{"prompt":{"type":"string"}}}""",
            ToolRisk.Write
        );
        tool.OnExecuteAsync = async (_, context, ct) =>
        {
            SubagentResult result = await context.Subagents!.RunAsync(
                new SubagentRequest("child prompt"),
                ct
            );
            return new ToolResult(result.Output, result.IsError);
        };
        return tool;
    }
}
