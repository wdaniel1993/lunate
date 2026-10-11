using System.Globalization;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

public sealed class ExtensionTrustPromptTests
{
    [Fact]
    public async Task Non_interactive_prompt_refuses_and_logs_an_actionable_message()
    {
        var log = new RecordingExtensionLog();
        var prompt = new NonInteractiveTrustPrompt(log);

        bool approved = await prompt.ApproveAsync(
            Descriptor("hello", "/work/.lunate/extensions/hello"),
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
        Assert.Contains(
            "project extension 'hello' was not trusted; run an interactive session to approve it.",
            log.Messages
        );
    }

    [Theory]
    [InlineData("y", true)]
    [InlineData("Y", true)]
    [InlineData("yes", true)]
    [InlineData("YES", true)]
    [InlineData(" yes ", true)]
    [InlineData("n", false)]
    [InlineData("N", false)]
    [InlineData("no", false)]
    [InlineData("yikes", false)]
    [InlineData("", false)]
    public async Task Console_prompt_approves_only_y_or_yes(string answer, bool expected)
    {
        using var errors = new StringWriter();
        using var input = new StringReader(answer + Environment.NewLine);
        var prompt = new ConsoleTrustPrompt(errors, input);

        bool approved = await prompt.ApproveAsync(
            Descriptor("hello", "/work/.lunate/extensions/hello"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expected, approved);
    }

    [Fact]
    public async Task Console_prompt_asks_on_stderr_and_an_end_of_input_denies()
    {
        using var errors = new StringWriter();
        using var input = new StringReader(string.Empty);
        var prompt = new ConsoleTrustPrompt(errors, input);

        bool approved = await prompt.ApproveAsync(
            Descriptor("hello", "/work/.lunate/extensions/hello"),
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
        Assert.Contains(
            "lunate: project extension 'hello' runs code from this repository "
                + "(/work/.lunate/extensions/hello). Load it? [y/N]",
            errors.ToString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Console_prompt_honors_cancellation_before_asking()
    {
        using var errors = new StringWriter();
        using var input = new StringReader("y" + Environment.NewLine);
        var prompt = new ConsoleTrustPrompt(errors, input);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await prompt.ApproveAsync(
                Descriptor("hello", "/work/.lunate/extensions/hello"),
                cancelled.Token
            );
        });

        Assert.Empty(errors.ToString());
    }

    internal static ExtensionDescriptor Descriptor(string id, string directory) =>
        new(
            id,
            "0.1.0",
            ExtensionScope.Project,
            directory,
            ExtensionManifest.Parse(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"E.dll"}"""
                ),
                Path.Combine(directory, "extension.json")
            )
        );
}
