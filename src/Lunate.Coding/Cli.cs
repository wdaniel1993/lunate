using System.Reflection;
using Lunate.Ai;

namespace Lunate.Coding;

internal static class Cli
{
    internal static int Run(string[] args, TextWriter output, TextWriter? error = null)
    {
        TextWriter errors = error ?? output;

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
