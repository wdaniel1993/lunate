namespace Lunate.Coding.Tests;

public sealed class ConsoleSecretPromptTests
{
    [Fact]
    public async Task Masks_every_typed_character()
    {
        using var output = new StringWriter();
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('g', ConsoleKey.G, shift: false, alt: false, control: false),
            new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
        ]);
        var prompt = new ConsoleSecretPrompt(output, keys.Dequeue);

        string? value = await prompt.ReadAsync("API key", TestContext.Current.CancellationToken);

        Assert.Equal("gr", value);
        string written = output.ToString();
        Assert.Contains("*", written, StringComparison.Ordinal);
        Assert.DoesNotContain("g", written, StringComparison.Ordinal);
        Assert.DoesNotContain("r", written, StringComparison.Ordinal);
    }
}
