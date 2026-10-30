using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class ToolOutputSpillReadTests
{
    [Fact]
    public async Task A_spill_path_is_readable_in_full()
    {
        using var temp = new TempDirectory();
        string workspace = CreateWorkspace(temp);
        Session session = CreateSession(temp, workspace);
        string full = new('x', 9_000);
        ToolOutputSpillResult spill = ToolOutputSpill.TryWrite(session, "run", 1, full);
        var tool = new ReadTool(new Workspace(workspace));

        ToolResult result = await ReadAsync(tool, spill.Path!, workspace, session);

        Assert.False(result.IsError);
        Assert.Equal("     1|" + full, result.Output);
    }

    [Fact]
    public async Task A_resumed_session_can_read_its_spill_path()
    {
        using var temp = new TempDirectory();
        string workspace = CreateWorkspace(temp);
        Session session = CreateSession(temp, workspace);
        string full = new('y', 9_000);
        ToolOutputSpillResult spill = ToolOutputSpill.TryWrite(session, "run", 1, full);
        Session resumed = Session.Load(session.Path);
        var tool = new ReadTool(new Workspace(workspace));

        ToolResult result = await ReadAsync(tool, spill.Path!, workspace, resumed);

        Assert.False(result.IsError);
        Assert.Equal("     1|" + full, result.Output);
    }

    [Fact]
    public async Task A_session_does_not_allow_a_sibling_path_outside_tool_output()
    {
        using var temp = new TempDirectory();
        string workspace = CreateWorkspace(temp);
        Session session = CreateSession(temp, workspace);
        string sessionDirectory = Path.Combine(
            Path.GetDirectoryName(session.Path)!,
            session.SessionId
        );
        Directory.CreateDirectory(sessionDirectory);
        string sibling = Path.Combine(sessionDirectory, "sibling.txt");
        File.WriteAllText(sibling, "not a spill");
        var tool = new ReadTool(new Workspace(workspace));

        ToolResult result = await ReadAsync(tool, sibling, workspace, session);

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace", result.Output, StringComparison.Ordinal);
    }

    private static string CreateWorkspace(TempDirectory temp)
    {
        string workspace = temp.File("workspace");
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static Session CreateSession(TempDirectory temp, string workspace) =>
        Session.Create(temp.File("sessions/session.jsonl"), workspace);

    private static Task<ToolResult> ReadAsync(
        ReadTool tool,
        string path,
        string workingDirectory,
        Session? session
    )
    {
        using JsonDocument args = JsonDocument.Parse(JsonSerializer.Serialize(new { path }));
        return tool.ExecuteAsync(
            args.RootElement,
            new ToolContext(workingDirectory, new NullAgentEvents()) { Session = session },
            CancellationToken.None
        );
    }
}
