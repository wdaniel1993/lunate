using Acp.JsonRpc;
using Acp.Schema;
using Lunate.Agent;

namespace Lunate.Protocols.Tests;

public sealed class LibAcpServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initialize_negotiates_the_version_down_and_reports_text_only_capabilities()
    {
        var log = new List<string>();
        var model = new AcpScriptedChatClient();
        await using AcpRuntime runtime = AcpTestSupport.Start(
            _ => AcpTestSupport.Harness(model),
            log.Add
        );

        InitializeResponse response = await runtime.Client.InitializeAsync(
            new InitializeRequest
            {
                ProtocolVersion = 42,
                ClientInfo = new Implementation { Name = "editor", Version = "1.0" },
                ClientCapabilities = new ClientCapabilities
                {
                    Fs = new FileSystemCapabilities { ReadTextFile = true },
                },
            },
            Ct
        );

        AgentCapabilities capabilities = Assert.IsType<AgentCapabilities>(
            response.AgentCapabilities
        );
        PromptCapabilities prompts = Assert.IsType<PromptCapabilities>(
            capabilities.PromptCapabilities
        );
        Assert.Equal(1, response.ProtocolVersion);
        Assert.Equal("lunate", Assert.IsType<Implementation>(response.AgentInfo).Name);
        Assert.Null(capabilities.LoadSession);
        Assert.False(prompts.Image);
        Assert.False(prompts.Audio);
        Assert.False(prompts.EmbeddedContext);
        Assert.Contains(log, line => line.Contains("responding with 1", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("file system", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Session_new_builds_the_harness_for_the_session_cwd()
    {
        using var temp = new TempDirectory();
        var capturedCwd = new List<string>();
        var model = new AcpScriptedChatClient();
        await using AcpRuntime runtime = AcpTestSupport.Start(cwd =>
        {
            capturedCwd.Add(cwd);
            return AcpTestSupport.Harness(model);
        });

        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequest, Ct);
        NewSessionResponse session = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );

        Assert.False(string.IsNullOrEmpty(session.SessionId.Value));
        Assert.Equal(temp.Root, Assert.Single(capturedCwd));
    }

    [Fact]
    public async Task Prompt_streams_updates_in_order_and_reports_end_turn()
    {
        using var temp = new TempDirectory();
        var model = new AcpScriptedChatClient()
            .Enqueue(
                AcpScripts.Text("Hel"),
                AcpScripts.Text("lo"),
                AcpScripts.Call(
                    "call-1",
                    "echo",
                    new Dictionary<string, object?> { ["message"] = "hi" }
                ),
                AcpScripts.ToolCalls()
            )
            .Enqueue(AcpScripts.Text("Done"), AcpScripts.Stop());
        await using AcpRuntime runtime = AcpTestSupport.Start(_ =>
            AcpTestSupport.Harness(model, new EchoTool())
        );

        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequest, Ct);
        NewSessionResponse session = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );

        PromptResponse response = await runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session.SessionId,
                Prompt = [new TextContent { Text = "hello" }],
            },
            Ct
        );

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal("hello", model.LastUserText);
        Assert.Collection(
            runtime.State.Updates,
            update =>
                Assert.Equal(
                    """{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Hel"}}""",
                    AcpTestSupport.Wire(update.Update)
                ),
            update =>
                Assert.Equal(
                    """{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"lo"}}""",
                    AcpTestSupport.Wire(update.Update)
                ),
            update =>
                Assert.Equal(
                    """{"sessionUpdate":"tool_call","toolCallId":"call-1","title":"echo","kind":"other","status":"in_progress"}""",
                    AcpTestSupport.Wire(update.Update)
                ),
            update =>
                Assert.Equal(
                    """{"sessionUpdate":"tool_call_update","toolCallId":"call-1","rawInput":{"message":"hi"}}""",
                    AcpTestSupport.Wire(update.Update)
                ),
            update =>
                Assert.Equal(
                    """{"sessionUpdate":"tool_call_update","toolCallId":"call-1"}""",
                    AcpTestSupport.Wire(update.Update)
                ),
            update =>
                Assert.Equal(
                    """{"sessionUpdate":"tool_call_update","toolCallId":"call-1","status":"completed","content":[{"type":"content","content":{"type":"text","text":"echo: hi"}}]}""",
                    AcpTestSupport.Wire(update.Update)
                ),
            update =>
                Assert.Equal(
                    """{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Done"}}""",
                    AcpTestSupport.Wire(update.Update)
                )
        );
        Assert.All(
            runtime.State.Updates,
            update => Assert.Equal(session.SessionId.Value, update.SessionId.Value)
        );
    }

    [Fact]
    public async Task Cancel_stops_the_run_and_no_updates_follow_the_response()
    {
        using var temp = new TempDirectory();
        var model = new AcpGatedChatClient();
        await using AcpRuntime runtime = AcpTestSupport.Start(_ => AcpTestSupport.Harness(model));

        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequest, Ct);
        NewSessionResponse session = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );

        Task<PromptResponse> prompt = runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session.SessionId,
                Prompt = [new TextContent { Text = "hello" }],
            },
            Ct
        );
        await runtime.State.WaitForUpdatesAsync(1, TimeSpan.FromSeconds(10), Ct);

        await runtime.Client.CancelAsync(
            new CancelNotification { SessionId = session.SessionId },
            Ct
        );
        PromptResponse response = await prompt.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(StopReason.Cancelled, response.StopReason);
        Assert.Equal(1, runtime.State.UpdateCount);
        await Task.Delay(200, Ct);
        Assert.Equal(1, runtime.State.UpdateCount);
    }

    [Fact]
    public async Task Cancel_during_tool_execution_sends_the_failed_result_and_nothing_follows()
    {
        using var temp = new TempDirectory();
        var model = new AcpScriptedChatClient().Enqueue(
            AcpScripts.Call("call-1", "gate", new Dictionary<string, object?>()),
            AcpScripts.ToolCalls()
        );
        var tool = new GatedTool();
        await using AcpRuntime runtime = AcpTestSupport.Start(_ =>
            AcpTestSupport.Harness(model, tool)
        );

        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequest, Ct);
        NewSessionResponse session = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );

        Task<PromptResponse> prompt = runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session.SessionId,
                Prompt = [new TextContent { Text = "hello" }],
            },
            Ct
        );
        await tool.Started.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        await runtime.Client.CancelAsync(
            new CancelNotification { SessionId = session.SessionId },
            Ct
        );
        await runtime.State.WaitForUpdatesAsync(4, TimeSpan.FromSeconds(10), Ct);
        PromptResponse response = await prompt.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(StopReason.Cancelled, response.StopReason);
        Assert.Equal(
            """{"sessionUpdate":"tool_call_update","toolCallId":"call-1","status":"failed","content":[{"type":"content","content":{"type":"text","text":"Tool call (gate) was cancelled by the user and was not executed."}}]}""",
            AcpTestSupport.Wire(runtime.State.Updates[^1].Update)
        );
        Assert.Equal(4, runtime.State.UpdateCount);
        await Task.Delay(200, Ct);
        Assert.Equal(4, runtime.State.UpdateCount);
    }

    [Fact]
    public async Task Cancel_while_a_prompt_is_queued_reports_cancellation_for_that_prompt()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();
        var gated = new AcpGatedChatClient();
        var queued = new AcpScriptedChatClient().Enqueue(AcpScripts.Text("never"));
        await using AcpRuntime runtime = AcpTestSupport.Start(cwd =>
            cwd == first.Root ? AcpTestSupport.Harness(gated) : AcpTestSupport.Harness(queued)
        );

        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequest, Ct);
        NewSessionResponse session1 = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = first.Root, McpServers = [] },
            Ct
        );
        NewSessionResponse session2 = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = second.Root, McpServers = [] },
            Ct
        );

        Task<PromptResponse> firstPrompt = runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session1.SessionId,
                Prompt = [new TextContent { Text = "first" }],
            },
            Ct
        );
        await runtime.State.WaitForUpdatesAsync(1, TimeSpan.FromSeconds(10), Ct);

        Task<PromptResponse> secondPrompt = runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session2.SessionId,
                Prompt = [new TextContent { Text = "second" }],
            },
            Ct
        );
        await runtime.Client.CancelAsync(
            new CancelNotification { SessionId = session2.SessionId },
            Ct
        );
        await runtime.Client.CancelAsync(
            new CancelNotification { SessionId = session1.SessionId },
            Ct
        );

        PromptResponse firstResponse = await firstPrompt.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        PromptResponse secondResponse = await secondPrompt.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(StopReason.Cancelled, firstResponse.StopReason);
        Assert.Equal(StopReason.Cancelled, secondResponse.StopReason);
    }

    [Fact]
    public async Task Unknown_method_is_a_json_rpc_error()
    {
        var model = new AcpScriptedChatClient();
        await using AcpRuntime runtime = AcpTestSupport.Start(_ => AcpTestSupport.Harness(model));

        RequestErrorException error = await Assert.ThrowsAsync<RequestErrorException>(() =>
            runtime.Client.ExtMethodAsync("lunate/mystery", null, Ct)
        );

        Assert.Equal(-32601, error.Code);
    }

    [Fact]
    public async Task Prompt_logs_and_skips_non_text_content_blocks()
    {
        using var temp = new TempDirectory();
        var log = new List<string>();
        var model = new AcpScriptedChatClient().Enqueue(AcpScripts.Text("ok"), AcpScripts.Stop());
        await using AcpRuntime runtime = AcpTestSupport.Start(
            _ => AcpTestSupport.Harness(model),
            log.Add
        );

        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequest, Ct);
        NewSessionResponse session = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );

        PromptResponse response = await runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session.SessionId,
                Prompt =
                [
                    new TextContent { Text = "hello" },
                    new ImageContent { Data = "aGVsbG8=", MimeType = "image/png" },
                ],
            },
            Ct
        );

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal("hello", model.LastUserText);
        Assert.Contains(
            log,
            line =>
                line.Contains("skipping non-text content block (image)", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_prompt_for_an_unknown_session_is_a_json_rpc_error()
    {
        var model = new AcpScriptedChatClient();
        await using AcpRuntime runtime = AcpTestSupport.Start(_ => AcpTestSupport.Harness(model));

        RequestErrorException error = await Assert.ThrowsAsync<RequestErrorException>(() =>
            runtime.Client.PromptAsync(
                new PromptRequest
                {
                    SessionId = new SessionId("missing"),
                    Prompt = [new TextContent { Text = "hello" }],
                },
                Ct
            )
        );

        Assert.Contains("unknown session", error.Message, StringComparison.Ordinal);
    }
}
