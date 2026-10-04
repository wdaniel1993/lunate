namespace Lunate.Coding.Tests;

public sealed class CliTests
{
    [Fact]
    public void Run_with_empty_args_is_a_no_op()
    {
        using var writer = new StringWriter();

        var exitCode = Cli.Run([], writer);

        Assert.Equal(0, exitCode);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void Run_with_unknown_arg_is_a_no_op()
    {
        using var writer = new StringWriter();

        var exitCode = Cli.Run(["--unknown"], writer);

        Assert.Equal(0, exitCode);
        Assert.Empty(writer.ToString());
    }
}
