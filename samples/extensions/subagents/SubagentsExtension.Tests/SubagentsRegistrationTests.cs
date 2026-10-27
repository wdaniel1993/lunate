using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Testing;

namespace SubagentsExtension.Tests;

/// <summary>The registration contract: one declared tool, destructive-capable, nothing runs yet.</summary>
public sealed class SubagentsRegistrationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registration_declares_the_spawn_tool_with_asking_annotations()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);

        IReadOnlyList<ExtensionToolAdapter> tools = host.ComposeTools();

        ExtensionToolAdapter spawn = Assert.Single(tools);
        Assert.Equal("subagents__spawn", spawn.Name);
        Assert.Equal("spawn", spawn.LocalName);
        Assert.Equal("subagents", spawn.Namespace!.Name);
        Assert.NotNull(spawn.Annotations);
        Assert.False(spawn.Annotations.ReadOnly);
        Assert.True(spawn.Annotations.Destructive);
        // Destructive is execute risk: the new derivation treats a destructive tool as one the
        // approval panel must always see, not as an ordinary write.
        Assert.Equal(ToolRisk.Execute, spawn.Risk);
        // Loading succeeded with the tool registered: the manifest declares it (D6 refuses
        // registrations the manifest does not cover).
        Assert.Single(host.Runner.Tools.List());
    }

    [Fact]
    public async Task Settings_override_the_description()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(
            temp,
            description: "Runs a helper and answers with its result."
        );
        await host.StartAsync(Ct);

        ExtensionToolAdapter spawn = Assert.Single(host.ComposeTools());

        Assert.Equal("Runs a helper and answers with its result.", spawn.Description);
    }
}
