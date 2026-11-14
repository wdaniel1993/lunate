namespace Lunate.Tui.Tests;

/// <summary>
/// The environment status the header and <c>/context</c> render: the header names the selection,
/// <c>/context</c> adds the readiness, and the local default has no status at all.
/// </summary>
public sealed class ExecutionEnvironmentStatusTests
{
    [Fact]
    public void The_header_names_the_selection_without_a_readiness()
    {
        Assert.Equal(
            "environment podman:alpine",
            ExecutionEnvironmentStatus.StartingFor("podman:alpine").HeaderText
        );
        Assert.Equal(
            "environment docker:ghcr.io/org/image:tag",
            ExecutionEnvironmentStatus.ReadyFor("docker:ghcr.io/org/image:tag").HeaderText
        );
    }

    [Theory]
    [InlineData("starting")]
    [InlineData("ready")]
    [InlineData("failed: cannot connect to the engine")]
    public void Context_reports_the_readiness_in_parentheses(string readiness)
    {
        var status = new ExecutionEnvironmentStatus("podman:alpine", readiness);

        Assert.Equal($"environment: podman:alpine ({readiness})", status.ContextText);
    }

    [Fact]
    public void A_failure_names_the_reason()
    {
        ExecutionEnvironmentStatus failed = ExecutionEnvironmentStatus.FailedFor(
            "docker:alpine",
            "image not found"
        );

        Assert.Equal("failed: image not found", failed.Readiness);
        Assert.Equal("environment: docker:alpine (failed: image not found)", failed.ContextText);
    }
}
