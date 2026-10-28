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
        // The trust command's terminal check; null reads the real stdin. Tests supply it so the
        // requirement does not depend on how the test host was started.
        bool? stdinIsTerminal = null,
        // The auth command's paths and boundaries; null uses the production defaults.
        AuthCommandSeams? authSeams = null
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
                launch: acpLaunch
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
            return RunInteractive(errors, ct, interactiveOptions, seams, launch);
        }

        PrintModeOptions options = (printOptions ?? new PrintModeOptions()) with
        {
            Prompt = launch.Prompt ?? string.Empty,
            Json = launch.Json,
            Yolo = launch.Yolo,
            Extensions = seams,
            Launch = launch,
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
        LaunchOptions launch
    )
    {
        IConsoleIO console;
        InteractiveSessionOptions resolved;
        if (options is null)
        {
            console = new SystemConsoleIO(Scheduler.Default);
            resolved = new InteractiveSessionOptions
            {
                Console = console,
                Scheduler = Scheduler.Default,
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
        };

        if (ConsoleSupport.Check(console) is not null)
        {
            errors.WriteLine(
                "lunate: interactive mode needs a terminal; use lunate -p \"<prompt>\""
            );
            return 2;
        }

        using var session = new InteractiveSession(resolved);
        session.RunAsync(ct).GetAwaiter().GetResult();
        return session.ExitCode;
    }

    private static int Discover(string target, TextWriter output, TextWriter errors)
    {
        DiscoverResult result;
        try
        {
            ModelCatalog catalog = ModelCatalog.Load();
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
