using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The interactive <c>/extensions</c> notice end to end (design D2): the state-group order with
/// the loaded extension's registered counts and the denied extension's declared counts, the empty
/// store and the <c>--no-extensions</c> views — all without a model call.
/// </summary>
public sealed class ExtensionsInteractiveEndToEndTests
{
    [Fact]
    public async Task Extensions_reports_an_inactive_tool_of_a_session_with_its_reason()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        ToolFixture.Install(
            store,
            toolActivation: """{"echo":{"when":"workspace-root-has-file","patterns":["*.md"]}}"""
        );
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            workingDirectory: worktree
        );
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "inactive");

        Assert.Contains(
            "tools 1, inactive 'tools__echo' (workspace-root-has-file): no file matching '*.md' at the workspace root, commands 0, hooks 0, services 0",
            host.LastFrame,
            StringComparison.Ordinal
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Extensions_lists_the_denied_project_before_the_loaded_global_extension()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        ToolFixture.Install(store);
        HelloFixture.Install(
            Path.Combine(worktree, ".lunate", "extensions", "hello-project"),
            id: "hello-project"
        );
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams
            {
                StorePath = store,
                Log = new RecordingExtensionLog(),
                Prompt = new ScriptedTrustPrompt(false),
            },
            workingDirectory: worktree
        );
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "trust-denied");

        string frame = host.LastFrame;
        Assert.Contains(
            "extensions: hello-project 0.1.0 (project) trust-denied, tools 0, commands 0, hooks 0, services 0",
            frame,
            StringComparison.Ordinal
        );
        Assert.Contains("hello 0.1.0 (global) loaded in ", frame, StringComparison.Ordinal);
        Assert.Contains(
            "hooks 3 (0 declared), services 1 (0 declared)",
            frame,
            StringComparison.Ordinal
        );
        Assert.Contains("tools 0.1.0 (global) loaded in ", frame, StringComparison.Ordinal);
        Assert.Contains(
            "tools 1, commands 0, hooks 0, services 0",
            frame,
            StringComparison.Ordinal
        );
        Assert.True(
            frame.IndexOf("trust-denied", StringComparison.Ordinal)
                < frame.IndexOf("(global) loaded in", StringComparison.Ordinal),
            "the denied extension must render before the loaded one; frame:\n" + frame
        );
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Extensions_says_none_configured_in_an_empty_store()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "extensions: none configured");

        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Extensions_says_disabled_with_the_no_extensions_flag()
    {
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { NoExtensions = true }
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "extensions disabled (--no-extensions)"
        );

        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}
