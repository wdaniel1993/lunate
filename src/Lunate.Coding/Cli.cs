using System.Reactive.Concurrency;
using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding;

internal static class Cli
{
    internal static int Run(
        string[] args,
        TextWriter output,
        TextWriter errors,
        CancellationToken ct = default,
        PrintModeOptions? printOptions = null,
        InteractiveSessionOptions? interactiveOptions = null,
        ExtensionSeams? extensions = null,
        // The trust command's repository root; null uses the process working directory.
        string? workingDirectory = null,
        CatalogUpdateSeams? catalogUpdate = null,
        // The interactive session's update-check seams; null uses the production cache and transport.
        UpdateCheckSeams? updateCheck = null,
        // The trust command's terminal check; null reads the real stdin. Tests supply it so the
        // requirement does not depend on how the test host was started.
        bool? stdinIsTerminal = null,
        // The auth command's paths and boundaries; null uses the production defaults.
        AuthCommandSeams? authSeams = null,
        // The setup command's paths, transport and client factory; null uses the production defaults.
        SetupCommandSeams? setupSeams = null,
        string? hardlinkScanRoot = null,
        IHardlinkScanFileSystem? hardlinkScanFileSystem = null
    )
    {
        var noExtensions = false;
        if (args.Contains("--no-extensions"))
        {
            noExtensions = true;
            args =
            [
                .. args.Where(arg =>
                    !string.Equals(arg, "--no-extensions", StringComparison.Ordinal)
                ),
            ];
        }

        if (args is ["--version"])
        {
            output.WriteLine(GetProductVersion());
            // A redirected or sandboxed run is never invisible: the active root prints on its own
            // line, in the same spelling the welcome header uses.
            if (LunateHome.ActiveHomeLine is { } home)
            {
                output.WriteLine(home);
            }

            return 0;
        }

        if (args is ["--help"] or ["-h"])
        {
            CliHelp.Write(output);
            return 0;
        }

        if (args.Length > 0 && args[0] == "trust")
        {
            return TrustCommand.Run(
                args[1..],
                workingDirectory,
                output,
                errors,
                noExtensions ? Suppress(extensions) : extensions,
                stdinIsTerminal: stdinIsTerminal
            );
        }

        if (args.Length > 0 && args[0] == "extensions")
        {
            return ExtensionsCommand.Run(
                args[1..],
                workingDirectory,
                output,
                errors,
                noExtensions ? Suppress(extensions) : extensions,
                ct
            );
        }

        if (args.Length > 0 && args[0] == "auth")
        {
            AuthCommandSeams auth = authSeams ?? new AuthCommandSeams();
            if (auth.StdinIsTerminal is null && stdinIsTerminal is { } terminal)
            {
                auth = auth with { StdinIsTerminal = terminal };
            }

            return AuthCommand.Run(args[1..], output, errors, auth);
        }

        if (args.Length > 0 && args[0] == "setup")
        {
            SetupCommandSeams setup = setupSeams ?? new SetupCommandSeams();
            if (setup.StdinIsTerminal is null && stdinIsTerminal is { } setupTerminal)
            {
                setup = setup with { StdinIsTerminal = setupTerminal };
            }

            // `lunate setup` on a terminal with no flags is the wizard (spec: Setup command line):
            // the entry menu `/setup` opens on a configured install, the walk bare `lunate` opens
            // on a first run. Flags keep the scripted walk; a flagless run without a terminal
            // refuses inside SetupCommand instead of opening a wizard nobody can answer.
            if (args.Length == 1 && (setup.StdinIsTerminal ?? !Console.IsInputRedirected))
            {
                return RunInteractive(
                    errors,
                    ct,
                    interactiveOptions,
                    noExtensions ? Suppress(extensions) : extensions,
                    new LaunchOptions(),
                    updateCheck,
                    hardlinkScanRoot,
                    hardlinkScanFileSystem,
                    setupOnStart: true
                );
            }

            return SetupCommand.Run(args[1..], output, errors, setup, ct);
        }

        if (args.Length > 0 && args[0] == "cache")
        {
            return CacheCommand.Run(args[1..], workingDirectory, output, errors, extensions);
        }

        if (args.Length > 0 && args[0] == "packages")
        {
            return PackagesCommand.Run(args[1..], workingDirectory, output, errors, extensions);
        }

        if (args.Length > 0 && args[0] == "mcp")
        {
            return McpCommand.Run(args[1..], errors, ct);
        }

        if (args.Length > 0 && args[0] == "install")
        {
            return InstallCommand.Run(args[1..], workingDirectory, output, errors, extensions, ct);
        }

        if (args.Length > 0 && args[0] == "uninstall")
        {
            return UninstallCommand.Run(args[1..], workingDirectory, output, errors, extensions);
        }

        if (args.Length > 0 && args[0] == "--discover")
        {
            if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                errors.WriteLine("Usage: lunate --discover <name-or-url>");
                return 2;
            }

            return Discover(args[1], output, errors);
        }

