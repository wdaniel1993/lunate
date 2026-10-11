using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// JSON mode's cells of the degradation matrix: the event stream on stdout is untouched (every
/// line still a JSON frame), notices go to stderr, and status/extension blocks are no-ops.
/// </summary>
public sealed class JsonInteractionTests
{
    [Fact]
    public async Task A_notice_lands_on_stderr_while_the_event_stream_stays_pure()
    {
        using var temp = new TempDirectory();
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(new LoopingChatClient())
        ) with
        {
            Prompt = "Loop forever.",
            Json = true,
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(2, exitCode);
        Assert.DoesNotContain("lunate:", output, StringComparison.Ordinal);
        List<JsonElement> lines = PrintModeTestSupport.ParseLines(output);
        Assert.Contains(lines, line => PrintModeTestSupport.TypeOf(line) == "step_limit_reached");
        Assert.Equal("run_finished", PrintModeTestSupport.TypeOf(lines[^1]));
        Assert.Contains(
            "lunate: step limit reached (50 steps)\n",
            errors,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Status_and_extension_blocks_are_no_ops_in_json_runs()
    {
        using var errors = new StringWriter();
        var interaction = new PrintUserInteraction(errors);

        interaction.UpdateStatus(
            new UserStatus("model", 1, 2, "/repo", "main", [new TextSegment("w", TextStyle.Dim)])
        );
        interaction.RenderExtension(
            new ExtensionBlock("bash", [new TextSegment("block", TextStyle.Error)])
        );

        Assert.Equal("", errors.ToString());
    }
}
