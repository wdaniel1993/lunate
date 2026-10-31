using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

/// <summary>
/// Observation masking at batch points (#351): a tool result older than the mask window becomes a
/// one-line stub carrying its spill pointer where one exists, error results are kept longer, and a
/// result inside the window is untouched.
/// </summary>
public sealed class ContextMaskingBatchTests
{
    [Fact]
    public async Task A_result_inside_the_mask_window_is_untouched()
    {
        (AgentHarness harness, ScriptedChatClient client) = CreateChain(
            Registry(ReadTool(new string('x', 6_000))),
            [("call-1", "read")]
        );

        CancellationToken ct = TestContext.Current.CancellationToken;
        await harness.RunAsync("go", ct).ToListAsync(ct);
        await harness.RunAsync("Continue", ct).ToListAsync(ct);

        // Request 2 is already past the first batch point; the turn-1 result is one turn old, and
        // the success window is masked at an age strictly greater than one turn.
        Assert.Equal(new string('x', 6_000), ToolResultText(client, 1, "call-1"));
    }

    [Fact]
    public async Task An_old_success_becomes_a_one_line_stub()
    {
        (AgentHarness harness, ScriptedChatClient client) = CreateChain(
            Registry(ReadTool(new string('x', 6_000))),
            [("call-1", "read")]
        );

        CancellationToken ct = TestContext.Current.CancellationToken;
        await harness.RunAsync("go", ct).ToListAsync(ct);
        await harness.RunAsync("Continue", ct).ToListAsync(ct);

        string text = ToolResultText(client, 2, "call-1");
        Assert.StartsWith("[masked:", text, StringComparison.Ordinal);
        Assert.Contains("turns old", text, StringComparison.Ordinal);
        Assert.Contains("re-run the tool", text, StringComparison.Ordinal);
        Assert.False(text.Contains("\n", StringComparison.Ordinal));
        Assert.DoesNotContain("xxxxx", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_old_success_keeps_its_spill_pointer_in_the_stub()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), temp.Root);
        string full = new('x', 6_000);
        (AgentHarness harness, ScriptedChatClient client) = CreateSessionChain(
            temp.Root,
            session,
            Registry(ReadTool(full)),
            spillThresholdTokens: 1
        );

        CancellationToken ct = TestContext.Current.CancellationToken;
        List<AgentEvent> admitted = await harness.RunAsync("go", ct).ToListAsync(ct);
        await harness.RunAsync("Continue", ct).ToListAsync(ct);

        ToolCallResult pointer = Assert.Single(
            admitted.OfType<ToolCallResult>(),
            result => result.CallId == "call-1"
        );
        string spillPath = ToolOutputSpillTests.PathFromMarker(pointer.Output);
        Assert.Equal(full, File.ReadAllText(spillPath));

        string text = ToolResultText(client, 2, "call-1");
        Assert.StartsWith("[masked:", text, StringComparison.Ordinal);
        Assert.Contains("full output at " + spillPath + "]", text, StringComparison.Ordinal);
        Assert.False(text.Contains("\n", StringComparison.Ordinal));

