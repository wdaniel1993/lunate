using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility.Tests;

public sealed class AgentHookAdapterTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_test_extension_runs_every_wired_hook_end_to_end()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("session.jsonl"), "/work");
        var runner = new HookRunner();
        var extension = new EndToEndExtension();
        runner.Register("ext", extension);
        var adapter = new AgentHookAdapter(runner);
        var read = new FakeTool("read", "contents");
        var registry = new ToolRegistry();
        registry.Add(read);
        registry.Add(new FakeTool("write", "unused"));
        FakeChatClient client = new FakeChatClient()
            .Enqueue(
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [
                        new FunctionCallContent(
                            "call-1",
                            "read",
                            new Dictionary<string, object?> { ["path"] = "a.txt" }
                        ),
                    ]
                ),
                new ChatResponseUpdate(ChatRole.Assistant, [])
                {
                    FinishReason = ChatFinishReason.ToolCalls,
                }
            )
            .Enqueue(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("draft")])
                {
                    FinishReason = ChatFinishReason.Stop,
                }
            )
            .Enqueue(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("done")])
                {
                    FinishReason = ChatFinishReason.Stop,
                }
            );
        var harness = new AgentHarness(
            client,
            registry,
            new AgentHarnessOptions
            {
                SystemPrompt = "base",
                Hooks = adapter,
                Session = session,
            }
        );

        List<AgentEvent> events = await AdapterTestSupport.Run(harness, Ct);

        string system = AdapterTestSupport.Text(client.Requests[0][0]);
        Assert.Contains("base", system, StringComparison.Ordinal);
        Assert.Contains("remember", system, StringComparison.Ordinal);
        Assert.Equal(["read"], client.RequestOptions[0]!.Tools!.Select(tool => tool.Name));
        Assert.Contains(client.Requests[0], message => AdapterTestSupport.Text(message) == "ctx");
        Assert.Equal(4, extension.StreamEvents);
        Assert.Contains(
            session.Entries.OfType<SessionMessageEntry>(),
            entry =>
                entry.Message.Role == ChatRole.Assistant
                && AdapterTestSupport.Text(entry.Message) == "final"
        );
        Assert.Contains("b.txt", read.ReceivedArgs!, StringComparison.Ordinal);
        ToolCallResult toolResult = events.OfType<ToolCallResult>().Single();
        Assert.Equal("redacted", toolResult.Output);
        Assert.True(((JsonElement)toolResult.Details!).GetProperty("ok").GetBoolean());
        SessionExtensionEntry entry = session.Entries.OfType<SessionExtensionEntry>().Single();
        Assert.Equal("ext/ext/note", entry.ExtensionType);
        Assert.Equal(2, extension.TurnEnds);
        Assert.Equal(1, extension.Settles);
        Assert.Equal(3, client.Requests.Count);
    }

    [Fact]
    public async Task Context_additions_are_source_tagged_and_over_budget_additions_are_dropped()
    {
        var log = new RecordingExtensionLog();
        var runner = new HookRunner(new HookRunnerOptions { ContextBudgetPerExtension = 5 }, log);
        runner.Register("ext", new FixedContextExtension("123456789012"));
        runner.Register("ext", new FixedContextExtension("210987654321"));
        var adapter = new AgentHookAdapter(runner);

        AgentContextBuildingResult result = await adapter.ContextBuildingAsync(
            new AgentContextBuildingContext("r1", []),
            Ct
        );

        AgentContextMessage message = Assert.Single(result.AddedMessages);
        Assert.Equal("123456789012", message.Text);
        Assert.Equal("ext", message.Source);
        Assert.Contains(
            log.Messages,
            entry =>
                entry.Contains("ext", StringComparison.Ordinal)
                && entry.Contains("budget", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_blocking_tool_calling_handler_denies_the_call_through_the_adapter()
    {
        var runner = new HookRunner();
        runner.Register("ext", new BlockingExtension());
        var adapter = new AgentHookAdapter(runner);
        FakeChatClient client = new FakeChatClient()
            .Enqueue(
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new FunctionCallContent("call-1", "read", new Dictionary<string, object?>())]
                ),
                new ChatResponseUpdate(ChatRole.Assistant, [])
                {
                    FinishReason = ChatFinishReason.ToolCalls,
                }
            )
            .Enqueue(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("done")])
                {
                    FinishReason = ChatFinishReason.Stop,
                }
            );
        var registry = new ToolRegistry();
        registry.Add(new FakeTool("read", "contents"));
        var harness = new AgentHarness(
            client,
            registry,
            new AgentHarnessOptions { Hooks = adapter }
        );

        List<AgentEvent> events = await AdapterTestSupport.Run(harness, Ct);

        ToolCallResult result = events.OfType<ToolCallResult>().Single();
        Assert.True(result.IsError);
        Assert.Contains("policy says no", result.Output, StringComparison.Ordinal);
    }

    private sealed class EndToEndExtension
        : IRunStartingHandler,
            IContextBuildingHandler,
            IProviderStreamEventHandler,
            IMessageCompletedHandler,
            IToolCallingHandler,
            IToolResultReadyHandler,
            ITurnEndedHandler,
            IRunSettledHandler
    {
        private int _turns;

        public int Priority => 0;

        public int StreamEvents { get; private set; }

        public int TurnEnds { get; private set; }

        public int Settles { get; private set; }

        public ValueTask<RunStartingResult> HandleAsync(
            RunStartingPayload payload,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<RunStartingResult>(
                new RunStartingResult.Apply([new PromptSectionEdit("memory", "remember")], ["read"])
            );

        public ValueTask<ContextBuildingResult> HandleAsync(
            ContextBuildingPayload payload,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(new ContextBuildingResult([new ContextMessage("system", "ctx")]));

        public ValueTask ObserveAsync(
            ProviderStreamEventPayload payload,
            CancellationToken cancellationToken
        )
        {
            StreamEvents++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<MessageCompletedResult> HandleAsync(
            MessageCompletedPayload payload,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<MessageCompletedResult>(
                payload.Text == "draft"
                    ? new MessageCompletedResult.Replace("final")
                    : new MessageCompletedResult.Keep()
            );

        public ValueTask<ToolCallingResult> HandleAsync(
            ToolCallingPayload payload,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<ToolCallingResult>(
                new ToolCallingResult.Proceed(AdapterTestSupport.Json("""{"path":"b.txt"}"""))
            );

        public ValueTask<ToolResultReadyResult> HandleAsync(
            ToolResultReadyPayload payload,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult(
                new ToolResultReadyResult("redacted", AdapterTestSupport.Json("""{"ok":true}"""))
            );

        public ValueTask<TurnEndedResult> HandleAsync(
            TurnEndedPayload payload,
            CancellationToken cancellationToken
        )
        {
            TurnEnds++;
            return ValueTask.FromResult<TurnEndedResult>(
                _turns++ == 0
                    ? new TurnEndedResult.TurnEnded(
                        [new TurnEndedEntry("note", AdapterTestSupport.Json("""{"n":1}"""))],
                        true
                    )
                    : new TurnEndedResult.None()
            );
        }

        public ValueTask ObserveAsync(
            RunSettledPayload payload,
            CancellationToken cancellationToken
        )
        {
            Settles++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedContextExtension(string text) : IContextBuildingHandler
    {
        public int Priority => 0;

        public ValueTask<ContextBuildingResult> HandleAsync(
            ContextBuildingPayload payload,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(new ContextBuildingResult([new ContextMessage("system", text)]));
    }

    private sealed class BlockingExtension : IToolCallingHandler
    {
        public int Priority => 0;

        public ValueTask<ToolCallingResult> HandleAsync(
            ToolCallingPayload payload,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<ToolCallingResult>(new ToolCallingResult.Block("policy says no"));
    }
}
