namespace Lunate.Tui.Tests;

public sealed class WidthBandsTests
{
    [Theory]
    [InlineData(40, "Compact")]
    [InlineData(59, "Compact")]
    [InlineData(60, "Standard")]
    [InlineData(99, "Standard")]
    [InlineData(100, "Full")]
    [InlineData(120, "Full")]
    public void The_width_bands_share_the_narrow_and_full_breakpoints(int width, string expected) =>
        Assert.Equal(expected, WidthBands.Of(width).ToString());
}
