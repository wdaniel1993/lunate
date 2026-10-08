using System.Text.Json;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentHookPointsTestSupport;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentHookPointsTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_run_without_hooks_matches_a_run_with_default_hook_points()
    {
        ScriptedChatClient withoutClient = ScriptedClientWithReadCall();
        List<AgentEvent> without = await Run(
            withoutClient,
            Registry(AnnotatedReadTool()),
            new AgentHarnessOptions { SystemPrompt = "base" }
        );

        ScriptedChatClient withClient = ScriptedClientWithReadCall();
        List<AgentEvent> with = await Run(
            withClient,
            Registry(AnnotatedReadTool()),
            new AgentHarnessOptions { SystemPrompt = "base", Hooks = new EmptyHookPoints() }
        );

        Assert.Equal(
            without.Select(agentEvent => agentEvent.GetType()),
            with.Select(e => e.GetType())
        );
        Assert.Equal(Messages(withoutClient), Messages(withClient));
    }

    [Fact]
    public async Task Tool_calling_context_carries_the_resolved_tools_annotations()
    {
        var hooks = new RecordingHookPoints();
        ScriptedTool write = new("write", "Writes.", """{"type":"object"}""", risk: ToolRisk.Write)
        {
            Annotations = new ToolAnnotations(Destructive: true, Idempotent: true),
        };
        ScriptedChatClient client = new ScriptedChatClient()
            .Enqueue(
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new FunctionCallContent("call-1", "write", new Dictionary<string, object?>())]
                ),
                new ChatResponseUpdate(ChatRole.Assistant, [])
                {
                    FinishReason = ChatFinishReason.ToolCalls,
                }
            )
            .Enqueue(AgentHookTestSupport.TextUpdate("done", ChatFinishReason.Stop));
        var harness = new AgentHarness(
            client,
            Registry(write),
            new AgentHarnessOptions { Hooks = hooks }
        );

        await Run(harness);

        Assert.Equal(
            new ToolAnnotations(Destructive: true, Idempotent: true),
            hooks.ToolCallings.Single().Annotations
        );
    }

    [Fact]
    public async Task Run_starting_edits_sections_and_selects_active_tools()
    {
        var hooks = new RecordingHookPoints
        {
            RunStarting = _ => new AgentRunStartingResult.Apply(
                [new AgentPromptSectionEdit("memory", "remember")],
                ["read"]
            ),
        };
        ScriptedChatClient client = new ScriptedChatClient().Enqueue(
            AgentHookTestSupport.TextUpdate("done", ChatFinishReason.Stop)
        );
        var harness = new AgentHarness(
            client,
            Registry(
                ReadTool("contents"),
                new ScriptedTool("write", "Writes.", """{"type":"object"}""")
            ),
            new AgentHarnessOptions { SystemPrompt = "base", Hooks = hooks }
        );

        await Run(harness);

        Assert.Equal(["read", "write"], hooks.RunStartings.Single().Tools);
        string system = string.Concat(
            client.Requests[0].Messages[0].Contents.OfType<TextContent>().Select(t => t.Text)
        );
        Assert.Contains("base", system, StringComparison.Ordinal);
        Assert.Contains("remember", system, StringComparison.Ordinal);
        Assert.Equal(["read"], client.Requests[0].Options!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public async Task Context_building_adds_messages_before_every_request()
    {
        var hooks = new RecordingHookPoints
        {
            ContextBuilding = _ => new AgentContextBuildingResult([
                new AgentContextMessage("system", "ctx"),
            ]),
        };
        ScriptedChatClient client = new ScriptedChatClient()
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
            .Enqueue(AgentHookTestSupport.TextUpdate("done", ChatFinishReason.Stop));
        var harness = new AgentHarness(
            client,
            Registry(ReadTool("contents")),
            new AgentHarnessOptions { SystemPrompt = "base", Hooks = hooks }
        );

        await Run(harness);

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(2, hooks.ContextBuildings.Count);
        foreach (var request in client.Requests)
        {
            Assert.Equal(ChatRole.System, request.Messages[0].Role);
            Assert.Equal(ChatRole.System, request.Messages[1].Role);
            Assert.Equal("ctx", ((TextContent)request.Messages[1].Contents[0]).Text);
            Assert.Equal(ChatRole.User, request.Messages[2].Role);
        }
    }

    [Fact]
    public async Task Provider_stream_events_observe_every_raw_update()
    {
        var hooks = new RecordingHookPoints();
        ScriptedChatClient client = new ScriptedChatClient().Enqueue(
            AgentHookTestSupport.TextUpdate("he"),
            AgentHookTestSupport.TextUpdate("llo", ChatFinishReason.Stop)
        );
        var harness = new AgentHarness(
            client,
            Registry(),
            new AgentHarnessOptions { Hooks = hooks }
        );

        await Run(harness);

        Assert.Equal(2, hooks.StreamEvents.Count);
        Assert.All(hooks.StreamEvents, observed => Assert.NotEmpty(observed.RunId));
        Assert.Equal(
            "hello",
            string.Concat(
                hooks.StreamEvents.SelectMany(observed =>
                    observed.Update.Contents.OfType<TextContent>().Select(text => text.Text)
                )
            )
        );
    }

    [Fact]
    public async Task Message_completed_replaces_the_persisted_assistant_message()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("session.jsonl"), "/work");
        var hooks = new RecordingHookPoints
        {
            MessageCompleted = _ => new AgentMessageCompletedResult.Replace("final"),
        };
        ScriptedChatClient client = new ScriptedChatClient().Enqueue(
            AgentHookTestSupport.TextUpdate("draft", ChatFinishReason.Stop)
        );
        var harness = new AgentHarness(
            client,
            Registry(),
            new AgentHarnessOptions { Hooks = hooks, Session = session }
        );

        await Run(harness);

        Assert.Equal("draft", hooks.MessageCompletions.Single().Text);
        SessionMessageEntry assistant = session
            .Entries.OfType<SessionMessageEntry>()
            .Single(entry => entry.Message.Role == ChatRole.Assistant);
        Assert.Equal("final", ((TextContent)assistant.Message.Contents[0]).Text);
    }

    [Fact]
    public async Task Tool_calling_mutates_arguments_before_approval_and_execution()
    {
        var hooks = new RecordingHookPoints
        {
            ToolCalling = _ => new AgentToolCallingResult.Proceed(
                AgentHookTestSupport.Json("""{"path":"b.txt"}""")
            ),
        };
        ScriptedTool read = ReadTool("contents");
        var approver = new RecordingApprover(true);
        ScriptedChatClient client = ScriptedClientWithReadCall();
        var harness = new AgentHarness(
            client,
            Registry(read),
            new AgentHarnessOptions { Hooks = hooks, Approver = approver }
        );

        await Run(harness);

        Assert.Equal(
            "a.txt",
            hooks.ToolCallings.Single().Arguments.GetProperty("path").GetString()
        );
        Assert.Contains("b.txt", approver.Calls.Single().Args, StringComparison.Ordinal);
        Assert.Contains("b.txt", read.ReceivedArgsRaw!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tool_calling_block_is_a_denial_style_result_and_never_runs_the_tool()
    {
        var hooks = new RecordingHookPoints
        {
            ToolCalling = _ => new AgentToolCallingResult.Block("secrets are off limits"),
        };
        ScriptedTool read = ReadTool("contents");
        var approver = new RecordingApprover(true);
        ScriptedChatClient client = ScriptedClientWithReadCall();
        var harness = new AgentHarness(
            client,
            Registry(read),
            new AgentHarnessOptions { Hooks = hooks, Approver = approver }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(approver.Calls);
        Assert.Null(read.ReceivedContext);
        ToolCallResult result = events.OfType<ToolCallResult>().Single();
        Assert.True(result.IsError);
        Assert.Contains("secrets are off limits", result.Output, StringComparison.Ordinal);
        Assert.Contains("was blocked", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tool_result_ready_transforms_output_and_attaches_data()
    {
        var hooks = new RecordingHookPoints
        {
            ToolResultReady = _ => new AgentToolResultReadyResult(
                "redacted",
                AgentHookTestSupport.Json("""{"x":1}""")
            ),
        };
        ScriptedChatClient client = ScriptedClientWithReadCall();
        var harness = new AgentHarness(
            client,
            Registry(ReadTool("original")),
            new AgentHarnessOptions { Hooks = hooks }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult result = events.OfType<ToolCallResult>().Single();
        Assert.Equal("redacted", result.Output);
        JsonElement details = Assert.IsType<JsonElement>(result.Details);
        Assert.Equal(1, details.GetProperty("x").GetInt32());
        Assert.Equal("original", hooks.ToolResults.Single().Output);
    }

    [Fact]
    public async Task Turn_ended_appends_extension_entries_and_grants_one_continuation()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("session.jsonl"), "/work");
        int turn = 0;
        var hooks = new RecordingHookPoints
        {
            TurnEnded = _ =>
                turn++ == 0
                    ? new AgentTurnEndedResult(
                        [
                            new AgentExtensionEntry(
                                "memory-ext",
                                "note",
                                AgentHookTestSupport.Json("""{"n":1}""")
                            ),
                        ],
                        true
                    )
                    : new AgentTurnEndedResult([], false),
        };
        ScriptedChatClient client = new ScriptedChatClient()
            .Enqueue(AgentHookTestSupport.TextUpdate("one", ChatFinishReason.Stop))
            .Enqueue(AgentHookTestSupport.TextUpdate("two", ChatFinishReason.Stop));
        var harness = new AgentHarness(
            client,
            Registry(),
            new AgentHarnessOptions { Hooks = hooks, Session = session }
        );

        await Run(harness);

        Assert.Equal(2, client.Requests.Count);
        SessionExtensionEntry entry = session.Entries.OfType<SessionExtensionEntry>().Single();
        Assert.Equal("ext/memory-ext/note", entry.ExtensionType);
        Assert.Contains("\"n\":1", entry.PayloadJson, StringComparison.Ordinal);
        Assert.Equal(2, hooks.TurnEnds.Count);
    }

    [Fact]
    public async Task Run_settled_observes_after_a_plain_run_in_order()
    {
        var hooks = new RecordingHookPoints();
        ScriptedChatClient client = new ScriptedChatClient().Enqueue(
            AgentHookTestSupport.TextUpdate("done", ChatFinishReason.Stop)
        );
        var harness = new AgentHarness(
            client,
            Registry(),
            new AgentHarnessOptions { Hooks = hooks }
        );

        await Run(harness);

        Assert.Equal(
            [
                "RunStarting",
                "ContextBuilding",
                "ProviderStreamEvent",
                "MessageCompleted",
                "TurnEnded",
                "RunSettled",
            ],
            hooks.Calls
        );
        Assert.Single(hooks.Settles);
    }
}
