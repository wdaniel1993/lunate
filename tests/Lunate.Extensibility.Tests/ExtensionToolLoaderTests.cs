using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The loader's tool lifecycle: load registers and fires ToolsChanged, unload removes, fires
/// ToolsChanged again and cancels in-flight calls into a cancelled result (design D7, D8).
/// </summary>
public sealed class ExtensionToolLoaderTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Loading_a_tool_extension_registers_its_tools_and_fires_tools_changed()
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
        List<ToolsChangedPayload> changes = ObserveToolChanges(loader);
        loader.Discover(temp.Root, "repo");
        Assert.Empty(loader.DiscoveryErrors);

        await loader.Load("tools", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        ExtensionToolAdapter tool = Assert.Single(loader.Tools.List());
        Assert.Equal("tools__echo", tool.Name);
        Assert.Equal("tools", tool.Namespace?.Name);
        ToolsChangedPayload change = Assert.Single(changes);
        Assert.Equal(["tools__echo"], change.Tools);
    }

    [Fact]
    public async Task Unloading_removes_the_tools_and_fires_tools_changed_again()
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
        List<ToolsChangedPayload> changes = ObserveToolChanges(loader);
        loader.Discover(temp.Root, "repo");
        var prompt = new FakeExtensionTrustPrompt(true);
        await loader.Load("tools", temp.Root, "repo", prompt, Ct);

        await loader.Unload("tools");

        Assert.Empty(loader.Tools.List());
        Assert.Equal(2, changes.Count);
        Assert.Empty(changes[1].Tools);

        await loader.Load("tools", temp.Root, "repo", prompt, Ct);

        Assert.Single(loader.Tools.List());
        Assert.Equal(3, changes.Count);
        Assert.Equal(["tools__echo"], changes[2].Tools);
    }

    [Fact]
    public async Task Unloading_cancels_an_in_flight_extension_tool_call()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("started.marker");
        TestExtensions.InstallToolExtension(
            temp,
            "tools",
            tools: """["block"]""",
            settings: $$"""{"tools":[{"name":"block","behavior":"block","marker":{{JsonSerializer.Serialize(marker)}}}]}"""
        );
        var loader = new ExtensionLoader(
            TestExtensions.Options(temp.Subdirectory("store"), new RecordingExtensionLog())
        );
        loader.Discover(temp.Root, "repo");
        await loader.Load("tools", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);
        ExtensionToolAdapter tool = Assert.Single(loader.Tools.List());
        var context = new ToolContext(temp.Root, new NullAgentEvents());
        JsonElement schema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();
        Task<ToolResult> call = tool.ExecuteAsync(schema, context, Ct);
        await WaitForFileAsync(marker);

        await loader.Unload("tools");
        ToolResult result = await call;

        Assert.True(result.IsError);
        Assert.Contains("tools__block", result.Output, StringComparison.Ordinal);
        Assert.Contains("unloaded", result.Output, StringComparison.Ordinal);
        Assert.Empty(loader.Tools.List());
    }

    [Fact]
    public async Task A_registered_tool_the_manifest_does_not_declare_refuses_the_load()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallToolExtension(
            temp,
            "tools",
            tools: """["echo"]""",
            settings: """{"tools":[{"name":"echo"},{"name":"extra"}]}"""
        );
        var loader = new ExtensionLoader(
            TestExtensions.Options(temp.Subdirectory("store"), new RecordingExtensionLog())
        );
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load("tools", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct)
        );

        Assert.Contains("extension 'tools'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'extra'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("manifest", exception.Message, StringComparison.Ordinal);
        Assert.Empty(loader.Tools.List());
    }

    [Fact]
    public async Task A_declared_tool_without_a_registration_is_warned_and_dropped()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallToolExtension(
            temp,
            "tools",
            tools: """["echo","ghost"]""",
            settings: """{"tools":[{"name":"echo"}]}"""
        );
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        await loader.Load("tools", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        Assert.Equal(["tools__echo"], loader.Tools.List().Select(tool => tool.Name));
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("'ghost'", StringComparison.Ordinal)
                && message.Contains("dropped", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_malformed_registration_refuses_the_load_naming_the_field()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallToolExtension(
            temp,
            "tools",
            tools: """["bad"]""",
            settings: """{"tools":[{"name":"bad","schema":"text"}]}"""
        );
        var loader = new ExtensionLoader(
            TestExtensions.Options(temp.Subdirectory("store"), new RecordingExtensionLog())
        );
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load("tools", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct)
        );

        Assert.Contains("extension 'tools'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("field 'parametersSchema'", exception.Message, StringComparison.Ordinal);
        Assert.Empty(loader.Tools.List());
    }

    private static List<ToolsChangedPayload> ObserveToolChanges(ExtensionLoader loader)
    {
        List<ToolsChangedPayload> changes = [];
        loader.Hooks.Register(
            "observe",
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
        return changes;
    }

    private static async Task WaitForFileAsync(string path)
    {
        for (var attempt = 0; attempt < 500 && !File.Exists(path); attempt++)
        {
            await Task.Delay(10, Ct);
        }

        Assert.True(File.Exists(path), $"the handler did not start; expected marker '{path}'.");
    }
}
