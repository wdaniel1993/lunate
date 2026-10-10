using System.Reactive.Concurrency;
using System.Reflection;
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
        InteractiveSessionOptions? interactiveOptions = null
    )
    {
        if (args is ["--version"])
        {
            output.WriteLine(GetProductVersion());
            return 0;
        }

        if (args is ["--help"] or ["-h"])
        {
            WriteHelp(output);
            return 0;
        }

        if (args.Length == 0)
        {
            return RunInteractive(errors, ct, interactiveOptions);
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

        if (!TryParsePrint(args, errors, out PrintModeOptions? parsed))
        {
            return 0;
        }

        if (parsed is null)
        {
            return 2;
        }

        PrintModeOptions options = (printOptions ?? new PrintModeOptions()) with
        {
            Prompt = parsed.Prompt,
            Json = parsed.Json,
            Yolo = parsed.Yolo,
        };
        return PrintMode.RunAsync(options, output, errors, ct).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Parses the print-mode flags; returns false when no print flag is present. A null
    /// <paramref name="options"/> means a usage error was written to stderr.
    /// </summary>
    private static bool TryParsePrint(
        string[] args,
        TextWriter errors,
        out PrintModeOptions? options
    )
    {
        options = null;
        if (!args.Contains("-p") && !args.Contains("--print"))
        {
            return false;
        }

        var json = false;
        var yolo = false;
        string? prompt = null;
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "-p" or "--print":
                    break;
                case "--json":
                    json = true;
                    break;
                case "--yolo":
                    yolo = true;
                    break;
                default:
                    // The prompt is the first (and only) non-flag argument; --json and
                    // --yolo may come before or after it, so `lunate -p --json "hi"`
                    // reads naturally. Flag-shaped unknown tokens stay usage errors.
                    if (prompt is not null || arg.StartsWith('-') || string.IsNullOrWhiteSpace(arg))
                    {
                        WritePrintUsage(errors);
                        return true;
                    }

                    prompt = arg;
                    break;
            }
        }

        if (prompt is null)
        {
            WritePrintUsage(errors);
            return true;
        }

        options = new PrintModeOptions
        {
            Prompt = prompt,
            Json = json,
            Yolo = yolo,
        };
        return true;
    }

    private static void WritePrintUsage(TextWriter errors) =>
        errors.WriteLine("Usage: lunate -p [--json] [--yolo] <prompt>");

    /// <summary>
    /// Bare <c>lunate</c> starts the interactive session; a console that is not a terminal
    /// (<c>TERM=dumb</c> included) exits with 2 and a hint to print mode.
    /// </summary>
    private static int RunInteractive(
        TextWriter errors,
        CancellationToken ct,
        InteractiveSessionOptions? options
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

        if (ConsoleSupport.Check(console) is not null)
        {
            errors.WriteLine(
                "lunate: interactive mode needs a terminal; use lunate -p \"<prompt>\""
            );
            return 2;
        }

        using var session = new InteractiveSession(resolved);
        session.RunAsync(ct).GetAwaiter().GetResult();
        return 0;
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

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("Lunate - a coding agent for the terminal.");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  lunate -p [--json] [--yolo] <prompt>");
        output.WriteLine(
            "                                 run one prompt and print the final answer to stdout;"
        );
        output.WriteLine(
            "                                 --json streams every event as one JSON line, --yolo"
        );
        output.WriteLine("                                 approves every tool call for this run");
        output.WriteLine(
            "  lunate                         start the interactive session on a terminal"
        );
        output.WriteLine(
            "                                 commands: /model /new /resume /compact /quit"
        );
        output.WriteLine("  lunate --version               print the version");
        output.WriteLine("  lunate --help                  show this help");
        output.WriteLine("  lunate --discover <name-or-url>");
        output.WriteLine(
            "                                 list an OpenAI-compatible endpoint's models and"
        );
        output.WriteLine(
            "                                 print a models.json draft to stdout (nothing is written)"
        );
    }

    private static string GetProductVersion()
    {
        var assembly = typeof(Cli).Assembly;

        return assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