        if (args.Length > 0 && args[0] == "--update-catalog")
        {
            return CatalogUpdateCommand.Run(args[1..], output, errors, ct, catalogUpdate);
        }

        if (args.Contains("--acp"))
        {
            string[] acpArgs =
            [
                .. args.Where(arg => !string.Equals(arg, "--acp", StringComparison.Ordinal)),
            ];
            if (!TryParseAcpOptions(acpArgs, out LaunchOptions acpLaunch, out _))
            {
                WriteAcpUsage(errors, args);
                return 2;
            }

            return AcpMode.Run(
                errors,
                Console.OpenStandardInput(),
                Console.OpenStandardOutput(),
                ct,
                extensions: noExtensions ? Suppress(extensions) : extensions,
                launch: acpLaunch,
                hardlinkScanRoot: hardlinkScanRoot,
                hardlinkScanFileSystem: hardlinkScanFileSystem
            );
        }

        if (!LaunchOptions.TryParse(args, out LaunchOptions launch, out string? error))
        {
            errors.WriteLine($"lunate: {error}");
            WriteLaunchUsage(errors);
            return 2;
        }

        // The picker needs the terminal UI; print mode refuses the flag before any composition
        // (design D3), so no client, extension host or MCP server starts.
        if (launch.Print && launch.Resume)
        {
            errors.WriteLine(
                "lunate: --resume needs the interactive picker; use --session <id|path> or --continue"
            );
            return 2;
        }

        ExtensionSeams? seams = launch.Print
            ? extensions ?? printOptions?.Extensions
            : extensions ?? interactiveOptions?.Extensions;
        if (noExtensions)
        {
            seams = Suppress(seams);
        }

        if (!launch.Print)
        {
            return RunInteractive(
                errors,
                ct,
                interactiveOptions,
                seams,
                launch,
                updateCheck,
                hardlinkScanRoot,
                hardlinkScanFileSystem
            );
        }

