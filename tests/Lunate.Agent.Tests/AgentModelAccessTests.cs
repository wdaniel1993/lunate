using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class AgentModelAccessTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Models_is_null_outside_a_run()
    {
        var context = new ToolContext(".", new RecordingAgentEvents());

        Assert.Null(context.Models);
    }

    [Fact]
    public async Task Models_is_null_when_the_run_has_no_registry()
    {
        var tool = new ScriptedTool("probe", "probes", """{"type":"object","properties":{}}""")
        {
            OnExecute = (_, context) =>
                new ToolResult(context.Models is null ? "null" : "set", IsError: false),
        };
        var client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Call("c1", "probe"), LoopScripts.ToolCalls())
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, AgentTestSupport.Registry(tool));

        List<AgentEvent> events = await harness.RunAsync("go", Ct).ToListAsync(Ct);

        Assert.Contains(events.OfType<ToolCallResult>(), result => result.Output == "null");
    }

    [Fact]
    public async Task A_nested_call_charges_the_owning_run_and_emits_usage_with_the_model_id()
    {
        var nested = new UsageChatClient("cheap", inputTokens: 7, outputTokens: 3);
        var registry = new ScriptedRegistry { ["cheap"] = nested };
        var tool = NestedCallTool("cheap");
        var client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Call("c1", "nested"), LoopScripts.ToolCalls())
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            AgentTestSupport.Registry(tool),
            new AgentHarnessOptions { ModelRegistry = registry, TokenBudget = 10 }
        );

        List<AgentEvent> events = await harness.RunAsync("go", Ct).ToListAsync(Ct);

        UsageUpdated usage = Assert.Single(
            events.OfType<UsageUpdated>(),
            update => update.ModelId == "cheap"
        );
        Assert.Equal(7L, usage.Usage.InputTokenCount);
        Assert.Equal(3L, usage.Usage.OutputTokenCount);
        // The nested call's tokens exhausted the budget: the loop stops before another model call.
        Assert.Equal(StopReasons.TokenLimit, events.OfType<RunFinished>().Single().StopReason);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task A_routed_nested_call_emits_ModelRouted_and_charges_the_resolved_model()
    {
        var routed = new UsageChatClient("ext/e/auto", inputTokens: 5, outputTokens: 1)
        {
            RouteTo = "deep",
        };
        var registry = new ScriptedRegistry { ["ext/e/auto"] = routed };
        var tool = NestedCallTool("ext/e/auto");
        var client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Call("c1", "nested"), LoopScripts.ToolCalls())
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            AgentTestSupport.Registry(tool),
            new AgentHarnessOptions { ModelRegistry = registry }
        );

        List<AgentEvent> events = await harness.RunAsync("go", Ct).ToListAsync(Ct);

        ModelRouted route = Assert.Single(events.OfType<ModelRouted>());
        Assert.Equal("ext/e/auto", route.RequestedModel);
        Assert.Equal("deep", route.ResolvedModel);
        Assert.Equal(
            "deep",
            Assert
                .Single(events.OfType<UsageUpdated>(), update => update.ModelId is not null)
                .ModelId
        );
    }

    [Fact]
    public async Task A_resolution_failure_is_a_teaching_error_through_the_seam()
    {
        var registry = new ScriptedRegistry();
        var tool = NestedCallTool("missing");
        var client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Call("c1", "nested"), LoopScripts.ToolCalls())
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            AgentTestSupport.Registry(tool),
            new AgentHarnessOptions { ModelRegistry = registry }
        );

        List<AgentEvent> events = await harness.RunAsync("go", Ct).ToListAsync(Ct);

        ToolCallResult result = Assert.Single(
            events.OfType<ToolCallResult>(),
            call => call.IsError
        );
        Assert.Contains("'missing'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_subagents_nested_call_charges_the_child_and_rolls_into_the_tree()
    {
        var nested = new UsageChatClient("cheap", inputTokens: 11, outputTokens: 4);
        var registry = new ScriptedRegistry { ["cheap"] = nested };
        var nestedTool = NestedCallTool("cheap");
        var spawnTool = new ScriptedTool("spawn", "spawns", """{"type":"object","properties":{}}""")
        {
            OnExecuteAsync = async (_, context, token) =>
            {
                SubagentResult result = await context.Subagents!.RunAsync(
                    new SubagentRequest("child work"),
                    token
                );
                return new ToolResult(result.Output, result.IsError);
            },
        };
        var client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Call("p1", "spawn"), LoopScripts.ToolCalls())
            .Enqueue(LoopScripts.Call("c1", "nested"), LoopScripts.ToolCalls())
            .Enqueue(LoopScripts.Text("child done"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            AgentTestSupport.Registry(spawnTool, nestedTool),
            new AgentHarnessOptions { ModelRegistry = registry }
        );

        List<AgentEvent> events = await harness.RunAsync("go", Ct).ToListAsync(Ct);

        UsageUpdated usage = Assert.Single(events.OfType<UsageUpdated>());
        string childRunId = events
            .OfType<RunStarted>()
            .First(started => started.ParentRunId is not null)
            .RunId;
        Assert.Equal(childRunId, usage.RunId);
        Assert.Equal("cheap", usage.ModelId);
        Assert.Equal(15L, harness.SubagentUsage.TokensUsed);
        Assert.Contains(
            events.OfType<RunFinished>(),
            finished => finished.ParentRunId is not null && finished.RunId == childRunId
        );
    }

    [Fact]
    public async Task A_routed_runs_assistant_entry_records_the_resolved_backend()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), "/work");
        var client = new ReportingStreamingClient("ext/e/auto", "deep");
        var harness = new AgentHarness(
            client,
            AgentTestSupport.Registry(),
            new AgentHarnessOptions { Session = session }
        );

        List<AgentEvent> events = await harness.RunAsync("go", Ct).ToListAsync(Ct);

        SessionMessageEntry assistant = session
            .Entries.OfType<SessionMessageEntry>()
            .Last(entry => entry.Message.Role == ChatRole.Assistant);
        Assert.Equal("deep", assistant.Model);
        Assert.Equal("deep", Assert.Single(events.OfType<ModelRouted>()).ResolvedModel);
        Assert.Equal("deep", Assert.Single(events.OfType<UsageUpdated>()).ModelId);
    }

    private static ScriptedTool NestedCallTool(string modelId) =>
        new("nested", "makes a nested model call", """{"type":"object","properties":{}}""")
        {
            OnExecuteAsync = async (_, context, token) =>
            {
                ChatResponse response = await context
                    .Models!.For(modelId)
                    .GetResponseAsync([new ChatMessage(ChatRole.User, "nested")], null, token);
                return new ToolResult(response.Text ?? string.Empty, IsError: false);
            },
        };

    private sealed class ScriptedRegistry : IModelRegistry
    {
        private readonly Dictionary<string, IChatClient> _clients = new(StringComparer.Ordinal);

        public IChatClient this[string modelId]
        {
            set => _clients[modelId] = value;
        }

        public IChatClient Resolve(string modelId) =>
            _clients.TryGetValue(modelId, out IChatClient? client)
                ? client
                : throw new InvalidOperationException(
                    $"Unknown model '{modelId}'. Known models: {string.Join(", ", _clients.Keys)}. Use a known model id."
                );
    }

    /// <summary>A streaming client that reports its routing and carries usage, like a router.</summary>
    private sealed class ReportingStreamingClient(string requested, string resolved) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            ModelRouteObserver.Report(requested, resolved);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "hello") { ModelId = resolved };
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [new UsageContent(new UsageDetails { InputTokenCount = 4, OutputTokenCount = 2 })]
            );
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class UsageChatClient(string id, long inputTokens, long outputTokens)
        : IChatClient
    {
        public string? RouteTo { get; init; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            if (RouteTo is { } resolved)
            {
                ModelRouteObserver.Report(id, resolved);
            }

            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, $"reply from {id}"))
                {
                    ModelId = id,
                    Usage = new UsageDetails
                    {
                        InputTokenCount = inputTokens,
                        OutputTokenCount = outputTokens,
                    },
                }
            );
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, $"stream from {id}")
            {
                ModelId = id,
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
