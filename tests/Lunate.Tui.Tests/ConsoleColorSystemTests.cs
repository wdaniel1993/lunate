using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The production colour-system bridge: every detected capability must reach Spectre's console as
/// the same concrete <see cref="ColorSystem"/>, so a detection regression is caught here rather
/// than hidden by the goldens' hand-picked console settings.
/// </summary>
public sealed class ConsoleColorSystemTests
{
    [Theory]
    [InlineData(ColorSystemSupport.NoColors, ColorSystem.NoColors)]
    [InlineData(ColorSystemSupport.Legacy, ColorSystem.Legacy)]
    [InlineData(ColorSystemSupport.Standard, ColorSystem.Standard)]
    [InlineData(ColorSystemSupport.EightBit, ColorSystem.EightBit)]
    [InlineData(ColorSystemSupport.TrueColor, ColorSystem.TrueColor)]
    [InlineData(ColorSystemSupport.Detect, ColorSystem.EightBit)]
    public void Each_capability_maps_to_its_console_colour_system(
        ColorSystemSupport capability,
        ColorSystem expected
    )
    {
        Assert.Equal(expected, ConsoleColorSystem.Resolve(capability));
    }

    [Fact]
    public void The_console_paints_with_the_resolved_system()
    {
        IAnsiConsole console = ConsoleColorSystem.Create(
            ColorSystemSupport.TrueColor,
            new StringWriter()
        );

        Assert.Equal(ColorSystem.TrueColor, console.Profile.Capabilities.ColorSystem);
    }
}
