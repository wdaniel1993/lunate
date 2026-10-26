using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Extensibility.Restore;

namespace Lunate.Coding;

/// <summary>
/// The <c>lunate uninstall &lt;package-id|extension-id&gt; [--force] [--json]</c> command (spec:
/// "Uninstall"). It deletes exactly what the record lists, in the scope the record names, and prints
/// what it did; it writes nothing to stdout on a failure, exits 2 on a usage error and 1 when it
/// refuses — a changed file without <c>--force</c>, an ambiguous extension id, or a record nothing
/// may act on.
/// <para>
/// Like <c>install</c>, it is reachable only from the command line: no tool, slash command or
/// protocol path wraps it, so a model cannot remove the user's extensions.
/// </para>
/// </summary>
internal static class UninstallCommand
{
    internal const int ExitOk = 0;
    internal const int ExitFailure = 1;
    internal const int ExitUsage = 2;

    internal const string Usage =
        "Usage: lunate uninstall <package-id|extension-id> [--force] [--json]";

    internal static int Run(
        string[] args,
        string? workingDirectory,
        TextWriter output,
        TextWriter errors,
        ExtensionSeams? seams
    )
    {
        if (!TryParse(args, errors, out Request request))
        {
            return ExitUsage;
        }

        try
        {
            string working = workingDirectory ?? Environment.CurrentDirectory;
            var workspace = new Workspace(working);
            // Every command that writes the store runs the guard, exactly as install does.
            string storeRoot =
                seams?.StorePath
                ?? LunateHome.StoreOutsideWorkspace(workspace.WorktreeRoot, workspace.RepoRoot);
            PackageUninstallResult? result = null;
            PackageBrokenUninstallResult? broken = null;
            var uninstaller = new PackageUninstaller();
            try
            {
                result = uninstaller.Uninstall(
                    new PackageUninstallRequest
                    {
                        Id = request.Id,
                        StoreRoot = storeRoot,
                        Force = request.Force,
                    }
                );
            }
            catch (InvalidDataException) when (request.Force)
            {
                // --force is the answer to a record nothing may act on: the package id names the
                // record's own directory and the extension directory beside the package id.
                broken = uninstaller.UninstallBroken(
                    new PackageUninstallRequest
                    {
                        Id = request.Id,
                        StoreRoot = storeRoot,
                        Force = true,
                    }
                );
            }

            output.Write(
                request.Json
                    ? broken is not null
                        ? UninstallView.Json(broken) + "\n"
                        : UninstallView.Json(result!, storeRoot) + "\n"
                    : broken is not null
                        ? UninstallView.Text(broken)
                        : UninstallView.Text(result!, storeRoot)
            );
            return ExitOk;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return ExitFailure;
        }
    }

    private static bool TryParse(string[] args, TextWriter errors, out Request request)
    {
        request = new Request(string.Empty, false, false);
        string? id = null;
        var force = false;
        var json = false;
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--force" when !force:
                    force = true;
                    break;
                case "--json" when !json:
                    json = true;
                    break;
                default:
                    if (id is not null || arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        errors.WriteLine(Usage);
                        return false;
                    }

                    id = arg;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            errors.WriteLine(Usage);
            return false;
        }

        request = new Request(id, force, json);
        return true;
    }

    private sealed record Request(string Id, bool Force, bool Json);
}
