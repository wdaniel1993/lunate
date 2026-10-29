using Lunate.Ai;
using Spectre.Console.Testing;

namespace Lunate.Coding.Tests;

public sealed class CliTests
{
    public static TheoryData<string[], string, string> ConflictingLaunchFlags =>
        new()
        {
            { ["--continue", "--resume"], "--continue", "--resume" },
            { ["-c", "-r"], "--continue", "--resume" },
            { ["--continue", "--session", "s_1"], "--continue", "--session" },
            { ["--resume", "--session", "s_1"], "--resume", "--session" },
            { ["--session", "s_1", "--no-session"], "--session", "--no-session" },
            { ["--continue", "--no-session"], "--continue", "--no-session" },
            { ["--tools", "bash", "--no-tools"], "--tools", "--no-tools" },
        };

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
            Extensions = TestExtensionSeams.Empty(temp),
            McpConfigPath = temp.File("global-mcp.json"),
            McpAgentsConfigPath = temp.File("agents-mcp.json"),
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
    public void Run_with_an_unknown_tool_in_interactive_mode_is_a_usage_error()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient();
        var factory = new FakeChatClientFactory(client);
        var console = new ScriptedConsoleIO { IsInteractive = true };
        using var diagnostics = new StringWriter();
        var options = new InteractiveSessionOptions
        {
            Factory = factory,
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
            Diagnostics = diagnostics,
            Extensions = TestExtensionSeams.Empty(temp),
            McpConfigPath = temp.File("global-mcp.json"),
            McpAgentsConfigPath = temp.File("agents-mcp.json"),
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--tools", "nope"],
            output,
            error,
            TestContext.Current.CancellationToken,
            interactiveOptions: options
        );

        Assert.Equal(2, exitCode);
        Assert.Contains(
            "--tools names unknown tools: 'nope'",
            diagnostics.ToString(),
            StringComparison.Ordinal
        );
        Assert.Contains("available: read, write", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.Null(factory.CreatedModel);
        Assert.Empty(client.Requests);
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
            "commands: /model /new /resume /name /session /compact /mcp /extensions /trust /quit",
            help,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Run_with_help_lists_install_uninstall_and_packages_with_one_line_each()
    {
        using var writer = new StringWriter();

        var exitCode = Cli.Run(
            ["--help"],
            writer,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, exitCode);
        string[] lines = writer.ToString().ReplaceLineEndings("\n").Split('\n');
        foreach (
            string usage in new[]
            {
                "  lunate install <package-id>[@<version>]",
                "  lunate uninstall <package-id|extension-id>",
                "  lunate packages [",
            }
        )
        {
            int index = Array.FindIndex(
                lines,
                line => line.StartsWith(usage, StringComparison.Ordinal)
            );
            Assert.True(index >= 0, $"the help does not document '{usage}'");
            string description = lines[index + 1];
            Assert.StartsWith(
                "                                 ",
                description,
                StringComparison.Ordinal
            );
            Assert.False(string.IsNullOrWhiteSpace(description), $"'{usage}' has no description");

            // One description line: what follows the entry is a blank line or the next entry, never
            // a second indented line belonging to the same command.
            string following = lines[index + 2];
            Assert.True(
                following.Length == 0
                    || following.StartsWith("  lunate", StringComparison.Ordinal)
                    || following.StartsWith("  -", StringComparison.Ordinal)
                    || following.StartsWith("Session", StringComparison.Ordinal),
                $"'{usage}' has more than one description line: '{following}'"
            );
        }
    }

    [Fact]
    public void Run_with_unknown_arg_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(["--unknown"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("--unknown", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Usage", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
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
    public void Run_with_help_lists_update_catalog()
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
        Assert.Contains("--update-catalog", help, StringComparison.Ordinal);
        Assert.Contains("--source", help, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_with_update_catalog_and_an_unknown_argument_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--update-catalog", "--bogus"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, exitCode);
        Assert.Contains(
            "lunate: unexpected argument '--bogus' for --update-catalog",
            error.ToString(),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Usage: lunate --update-catalog [--output <path>] [--source <path-or-url>]",
            error.ToString(),
            StringComparison.Ordinal
        );
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("--output", "--output needs a path")]
    [InlineData("--source", "--source needs a path or URL")]
    public void Run_with_update_catalog_and_a_flag_missing_its_value_is_a_usage_error(
        string flag,
        string message
    )
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--update-catalog", flag],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, exitCode);
        Assert.Contains(message, error.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "Usage: lunate --update-catalog [--output <path>] [--source <path-or-url>]",
            error.ToString(),
            StringComparison.Ordinal
        );
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_update_catalog_and_help_prints_the_usage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--update-catalog", "--help"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "Usage: lunate --update-catalog [--output <path>] [--source <path-or-url>]",
            output.ToString(),
            StringComparison.Ordinal
        );
        Assert.Empty(error.ToString());
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
        Assert.Equal(
            $"lunate: {PrintMode.YoloWarning}\n",
            error.ToString().ReplaceLineEndings("\n")
        );
    }

    [Fact]
    public void Run_with_print_and_resume_refuses_before_composing()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        var factory = new FakeChatClientFactory(client);
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["-p", "--resume", "Hi"],
            output,
            error,
            TestContext.Current.CancellationToken,
            options
        );

        Assert.Equal(2, exitCode);
        Assert.Equal(
            "lunate: --resume needs the interactive picker; use --session <id|path> or --continue"
                + Environment.NewLine,
            error.ToString()
        );
        Assert.Empty(output.ToString());
        Assert.Null(factory.CreatedModel);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public void Run_with_print_and_a_missing_session_id_fails_naming_it()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        var factory = new FakeChatClientFactory(client);
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["-p", "--session", "missing-id", "Hi"],
            output,
            error,
            TestContext.Current.CancellationToken,
            options
        );

