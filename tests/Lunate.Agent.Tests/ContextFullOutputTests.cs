using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class ContextFullOutputTests
{
    [Fact]
    public async Task An_equal_full_output_is_absent()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("session.jsonl"), temp.Root);
        var events = await RunAsync(
            new ToolResult("short result", IsError: false) { FullOutput = "short result" },
            new AgentHarnessOptions { Session = session, WorkingDirectory = temp.Root }
        );

        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.Equal("short result", result.Output);
        Assert.False(Directory.Exists(Path.Combine(temp.Root, session.SessionId, "tool-output")));
    }

    [Fact]
    public async Task An_error_result_can_spill_its_full_output()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("session.jsonl"), temp.Root);
        string transcript = new('t', 9_000);
        var events = await RunAsync(
            new ToolResult("command failed", IsError: true) { FullOutput = transcript },
            new AgentHarnessOptions { Session = session, WorkingDirectory = temp.Root }
        );

        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.StartsWith("command failed\n\n... [", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(transcript, result.Output, StringComparison.Ordinal);
        Assert.Equal(transcript, File.ReadAllText(ToolOutputSpillTests.PathFromMarker(result.Output)));
    }

    [Fact]
    public async Task The_loop_preserves_the_tool_answer_before_its_pointer()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("session.jsonl"), temp.Root);
        const string answer = "The exact answer.\nKeep every punctuation mark!";
        var events = await RunAsync(
            new ToolResult(answer, IsError: false) { FullOutput = new string('z', 9_000) },
            new AgentHarnessOptions { Session = session, WorkingDirectory = temp.Root }
        );

        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.StartsWith(answer + "\n\n... [", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_session_the_bounded_transcript_follows_the_answer()
    {
        using var temp = new TempDirectory();
        const string answer = "answer";
        var events = await RunAsync(
            new ToolResult(answer, IsError: false) { FullOutput = new string('q', 1_000) },
            new AgentHarnessOptions
            {
                ToolOutputLimit = 100,
                WorkingDirectory = temp.Root,
            }
        );

        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.StartsWith(answer + "\n\n", result.Output, StringComparison.Ordinal);
        Assert.Contains("characters truncated", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("full output at ", result.Output, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(temp.Root, "tool-output", SearchOption.AllDirectories));
    }

    private static async Task<List<AgentEvent>> RunAsync(
        ToolResult toolResult,
        AgentHarnessOptions options
    )
    {
        var tool = new ScriptedTool("answer", "Returns a scripted result.", """{"type":"object"}""")
        {
            OnExecute = (_, _) => toolResult,
        };
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "answer", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(tool), options);
        return await Run(harness);
    }
}
