using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// A resumed session containing a call to an extension tool that is not present continues and
/// renders through the default view: no renderer is registered, the recorded call carries into the
/// next request, and the default tool block renders the name without a crash (design D9).
/// </summary>
public sealed class ExtensionToolResumeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_resumed_session_with_a_recorded_call_to_an_absent_extension_tool_renders()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string sessions = temp.File("sessions");
        Directory.CreateDirectory(sessions);
        string sessionPath = Path.Combine(sessions, "session.jsonl");
        Session recorded = Session.Create(sessionPath, worktree, null, worktree);
        recorded.AppendMessage(new ChatMessage(ChatRole.User, "earlier question"));
        recorded.AppendMessage(
            new ChatMessage(
                ChatRole.Assistant,
                [
                    new FunctionCallContent(
                        "call-1",
                        "tools__echo",
                        new Dictionary<string, object?>()
                    ),
                ]
            )
        );
        recorded.AppendMessage(
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "echo: earlier")])
        );
        recorded.AppendMessage(new ChatMessage(ChatRole.Assistant, "earlier answer"));

        await using ExtensionSession host = await ExtensionSession.StartAsync(
            new Workspace(worktree).Identity,
            new ExtensionSeams
            {
                StorePath = temp.File("store"),
                Log = new RecordingExtensionLog(),
            },
            Ct
        );
        Session resumed = Session.Load(sessionPath);
        ToolRegistry tools = HarnessFactory.CreateTools(new Workspace(worktree));
        foreach (ITool tool in host.Tools)
        {
            tools.Add(tool);
        }

        var client = new ScriptedChatClient().Enqueue(Scripts.Text("continued"), Scripts.Stop());
        var harness = new AgentHarness(
            client,
            tools,
            new AgentHarnessOptions { WorkingDirectory = worktree, Session = resumed }
        );
        List<AgentEvent> events = [];
        await foreach (AgentEvent agentEvent in harness.RunAsync("next question", Ct))
        {
            events.Add(agentEvent);
        }

        Assert.Equal(StopReasons.Stop, Assert.Single(events.OfType<RunFinished>()).StopReason);

        // No renderer is registered for the absent extension tool: the resolution signal the
        // session's fallback rides on is false (the real session path is driven by the
        // interactive test below).
        Assert.False(
            host.ToolResultRenderers.TryRender(
                "tools__echo",
                new ToolResultView("tools__echo", "call-1", "echo: earlier", IsError: false),
                out IReadOnlyList<TextSegment> segments
            )
        );
        Assert.Empty(segments);
    }

    [Fact]
    public async Task A_call_to_an_absent_extension_tool_renders_through_the_session()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(
            Scripts.Call("call-1", "tools__echo", Scripts.Args(("text", "hi"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "\u2713 tools__echo");

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}