        // The projection is request-time: the session entry keeps the admitted pointer text.
        Assert.Contains(
            "full output at " + spillPath,
            session
                .ToHistory()
                .Single(message => message.Role == ChatRole.Tool)
                .Contents.OfType<FunctionResultContent>()
                .Single(result => result.CallId == "call-1")
                .Result!.ToString()!,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task An_old_error_survives_past_the_success_window()
    {
        ScriptedTool okTool = ReadTool(new string('y', 6_000), name: "ok");
        ScriptedTool errTool = ReadTool(string.Empty, name: "err");
        errTool.OnExecute = (_, _) => new ToolResult("boom: the file is locked", IsError: true);
        (AgentHarness harness, ScriptedChatClient client) = CreateChain(
            Registry(okTool, errTool),
            [("call-ok", "ok"), ("call-err", "err")],
            maskTurns: 1,
            maskErrorTurns: 2
        );

        CancellationToken ct = TestContext.Current.CancellationToken;
        await harness.RunAsync("go", ct).ToListAsync(ct);
        await harness.RunAsync("Continue", ct).ToListAsync(ct);
        await harness.RunAsync("Continue", ct).ToListAsync(ct);

        // Request 3 (turn 3): the success result from turn 1 is two turns old — past the success
        // window of 1 — while the error result is still inside its own window of 2.
        Assert.StartsWith(
            "[masked:",
            ToolResultText(client, 2, "call-ok"),
            StringComparison.Ordinal
        );
        Assert.Equal("boom: the file is locked", ToolResultText(client, 2, "call-err"));

        // Request 4 (turn 4): the error is three turns old, past its own window. Its stub stays an
        // error stub; nothing reports a success that did not happen.
        string errorStub = ToolResultText(client, 3, "call-err");
        Assert.StartsWith("[masked:", errorStub, StringComparison.Ordinal);
        Assert.Contains("error result", errorStub, StringComparison.Ordinal);
        Assert.Contains("re-run the tool", errorStub, StringComparison.Ordinal);
        Assert.False(errorStub.Contains("\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_resumed_session_masks_an_old_result_with_its_pointer()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), temp.Root);
        string spillPath = temp.File("spilled.txt");
        session.AppendMessage(new ChatMessage(ChatRole.User, "go"));
        session.AppendMessage(
            new ChatMessage(
                ChatRole.Assistant,
                [new FunctionCallContent("call-old", "read", LoopScripts.Args(("path", "x.txt")))]
            )
        );
        session.AppendMessage(
            new ChatMessage(
                ChatRole.Tool,
                [
                    new FunctionResultContent(
                        "call-old",
                        new string('z', 4_000)
                            + $"\n\n... [100 characters omitted; full output at {spillPath}] ... read it with offset and limit."
                    ),
                ]
            )
        );
        (AgentHarness harness, ScriptedChatClient client) = CreateSessionChain(
            temp.Root,
            session,
            Registry(ReadTool(new string('x', 10))),
            spillThresholdTokens: 2_000,
            firstCallScripts: false
        );

        CancellationToken ct = TestContext.Current.CancellationToken;
        await harness.RunAsync("go", ct).ToListAsync(ct);
        await harness.RunAsync("Continue", ct).ToListAsync(ct);

        string text = ToolResultText(client, 1, "call-old");
        Assert.StartsWith("[masked:", text, StringComparison.Ordinal);
        Assert.Contains("full output at " + spillPath + "]", text, StringComparison.Ordinal);
        Assert.False(text.Contains("\n", StringComparison.Ordinal));
        Assert.DoesNotContain("zzzzz", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Savings_are_reported_by_mechanism()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s.jsonl"), temp.Root);
        (AgentHarness harness, ScriptedChatClient client) = CreateSessionChain(
            temp.Root,
            session,
            Registry(ReadTool(new string('x', 6_000))),
            spillThresholdTokens: 1
        );

        CancellationToken ct = TestContext.Current.CancellationToken;
        await harness.RunAsync("go", ct).ToListAsync(ct);
        await harness.RunAsync("Continue", ct).ToListAsync(ct);

        ContextSavings savings = harness.ContextSavings;
        Assert.True(savings.Spills > 0, "a spill is its own saving and must be counted");
        Assert.True(savings.Masking > 0, "a masked result must count its removed tokens");
        Assert.Equal(0, savings.ReadStubs);
        Assert.True(savings.Masking < 1_000, "a stub replaced the transcript");
    }

    /// <summary>
    /// A two-turn chain plus two continuations: request 1 performs the calls, request 2 closes the
    /// run while mid-window, then each continuation request crosses the batch point again.
    /// </summary>
    private static (AgentHarness Harness, ScriptedChatClient Client) CreateChain(
        ToolRegistry registry,
        (string CallId, string ToolName)[] calls,
        int maskTurns = 1,
        int maskErrorTurns = 20
    )
    {
        var first = new List<ChatResponseUpdate>
        {
            LoopScripts.Calls(
                new List<FunctionCallContent>(
                    calls.Select(call => new FunctionCallContent(
                        call.CallId,
                        call.ToolName,
                        LoopScripts.Args()
                    ))
                ).ToArray()
            ),
            LoopScripts.ToolCalls(),
        };
        var client = new ScriptedChatClient().Enqueue([.. first]);
        client = client
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("Continue"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("Continue"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            registry,
            new AgentHarnessOptions
            {
                ProjectionBatchTurns = 1,
                ProjectionMaskTurns = maskTurns,
                ProjectionMaskErrorTurns = maskErrorTurns,
            }
        );
        return (harness, client);
    }

    private static (AgentHarness Harness, ScriptedChatClient Client) CreateSessionChain(
        string workingDirectory,
        Session session,
        ToolRegistry registry,
        int spillThresholdTokens,
        bool firstCallScripts = true
    )
    {
        ScriptedChatClient client = firstCallScripts
            ? new ScriptedChatClient()
                .Enqueue(
                    LoopScripts.Call("call-1", "read", LoopScripts.Args(("path", "x.txt"))),
                    LoopScripts.ToolCalls()
                )
                .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop())
                .Enqueue(LoopScripts.Text("Continue"), LoopScripts.Stop())
            : new ScriptedChatClient()
                .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop())
                .Enqueue(LoopScripts.Text("Continue"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            registry,
            new AgentHarnessOptions
            {
                Session = session,
                WorkingDirectory = workingDirectory,
                SpillThresholdTokens = spillThresholdTokens,
                ProjectionBatchTurns = 1,
                ProjectionMaskTurns = 1,
                ProjectionMaskErrorTurns = 20,
            }
        );
        return (harness, client);
    }

    private static string ToolResultText(ScriptedChatClient client, int request, string callId) =>
        client
            .Requests[request]
            .Messages.SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single(result => result.CallId == callId)
            .Result!.ToString()!;
}