        PrintModeOptions options = (printOptions ?? new PrintModeOptions()) with
        {
            Prompt = launch.Prompt ?? string.Empty,
            Json = launch.Json,
            Yolo = launch.Yolo,
            Extensions = seams,
            Launch = launch,
            HardlinkScanRoot = printOptions?.HardlinkScanRoot ?? hardlinkScanRoot,
            HardlinkScanFileSystem = printOptions?.HardlinkScanFileSystem ?? hardlinkScanFileSystem,
        };
        return PrintMode.RunAsync(options, output, errors, ct).GetAwaiter().GetResult();
    }

    /// <summary>The <c>--no-extensions</c> seam: the same store and log, the host switch off.</summary>
    private static ExtensionSeams Suppress(ExtensionSeams? seams) =>
        (seams ?? new ExtensionSeams()) with
        {
            NoExtensions = true,
        };

    private static void WriteLaunchUsage(TextWriter errors)
    {
        errors.WriteLine("Usage: lunate [options] [-p [--json] [--yolo] <prompt>]");
        errors.WriteLine("Run 'lunate --help' for the full list of options.");
    }

    /// <summary>
    /// Parses the flags ACP may carry (<paramref name="args"/> excludes <c>--acp</c>): the
    /// composition flags parse through the one launch parser, while print and selection flags are
    /// usage errors because the editor owns the session lifecycle (design D3).
    /// </summary>
    internal static bool TryParseAcpOptions(
        IReadOnlyList<string> args,
        out LaunchOptions launch,
        out string? error
    )
    {
        launch = new LaunchOptions();
        if (!LaunchOptions.TryParse(args, out LaunchOptions parsed, out error))
        {
            return false;
        }

        if (parsed.Print)
        {
            error = "-p/--print is a usage error with --acp";
            return false;
        }

        if (parsed.Json)
        {
            error = "--json is a usage error with --acp";
            return false;
        }

        if (parsed.Yolo)
        {
            error = "--yolo is a usage error with --acp";
            return false;
        }

        if (parsed.HasSelection)
        {
            error = "session selection is a usage error with --acp";
            return false;
        }

        if (parsed.Environment is not null)
        {
            // ACP keeps the host path in this change; a selection it silently dropped would be
            // exactly the silent host fallback the setting forbids.
            error = "--env is a usage error with --acp; ACP runs tool commands on the host";
            return false;
        }

        launch = parsed;
        return true;
    }

    private static void WriteAcpUsage(TextWriter errors, string[] args)
    {
        errors.WriteLine("Usage: lunate --acp");
        errors.WriteLine(
            "lunate: --acp speaks the Agent Client Protocol over stdio and cannot be combined with -p, --json or a prompt"
        );
        if (
            args.Any(arg =>
                arg is "-c" or "--continue" or "-r" or "--resume" or "--session" or "--no-session"
            )
        )
        {
            errors.WriteLine(
                "lunate: --acp cannot select a session; --continue, --resume, --session and --no-session are usage errors with --acp"
            );
        }
        if (args.Any(arg => arg == "--env" || arg.StartsWith("--env=", StringComparison.Ordinal)))
        {
            errors.WriteLine(
                "lunate: --acp cannot select a container environment; ACP runs tool commands on the host"
            );
        }
    }

    /// <summary>
    /// Bare <c>lunate</c> starts the interactive session; a console that is not a terminal
    /// (<c>TERM=dumb</c> included) exits with 2 and a hint to print mode.
    /// </summary>
    private static int RunInteractive(
        TextWriter errors,
        CancellationToken ct,
        InteractiveSessionOptions? options,
        ExtensionSeams? extensions,
        LaunchOptions launch,
        UpdateCheckSeams? updateCheck,
        string? hardlinkScanRoot,
        IHardlinkScanFileSystem? hardlinkScanFileSystem,
        bool setupOnStart = false
    )
    {
        IConsoleIO console;
        InteractiveSessionOptions resolved;
        UpdateCheckSeams? updateSeams = options is null
            ? updateCheck ?? new UpdateCheckSeams()
            : updateCheck ?? options.UpdateCheck;
        if (launch.Offline)
        {
            updateSeams = null;
        }

        if (options is null)
        {
            console = new SystemConsoleIO(Scheduler.Default);
            resolved = new InteractiveSessionOptions
            {
                Console = console,
                Scheduler = Scheduler.Default,
                HardlinkScanRoot = hardlinkScanRoot,
                HardlinkScanFileSystem = hardlinkScanFileSystem,
            };
        }
        else
        {
            console = options.Console;
            resolved = options;
        }

        resolved = resolved with
        {
            Extensions = extensions ?? options?.Extensions,
            Launch = launch,
            UpdateCheck = updateSeams,
            HardlinkScanRoot = options?.HardlinkScanRoot ?? hardlinkScanRoot,
            HardlinkScanFileSystem = options?.HardlinkScanFileSystem ?? hardlinkScanFileSystem,
            SetupOnStart = setupOnStart || resolved.SetupOnStart,
        };

        if (ConsoleSupport.Check(console) is { } unsupportedMode)
        {
            int columns = console.IsInteractive ? console.Size.Columns : 0;
            // A piped start on a machine with no chat answer is the configure-it case: the one
            // refusal names `lunate setup` and its flags and exits 2, instead of sending the user
            // to print mode on a machine print mode cannot run either (spec: The wizard trigger).
            if (NeedsSetup(options))
            {
                TerminalMessage.Write(errors, $"lunate: {SetupRefusal.Text}", columns);
                return SetupRefusal.ExitCode;
            }

            TerminalMessage.Write(
                errors,
                $"lunate: {unsupportedMode}; use lunate -p \"<prompt>\"",
                columns
            );
            return 2;
        }

        using var session = new InteractiveSession(resolved);
        session.RunAsync(ct).GetAwaiter().GetResult();
        return session.ExitCode;
    }

    /// <summary>
    /// The piped/interactive refusal's one question, asked from configuration alone: does this
    /// machine have a chat answer at all? A settings file that does not load is the session's own
    /// error to report, so the terminal hint stands and composition reports it.
    /// </summary>
    private static bool NeedsSetup(InteractiveSessionOptions? options)
    {
        try
        {
            return SetupRefusal.Applies(
                SettingsStore.Resolve(options?.SettingsPath, options?.Environment)
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private static int Discover(string target, TextWriter output, TextWriter errors)
    {
        DiscoverResult result;
        try
        {
            // The stored custom providers are in the catalog, so a stored entry's models URL and
            // credential reach discovery the same way a preset's do.
            AgentSettings settings = SettingsStore.Resolve(AuthStore.SettingsFilePath(null), null);
            ModelCatalog catalog = HarnessFactory.LoadCatalog(settings, modelsPath: null);
            AuthStore auth = AuthStore.Load();
            result = DiscoverCommand
                .RunAsync(target, catalog, namedKeySource: auth.TryGet)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return 1;
        }

        if (!result.Succeeded)
        {
            errors.WriteLine(result.Error);
            return 1;
        }

        output.WriteLine(result.Draft);
        return 0;
    }

    private static string GetProductVersion() => ProductVersion.Informational;
}
