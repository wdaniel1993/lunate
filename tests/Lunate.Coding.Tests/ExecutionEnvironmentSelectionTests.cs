namespace Lunate.Coding.Tests;

/// <summary>
/// The one accepted selection shape: <c>docker:&lt;image&gt;</c> or <c>podman:&lt;image&gt;</c>, with
/// the image keeping its own tag and registry path, and everything else refused with both shapes.
/// </summary>
public sealed class ExecutionEnvironmentSelectionTests
{
    [Theory]
    [InlineData("docker:alpine", "docker", "alpine")]
    [InlineData("podman:alpine", "podman", "alpine")]
    [InlineData("docker:ghcr.io/org/image:tag", "docker", "ghcr.io/org/image:tag")]
    [InlineData("podman:localhost:5000/repo/image:v1", "podman", "localhost:5000/repo/image:v1")]
    public void An_accepted_shape_parses_its_engine_and_image(
        string value,
        string engine,
        string image
    )
    {
        Assert.True(ExecutionEnvironmentSelection.TryParse(value, out var selection));
        Assert.NotNull(selection);
        Assert.Equal(engine, selection.Engine);
        Assert.Equal(image, selection.Image);
        Assert.Equal(engine, selection.Executable);
        Assert.Equal(value, selection.Description);
        Assert.Equal(value, selection.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("alpine")]
    [InlineData("docker")]
    [InlineData("docker:")]
    [InlineData(":alpine")]
    [InlineData("Docker:alpine")]
    [InlineData("containerd:alpine")]
    [InlineData("docker:al pine")]
    [InlineData("docker:-rm")]
    public void Anything_else_is_refused(string? value)
    {
        Assert.False(ExecutionEnvironmentSelection.TryParse(value, out var selection));
        Assert.Null(selection);
    }

    [Fact]
    public void The_refusal_names_both_accepted_shapes()
    {
        Assert.Equal(
            "docker:<image> or podman:<image>",
            ExecutionEnvironmentSelection.AcceptedShapes
        );
    }
}
