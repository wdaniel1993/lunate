using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentSessionMirrorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_sessioned_run_mirrors_messages_models_and_usage()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        ScriptedChatClient client = new ScriptedChatClient()
            .Enqueue(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("One moment")])
                {
                    ModelId = "gpt-4o-mini",
                },
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [
                        new UsageContent(
                            new UsageDetails { InputTokenCount = 11, OutputTokenCount = 5 }
                        ),
                    ]
                ),
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
                    ModelId = "gpt-4o-mini",
                }
            )
            .Enqueue(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Done")])
                {
                    ModelId = "gpt-4o-mini",
                },
                new ChatResponseUpdate(ChatRole.Assistant, [])
                {
                    FinishReason = ChatFinishReason.Stop,
                    ModelId = "gpt-4o-mini",
                }
            );
        var harness = new AgentHarness(
            client,
            Registry(ReadTool("contents")),
            new AgentHarnessOptions { Session = session }
        );

        await harness
            .RunAsync("go", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, session.Entries.Count);
        Assert.All(session.Entries, entry => Assert.IsType<SessionMessageEntry>(entry));
        SessionMessageEntry user = Entry(session, 0);
        Assert.Equal(ChatRole.User, user.Message.Role);
        Assert.Null(user.ParentId);
        SessionMessageEntry assistant = Entry(session, 1);
        Assert.Equal(ChatRole.Assistant, assistant.Message.Role);
        Assert.Equal("e_01", assistant.ParentId);
        Assert.Equal("gpt-4o-mini", assistant.Model);
        Assert.Equal(new SessionUsage(11, 5), assistant.Usage);
        SessionMessageEntry tool = Entry(session, 2);
        Assert.Equal(ChatRole.Tool, tool.Message.Role);
        Assert.Equal("e_02", tool.ParentId);
        SessionMessageEntry final = Entry(session, 3);
        Assert.Equal("Done", final.Message.Text);
        Assert.Equal("e_03", final.ParentId);

        Session reloaded = Session.Load(session.Path);
        Assert.Equal(
            session.Entries.Select(SessionFormat.Serialize),
            reloaded.Entries.Select(SessionFormat.Serialize)
        );
    }

    [Fact]
    public async Task A_resumed_session_seeds_the_first_request_and_continues_the_chain()
    {
        using var temp = new TempDirectory();
        string path = temp.File("s.jsonl");
        Session created = Session.Create(path, "/work", new FixedTimeProvider(Start));
        created.AppendMessage(new ChatMessage(ChatRole.User, "earlier question"));
        created.AppendMessage(
            new ChatMessage(ChatRole.Assistant, "earlier answer"),
            "gpt-4o-mini",
            new SessionUsage(3, 1)
        );
        Session loaded = Session.Load(path, new FixedTimeProvider(Start));
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("fresh answer"),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = loaded }
        );

        await harness
            .RunAsync("new question", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        ScriptedRequest request = client.Requests[0];
        Assert.Equal(
            ["user", "assistant", "user"],
            request.Messages.Select(message => message.Role.Value)
        );
        Assert.Equal("earlier question", request.Messages[0].Text);
        Assert.Equal("earlier answer", request.Messages[1].Text);
        Assert.Equal("new question", request.Messages[2].Text);

        Assert.Equal(4, loaded.Entries.Count);
        SessionMessageEntry user = Entry(loaded, 2);
        Assert.Equal(ChatRole.User, user.Message.Role);
        Assert.Equal("e_02", user.ParentId);
        SessionMessageEntry assistant = Entry(loaded, 3);
        Assert.Equal("fresh answer", assistant.Message.Text);
        Assert.Equal("e_03", assistant.ParentId);
    }

    [Fact]
    public async Task Cancelled_tool_calls_are_mirrored_as_repaired_tool_entries()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        var tool = new BlockingTool("wait");
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Call("call-1", "wait", LoopScripts.Args(("path", "a.txt"))),
            LoopScripts.ToolCalls()
        );
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions { Session = session }
        );
        using var cancellation = new CancellationTokenSource();

        Task<List<AgentEvent>> run = harness
            .RunAsync("go", cancellation.Token)
            .ToListAsync(TestContext.Current.CancellationToken);
        await tool.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        cancellation.Cancel();
        await run;

        Assert.Equal(3, session.Entries.Count);
        SessionMessageEntry repaired = Entry(session, 2);
        Assert.Equal(ChatRole.Tool, repaired.Message.Role);
        Assert.Equal("e_02", repaired.ParentId);
        var result = Assert.IsType<FunctionResultContent>(Assert.Single(repaired.Message.Contents));
        Assert.Contains("cancelled by the user", result.Result!.ToString());
    }

    [Fact]
    public async Task A_sessioned_run_records_the_provider_cache_lanes()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        var details = new UsageDetails
        {
            InputTokenCount = 1500,
            OutputTokenCount = 50,
            CachedInputTokenCount = 1200,
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["CacheCreationInputTokens"] = 300,
            },
        };
        var client = new ScriptedChatClient().Enqueue(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Priced")])
            {
                ModelId = "claude-sonnet-5-5",
            },
            new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(details)]),
            new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                FinishReason = ChatFinishReason.Stop,
                ModelId = "claude-sonnet-5-5",
            }
        );
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = session }
        );

        List<AgentEvent> events = await harness
            .RunAsync("go", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        SessionMessageEntry assistant = Entry(session, 1);
        Assert.Equal(new SessionUsage(1500, 50, 1200, 300), assistant.Usage);
        UsageUpdated updated = Assert.Single(events.OfType<UsageUpdated>());
        Assert.Equal(1500, updated.Usage.InputTokenCount);
        Assert.Equal(50, updated.Usage.OutputTokenCount);
        Assert.Equal(1200, updated.Usage.CachedInputTokenCount);
        Assert.Equal(300, updated.Usage.AdditionalCounts!["CacheCreationInputTokens"]);
    }

    [Fact]
    public async Task A_sessioned_run_records_request_size_from_the_accounting_client()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        var scripted = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("Done"),
            LoopScripts.Stop()
        );
        var client = new ContextAccountingChatClient(scripted);
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = session }
        );

        List<AgentEvent> events = await harness
            .RunAsync("go", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        long expected = CompactionReducer.Utf8Length(scripted.Requests[0].Messages) / 4;
        SessionUsage usage = Assert.IsType<SessionUsage>(Entry(session, 1).Usage);
        Assert.Equal(expected, usage.Input);
        Assert.Equal(expected, Assert.Single(events.OfType<UsageUpdated>()).Usage.InputTokenCount);
    }

    [Fact]
    public async Task A_sessioned_run_leaves_the_cache_lanes_unknown_without_provider_counts()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        var client = new ScriptedChatClient().Enqueue(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Done")]),
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [new UsageContent(new UsageDetails { InputTokenCount = 11, OutputTokenCount = 5 })]
            ),
            new ChatResponseUpdate(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.Stop }
        );
        var harness = new AgentHarness(
            client,
            new ToolRegistry(),
            new AgentHarnessOptions { Session = session }
        );

        await harness
            .RunAsync("go", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        SessionUsage usage = Assert.IsType<SessionUsage>(Entry(session, 1).Usage);
        Assert.Equal(11, usage.Input);
        Assert.Equal(5, usage.Output);
        Assert.Null(usage.CacheRead);
        Assert.Null(usage.CacheWrite);
    }

    private static SessionMessageEntry Entry(Session session, int index) =>
        Assert.IsType<SessionMessageEntry>(session.Entries[index]);

    private sealed class BlockingTool(string name) : ITool
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name { get; } = name;

        public string Description => "Blocks until the run is cancelled.";

        public string SchemaJson => """{"type":"object"}""";

        public JsonElement ParametersSchema { get; } = ParseSchema();

        public ToolRisk Risk => ToolRisk.ReadOnly;

        public async Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        )
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new ToolResult("done", IsError: false);
        }

        private static JsonElement ParseSchema()
        {
            using JsonDocument document = JsonDocument.Parse("""{"type":"object"}""");
            return document.RootElement.Clone();
        }
    }
}
