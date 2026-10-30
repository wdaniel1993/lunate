using System.Text.Json.Nodes;
using Lunate.Ai;
using Lunate.Extensibility;

namespace Lunate.Coding;

/// <summary>
/// The <c>lunate extensions reload &lt;id|path&gt; [--json]</c> subcommand: the headless half of
/// <c>/extensions reload</c>. It is not a session swap — there is no session — so it does what a
/// reload's preflight and load can be held to on their own: trust for the bytes on disk, the build
/// against the lock bound to them, and a load of the result into a register-only host, which is the
/// only way to know the factory runs and the declarations match before an interactive session tries
/// it. It never prompts: an untrusted or changed file reports <c>failed (trust)</c> and says to
/// approve it in a session.
/// <para>
/// Exit codes: 0 reloaded (built and loaded), 1 the reload failed, 2 usage.
/// </para>
/// </summary>
internal static class ExtensionReloadCommand
{
    internal const int ExitOk = 0;
    internal const int ExitFailure = 1;
    internal const int ExitUsage = 2;

    internal const string Usage = "Usage: lunate extensions reload <id|path> [--json]";

    internal static int Run(
        string[] args,
        string? workingDirectory,
        TextWriter output,
        TextWriter errors,
        ExtensionSeams? seams,
        CancellationToken cancellationToken = default
    )
    {
        if (!TryParse(args, errors, out string? target, out bool json) || target is null)
        {
            return ExitUsage;
        }

        try
        {
            return ReloadAsync(
                    target,
                    workingDirectory,
                    output,
                    errors,
                    seams,
                    json,
                    cancellationToken
                )
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return ExitFailure;
        }
    }

    private static async Task<int> ReloadAsync(
        string target,
        string? workingDirectory,
        TextWriter output,
        TextWriter errors,
        ExtensionSeams? seams,
        bool json,
        CancellationToken cancellationToken
    )
    {
        ExtensionSeams resolved = seams ?? new ExtensionSeams();
        string working = workingDirectory ?? Environment.CurrentDirectory;
        var workspace = new Workspace(working);
        LunateHome.StoreOutsideWorkspace(workspace.WorktreeRoot, workspace.RepoRoot);
        var options = new ExtensionHostOptions
        {
            StorePath = resolved.StorePath ?? ExtensionHostOptions.Default.StorePath,
            Log = resolved.Log ?? new StderrExtensionLog(errors),
            ReservedCommands = Commands.BuiltinNames,
            ReservedToolNames = ExtensionSession.CoreToolNames,
            BuiltIns = resolved.BuiltIns ?? [],
        };

        var loader = new ExtensionLoader(options);
        loader.Discover(working, working);

        // A built-in cannot be reloaded: its code ran with the host (no build, no collectible
        // context to swap), so the headless reload refuses with the session's teaching message.
        if (
            loader.Descriptors.FirstOrDefault(descriptor =>
                descriptor.Scope is ExtensionScope.Builtin
                && string.Equals(descriptor.Id, target, StringComparison.Ordinal)
            )
            is not null
        )
        {
            errors.WriteLine(
                $"lunate: extension '{target}' is a built-in; built-ins load with the host and cannot be reloaded or unloaded."
            );
            return ExitFailure;
        }

        string? id = ExtensionBuildCommand.Resolve(loader.SingleFileExtensions, target)?.Id;
        if (id is null)
        {
            errors.WriteLine(
                FormattableString.Invariant(
                    $"lunate: no single-file extension matches '{target}'; pass a declared id or the path of an existing .cs file."
                )
            );
            return ExitUsage;
        }

        var builder = new SingleFileExtensionBuilder(
            options,
            resolved.RestoreEngine ?? SingleFileRestoreFactory.Engine,
            resolved.Compiler ?? SingleFileCompileFactory.Compiler
        );
        SingleFileReloadOutcome outcome = await loader
            .ReloadSingleFileAsync(
                id,
                builder,
                prompt: null,
                cancellationToken,
                // A headless reload verifies the load and leaves nothing running: no session, no
                // background services, no ToolsChanged.
                startSession: false
            )
            .ConfigureAwait(false);

        // The verifying load is real, so release its context before reporting: a reload that leaves
        // the extension loaded in a host about to exit would read as success it cannot keep.
        if (outcome.Reloaded)
        {
            await loader.Unload(id, notifyToolsChanged: false).ConfigureAwait(false);
        }

        return outcome.Reloaded
            ? Report(outcome, output, json)
            : ExtensionBuildCommand.ReportFailure(outcome.Build, errors, json, outcome.Reason);
    }

    private static int Report(SingleFileReloadOutcome outcome, TextWriter output, bool json)
    {
        SingleFileBuild build = outcome.Build.Build!;
        if (json)
        {
            var node = new JsonObject
            {
                ["id"] = outcome.Id,
                ["version"] = outcome.Build.Extension.Version,
                ["path"] = outcome.Build.Extension.Path,
                ["state"] = "reloaded",
                ["build"] = build.Key,
                ["assembly"] = build.AssemblyPath,
                ["tfm"] = build.Manifest.Tfm,
                ["rid"] = build.Manifest.Rid,
            };
            output.WriteLine(node.ToJsonString());
        }
        else
        {
            output.WriteLine(
                FormattableString.Invariant(
                    $"{outcome.Id} {outcome.Build.Extension.Version} reloaded {build.Key} — {build.AssemblyPath}"
                )
            );
        }

        return ExitOk;
    }

    private static bool TryParse(
        string[] args,
        TextWriter errors,
        out string? target,
        out bool json
    )
    {
        target = null;
        json = false;
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--json" when !json:
                    json = true;
                    break;
                case var flag when flag.StartsWith('-'):
                    errors.WriteLine(Usage);
                    return false;
                default:
                    if (target is not null)
                    {
                        errors.WriteLine(Usage);
                        return false;
                    }

                    target = arg;
                    break;
            }
        }

        if (target is null)
        {
            errors.WriteLine(Usage);
            return false;
        }

        return true;
    }
}
