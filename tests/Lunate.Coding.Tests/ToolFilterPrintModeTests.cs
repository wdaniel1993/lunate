using Lunate.Extensibility;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The per-run tool filter in print mode: the launch selection narrows the registry after
/// extension and MCP composition, <c>--no-tools</c> declares nothing, and an unknown name is a
/// usage error (exit 2) that names the available tools.
/// </summary>
public sealed class ToolFilterPrintModeTests
{
    [Fact]
    public async Task Print_mode_exposes_only_the_launch_selected_tools()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Tools = ["read"] },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        Assert.Equal(["read"], Names(client.RequestOptions[0]));
    }

    [Fact]
    public async Task Print_mode_can_select_an_extension_tool_by_name()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        ToolFixture.Install(store);
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Extensions = new ExtensionSeams
            {
                StorePath = store,
                Log = new RecordingExtensionLog(),
            },
            Launch = new LaunchOptions { Tools = ["tools__echo"] },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        Assert.Equal(["tools__echo"], Names(client.RequestOptions[0]));
    }

    [Fact]
    public async Task Print_mode_no_tools_exposes_no_declarations()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { NoTools = true },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Empty(errors);
        Assert.Empty(Names(client.RequestOptions[0]));
    }

    [Fact]
    public async Task Print_mode_exits_two_for_an_unknown_tool_and_lists_the_available_names()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        var factory = new FakeChatClientFactory(client);
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory) with
        {
            Prompt = "Hi",
            Launch = new LaunchOptions { Tools = ["nope"] },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(2, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "lunate: --tools names unknown tools: 'nope'",
            errors,
            StringComparison.Ordinal
        );
        Assert.Contains("available: read, write", errors, StringComparison.Ordinal);
        Assert.Null(factory.CreatedModel);
        Assert.Empty(client.Requests);
    }

    private static IReadOnlyList<string> Names(ChatOptions? options) =>
        [.. (options?.Tools ?? []).Select(tool => tool.Name ?? string.Empty)];
}
