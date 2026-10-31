using Lunate.Extensibility.Abstractions;
using Lunate.Contracts;

namespace Lunate.Extensibility.Tests;

public sealed class ExtensionApiTests
{
    [Fact]
    public void Current_is_the_released_contract_version() =>
        Assert.Equal("1.15.0", ExtensionApi.Current);

    /// <summary>
    /// The gate compares an extension's referenced <c>Lunate.Extensibility.Abstractions</c> version
    /// with <see cref="ExtensionApi.Current"/>; the bottom contract assembly is versioned in
    /// lockstep, so both references name the same shipped API.
    /// </summary>
    [Fact]
    public void The_contract_assembly_version_is_the_api_version() =>
        Assert.Equal(
            ExtensionApi.Current,
            typeof(ExtensionApi).Assembly.GetName().Version!.ToString(3)
        );

    [Fact]
    public void The_bottom_contract_assembly_version_is_the_api_version() =>
        Assert.Equal(
            ExtensionApi.Current,
            typeof(DecisionRequest).Assembly.GetName().Version!.ToString(3)
        );

    [Theory]
    [InlineData("1.15.0")]
    [InlineData("^1.15.0")]
    [InlineData("^1.13.0")]
    [InlineData("^1.11.0")]
    [InlineData("^1.10.0")]
    [InlineData("^1.9.0")]
    [InlineData("^1.8.0")]
    [InlineData("^1.7.0")]
    [InlineData("^1.6.0")]
    [InlineData("^1.5.0")]
    [InlineData("^1.4.0")]
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
    [InlineData("1.3.0")]
    [InlineData("1.4.0")]
    [InlineData("1.5.0")]
    [InlineData("1.6.0")]
    [InlineData("1.7.0")]
    [InlineData("1.8.0")]
    [InlineData("1.9.0")]
    [InlineData("1.10.0")]
    [InlineData("1.11.0")]
    [InlineData("1.12.0")]
    [InlineData("1.13.0")]
    [InlineData("1.14.0")]
    [InlineData("2.0.0")]
    [InlineData("^0.9.9")]
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
