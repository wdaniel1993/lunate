using System.Text;
using Lunate.Coding;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class ReadLedgerProjectionTests
{
    [Fact]
    public async Task An_out_of_band_change_after_an_edit_refuses_and_stubs_the_old_read()
    {
        using var temp = new TempDirectory();
        string contents = string.Join('\n', Enumerable.Range(1, 2_000).Select(Line)) + "\n";
        File.WriteAllText(temp.File("large.txt"), contents);
        Session session = Session.Create(
            temp.File("s.jsonl"),
            temp.Root,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero))
        );
        var external = new ScriptedTool(
            "external",
            "Changes a file without publishing an event.",
            """{"type":"object"}"""
        )
        {
            OnExecute = (_, _) =>
            {
                File.WriteAllText(
                    temp.File("large.txt"),
                    "first-edit\n"
                        + string.Join('\n', Enumerable.Range(2, 1_998).Select(Line))
                        + "\noutside\n"
                );
                return new ToolResult("changed outside the file tools", IsError: false);
            },
        };
        var scripted = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("read-call", "read", LoopScripts.Args(("path", "large.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "first-edit",
                    "edit",
                    LoopScripts.Args(
                        ("path", "large.txt"),
                        ("old_text", Line(1)),
                        ("new_text", "first-edit")
                    )
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("external-call", "external", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call(
                    "stale-edit",
                    "edit",
                    LoopScripts.Args(
                        ("path", "large.txt"),
                        ("old_text", Line(2_000)),
                        ("new_text", "stale-edit")
                    )
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            new ContextAccountingChatClient(scripted),
            Registry(
                new ReadTool(new Workspace(temp.Root)),
                new EditTool(new Workspace(temp.Root)),
                external
            ),
            new AgentHarnessOptions
            {
                Session = session,
                WorkingDirectory = temp.Root,
                ToolOutputLimit = 100_000,
                SpillThresholdTokens = 100_000,
            }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult stale = Assert.Single(
            events.OfType<ToolCallResult>(),
            result => result.CallId == "stale-edit"
        );
        Assert.True(stale.IsError);
        Assert.Contains(
            "large.txt changed after your last read",
            stale.Output,
            StringComparison.Ordinal
        );
        Assert.Contains("re-read", stale.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "first-edit\n"
                + string.Join('\n', Enumerable.Range(2, 1_998).Select(Line))
                + "\noutside\n",
            File.ReadAllText(temp.File("large.txt"))
        );

        FunctionResultContent priorRead = Assert.Single(
            scripted
                .Requests[3]
                .Messages.Where(message => message.Role == ChatRole.Tool)
                .SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>(),
            result => result.CallId == "read-call"
        );
        Assert.Contains("[stale:", priorRead.Result!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("     1|", priorRead.Result.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            session.Entries.OfType<SessionMessageEntry>(),
            entry =>
                entry
                    .Message.Contents.OfType<FunctionResultContent>()
                    .Any(result =>
                        result.CallId == "read-call"
                        && result.Result!.ToString()!.Contains("     1|", StringComparison.Ordinal)
                    )
        );
        string originalRead = session
            .Entries.OfType<SessionMessageEntry>()
            .SelectMany(entry => entry.Message.Contents)
            .OfType<FunctionResultContent>()
            .Single(result => result.CallId == "read-call")
            .Result!.ToString()!;
        long savedTokens =
            (
                Encoding.UTF8.GetByteCount(originalRead)
                - Encoding.UTF8.GetByteCount(priorRead.Result!.ToString()!)
            ) / 4;
        Assert.True(savedTokens >= 4_000, $"the stale read saves {savedTokens} estimated tokens");
    }

    private static string Line(int number) => $"line-{number:D4}";
}
