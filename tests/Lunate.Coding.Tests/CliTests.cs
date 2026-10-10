using Spectre.Console.Testing;

namespace Lunate.Coding.Tests;

public sealed class CliTests
{
    [Fact]
    public void Run_with_empty_args_and_no_terminal_hints_at_print_mode()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var options = new InteractiveSessionOptions
        {
            Console = new ScriptedConsoleIO(),
            Scheduler = new ManualScheduler(),
        };

        var exitCode = Cli.Run(
            [],
            output,
            error,
            TestContext.Current.CancellationToken,
            interactiveOptions: options
        );

        Assert.Equal(2, exitCode);
        Assert.Contains("lunate -p", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_a_scripted_terminal_starts_and_ends_the_session()
    {
        using var temp = new TempDirectory();
        var console = new ScriptedConsoleIO { IsInteractive = true };
        console.Complete();
        var options = new InteractiveSessionOptions
        {
            Factory = new FakeChatClientFactory(new ScriptedChatClient()),
            SettingsPath = temp.File("settings.json"),
            ModelsPath = temp.File("models.json"),
            AuthPath = temp.File("auth.json"),
            SessionDirectory = temp.File("sessions"),
            WorkingDirectory = temp.Root,
            HistoryPath = temp.File("history"),
            Environment = PrintModeTestSupport.Environment(),
            Console = console,
            Scheduler = new ManualScheduler(),
            Scrollback = new TestConsole(),
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            [],
            output,
            error,
            TestContext.Current.CancellationToken,
            interactiveOptions: options
        );

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_help_documents_the_interactive_entry_and_commands()
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
        Assert.Contains("start the interactive session", help, StringComparison.Ordinal);
        Assert.Contains(
            "commands: /model /new /resume /compact /quit",
            help,
            StringComparison.Ordinal
        );
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
        Assert.Contains("-p [--json] [--yolo] <prompt>", help, StringComparison.Ordinal);
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
    public void Run_with_print_accepts_flags_before_the_prompt()
    {
        using var temp = new TempDirectory();
        var factory = new FakeChatClientFactory(
            new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop())
        );
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["-p", "--json", "--yolo", "Hi"],
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
    public void Run_with_help_lists_acp_mode()
    {
        using var writer = new StringWriter();

        var exitCode = Cli.Run(
            ["--help"],
            writer,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, exitCode);
        Assert.Contains("--acp", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "Agent Client Protocol over stdio",
            writer.ToString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Run_with_acp_and_print_mode_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--acp", "-p", "hi"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage: lunate --acp", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_acp_and_a_prompt_argument_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--acp", "hi"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage: lunate --acp", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("--acp", "--json")]
    [InlineData("--acp", "--yolo")]
    public void Run_with_acp_and_a_print_flag_is_a_usage_error(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(args, output, error, TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage: lunate --acp", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
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
