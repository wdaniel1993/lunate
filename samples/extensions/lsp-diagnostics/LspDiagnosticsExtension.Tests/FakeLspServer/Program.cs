using System.Globalization;

namespace FakeLspServer;

/// <summary>
/// A deterministic stub language server for the lsp-diagnostics sample tests. It speaks the same
/// stdio JSON-RPC framing as a real server and derives diagnostics from the document text:
/// a line containing <c>FIXME</c> yields an error, <c>TODO</c> a warning, <c>INFO</c> an
/// information and <c>HINT</c> a hint (the first match per line wins). Modes:
/// <c>--silent</c> never publishes, <c>--marker &lt;path&gt;</c> appends the PID on start,
/// <c>--crash-after &lt;n&gt;</c> exits after n received messages, <c>--stale-first</c> publishes a
/// stale version before the matching one. A second <c>didOpen</c> for an open document publishes a
/// protocol error diagnostic instead of the document's diagnostics.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        bool silent = false;
        bool staleFirst = false;
        string? marker = null;
        int? crashAfter = null;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--silent":
                    silent = true;
                    break;
                case "--stale-first":
                    staleFirst = true;
                    break;
                case "--marker":
                    marker = Value(args, ref index, "--marker");
                    break;
                case "--crash-after":
                    crashAfter = int.Parse(
                        Value(args, ref index, "--crash-after"),
                        CultureInfo.InvariantCulture
                    );
                    break;
                default:
                    Console.Error.WriteLine(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"FakeLspServer: unknown argument '{args[index]}'"
                        )
                    );
                    return 2;
            }
        }

        if (marker is not null)
        {
            File.AppendAllText(
                marker,
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + Environment.NewLine
            );
        }

        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        await new FakeServer(silent, staleFirst, crashAfter).RunAsync(input, output);
        return 0;
    }

    private static string Value(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            Console.Error.WriteLine($"FakeLspServer: {option} needs a value");
            Environment.Exit(2);
        }

        return args[++index];
    }
}
