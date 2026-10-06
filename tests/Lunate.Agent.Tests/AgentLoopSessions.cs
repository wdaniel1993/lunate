using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

internal sealed record AgentLoopSession(
    string Name,
    string UserInput,
    IReadOnlyList<ChatResponseUpdate[]> Exchanges,
    Func<ToolRegistry> CreateTools,
    int ModelCalls,
    int ToolCalls
)
{
    internal string FixturePath => Path.Combine(AgentLoopReplay.FixturesDirectory, Name + ".jsonl");

    internal string SnapshotPath =>
        Path.Combine(AgentLoopReplay.SnapshotsDirectory, Name + ".events.txt");
}

/// <summary>The three acceptance sessions, recorded once into committed fixtures (T-09 task 6).</summary>
internal static class AgentLoopSessions
{
    internal static IReadOnlyList<AgentLoopSession> All { get; } =
    [TextOnly(), SingleToolCall(), MultiStep()];

    internal static AgentLoopSession TextOnly() =>
        new(
            "agent-loop-text",
            "Say hello.",
            [
                [
                    LoopScripts.Text("Hello! "),
                    LoopScripts.Text("How can I help?"),
                    LoopScripts.Stop(),
                ],
            ],
            static () => new ToolRegistry(),
            ModelCalls: 1,
            ToolCalls: 0
        );

    internal static AgentLoopSession SingleToolCall() =>
        new(
            "agent-loop-single-tool",
            "List the files.",
            [
                [
                    LoopScripts.Text("I will check the workspace. "),
                    LoopScripts.CallFragment("call-1", "list_files", """{"directory":"""),
                    LoopScripts.CallFragment("call-1", string.Empty, """ "."}"""),
                    LoopScripts.ToolCalls(),
                ],
                [LoopScripts.Text("The workspace has a.txt and b.txt."), LoopScripts.Stop()],
            ],
            static () =>
            {
                var registry = new ToolRegistry();
                registry.Add(
                    new ScriptedTool(
                        "list_files",
                        "Lists the files in a directory.",
                        """{"type":"object","properties":{"directory":{"type":"string"}}}"""
                    )
                    {
                        OnExecute = (_, _) => new ToolResult("a.txt\nb.txt", IsError: false),
                    }
                );
                return registry;
            },
            ModelCalls: 2,
            ToolCalls: 1
        );

    internal static AgentLoopSession MultiStep() =>
        new(
            "agent-loop-multi-step",
            "Read both files.",
            [
                [
                    LoopScripts.Call("call-1", "read", LoopScripts.Args(("path", "a.txt"))),
                    LoopScripts.ToolCalls(),
                ],
                [
                    LoopScripts.Call("call-2", "read", LoopScripts.Args(("path", "b.txt"))),
                    LoopScripts.ToolCalls(),
                ],
                [LoopScripts.Text("I read both files."), LoopScripts.Stop()],
            ],
            static () =>
            {
                var registry = new ToolRegistry();
                registry.Add(
                    new ScriptedTool(
                        "read",
                        "Reads a file.",
                        """{"type":"object","properties":{"path":{"type":"string"}}}"""
                    )
                    {
                        OnExecute = (args, _) =>
                            new ToolResult(
                                $"contents of {args.GetProperty("path").GetString()}",
                                IsError: false
                            ),
                    }
                );
                return registry;
            },
            ModelCalls: 3,
            ToolCalls: 2
        );
}
