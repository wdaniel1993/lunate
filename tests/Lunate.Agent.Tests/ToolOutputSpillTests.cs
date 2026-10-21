using System.Text.Json;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class ToolOutputSpillTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_result_over_the_limit_writes_the_full_output_to_the_session_directory()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        string full = FullOutput();

        (List<AgentEvent> events, string output) = await RunReadAsync(session, full);

        string path = PathFromMarker(output);
        string directory = Path.Combine(temp.Root, session.SessionId, "tool-output");
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.Equal(directory, Path.GetDirectoryName(path));
        Assert.Equal(full, File.ReadAllText(path));
        string runId = Assert.Single(events.OfType<RunStarted>()).RunId;
        Assert.Equal(runId + "-1.txt", Path.GetFileName(path));
    }

    [Fact]
    public async Task A_result_at_or_under_the_limit_writes_nothing()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        string full = new('x', 100);

        (_, string output) = await RunReadAsync(session, full);

        Assert.Equal(full, output);
        Assert.False(Directory.Exists(Path.Combine(temp.Root, session.SessionId, "tool-output")));
    }

    [Fact]
    public async Task A_resumed_session_reads_the_spilled_path_from_the_marker()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        string full = FullOutput();

        (_, string output) = await RunReadAsync(session, full);

        Session resumed = Session.Load(session.Path);
        ChatMessage tool = Assert.Single(
            resumed.ToHistory(),
            message => message.Role == ChatRole.Tool
        );
        FunctionResultContent result = Assert.IsType<FunctionResultContent>(
            Assert.Single(tool.Contents)
        );
        string marker = Assert.IsType<JsonElement>(result.Result).GetString()!;
        string path = PathFromMarker(marker);
        Assert.Equal(full, File.ReadAllText(path));
    }

    [Fact]
    public void Writing_the_same_run_and_sequence_overwrites_its_file()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );

        ToolOutputSpillResult first = ToolOutputSpill.TryWrite(session, "run_1", 1, "first");
        ToolOutputSpillResult second = ToolOutputSpill.TryWrite(session, "run_1", 1, "second");

        Assert.NotNull(first.Path);
        Assert.Equal(first.Path, second.Path);
        Assert.Equal("second", File.ReadAllText(first.Path));
        string directory = Path.Combine(temp.Root, session.SessionId, "tool-output");
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void An_output_over_the_ceiling_is_not_written()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(
            temp.File("s.jsonl"),
            "/work",
            new FixedTimeProvider(Start)
        );
        string output = new('\u0800', (int)(ToolOutputSpill.MaxBytes / 3) + 1);

        ToolOutputSpillResult result = ToolOutputSpill.TryWrite(session, "run_1", 1, output);

        Assert.True(result.TooLarge);
        Assert.Null(result.Path);
        Assert.False(Directory.Exists(Path.Combine(temp.Root, session.SessionId, "tool-output")));
    }

    private static async Task<(List<AgentEvent> Events, string Output)> RunReadAsync(
        Session session,
        string full
    )
    {
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(ReadTool(full)),
            new AgentHarnessOptions { Session = session, ToolOutputLimit = 100 }
        );

        List<AgentEvent> events = await Run(harness);
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        return (events, result.Output);
    }

    private static string FullOutput() => string.Join("\n", Enumerable.Repeat("line", 200));

    internal static string PathFromMarker(string marker)
    {
        const string prefix = "full output at ";
        int start = marker.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"marker has no spill path: {marker}");
        start += prefix.Length;
        int end = marker.IndexOf(']', start);
        Assert.True(end > start, $"marker has no closing bracket: {marker}");
        return marker[start..end];
    }
}
