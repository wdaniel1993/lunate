namespace Lunate.Coding.Tests;

public sealed class ProductVersionTests
{
    [Theory]
    [InlineData("0.1.0+0c96dc6e5f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c", "0.1.0+0c96dc6")]
    [InlineData("0.1.0+0c96dc6", "0.1.0+0c96dc6")]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.1.0+abc", "0.1.0+abc")]
    public void The_header_names_the_short_build(string informational, string expected) =>
        Assert.Equal(expected, ProductVersion.BuildForm(informational));

    [Fact]
    public void The_header_form_never_carries_more_than_seven_hash_characters()
    {
        string build = ProductVersion.BuildForm("1.2.3+0123456789abcdef");

        Assert.Equal("1.2.3+0123456", build);
        Assert.DoesNotContain("+01234567", build, StringComparison.Ordinal);
    }
}
