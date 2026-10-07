using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class ExtensionApiTests
{
    [Fact]
    public void Current_is_the_released_contract_version() =>
        Assert.Equal("1.3.0", ExtensionApi.Current);

    [Theory]
    [InlineData("1.3.0")]
    [InlineData("^1.3.0")]
    [InlineData("^1.2.0")]
    [InlineData("^1.1.0")]
    [InlineData("^1.0.0")]
    [InlineData("^1.0.1")]
    public void Exact_and_caret_ranges_accept_the_current_version(string range) =>
        Assert.True(ExtensionApi.IsCompatible(range));

    [Theory]
    [InlineData("0.9.9")]
    [InlineData("1.0.0")]
    [InlineData("1.0.1")]
    [InlineData("1.1.0")]
    [InlineData("1.2.0")]
    [InlineData("1.4.0")]
    [InlineData("2.0.0")]
    [InlineData("^0.9.9")]
    [InlineData("^1.4.0")]
    [InlineData("^2.0.0")]
    public void Ranges_outside_the_current_version_are_incompatible(string range) =>
        Assert.False(ExtensionApi.IsCompatible(range));

    [Theory]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("v1.0.0")]
    [InlineData(">=1.0.0")]
    [InlineData("~1.0.0")]
    [InlineData("1.x")]
    [InlineData("1.0.0-rc.1")]
    [InlineData(" 1.0.0")]
    [InlineData("1.0.0 ")]
    [InlineData("^")]
    [InlineData("^1.0")]
    [InlineData("")]
    public void Unsupported_range_forms_are_refused(string range)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionApi.IsCompatible(range)
        );

        Assert.Contains(
            "unsupported apiVersion range",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains($"'{range}'", exception.Message, StringComparison.Ordinal);
    }
}
