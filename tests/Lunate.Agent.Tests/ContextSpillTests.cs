using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class ContextSpillTests
{
    [Fact]
    public async Task A_nine_thousand_character_result_costs_under_seven_hundred_tokens()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero))
        );
        var scripted = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "large.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var client = new ContextAccountingChatClient(scripted);
        var harness = new AgentHarness(
            client,
            Registry(ReadTool(new string('x', 9_000))),
            new AgentHarnessOptions { Session = session }
        );

        List<AgentEvent> events = await Run(harness);

        long inputTokens = Assert
            .IsType<SessionUsage>(
                session
                    .Entries.OfType<SessionMessageEntry>()
                    .Last(entry => entry.Message.Role == ChatRole.Assistant)
                    .Usage
            )
            .Input;
        Assert.True(
            inputTokens < 700,
            $"the request carrying a 9,000-character result was {inputTokens} estimated tokens"
        );
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.Contains("full output at ", result.Output, StringComparison.Ordinal);
        Assert.Equal(
            new string('x', 9_000),
            File.ReadAllText(ToolOutputSpillTests.PathFromMarker(result.Output))
        );
    }
}
