using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Testing;
using Microsoft.Extensions.AI;

namespace ModelRouterExtension.Tests;

/// <summary>
/// The done-when through the real composition: the virtual model routes per request over the
/// declared provider (secret only in the request header), the counting middleware observes backend
/// calls, middleware order and fail-closed semantics hold, a tool's nested call charges the owning
/// run with the resolved model id and reports the route, and the teaching errors name the fix.
/// </summary>
public sealed class ModelRouterRunTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_virtual_model_routes_per_request_over_one_client()
    {
        using var temp = new TempDirectory();
        await using var endpoint = new FakeOpenAiEndpoint();
        await using var host = TestSupport.CreateHost(temp, endpoint);
        await host.StartAsync(Ct);

        IChatClient client = host.Runner.ModelRegistry.Resolve("ext/model-router/auto");
        ChatResponse shortReply = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "a short request")],
            null,
            Ct
        );
        ChatResponse longReply = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, new string('x', 5000))],
            null,
            Ct
        );

        Assert.Equal("reply from fast", shortReply.Text);
        Assert.Equal("reply from deep", longReply.Text);
        Assert.Same(client, host.Runner.ModelRegistry.Resolve("ext/model-router/auto"));
        IReadOnlyList<RecordedRequest> requests = endpoint.Requests;
        Assert.Equal(2, requests.Count);
        Assert.Equal("fast", requests[0].Model);
        Assert.Equal("deep", requests[1].Model);
        Assert.All(
            requests,
            request => Assert.Equal("Bearer " + TestSupport.Secret, request.Authorization)
        );
        // The secret reaches the wire, never an extension-visible surface (the log here).
        Assert.DoesNotContain(
            TestSupport.Secret,
            string.Join("\n", host.Log.Messages),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task The_counting_middleware_sees_only_calls_that_reach_a_backend()
    {
        using var temp = new TempDirectory();
        await using var endpoint = new FakeOpenAiEndpoint();
        await using var host = TestSupport.CreateHost(temp, endpoint);
        await host.StartAsync(Ct);

        IChatClient auto = host.Runner.ModelRegistry.Resolve("ext/model-router/auto");
        await auto.GetResponseAsync([new ChatMessage(ChatRole.User, "short")], null, Ct);
        await auto.GetResponseAsync(
            [new ChatMessage(ChatRole.User, new string('y', 4500))],
            null,
            Ct
        );

        RouterCounterView counted = TestSupport.Counters(host);
        Assert.Equal(2, counted.Total);
        Assert.Equal(1, counted.CallsByModel["fast"]);
        Assert.Equal(1, counted.CallsByModel["deep"]);

        // A direct backend call is a call that reaches a backend: counted too.
        await host
            .Runner.ModelRegistry.Resolve("fast")
            .GetResponseAsync([new ChatMessage(ChatRole.User, "direct")], null, Ct);
        Assert.Equal(3, TestSupport.Counters(host).Total);
    }

    [Fact]
    public async Task Middleware_composes_by_priority_with_the_first_outermost()
    {
        using var temp = new TempDirectory();
        await using var endpoint = new FakeOpenAiEndpoint();
        await using var host = TestSupport.CreateHost(temp, endpoint);
        await host.StartAsync(Ct);
        var steps = new List<string>();
        host.Runner.ModelRegistry.RegisterModelMiddleware(
            "test-order",
            new ExtensionModelMiddleware(
                ModelMiddlewareSlot.BeforeRouting,
                0,
                inner => new StepClient(inner, steps, "outer-before", "outer-after")
            )
        );
        host.Runner.ModelRegistry.RegisterModelMiddleware(
            "test-order",
            new ExtensionModelMiddleware(
                ModelMiddlewareSlot.BeforeRouting,
                200,
                inner => new StepClient(inner, steps, "inner", after: null)
            )
        );

        await host
            .Runner.ModelRegistry.Resolve("ext/model-router/auto")
            .GetResponseAsync([new ChatMessage(ChatRole.User, "short")], null, Ct);

        Assert.Equal(["outer-before", "inner", "outer-after"], steps);
    }

    [Fact]
    public async Task A_throwing_middleware_fails_closed_and_the_backend_is_never_reached()
    {
        using var temp = new TempDirectory();
        await using var endpoint = new FakeOpenAiEndpoint();
        await using var host = TestSupport.CreateHost(temp, endpoint);
        await host.StartAsync(Ct);
        host.Runner.ModelRegistry.RegisterModelMiddleware(
            "test-breaker",
            new ExtensionModelMiddleware(
                ModelMiddlewareSlot.BeforeRouting,
                0,
                _ => throw new InvalidOperationException("breaker exploded")
            )
        );

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host
                .Runner.ModelRegistry.Resolve("ext/model-router/auto")
                .GetResponseAsync([new ChatMessage(ChatRole.User, "short")], null, Ct)
        );

        Assert.Contains("'test-breaker'", error.Message, StringComparison.Ordinal);
        Assert.Contains("breaker exploded", error.Message, StringComparison.Ordinal);
        Assert.Empty(endpoint.Requests);
    }

    [Fact]
    public async Task A_routed_tool_call_charges_the_run_with_the_resolved_model()
    {
        using var temp = new TempDirectory();
        await using var endpoint = new FakeOpenAiEndpoint();
        await using var host = TestSupport.CreateHost(
            temp,
            endpoint,
            configure: options => options with { TokenBudget = 15 }
        );
        await host.StartAsync(Ct);
        host.ComposeTools();
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "model-router__ask",
                    ModelScripts.Args(("prompt", "short"))
                )
            )
            .Enqueue(ModelScripts.Text("parent done"));

        ExtensionRunResult result = await host.RunAsync(client, "go", Ct);

        string runId = result.Events.OfType<RunStarted>().Single().RunId;
        ModelRouted routed = Assert.Single(result.Events.OfType<ModelRouted>());
        Assert.Equal("ext/model-router/auto", routed.RequestedModel);
        Assert.Equal("fast", routed.ResolvedModel);
        UsageUpdated usage = Assert.Single(result.Events.OfType<UsageUpdated>());
        Assert.Equal(runId, usage.RunId);
        Assert.Equal("fast", usage.ModelId);
        Assert.Equal(11L, usage.Usage.InputTokenCount);
        Assert.Equal(4L, usage.Usage.OutputTokenCount);
        // 15 charged tokens exhausted the budget before another model call.
        Assert.Equal(
            StopReasons.TokenLimit,
            Assert.Single(result.Events.OfType<RunFinished>()).StopReason
        );
        Assert.Single(client.Requests);
        Assert.Single(endpoint.Requests);
    }

    [Fact]
    public async Task A_subagents_nested_call_charges_the_child_run_and_rolls_up()
    {
        using var temp = new TempDirectory();
        await using var endpoint = new FakeOpenAiEndpoint();
        ExtensionLoader loader = await TestSupport.LoadBothExtensionsAsync(temp, endpoint, Ct);
        try
        {
            string worktree = Path.Combine(temp.Root, "worktree");
            string sessions = Path.Combine(temp.Root, "sessions");
            Directory.CreateDirectory(sessions);
            Session session = Session.Create(Path.Combine(sessions, "s.jsonl"), worktree);
            var client = new ScriptedModelClient()
                .Enqueue(
                    ModelScripts.Call(
                        "p1",
                        "subagents__spawn",
                        ModelScripts.Args(("prompt", "child work"))
                    )
                )
                .Enqueue(
                    ModelScripts.Call(
                        "c1",
                        "model-router__ask",
                        ModelScripts.Args(("prompt", "hi"))
                    )
                )
                .Enqueue(ModelScripts.Text("child done"))
                .Enqueue(ModelScripts.Text("parent done"));
            var harness = new AgentHarness(
                client,
                TestSupport.Tools(loader),
                new AgentHarnessOptions
                {
                    WorkingDirectory = worktree,
                    Session = session,
                    ModelRegistry = loader.ModelRegistry,
                    Approver = new ScriptedApprover(true, true),
                }
            );

            List<AgentEvent> events = [];
            await foreach (AgentEvent agentEvent in harness.RunAsync("go", Ct))
            {
                events.Add(agentEvent);
            }

            RunStarted child = events
                .OfType<RunStarted>()
                .Single(started => started.ParentRunId is not null);
            UsageUpdated usage = Assert.Single(
                events.OfType<UsageUpdated>(),
                update => update.RunId == child.RunId
            );
            Assert.Equal("fast", usage.ModelId);
            Assert.Equal(11L, usage.Usage.InputTokenCount);
            Assert.Equal(4L, usage.Usage.OutputTokenCount);
            Assert.Contains(
                events.OfType<ModelRouted>(),
                routed =>
                    routed.RunId == child.RunId
                    && routed.RequestedModel == "ext/model-router/auto"
                    && routed.ResolvedModel == "fast"
            );
            // The child's nested call is the tree's usage: charged to the child and rolled up.
            Assert.Equal(15L, harness.SubagentUsage.TokensUsed);
            Assert.Equal(StopReasons.Stop, events.OfType<RunFinished>().Last().StopReason);
            Assert.Single(endpoint.Requests);
        }
        finally
        {
            await loader.Unload("model-router");
            await loader.Unload("subagents");
            await loader.EndSessionAsync(Ct);
        }
    }

    [Fact]
    public async Task Missing_secret_is_a_teaching_error_naming_provider_and_secret()
    {
        using var temp = new TempDirectory();
        await using var endpoint = new FakeOpenAiEndpoint();
        await using var host = TestSupport.CreateHost(temp, endpoint, withSecret: false);
        await host.StartAsync(Ct);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            host.Runner.ModelRegistry.Resolve("fast")
        );

        Assert.Contains("'ext/model-router/router-local'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'router-key'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_ids_backends_cycles_and_depth_are_teaching_errors()
    {
        using var temp = new TempDirectory();
        await using var endpoint = new FakeOpenAiEndpoint();
        await using var host = TestSupport.CreateHost(temp, endpoint);
        await host.StartAsync(Ct);
        ModelRegistry registry = host.Runner.ModelRegistry;

        InvalidOperationException unknown = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve("nope")
        );
        Assert.Contains("'nope'", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("fast", unknown.Message, StringComparison.Ordinal);

        registry.RegisterVirtualModel("model-router", new("broken", ["missing"], _ => "missing"));
        InvalidOperationException backend = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve("ext/model-router/broken")
        );
        Assert.Contains("'ext/model-router/broken'", backend.Message, StringComparison.Ordinal);
        Assert.Contains("'missing'", backend.Message, StringComparison.Ordinal);

        registry.RegisterVirtualModel("loop-a", new("a", ["ext/loop-b/b"], _ => "ext/loop-b/b"));
        registry.RegisterVirtualModel("loop-b", new("b", ["ext/loop-a/a"], _ => "ext/loop-a/a"));
        InvalidOperationException cycle = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve("ext/loop-a/a")
        );
        Assert.Contains("cycle", cycle.Message, StringComparison.OrdinalIgnoreCase);

        string previous = "fast";
        for (var index = 0; index < 9; index++)
        {
            string extensionId = index == 8 ? "deep-chain" : "chain";
            string id = $"v{index}";
            registry.RegisterVirtualModel(extensionId, new(id, [previous], _ => previous));
            previous = $"ext/{extensionId}/{id}";
        }

        InvalidOperationException depth = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve("ext/deep-chain/v8")
        );
        Assert.Contains("deeper than 8", depth.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tools_unsupported_declaration_refuses_requests_offering_tools()
    {
        using var temp = new TempDirectory();
        await using var endpoint = new FakeOpenAiEndpoint();
        await using var host = TestSupport.CreateHost(temp, endpoint);
        await host.StartAsync(Ct);
        host.Runner.ModelRegistry.RegisterDeclared(
            "test-declared",
            [
                new ModelProviderDescriptor(
                    "no-tools",
                    "No Tools",
                    endpoint.BaseUrl,
                    "declared-key",
                    ["no-tool-model"],
                    4096,
                    SupportsTools: false
                ),
            ],
            new DeclaredSecrets()
        );

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host
                .Runner.ModelRegistry.Resolve("no-tool-model")
                .GetResponseAsync(
                    [new ChatMessage(ChatRole.User, "x")],
                    new ChatOptions { Tools = [new StubTool()] },
                    Ct
                )
        );

        Assert.Contains("'ext/test-declared/no-tools'", error.Message, StringComparison.Ordinal);
        Assert.Contains("supportsTools", error.Message, StringComparison.Ordinal);
    }

    private sealed class StepClient(
        IChatClient inner,
        List<string> steps,
        string before,
        string? after
    ) : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            steps.Add(before);
            ChatResponse response = await base.GetResponseAsync(
                    messages,
                    options,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (after is not null)
            {
                steps.Add(after);
            }

            return response;
        }
    }

    private sealed class StubTool : AITool
    {
        public override string Name => "stub";
    }

    private sealed class DeclaredSecrets : IExtensionSecrets
    {
        public bool TryGet(
            string name,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value
        )
        {
            value = name == "declared-key" ? "secret" : null;
            return value is not null;
        }
    }
}
