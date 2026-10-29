using Lunate.Extensibility;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The composed built-in at the composition level (spec: Built-in extension provenance, the
/// <c>--no-extensions</c> and the <c>/extensions</c> scenarios): the flag composes only the
/// built-in set, its tools register and run while no store is read; a failing built-in is
/// reported and the session keeps the rest; <c>/extensions</c> and the reload surfaces treat it
/// as <c>builtin</c> and refuse reload with the teaching message.
/// </summary>
public sealed class ExtensionBuiltInCompositionTests
{
    private const string RefusalMessage =
        "extension 'pinboard' is a built-in; built-ins load with the host and cannot be reloaded or unloaded.";

    [Fact]
    public void Print_mode_with_the_flag_composes_only_the_builtin_set_and_runs_its_tool()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        string helloMarker = temp.File("hello-marker.txt");
        HelloFixture.WriteSettings(store, "hello", helloMarker);
        string builtinMarker = temp.File("builtin-marker.txt");
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient()
            .Enqueue(Scripts.Call("call-1", "pinboard__ping", Scripts.Args()), Scripts.ToolCalls())
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        PrintModeOptions printOptions = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Extensions = new ExtensionSeams
            {
                StorePath = store,
                Log = log,
                NoExtensions = true,
                BuiltIns = [BuiltinFixture.Marker("pinboard", builtinMarker)],
            },
        };
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["--no-extensions", "-p", "Hi"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            printOptions
        );

        Assert.Equal(0, exitCode);
        Assert.Equal("done", output.ToString());
        Assert.True(
            File.Exists(builtinMarker),
            "the composed built-in's tool must run under --no-extensions"
        );
        Assert.False(File.Exists(helloMarker), "--no-extensions must not read the extension store");
    }

    [Fact]
    public async Task A_failing_builtin_is_reported_and_the_session_keeps_the_rest()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        string builtinMarker = temp.File("builtin-marker.txt");
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient()
            .Enqueue(Scripts.Call("call-1", "pinboard__ping", Scripts.Args()), Scripts.ToolCalls())
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        BuiltInExtension bomb = new(
            "host/bomb/extension.json",
            Lunate.Extensibility.Abstractions.ExtensionManifest.Parse(
                """{"id":"bomb","version":"0.1.0","apiVersion":"^1.0.0","builtin":true}""",
                "host/bomb/extension.json"
            ),
            () => throw new InvalidOperationException("boom: the built-in factory broke")
        );
        PrintModeOptions printOptions = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Extensions = new ExtensionSeams
            {
                StorePath = store,
                Log = log,
                BuiltIns = [bomb, BuiltinFixture.Marker("pinboard", builtinMarker)],
            },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(
            printOptions
        );

        Assert.Equal(0, exitCode);
        Assert.Equal("done", output);
        Assert.True(
            File.Exists(builtinMarker),
            "a failing built-in must not stop the composed rest"
        );
        Assert.True(
            log.Contains("extension 'bomb'"),
            "the failure must be reported; log:\n" + string.Join("\n", log.Messages)
        );
    }

    [Fact]
    public async Task Extensions_lists_the_composed_builtin_with_scope_builtin()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        string builtinMarker = temp.File("builtin-marker.txt");
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams
            {
                StorePath = store,
                Log = new RecordingExtensionLog(),
                BuiltIns = [BuiltinFixture.Marker("pinboard", builtinMarker)],
            }
        );
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "(builtin) loaded in ");

        string frame = host.LastFrame;
        Assert.Contains(
            "extensions: pinboard 0.1.0 (builtin) loaded in ",
            frame,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "tools 1, commands 0, hooks 0, services 0",
            frame,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("trust-denied", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("pending", frame, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Extensions_reload_of_a_builtin_is_refused_and_it_stays_loaded()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        string builtinMarker = temp.File("builtin-marker.txt");
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams
            {
                StorePath = store,
                Log = new RecordingExtensionLog(),
                BuiltIns = [BuiltinFixture.Marker("pinboard", builtinMarker)],
            }
        );
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/extensions reload pinboard");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, RefusalMessage);

        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "(builtin) loaded in ");
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public void The_headless_reload_of_a_builtin_is_refused_with_the_teaching_message()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        string builtinMarker = temp.File("builtin-marker.txt");
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exit = Cli.Run(
            ["extensions", "reload", "pinboard"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams
            {
                StorePath = store,
                BuiltIns = [BuiltinFixture.Marker("pinboard", builtinMarker)],
            }
        );

        Assert.NotEqual(0, exit);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains($"lunate: {RefusalMessage}", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_extensions_subcommand_lists_the_composed_builtin()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        string builtinMarker = temp.File("builtin-marker.txt");
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exit = Cli.Run(
            ["extensions"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams
            {
                StorePath = store,
                BuiltIns = [BuiltinFixture.Marker("pinboard", builtinMarker)],
            }
        );
        string text = output.ToString();

        Assert.Equal(0, exit);
        Assert.Contains("pinboard 0.1.0 (builtin)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("trust-denied", text, StringComparison.Ordinal);
    }
}
