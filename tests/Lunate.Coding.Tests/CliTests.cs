namespace Lunate.Coding.Tests;

public sealed class CliTests
{
    [Fact]
    public void Run_with_empty_args_is_a_no_op()
    {
        using var writer = new StringWriter();

        var exitCode = Cli.Run([], writer, TextWriter.Null, TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void Run_with_unknown_arg_is_a_no_op()
    {
        using var writer = new StringWriter();

        var exitCode = Cli.Run(
            ["--unknown"],
            writer,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, exitCode);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void Run_with_help_lists_discover()
    {
        using var writer = new StringWriter();

        var exitCode = Cli.Run(
            ["--help"],
            writer,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, exitCode);
        Assert.Contains("--discover", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_with_help_lists_print_mode()
    {
        using var writer = new StringWriter();

        var exitCode = Cli.Run(
            ["--help"],
            writer,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, exitCode);
        string help = writer.ToString();
        Assert.Contains("-p <prompt>", help, StringComparison.Ordinal);
        Assert.Contains("--json", help, StringComparison.Ordinal);
        Assert.Contains("--yolo", help, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_with_print_and_no_prompt_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(["-p"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_print_and_an_unknown_flag_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["-p", "hi", "--bogus"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_print_accepts_flags_in_any_order()
    {
        using var temp = new TempDirectory();
        var factory = new FakeChatClientFactory(
            new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop())
        );
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--json", "--yolo", "-p", "Hi"],
            output,
            error,
            TestContext.Current.CancellationToken,
            options
        );

        Assert.Equal(0, exitCode);
        Assert.StartsWith(
            """{"type":"run_started","runId":""",
            output.ToString(),
            StringComparison.Ordinal
        );
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void Run_with_discover_and_no_target_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--discover"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--discover", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_discover_and_extra_args_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--discover", "a", "b"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--discover", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }
}
