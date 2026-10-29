using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// Issue #245: the trust prompt must never show its question without the two choices at the
/// common widths where a realistic absolute path overflows the headline. This title makes the
/// one-line headline 121 cells wide, so it overflows 80, 100 and 120 columns. Regression #234
/// made the live area wrap the title and move the choices to their own line; these tests pin
/// that through the frame the user actually sees.
/// </summary>
public sealed class TrustPromptWidthTests
{
    private const string LongTitle =
        "Load project extension 'hello-world' 0.1.0 from /Users/danielwallner/dev/lunate/demo/extensions/hello-world";

    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(120)]
    public void Both_choices_survive_the_common_widths(int width)
    {
        string[] lines = Frame(width);

        Assert.Contains(lines, line => line.Contains("[y]es", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("[n]o", StringComparison.Ordinal));
        AssertWithin(lines, width);
    }

    [Fact]
    public void No_width_from_forty_to_two_hundred_leaves_the_question_without_its_choices()
    {
        for (var width = 40; width <= 200; width++)
        {
            string[] lines = Frame(width);
            string joined = string.Join('\n', lines);

            Assert.Contains("?", joined, StringComparison.Ordinal);
            Assert.Contains("[y]es", joined, StringComparison.Ordinal);
            Assert.Contains("[n]o", joined, StringComparison.Ordinal);
            AssertWithin(lines, width);
        }
    }

    private static string[] Frame(int width) =>
        [
            .. LiveAreaRenderer
                .Render(
                    new LiveAreaState
                    {
                        Trust = new TrustPromptModel(LongTitle, [], 0, 0),
                        Color = ColorSystemSupport.NoColors,
                        Unicode = true,
                        Size = new ConsoleSize(width, 24),
                    }
                )
                .Lines,
        ];

    private static void AssertWithin(IReadOnlyList<string> lines, int width) =>
        Assert.All(
            lines,
            line =>
                Assert.True(
                    CellText.Width(line) <= width,
                    $"{CellText.Width(line)} cells exceed {width}: {line}"
                )
        );
}