        Assert.Equal(2, exitCode);
        Assert.Contains(
            "lunate: no session with id 'missing-id'; run a prompt to start one",
            error.ToString(),
            StringComparison.Ordinal
        );
        Assert.Empty(output.ToString());
        Assert.Null(factory.CreatedModel);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public void Run_with_print_and_continue_without_a_session_names_the_working_directory()
    {
        using var temp = new TempDirectory();
        string cwd = new Workspace(temp.Root).WorktreeRoot;
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        var factory = new FakeChatClientFactory(client);
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(temp, factory);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["-p", "--continue", "Hi"],
            output,
            error,
            TestContext.Current.CancellationToken,
            options
        );

        Assert.Equal(2, exitCode);
        Assert.Contains(
            $"lunate: no session for '{cwd}'; run a prompt to start one",
            error.ToString(),
            StringComparison.Ordinal
        );
        Assert.Empty(output.ToString());
        Assert.Null(factory.CreatedModel);
        Assert.Empty(client.Requests);
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
        Assert.Equal(
            $"lunate: {PrintMode.YoloWarning}\n",
            error.ToString().ReplaceLineEndings("\n")
        );
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

    [Fact]
    public void Launch_options_parse_continue()
    {
        LaunchOptions options = Parse("--continue");

        Assert.True(options.Continue);
        Assert.True(Parse("-c").Continue);
    }

    [Fact]
    public void Launch_options_parse_resume()
    {
        LaunchOptions options = Parse("--resume");

        Assert.True(options.Resume);
        Assert.True(Parse("-r").Resume);
    }

    [Fact]
    public void Launch_options_parse_a_session_id()
    {
        LaunchOptions options = Parse("--session", "s_20261020-120000-abcd");

        Assert.Equal("s_20261020-120000-abcd", options.Session);
    }

    [Fact]
    public void Launch_options_parse_no_session()
    {
        Assert.True(Parse("--no-session").NoSession);
    }

    [Fact]
    public void Launch_options_parse_a_model()
    {
        Assert.Equal("gpt-4o", Parse("--model", "gpt-4o").Model);
    }

    [Fact]
    public void Launch_options_parse_an_inline_model()
    {
        Assert.Equal("gpt-4o", Parse("--model=gpt-4o").Model);
    }

    [Fact]
    public void Launch_options_parse_a_thinking_level()
    {
        Assert.Equal(ThinkingLevel.High, Parse("--thinking", "high").Thinking);
    }

    [Fact]
    public void Launch_options_parse_an_inline_thinking_level()
    {
        Assert.Equal(ThinkingLevel.High, Parse("--thinking=high").Thinking);
    }

    [Fact]
    public void Launch_options_parse_an_inline_session_id()
    {
        Assert.Equal("s_1", Parse("--session=s_1").Session);
    }

    [Fact]
    public void Launch_options_parse_a_tool_list()
    {
        Assert.Equal(["bash", "read"], Parse("--tools", "bash, read").Tools);
    }

    [Fact]
    public void Launch_options_parse_an_inline_tool_list()
    {
        Assert.Equal(["read", "write"], Parse("--tools=read,write").Tools);
    }

