namespace Lunate.Coding.Tests;

public sealed class CliTests
{
    [Fact]
    public void Run_with_empty_args_is_a_no_op()
    {
        using var writer = new StringWriter();

        var exitCode = Cli.Run([], writer, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void Run_with_unknown_arg_is_a_no_op()
    {
        using var writer = new StringWriter();

        var exitCode = Cli.Run(["--unknown"], writer, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void Run_with_help_lists_discover()
    {
        using var writer = new StringWriter();

        var exitCode = Cli.Run(["--help"], writer, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Contains("--discover", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_with_discover_and_no_target_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(["--discover"], output, error);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--discover", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_discover_and_extra_args_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(["--discover", "a", "b"], output, error);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--discover", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }
}
