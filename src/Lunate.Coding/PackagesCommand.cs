using Lunate.Extensibility;
using Lunate.Extensibility.Restore;

namespace Lunate.Coding;

/// <summary>
/// The <c>lunate packages [--json] [--verify]</c> subcommand (spec: "The <c>lunate packages</c>
/// view"): every installed package this working directory can see — the user scope and the project
/// installs whose worktree is this one — one line each, ordered by scope then package id, carrying
/// the canonical id, the version, the scope and what it provides.
/// <para>
/// <c>--verify</c> recomputes each recorded file's hash and appends the changed and missing counts;
/// without it nothing is hashed. It exits 3 when any record is <c>broken</c>, lists a file whose hash
/// changed, or names content that is gone. A store that cannot be read exits 1 with the reason on
/// stderr; a usage error exits 2.
/// </para>
/// </summary>
internal static class PackagesCommand
{
    internal const int ExitOk = 0;
    internal const int ExitFailure = 1;
    internal const int ExitUsage = 2;
    internal const int ExitVerificationFailed = 3;

    internal const string Usage = "Usage: lunate packages [--json] [--verify]";

    internal const string NothingText = "packages: none installed";

    internal static int Run(
        string[] args,
        string? workingDirectory,
        TextWriter output,
        TextWriter errors,
        ExtensionSeams? seams
    )
    {
        if (!TryParse(args, errors, out bool json, out bool verify))
        {
            return ExitUsage;
        }

        try
        {
            return List(workingDirectory, output, seams, json, verify);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return ExitFailure;
        }
    }

    private static int List(
        string? workingDirectory,
        TextWriter output,
        ExtensionSeams? seams,
        bool json,
        bool verify
    )
    {
        string working = workingDirectory ?? Environment.CurrentDirectory;
        var workspace = new Workspace(working);
        string storeRoot =
            (seams ?? new ExtensionSeams()).StorePath ?? ExtensionHostOptions.Default.StorePath;
        IReadOnlyList<InstalledPackageView> installed = Read(
            storeRoot,
            workspace.WorktreeRoot,
            verify
        );

        if (json)
        {
            output.Write(PackagesView.Json(installed) + "\n");
        }
        else if (installed.Count == 0)
        {
            output.Write(NothingText + "\n");
        }
        else
        {
            output.Write(PackagesView.Text(installed));
        }

        bool failed = installed.Any(package => package.Broken || package.State is { Clean: false });
        return verify && failed ? ExitVerificationFailed : ExitOk;
    }

    /// <summary>
    /// Every record under <c>&lt;root&gt;/packages/</c>, filtered to what this working directory can
    /// see and ordered by scope then id. A project record belongs to the worktree it names, so it is
    /// listed only there.
    /// </summary>
    private static IReadOnlyList<InstalledPackageView> Read(
        string storeRoot,
        string worktree,
        bool verify
    )
    {
        string packages = Path.Combine(storeRoot, "packages");
        List<InstalledPackageView> views = [];
        if (!Directory.Exists(packages))
        {
            return views;
        }

        foreach (
            string directory in Directory
                .EnumerateDirectories(packages)
                .Order(StringComparer.Ordinal)
        )
        {
            string recordPath = Path.Combine(directory, PackageInstallRecordJson.FileName);
            if (!File.Exists(recordPath))
            {
                // A directory without a record is a half-cleaned install, not an installed package:
                // no record names it, so nothing lists it.
                continue;
            }

            PackageInstallRecord? record = PackageInstallRecordJson.TryRead(
                recordPath,
                out string? problem
            );
            if (record is null)
            {
                views.Add(
                    new InstalledPackageView
                    {
                        Id = Path.GetFileName(directory),
                        RecordPath = recordPath,
                        Error = problem ?? "the record could not be read",
                    }
                );
                continue;
            }

            if (
                record.Scope == PackageScope.Project
                && !IsCurrentWorktree(record.Worktree, worktree)
            )
            {
                continue;
            }

            views.Add(
                new InstalledPackageView
                {
                    Id = record.Package.Id,
                    Version = record.Package.Version,
                    Scope = record.Scope,
                    ExtensionId = record.Content.ExtensionId,
                    ExtensionVersion = record.Content.ExtensionVersion,
                    Skills = record.Content.Skills,
                    RecordPath = recordPath,
                    InstalledAt = record.InstalledAt,
                    State = verify
                        ? PackageContentVerifier.Verify(record, ScopeRoot(record, storeRoot))
                        : null,
                }
            );
        }

        return
        [
            .. views
                .OrderBy(package => package.Scope == PackageScope.Project ? 1 : 0)
                .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.Version, StringComparer.Ordinal),
        ];
    }

    /// <summary>The directory a record's relative paths resolve against.</summary>
    private static string ScopeRoot(PackageInstallRecord record, string storeRoot) =>
        record.Scope == PackageScope.Project && record.Worktree is { Length: > 0 } worktree
            ? worktree
            : storeRoot;

    /// <summary>Paths compare the way the platform's file system does.</summary>
    private static bool IsCurrentWorktree(string? worktree, string current)
    {
        if (string.IsNullOrWhiteSpace(worktree))
        {
            return false;
        }

        StringComparison comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return string.Equals(
            Workspace.Canonicalize(worktree),
            Workspace.Canonicalize(current),
            comparison
        );
    }

    private static bool TryParse(string[] args, TextWriter errors, out bool json, out bool verify)
    {
        json = false;
        verify = false;
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--json" when !json:
                    json = true;
                    break;
                case "--verify" when !verify:
                    verify = true;
                    break;
                default:
                    errors.WriteLine(Usage);
                    return false;
            }
        }

        return true;
    }
}
