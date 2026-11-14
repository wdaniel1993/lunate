namespace Lunate.Coding.Tests;

/// <summary>
/// <c>--env</c> parses through the one launch parser: a value, the inline form, a missing value,
/// and a value that is not one of the two accepted shapes refused with both shapes named.
/// </summary>
public sealed class LaunchOptionsEnvironmentTests
{
    [Theory]
    [InlineData("docker:alpine")]
    [InlineData("podman:alpine")]
    [InlineData("docker:ghcr.io/org/image:tag")]
    public void The_flag_selects_the_container_environment(string value)
    {
        Assert.True(LaunchOptions.TryParse(["--env", value], out LaunchOptions launch, out _));
        Assert.Equal(value, launch.Environment?.Description);

        Assert.True(LaunchOptions.TryParse([$"--env={value}"], out LaunchOptions inline, out _));
        Assert.Equal(value, inline.Environment?.Description);
    }

    [Fact]
    public void The_flag_needs_a_value()
    {
        Assert.False(LaunchOptions.TryParse(["--env"], out _, out string? error));
        Assert.Equal("option '--env' expects a value", error);
    }

    [Theory]
    [InlineData("alpine")]
    [InlineData("docker:")]
    [InlineData("containerd:alpine")]
    public void An_invalid_selection_is_refused_with_both_shapes(string value)
    {
        Assert.False(
            LaunchOptions.TryParse(["--env", value], out LaunchOptions launch, out string? error)
        );
        Assert.Equal(
            $"option '--env' expects docker:<image> or podman:<image>, but was '{value}'",
            error
        );
        Assert.Null(launch.Environment);
    }

    [Fact]
    public void No_flag_keeps_the_local_default()
    {
        Assert.True(LaunchOptions.TryParse([], out LaunchOptions launch, out _));
        Assert.Null(launch.Environment);
    }
}
