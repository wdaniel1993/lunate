namespace Lunate.Coding.Tests;

/// <summary>
/// The bounded single-line rendering of MCP failure reasons that reaches notices: embedded
/// newlines collapse to spaces, over-long reasons clip with an ellipsis, short reasons pass
/// through, and a blank reason contributes nothing to the caller's wording.
/// </summary>
public sealed class McpNoticeTextTests
{
    [Fact]
    public void A_multiline_reason_collapses_to_one_line()
    {
        const string Reason = "first\nsecond\r\nthird\tfourth  fifth";

        var collapsed = McpComposition.ShortReason(Reason);

        Assert.Equal("first second third fourth fifth", collapsed);
    }

    [Fact]
    public void A_reason_longer_than_the_bound_is_clipped_with_an_ellipsis()
    {
        string reason = new('a', 150);

        string shortened = McpComposition.ShortReason(reason);

        Assert.Equal(new string('a', 100) + "…", shortened);
    }

    [Fact]
    public void A_reason_at_the_bound_is_unchanged()
    {
        string reason = new('a', 100);

        Assert.Equal(reason, McpComposition.ShortReason(reason));
    }

    [Fact]
    public void A_short_reason_is_unchanged()
    {
        Assert.Equal("boom", McpComposition.ShortReason("boom"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public void A_blank_reason_becomes_empty(string? reason) =>
        Assert.Equal(string.Empty, McpComposition.ShortReason(reason));
}
