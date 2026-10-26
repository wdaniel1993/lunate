using System.Text.Json.Nodes;
using Lunate.Ai;
using Lunate.Extensibility;

namespace Lunate.Coding;

/// <summary>
/// The <c>lunate cache prune [--json]</c> subcommand: removes the extension cache entries nothing
/// live declares. An entry is an orphan when the <c>.cs</c> file that declared its id is gone — which
/// is also how removing an extension deletes its builds, since the host never runs at the moment a
/// file is deleted — or, for an installed package, when no record names its extension. The shared
/// NuGet packages folder is not ours and is never touched: it is content-addressed and the user's SDK
/// reads it too.
/// <para>
/// The live set is what this working directory can see — the user extensions root and this project's,
/// exactly as a session discovers them — plus every installed package's extension, wherever its
/// record lives: an install's lock is a resolution nothing regenerates, so pruning it would leave the
/// record vouching for a graph that is gone. An entry belonging to another project and to no install
/// is therefore an orphan here, which is the honest answer for "prune what nothing points at".
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
        string working = workingDirectory ?? Environment.CurrentDirectory;
        // This command writes the store it resolves, so a LUNATE_HOME inside the working tree or
        // its repository is refused before the store is opened.
        var workspace = new Workspace(working);
        LunateHome.StoreOutsideWorkspace(workspace.WorktreeRoot, workspace.RepoRoot);
        var options = new ExtensionHostOptions
        {
            StorePath = resolved.StorePath ?? ExtensionHostOptions.Default.StorePath,
            Log = resolved.Log ?? new StderrExtensionLog(errors),
        };

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

        // An installed package's cache entry holds its dependency lock, which no source file
        // regenerates: the install is the only thing that writes it, so pruning it would leave the
        // record vouching for a resolution that is gone. A reinstall is the repair, not a prune.
        foreach (
            InstalledPackageProvenance provenance in InstalledPackages.Read(options.StorePath).All
        )
        {
            if (provenance.ExtensionId is { Length: > 0 } extensionId)
            {
                live.Add(extensionId);
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
