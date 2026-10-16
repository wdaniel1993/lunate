using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The unload path's <c>ToolsChanged</c> notify flag (design D3): the register-only inspect
/// teardown unloads silently, while the default unload still dispatches.
/// </summary>
public sealed class ExtensionToolUnloadNotifyTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unloading_silently_skips_the_tools_changed_notification()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallToolExtension(
            temp,
            "tools",
            tools: """["echo"]""",
            settings: """{"tools":[{"name":"echo"}]}"""
        );
        var loader = new ExtensionLoader(
            TestExtensions.Options(temp.Subdirectory("store"), new RecordingExtensionLog())
        );
        List<ToolsChangedPayload> changes = [];
        loader.Hooks.Register(
            "probe",
            new TestToolsChangedHandler(
                (payload, _) =>
                {
                    lock (changes)
                    {
                        changes.Add(payload);
                    }

                    return ValueTask.CompletedTask;
                }
            )
        );
        loader.Discover(temp.Root, "repo");
        var prompt = new FakeExtensionTrustPrompt(true);

        await loader.Load("tools", temp.Root, "repo", prompt, Ct);
        Assert.Equal(["tools__echo"], Assert.Single(changes).Tools);

        await loader.Unload("tools", notifyToolsChanged: false);

        Assert.Empty(loader.Tools.List());
        Assert.Single(changes);

        await loader.Load("tools", temp.Root, "repo", prompt, Ct);
        await loader.Unload("tools");

        Assert.Equal(3, changes.Count);
        Assert.Empty(changes[2].Tools);
    }
}
