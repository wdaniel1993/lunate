using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// Print mode's cells of the degradation matrix: notices are <c>lunate: …</c> lines on stderr,
/// status and extension blocks are no-ops, and stdout carries only the answer.
/// </summary>
public sealed class PrintInteractionTests
{
    [Fact]
    public async Task A_step_limit_notice_lands_on_stderr_and_stdout_stays_pure()
    {
        using var temp = new TempDirectory();
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(new LoopingChatClient())
        ) with
        {
            Prompt = "Loop forever.",
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(2, exitCode);
        Assert.Equal("", output);
        Assert.Contains(
            "lunate: step limit reached (50 steps)\n",
            errors,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Status_and_extension_blocks_are_no_ops()
    {
        using var errors = new StringWriter();
        var interaction = new PrintUserInteraction(errors);

        interaction.UpdateStatus(
            new UserStatus(
                "model",
                1,
                2,
                "/repo",
                "main",
                ThinkLevel: null,
                [new TextSegment("w", TextStyle.Dim)]
            )
        );
        interaction.RenderExtension(
            new ExtensionBlock("read", [new TextSegment("block", TextStyle.Accent)])
        );

        Assert.Equal("", errors.ToString());
    }
}
