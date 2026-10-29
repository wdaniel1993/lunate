using System.Text.Json.Nodes;
using Lunate.Ai;
using Lunate.Extensibility;

namespace Lunate.Coding;

/// <summary>
/// The <c>lunate extensions build &lt;id|path&gt; [--json]</c> subcommand: the one headless way to
/// compile a single-file extension. Listing deliberately does not compile and startup stays lazy
/// (ADR-0005), so the build needs a trigger of its own — and this is the smallest honest one: it runs
/// the same <see cref="SingleFileExtensionBuilder"/> the phase-3 reload path will call, so there is
/// one order (trust, then the lock that answers for those bytes, then the cache, then the compiler)
/// and one place to change it.
/// <para>
/// It builds a file that is already trusted. Trust is the user's decision and this subcommand never
/// makes it: a stale or untrusted file reports <c>failed (trust)</c> with the way to approve it, and
/// nothing is restored or compiled.
/// </para>
/// Exit codes: 0 built (now or from the cache), 1 the build failed, 2 usage.
/// </summary>
internal static class ExtensionBuildCommand
{
    internal const int ExitOk = 0;
    internal const int ExitFailure = 1;
    internal const int ExitUsage = 2;

    internal const string Usage = "Usage: lunate extensions build <id|path> [--json]";

    /// <summary>The two cycles the CLI cannot make and does not pretend to.</summary>
    internal static string TrustHint(string id, string path) =>
        FormattableString.Invariant(
            $"single-file extension '{id}' at '{path}' is not trusted for these bytes; run an interactive session to approve it, then build it again."
        );

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
            return BuildAsync(
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

    private static async Task<int> BuildAsync(
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
        // A build writes the lock, the cache and the trust lookup under the store, so the refusal
        // comes before any of them.
        var workspace = new Workspace(working);
        LunateHome.StoreOutsideWorkspace(workspace.WorktreeRoot, workspace.RepoRoot);
        var options = new ExtensionHostOptions
        {
            StorePath = resolved.StorePath ?? ExtensionHostOptions.Default.StorePath,
            Log = resolved.Log ?? new StderrExtensionLog(errors),
            ReservedCommands = Commands.BuiltinNames,
            ReservedToolNames = ExtensionSession.CoreToolNames,
        };

        var loader = new ExtensionLoader(options);
        loader.Discover(working, working);

        SingleFileExtension? candidate = Resolve(loader.SingleFileExtensions, target);
        if (candidate is null)
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
        SingleFileBuildOutcome outcome = await builder
            .BuildAsync(candidate.Path, candidate.Scope, prompt: null, cancellationToken)
            .ConfigureAwait(false);

        return outcome.Succeeded
            ? Report(outcome, output, json)
            : ReportFailure(outcome, errors, json);
    }

    /// <summary>
    /// The target as the user named it: an id when one matches, else a path that exists. A path wins
    /// over an id only when it is the path of a discovered candidate, so a typo never builds a file
    /// discovery has not seen.
    /// </summary>
    internal static SingleFileExtension? Resolve(
        IReadOnlyList<SingleFileExtension> candidates,
        string target
    )
    {
        string full = Path.GetFullPath(target);
        SingleFileExtension? byPath = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, full, StringComparison.Ordinal)
        );
        return byPath
            ?? candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, target, StringComparison.Ordinal)
            );
    }

    private static int Report(SingleFileBuildOutcome outcome, TextWriter output, bool json)
    {
        SingleFileBuild build = outcome.Build!;
        if (json)
        {
            var node = new JsonObject
            {
                ["id"] = outcome.Extension.Id,
                ["version"] = outcome.Extension.Version,
                ["path"] = outcome.Extension.Path,
                ["state"] =
                    outcome.Build is null ? "failed"
                    : outcome.Status == SingleFileBuildStatus.CacheHit ? "up-to-date"
                    : "built",
                ["build"] = build.Key,
                ["assembly"] = build.AssemblyPath,
                ["tfm"] = build.Manifest.Tfm,
                ["rid"] = build.Manifest.Rid,
            };
            GeneratorsSkipped(outcome, node);
            output.WriteLine(node.ToJsonString());
        }
        else
        {
            output.WriteLine(
                FormattableString.Invariant(
                    $"{outcome.Extension.Id} {outcome.Extension.Version} {(outcome.Status == SingleFileBuildStatus.CacheHit ? "up-to-date" : "built")} {build.Key} — {build.AssemblyPath}"
                )
            );

            // The build succeeded, but a package shipped build-time code this file did not opt in
            // for: say so here too, so a headless build is not the one surface that hides it.
            if (outcome.Extension.GeneratorsSkipped is { Count: > 0 } skipped)
            {
                output.WriteLine(
                    FormattableString.Invariant(
                        $"generators skipped: {string.Join(", ", skipped)} — add '#:generators on' to the header to compile with them"
                    )
                );
            }
        }

        return ExitOk;
    }

    /// <summary>
    /// The JSON form of what <c>/extensions</c> says on the row: the locked packages whose build-time
    /// code this build left out because the header did not opt in. Present only when there are some
    /// — a file that opted in, or one with no lock, says nothing rather than "none".
    /// </summary>
    private static void GeneratorsSkipped(SingleFileBuildOutcome outcome, JsonObject node)
    {
        IReadOnlyList<string> packages = outcome.Extension.GeneratorsSkipped;
        if (packages.Count == 0)
        {
            return;
        }

        var skipped = new JsonArray();
        foreach (string package in packages)
        {
            skipped.Add(package);
        }

        node["generatorsSkipped"] = skipped;
    }

    internal static int ReportFailure(
        SingleFileBuildOutcome outcome,
        TextWriter errors,
        bool json,
        string? reason = null
    )
    {
        string step = outcome.Status switch
        {
            SingleFileBuildStatus.TrustRequired or SingleFileBuildStatus.Denied => "trust",
            SingleFileBuildStatus.RestoreFailed => "restore",
            SingleFileBuildStatus.CompileFailed => "compile",
            _ => "invalid",
        };
        reason ??= outcome.Reason ?? outcome.Extension.Reason ?? "no reason recorded";

        if (json)
        {
            var node = new JsonObject
            {
                ["id"] = outcome.Extension.Id,
                ["version"] = outcome.Extension.Version,
                ["path"] = outcome.Extension.Path,
                ["state"] = outcome.Status == SingleFileBuildStatus.Invalid ? "invalid" : "failed",
                ["failure"] = step,
                ["reason"] = reason,
            };
            if (outcome.Diagnostic is { } diagnostic)
            {
                node["diagnostic"] = new JsonObject
                {
                    ["file"] = diagnostic.File,
                    ["line"] = diagnostic.Line,
                    ["column"] = diagnostic.Column,
                    ["id"] = diagnostic.Id,
                    ["message"] = diagnostic.Message,
                };
            }

            GeneratorsSkipped(outcome, node);
            errors.WriteLine(node.ToJsonString());
        }
        else if (outcome.Status == SingleFileBuildStatus.Invalid)
        {
            errors.WriteLine(
                FormattableString.Invariant(
                    $"lunate: '{outcome.Extension.FileName}' is invalid ({reason})"
                )
            );
        }
        else
        {
            errors.WriteLine(
                FormattableString.Invariant(
                    $"lunate: {outcome.Extension.Id} failed ({step}) — {reason}"
                )
            );
            if (
                outcome.Status == SingleFileBuildStatus.TrustRequired
                && outcome.Extension.Id is { } id
            )
            {
                errors.WriteLine("lunate: " + TrustHint(id, outcome.Extension.Path));
            }
        }

        return ExitFailure;
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
