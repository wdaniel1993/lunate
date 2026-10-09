using System.Reflection;
using Lunate.Ai;

namespace Lunate.Coding;

internal static class Cli
{
    internal static int Run(
        string[] args,
        TextWriter output,
        TextWriter errors,
        CancellationToken ct = default,
        PrintModeOptions? printOptions = null
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
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "-p" or "--print":
                    if (prompt is not null || index + 1 >= args.Length)
                    {
                        WritePrintUsage(errors);
                        return true;
                    }

                    prompt = args[++index];
                    if (string.IsNullOrWhiteSpace(prompt))
                    {
                        WritePrintUsage(errors);
                        return true;
                    }

                    break;
                case "--json":
                    json = true;
                    break;
                case "--yolo":
                    yolo = true;
                    break;
                default:
                    WritePrintUsage(errors);
                    return true;
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
        errors.WriteLine("Usage: lunate -p <prompt> [--json] [--yolo]");

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
        output.WriteLine("  lunate -p <prompt> [--json] [--yolo]");
        output.WriteLine(
            "                                 run one prompt and print the final answer to stdout;"
        );
        output.WriteLine(
            "                                 --json streams every event as one JSON line, --yolo"
        );
        output.WriteLine("                                 approves every tool call for this run");
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
