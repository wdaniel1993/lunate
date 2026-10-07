using System.Text.Json;
using Lunate.Agent;
using Lunate.Coding;

namespace Lunate.Extensibility.Tests;

public sealed class FileChangeIntegrationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    [Fact]
    public async Task Write_and_edit_emit_one_event_each_via_the_real_tools()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        using var bus = new FileChangeBus(workspace.WorktreeRoot);
        var handler = new RecordingFileChangedHandler();
        bus.Subscribe(handler);

        ToolResult write = await new WriteTool(workspace, changes: bus).ExecuteAsync(
            Args("""{"path":"src/a.txt","content":"hello\nworld\n"}"""),
            Context,
            Ct
        );
        ToolResult edit = await new EditTool(workspace, changes: bus).ExecuteAsync(
            Args("""{"path":"src/a.txt","old_text":"world","new_text":"there"}"""),
            Context,
            Ct
        );
        await bus.DrainAsync(Ct);

        Assert.False(write.IsError);
        Assert.False(edit.IsError);
        Assert.Equal(2, handler.Payloads.Count);
        string canonical = Path.Combine(workspace.WorktreeRoot, "src", "a.txt");
        Assert.All(handler.Payloads, payload => Assert.Equal(canonical, payload.Path));
        Assert.All(
            handler.Payloads,
            payload => Assert.Equal(workspace.WorktreeRoot, payload.WorkspaceId)
        );
    }

    [Fact]
    public async Task Refused_mutations_emit_nothing()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        using var bus = new FileChangeBus(workspace.WorktreeRoot);
        var handler = new RecordingFileChangedHandler();
        bus.Subscribe(handler);

        ToolResult refusedEdit = await new EditTool(workspace, changes: bus).ExecuteAsync(
            Args("""{"path":"missing.txt","old_text":"a","new_text":"b"}"""),
            Context,
            Ct
        );
        ToolResult refusedWrite = await new WriteTool(workspace, changes: bus).ExecuteAsync(
            Args("""{"path":"../outside.txt","content":"x"}"""),
            Context,
            Ct
        );
        await bus.DrainAsync(Ct);

        Assert.True(refusedEdit.IsError);
        Assert.True(refusedWrite.IsError);
        Assert.Empty(handler.Payloads);
    }

    [Fact]
    public async Task Two_worktrees_carry_their_own_workspace_id()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();
        var firstWorkspace = new Workspace(first.Root);
        var secondWorkspace = new Workspace(second.Root);
        using var firstBus = new FileChangeBus(firstWorkspace.WorktreeRoot);
        using var secondBus = new FileChangeBus(secondWorkspace.WorktreeRoot);
        var firstHandler = new RecordingFileChangedHandler();
        var secondHandler = new RecordingFileChangedHandler();
        firstBus.Subscribe(firstHandler);
        secondBus.Subscribe(secondHandler);

        await new WriteTool(firstWorkspace, changes: firstBus).ExecuteAsync(
            Args("""{"path":"a.txt","content":"one\n"}"""),
            Context,
            Ct
        );
        await new WriteTool(secondWorkspace, changes: secondBus).ExecuteAsync(
            Args("""{"path":"b.txt","content":"two\n"}"""),
            Context,
            Ct
        );
        await firstBus.DrainAsync(Ct);
        await secondBus.DrainAsync(Ct);

        Assert.Equal(firstWorkspace.WorktreeRoot, Assert.Single(firstHandler.Payloads).WorkspaceId);
        Assert.Equal(
            secondWorkspace.WorktreeRoot,
            Assert.Single(secondHandler.Payloads).WorkspaceId
        );
        Assert.NotEqual(firstWorkspace.WorktreeRoot, secondWorkspace.WorktreeRoot);
    }

    [Fact]
    public async Task A_throwing_handler_is_reported_and_the_mutation_is_unaffected()
    {
        using var temp = new TempDirectory();
        var log = new RecordingExtensionLog();
        var workspace = new Workspace(temp.Root);
        using var bus = new FileChangeBus(workspace.WorktreeRoot, log);
        bus.Subscribe(
            new TestFileChangedHandler(
                0,
                (_, _) => throw new InvalidOperationException("handler exploded")
            )
        );

        ToolResult result = await new WriteTool(workspace, changes: bus).ExecuteAsync(
            Args("""{"path":"a.txt","content":"written\n"}"""),
            Context,
            Ct
        );
        await bus.DrainAsync(Ct);

        Assert.False(result.IsError);
        Assert.True(File.Exists(temp.File("a.txt")));
        Assert.Contains(
            log.Messages,
            message => message.Contains("failed", StringComparison.Ordinal)
        );
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
