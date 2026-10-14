using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Testing;

namespace WorktreeTasksExtension.Tests;

/// <summary>The registration contract: four declared tools with their annotations and risks.</summary>
public sealed class WorktreeTasksRegistrationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registration_declares_the_four_worktree_tools()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);

        IReadOnlyList<ExtensionToolAdapter> tools = host.ComposeTools();

        Assert.Equal(
            ["worktree__start", "worktree__merge", "worktree__clean", "worktree__list"],
            tools.Select(tool => tool.Name)
        );
        ExtensionToolAdapter start = tools[0];
        Assert.Equal("start", start.LocalName);
        Assert.Equal("worktree", start.Namespace!.Name);
        Assert.NotNull(start.Annotations);
        Assert.False(start.Annotations.ReadOnly);
        Assert.True(start.Annotations.Destructive);
        Assert.Equal(ToolRisk.Execute, start.Risk);
        ExtensionToolAdapter merge = tools[1];
        Assert.NotNull(merge.Annotations);
        Assert.False(merge.Annotations.Destructive);
        Assert.Equal(ToolRisk.Write, merge.Risk);
        ExtensionToolAdapter clean = tools[2];
        Assert.NotNull(clean.Annotations);
        Assert.True(clean.Annotations.Destructive);
        Assert.Equal(ToolRisk.Execute, clean.Risk);
        ExtensionToolAdapter list = tools[3];
        Assert.NotNull(list.Annotations);
        Assert.True(list.Annotations.ReadOnly);
        Assert.Equal(ToolRisk.ReadOnly, list.Risk);
        Assert.Equal(4, host.Runner.Tools.List().Count);
    }
}
