using System.Text.Json;
using Lunate.Extensibility;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The T-50 mode proofs with the real ui-showcase sample installed into a temp store: the TUI
/// renders the sample's bars into the scrollback and its widget into the footer, while print, JSON
/// and ACP stay structurally clean with the renderer and widget loaded — the fitness row's
/// "degrades cleanly in print/json/ACP".
/// </summary>
public sealed class UiShowcaseSampleTests
{
    private const string RawResult = """{"values":[3,7,1,9]}""";

    private static readonly JsonElement Values = JsonSerializer.SerializeToElement(
        new[] { 3, 7, 1, 9 }
    );

    [Fact]
    public async Task The_tui_renders_the_samples_bars_and_widget_from_a_temp_store()
    {
        using var host = new InteractiveSessionHost();
        UiShowcaseFixture.Install(host.Temp.File("extensions-store"));
        host.Console.Size = new ConsoleSize(200, 24);
        host.Client.Enqueue(
            Scripts.Call("call-1", "uikit__bars", Scripts.Args(("values", Values))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // The widget is part of the composed footer before any turn (zero usage of the window).
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.Frames.Contains("▱▱▱▱▱▱▱▱ 0%", StringComparison.Ordinal);
        });

        host.Console.SendText("go");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "█████████ 9");

        Assert.Contains("███ 3", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains("███████ 7", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains("█ 1", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains("█████████ 9", host.ScrollbackText, StringComparison.Ordinal);

        // The scrollback helper waited for the turn to finish: a slash command sent mid-turn
        // would be refused, so quit only after the session is idle.
        Assert.False(host.Session.IsRunning);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_print_run_stays_raw_text_and_renders_no_ui()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        UiShowcaseFixture.Install(store);
        var log = new RecordingExtensionLog();
        // Print mode's stdout contract is the final answer only; the script's answer is the raw
        // tool-result text, so "stdout contains the raw result text" is exactly testable — and it
        // must stay that: no bars, no widget, nothing the renderer produced.
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "uikit__bars", Scripts.Args(("values", Values))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text(RawResult), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "go",
            Extensions = new ExtensionSeams { StorePath = store, Log = log },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal(RawResult, output);
        Assert.DoesNotContain("█", output, StringComparison.Ordinal);
        Assert.DoesNotContain("▰", output, StringComparison.Ordinal);
        Assert.DoesNotContain("▱", output, StringComparison.Ordinal);
        Assert.DoesNotContain("ctx n/a", output, StringComparison.Ordinal);
        Assert.Empty(errors);
        Assert.True(
            log.Contains("ui-showcase: registered"),
            "the sample must be loaded; log:\n" + string.Join("\n", log.Messages)
        );
    }

    [Fact]
    public async Task A_json_run_matches_a_no_extension_run_and_carries_the_raw_text()
    {
        using var withTemp = new TempDirectory();
        using var bareTemp = new TempDirectory();
        string store = withTemp.File("store");
        UiShowcaseFixture.Install(store);

        (int withExit, string withOutput, string withErrors) = await PrintModeTestSupport.RunAsync(
            JsonOptions(
                withTemp,
                new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() }
            )
        );
        (int bareExit, string bareOutput, _) = await PrintModeTestSupport.RunAsync(
            JsonOptions(bareTemp, TestExtensionSeams.Empty(bareTemp))
        );

        // The stream is the same shape with and without the sample: the UI contributions add
        // nothing to it, so the no-extension run is the structural baseline.
        Assert.Equal(0, withExit);
        Assert.Equal(0, bareExit);
        List<JsonElement> withLines = PrintModeTestSupport.ParseLines(withOutput);
        List<JsonElement> bareLines = PrintModeTestSupport.ParseLines(bareOutput);
        Assert.NotEmpty(withLines);
        Assert.Equal(
            bareLines.Select(PrintModeTestSupport.TypeOf),
            withLines.Select(PrintModeTestSupport.TypeOf)
        );

        // No UI artifact anywhere on stdout; the tool-result event carries the raw text.
        Assert.DoesNotContain("█", withOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("▰", withOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("▱", withOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("ctx n/a", withOutput, StringComparison.Ordinal);
        JsonElement result = Assert.Single(
            withLines,
            line => PrintModeTestSupport.TypeOf(line) == "tool_call_result"
        );
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Equal(RawResult, result.GetProperty("output").GetString());
        Assert.Empty(withErrors);
    }

    [Fact]
    public void An_acp_session_runs_with_the_sample_loaded_and_writes_no_ui_to_stdout()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        UiShowcaseFixture.Install(store);
        var log = new RecordingExtensionLog();
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        using var errors = new StringWriter();

        int exitCode = AcpMode.Run(
            errors,
            input,
            output,
            TestContext.Current.CancellationToken,
            new ExtensionSeams { StorePath = store, Log = log }
        );

        Assert.Equal(0, exitCode);
        Assert.True(
            log.Contains("ui-showcase: registered"),
            "the sample must be loaded; log:\n" + string.Join("\n", log.Messages)
        );
        Assert.Empty(output.ToArray());
    }

    private static PrintModeOptions JsonOptions(TempDirectory temp, ExtensionSeams extensions)
    {
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "uikit__bars", Scripts.Args(("values", Values))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text(RawResult), Scripts.Stop());
        return PrintModeTestSupport.BaseOptions(temp, new FakeChatClientFactory(client)) with
        {
            Prompt = "go",
            Json = true,
            Extensions = extensions,
        };
    }
}

/// <summary>The ui-showcase sample copied into this test project's output under <c>Samples/</c>.</summary>
internal static class UiShowcaseFixture
{
    internal static string SourceDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "UiShowcaseExtension");

    /// <summary>Installs the sample as a global extension (no trust prompt) into the store.</summary>
    internal static string Install(string storePath)
    {
        string directory = Path.Combine(storePath, "extensions", "uikit");
        Directory.CreateDirectory(directory);
        foreach (
            string file in Directory.EnumerateFiles(
                SourceDirectory,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            string target = Path.Combine(directory, Path.GetRelativePath(SourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        return directory;
    }
}
