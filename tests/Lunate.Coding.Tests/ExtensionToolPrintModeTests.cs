using System.Text.Json;
using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// The print-mode extension-tool e2e: the model calls a tool the sample-style extension
/// contributed, the result surfaces, and a throwing tool yields the teaching error while the run
/// continues (design D10, D12).
/// </summary>
public sealed class ExtensionToolPrintModeTests
{
    [Fact]
    public async Task A_print_run_calls_an_extension_tool_and_surfaces_the_result()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        ToolFixture.Install(store, settings: """{"tools":[{"name":"echo"}]}""");
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "tools__echo", Scripts.Args(("q", "hello"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("final answer"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "go",
            Json = true,
            Yolo = true,
            Extensions = new ExtensionSeams { StorePath = store, Log = log },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        List<JsonElement> lines = PrintModeTestSupport.ParseLines(output);
        JsonElement call = Assert.Single(
            lines,
            line => PrintModeTestSupport.TypeOf(line) == "tool_call_result"
        );
        Assert.False(call.GetProperty("isError").GetBoolean());
        string toolOutput = call.GetProperty("output").GetString()!;
        Assert.Contains("""args={"q":"hello"}""", toolOutput, StringComparison.Ordinal);
        Assert.Contains("cwd=", toolOutput, StringComparison.Ordinal);
        Assert.Contains("run=run_", toolOutput, StringComparison.Ordinal);
        Assert.Contains("call=call-1", toolOutput, StringComparison.Ordinal);
        Assert.Contains("final answer", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_throwing_extension_tool_yields_the_teaching_error_and_the_run_continues()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        ToolFixture.Install(
            store,
            tools: """["explode"]""",
            settings: """{"tools":[{"name":"explode","behavior":"throw"}]}"""
        );
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient()
            .Enqueue(Scripts.Call("call-1", "tools__explode", Scripts.Args()), Scripts.ToolCalls())
            .Enqueue(Scripts.Text("recovered"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "go",
            Json = true,
            Yolo = true,
            Extensions = new ExtensionSeams { StorePath = store, Log = log },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        List<JsonElement> lines = PrintModeTestSupport.ParseLines(output);
        JsonElement call = Assert.Single(
            lines,
            line => PrintModeTestSupport.TypeOf(line) == "tool_call_result"
        );
        Assert.True(call.GetProperty("isError").GetBoolean());
        Assert.Contains(
            "Tool 'tools__explode' failed: the scripted tool exploded. Fix the call or try a different approach.",
            call.GetProperty("output").GetString()!,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "tool 'tools__explode' failed: Tool 'tools__explode' failed: the scripted tool exploded.",
            errors,
            StringComparison.Ordinal
        );
        Assert.Contains("recovered", output, StringComparison.Ordinal);
    }
}
