using System.Text.Json.Nodes;
using Lunate.Extensibility;

namespace Lunate.Coding;

/// <summary>
/// The <c>lunate cache prune [--json]</c> subcommand: removes the extension cache entries no
/// discovered single-file extension declares. An entry is an orphan when the <c>.cs</c> file that
/// declared its id is gone — which is also how removing an extension deletes its builds, since the
/// host never runs at the moment a file is deleted. The shared NuGet packages folder is not ours and
/// is never touched: it is content-addressed and the user's SDK reads it too.
/// <para>
/// The live set is what this working directory can see: the user extensions root and this
/// project's, exactly as a session discovers them. An entry belonging to another project is
/// therefore an orphan here, which is the honest answer for "prune what nothing points at".
/// </para>
/// Exit codes: 0 nothing failed, 1 the store could not be scanned, 2 usage.
/// </summary>
internal static class CacheCommand
{
    internal const int ExitOk = 0;
    internal const int ExitFailure = 1;
    internal const int ExitUsage = 2;

    internal const string Usage = "Usage: lunate cache prune [--json]";

    internal static int Run(
        string[] args,
        string? workingDirectory,
        TextWriter output,
        TextWriter errors,
        ExtensionSeams? seams
    )
    {
        if (!TryParse(args, errors, out bool json))
        {
            return ExitUsage;
        }

        try
        {
            return Prune(workingDirectory, output, errors, seams, json);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return ExitFailure;
        }
    }

    private static int Prune(
        string? workingDirectory,
        TextWriter output,
        TextWriter errors,
        ExtensionSeams? seams,
        bool json
    )
    {
        ExtensionSeams resolved = seams ?? new ExtensionSeams();
        var options = new ExtensionHostOptions
        {
            StorePath = resolved.StorePath ?? ExtensionHostOptions.Default.StorePath,
            Log = resolved.Log ?? new StderrExtensionLog(errors),
        };

        string working = workingDirectory ?? Environment.CurrentDirectory;
        var loader = new ExtensionLoader(options);
        loader.Discover(working, working);
        HashSet<string> live = new(StringComparer.Ordinal);
        foreach (SingleFileExtension candidate in loader.SingleFileExtensions)
        {
            if (candidate.Id is { } id)
            {
                live.Add(id);
            }
        }

        IReadOnlyList<string> removed = SingleFileCachePruner.Prune(options.StorePath, live);
        if (json)
        {
            var names = new JsonArray();
            foreach (string id in removed)
            {
                names.Add(id);
            }

            output.WriteLine(new JsonObject { ["removed"] = names }.ToJsonString());
            return ExitOk;
        }

        if (removed.Count == 0)
        {
            output.WriteLine("cache: nothing to prune");
            return ExitOk;
        }

        foreach (string id in removed)
        {
            output.WriteLine(
                FormattableString.Invariant(
                    $"cache: removed '{id}' — no single-file extension declares it"
                )
            );
        }

        return ExitOk;
    }

    private static bool TryParse(string[] args, TextWriter errors, out bool json)
    {
        json = false;
        if (args.Length == 0 || args[0] != "prune")
        {
            errors.WriteLine(Usage);
            return false;
        }

        foreach (string arg in args[1..])
        {
            switch (arg)
            {
                case "--json" when !json:
                    json = true;
                    break;
                default:
                    errors.WriteLine(Usage);
                    return false;
            }
        }

        return true;
    }
}