    [Fact]
    public void Launch_options_refuse_an_optional_tool_name_in_the_filter()
    {
        Assert.False(LaunchOptions.TryParse(["--tools", "grep"], out _, out string? error));
        Assert.Contains("--extra-tools", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Launch_options_parse_an_extra_tool_list()
    {
        Assert.Equal(["grep", "find"], Parse("--extra-tools=grep,find").ExtraTools);
    }

    [Fact]
    public void Launch_options_refuse_an_unknown_extra_tool()
    {
        Assert.False(LaunchOptions.TryParse(["--extra-tools", "wat"], out _, out string? error));
        Assert.Contains("'grep', 'find' or 'ls'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Launch_options_parse_no_tools()
    {
        LaunchOptions options = Parse("--no-tools");

        Assert.True(options.NoTools);
        Assert.Null(options.Tools);
    }

    [Fact]
    public void Launch_options_parse_the_prompt_directives()
    {
        LaunchOptions options = Parse(
            "--system-prompt",
            "system",
            "--append-system-prompt",
            "append"
        );

        Assert.Equal("system", options.SystemPrompt);
        Assert.Equal("append", options.AppendSystemPrompt);
    }

    [Fact]
    public void Launch_options_parse_an_inline_system_prompt_with_a_leading_dash()
    {
        Assert.Equal("-be concise", Parse("--system-prompt=-be concise").SystemPrompt);
    }

    [Fact]
    public void Launch_options_parse_an_inline_appended_system_prompt()
    {
        Assert.Equal("extra", Parse("--append-system-prompt=extra").AppendSystemPrompt);
    }

    [Fact]
    public void Launch_options_parse_the_print_flags()
    {
        LaunchOptions options = Parse("-p", "--json", "--yolo", "Hi");

        Assert.True(options.Print);
        Assert.True(options.Json);
        Assert.True(options.Yolo);
        Assert.Equal("Hi", options.Prompt);
    }

    [Fact]
    public void Launch_options_parse_offline()
    {
        Assert.False(Parse().Offline);
        Assert.True(Parse("--offline").Offline);
    }

    [Theory]
    [MemberData(nameof(ConflictingLaunchFlags))]
    public void Run_with_conflicting_launch_flags_is_a_usage_error(
        string[] args,
        string first,
        string second
    )
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(args, output, error, TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains(first, error.ToString(), StringComparison.Ordinal);
        Assert.Contains(second, error.ToString(), StringComparison.Ordinal);
        Assert.Contains("mutually exclusive", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("--continue", "--continue")]
    [InlineData("-c", "--continue")]
    [InlineData("--resume", "--resume")]
    [InlineData("-r", "--resume")]
    [InlineData("--no-session", "--no-session")]
    public void Run_with_acp_and_a_selection_flag_is_a_usage_error(string flag, string named)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--acp", flag],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, exitCode);
        Assert.Contains("--acp", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(named, error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_acp_and_a_session_flag_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--acp", "--session", "s_1"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, exitCode);
        Assert.Contains("--acp", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("--session", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("--session")]
    [InlineData("--model")]
    [InlineData("--thinking")]
    [InlineData("--tools")]
    [InlineData("--system-prompt")]
    [InlineData("--append-system-prompt")]
    public void Run_with_a_launch_flag_missing_its_value_is_a_usage_error(string flag)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run([flag], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains(flag, error.ToString(), StringComparison.Ordinal);
        Assert.Contains("expects a value", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_an_unknown_thinking_level_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--thinking", "max"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, exitCode);
        Assert.Contains("'--thinking'", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("max", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("--print=x", "--print")]
    [InlineData("--json=x", "--json")]
    [InlineData("--yolo=x", "--yolo")]
    [InlineData("--continue=x", "--continue")]
    [InlineData("--resume=x", "--resume")]
    [InlineData("--no-session=x", "--no-session")]
    [InlineData("--no-tools=x", "--no-tools")]
    [InlineData("--offline=x", "--offline")]
    public void Run_with_an_inline_value_on_a_boolean_flag_is_a_usage_error(string arg, string flag)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run([arg], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains(
            $"option '{flag}' does not take a value",
            error.ToString(),
            StringComparison.Ordinal
        );
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("--model=")]
    [InlineData("--model= ")]
    [InlineData("--system-prompt=")]
    [InlineData("--system-prompt=  ")]
    public void Run_with_an_empty_inline_value_is_a_usage_error(string arg)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run([arg], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("expects a value", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_an_unknown_inline_option_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(["--nope=x"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("unknown option '--nope=x'", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_a_space_separated_dash_value_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["--system-prompt", "-be concise"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, exitCode);
        Assert.Contains(
            "option '--system-prompt' expects a value",
            error.ToString(),
            StringComparison.Ordinal
        );
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_help_lists_every_launch_flag()
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
        Assert.Contains("-c, --continue", help, StringComparison.Ordinal);
        Assert.Contains("-r, --resume", help, StringComparison.Ordinal);
        Assert.Contains("--session <id|path>", help, StringComparison.Ordinal);
        Assert.Contains("--no-session", help, StringComparison.Ordinal);
        Assert.Contains("--model <id>", help, StringComparison.Ordinal);
        Assert.Contains("--thinking <level>", help, StringComparison.Ordinal);
        Assert.Contains("--tools <list>", help, StringComparison.Ordinal);
        Assert.Contains("--no-tools", help, StringComparison.Ordinal);
        Assert.Contains("--offline", help, StringComparison.Ordinal);
        Assert.Contains("--system-prompt <text>", help, StringComparison.Ordinal);
        Assert.Contains("--append-system-prompt <text>", help, StringComparison.Ordinal);
        Assert.Contains("--flag=value", help, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_with_an_interactive_prompt_argument_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(["hello"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("hello", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_an_empty_print_prompt_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(["-p", " "], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("cannot be empty", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Run_with_print_and_two_prompts_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Cli.Run(
            ["-p", "one", "two"],
            output,
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    private static LaunchOptions Parse(params string[] args)
    {
        Assert.True(
            LaunchOptions.TryParse(args, out LaunchOptions options, out string? error),
            error
        );
        return options;
    }
}
