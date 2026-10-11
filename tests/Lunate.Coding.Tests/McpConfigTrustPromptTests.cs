using System.Globalization;

namespace Lunate.Coding.Tests;

public sealed class McpConfigTrustPromptTests
{
    private const string ConfigPath = "/work/.lunate/mcp.json";

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
        var prompt = new ConsoleMcpConfigTrustPrompt(errors, input);

        bool approved = await prompt.ApproveAsync(
            ConfigPath,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expected, approved);
    }

    [Fact]
    public async Task Console_prompt_asks_on_stderr_naming_the_file_and_an_end_of_input_denies()
    {
        using var errors = new StringWriter();
        using var input = new StringReader(string.Empty);
        var prompt = new ConsoleMcpConfigTrustPrompt(errors, input);

        bool approved = await prompt.ApproveAsync(
            ConfigPath,
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
        Assert.Contains(
            string.Create(
                CultureInfo.InvariantCulture,
                $"lunate: project MCP config '{ConfigPath}' runs commands from this repository. Start its servers? [y/N]"
            ),
            errors.ToString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Console_prompt_honors_cancellation_before_asking()
    {
        using var errors = new StringWriter();
        using var input = new StringReader("y" + Environment.NewLine);
        var prompt = new ConsoleMcpConfigTrustPrompt(errors, input);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await prompt.ApproveAsync(ConfigPath, cancelled.Token);
        });

        Assert.Empty(errors.ToString());
    }

    [Fact]
    public async Task Non_interactive_prompt_refuses_without_output()
    {
        var prompt = new NonInteractiveMcpConfigTrustPrompt();

        bool approved = await prompt.ApproveAsync(
            ConfigPath,
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
    }
}
