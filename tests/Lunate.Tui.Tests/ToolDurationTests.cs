namespace Lunate.Tui.Tests;

public sealed class ToolDurationTests
{
    [Theory]
    [InlineData(0, "0.0s")]
    [InlineData(400, "0.4s")]
    [InlineData(9_990, "9.9s")]
    [InlineData(10_000, "10s")]
    [InlineData(12_400, "12s")]
    [InlineData(59_900, "59s")]
    [InlineData(60_000, "1m 00s")]
    [InlineData(62_000, "1m 02s")]
    [InlineData(5_400_000, "90m 00s")]
    public void Durations_format_compactly(int milliseconds, string expected) =>
        Assert.Equal(expected, ToolDuration.Format(TimeSpan.FromMilliseconds(milliseconds)));

    [Fact]
    public void A_negative_duration_clamps_to_zero() =>
        Assert.Equal("0.0s", ToolDuration.Format(TimeSpan.FromMilliseconds(-50)));
}
