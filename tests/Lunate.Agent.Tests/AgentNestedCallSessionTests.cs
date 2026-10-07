using System.Text.Json;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

/// <summary>Session mirroring of nested tool calls: one bounded nestedCalls entry per top-level call.</summary>
public sealed class AgentNestedCallSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Nested_calls_are_mirrored_as_one_bounded_entry()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        ScriptedTool inner = ReadTool("nested contents", "inner");
        ScriptedTool outer = Caller("outer", "inner");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "outer", LoopScripts.Args(("x", "1"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(outer, inner),
            new AgentHarnessOptions { Session = session }
        );

        await Run(harness);

        SessionNestedCallsEntry entry = Assert.Single(
            session.Entries.OfType<SessionNestedCallsEntry>()
        );
        Assert.Equal("call_1", entry.CallId);
        SessionNestedCall call = Assert.Single(entry.Calls);
        Assert.Equal("inner", call.Name);
        Assert.Equal("{}", call.Args);
        Assert.Equal("ok", call.Status);
        Assert.True(call.DurationMs >= 0);

        Session reloaded = Session.Load(session.Path);
        SessionNestedCallsEntry reloadedEntry = Assert.Single(
            reloaded.Entries.OfType<SessionNestedCallsEntry>()
        );
        Assert.Equal(entry.CallId, reloadedEntry.CallId);
        Assert.Equal(call, Assert.Single(reloadedEntry.Calls));
    }

    [Fact]
    public async Task Nested_call_arguments_are_capped_when_mirrored()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        ScriptedTool inner = ReadTool("nested contents", "inner");
        ScriptedTool outer = Caller("outer", "inner", ArgsWithPath(250));
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "outer", LoopScripts.Args(("x", "1"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(outer, inner),
            new AgentHarnessOptions { Session = session }
        );

        await Run(harness);

        SessionNestedCall call = Assert.Single(
            Assert.Single(session.Entries.OfType<SessionNestedCallsEntry>()).Calls
        );
        string raw = inner.ReceivedArgsRaw!;
        Assert.True(raw.Length > 200);
        Assert.Equal(raw[..200] + "…", call.Args);
    }

    [Fact]
    public async Task A_clean_tool_call_writes_no_nested_calls_entry()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(ReadTool("contents")),
            new AgentHarnessOptions { Session = session }
        );

        await Run(harness);

        Assert.Empty(session.Entries.OfType<SessionNestedCallsEntry>());
    }

    [Fact]
    public async Task Nested_of_nested_calls_are_recorded_flat_in_one_entry()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        ScriptedTool inner = ReadTool("deep contents", "inner");
        ScriptedTool middle = Caller("middle", "inner");
        ScriptedTool outer = Caller("outer", "middle");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "outer", LoopScripts.Args(("x", "1"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(outer, middle, inner),
            new AgentHarnessOptions { Session = session }
        );

        await Run(harness);

        SessionNestedCallsEntry entry = Assert.Single(
            session.Entries.OfType<SessionNestedCallsEntry>()
        );
        Assert.Equal("call_1", entry.CallId);
        Assert.Equal(["inner", "middle"], entry.Calls.Select(call => call.Name));
        Assert.All(entry.Calls, call => Assert.Equal("ok", call.Status));
    }

    [Fact]
    public async Task A_failed_nested_call_is_mirrored_with_error_status()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        var inner = new ScriptedTool("inner", "Fails.", """{"type":"object"}""")
        {
            OnExecute = (_, _) => new ToolResult("boom", IsError: true),
        };
        ScriptedTool outer = Caller("outer", "inner");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "outer", LoopScripts.Args(("x", "1"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(outer, inner),
            new AgentHarnessOptions { Session = session }
        );

        await Run(harness);

        SessionNestedCall call = Assert.Single(
            Assert.Single(session.Entries.OfType<SessionNestedCallsEntry>()).Calls
        );
        Assert.Equal("error", call.Status);
    }

    private static ScriptedTool Caller(
        string name,
        string nestedName,
        JsonElement? nestedArgs = null
    )
    {
        var tool = new ScriptedTool(name, $"Calls {nestedName}.", """{"type":"object"}""");
        tool.OnExecuteAsync = async (_, ctx, ct) =>
        {
            var execute =
                ctx.ExecuteToolAsync
                ?? throw new InvalidOperationException("The context has no nested executor.");
            ToolResult nested = await execute(nestedName, nestedArgs ?? EmptyArgs(), ct);
            return new ToolResult($"outer saw: {nested.Output}", IsError: nested.IsError);
        };
        return tool;
    }

    private static JsonElement EmptyArgs()
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static JsonElement ArgsWithPath(int length)
    {
        using JsonDocument document = JsonDocument.Parse(
            $$"""{"path":"{{new string('x', length)}}"}"""
        );
        return document.RootElement.Clone();
    }
}
