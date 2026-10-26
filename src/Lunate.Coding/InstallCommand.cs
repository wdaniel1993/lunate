using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Extensibility.Restore;

namespace Lunate.Coding;

/// <summary>
/// The <c>lunate install &lt;package-id&gt;[@&lt;version&gt;] [--project]</c> command (spec: "The
/// installable unit", "Where installed content lands", "Install output and <c>--dry-run</c>").
/// User scope is the default and follows the state root; <c>--project</c> writes the content into
/// the current worktree while the record stays machine state. It prints the identity it resolved
/// and what it wrote, writes nothing to stdout on a failure, exits 2 on a usage error and 3 never
/// — verification belongs to <c>lunate packages --verify</c>.
/// <para>
/// The command is the only way a package reaches a store: no tool, slash command or protocol path
/// wraps it, so a model cannot install code (design D7).
/// </para>
/// </summary>
internal static class InstallCommand
{
    internal const int ExitOk = 0;
    internal const int ExitFailure = 1;
    internal const int ExitUsage = 2;

    internal const string Usage =
        "Usage: lunate install <package-id>[@<version>] [--project] [--dry-run] [--json] [--offline] [--force] [--yes]";

    internal static int Run(
        string[] args,
        string? workingDirectory,
        TextWriter output,
        TextWriter errors,
        ExtensionSeams? seams,
        CancellationToken cancellationToken = default,
        Func<string, string?>? environment = null
    )
    {
        if (!TryParse(args, errors, out Request request))
        {
            return ExitUsage;
        }

        if (RefuseInsideSession(request, errors, environment))
        {
            return ExitUsage;
        }

        try
        {
            return ExecuteAsync(request, workingDirectory, output, errors, seams, cancellationToken)
                .GetAwaiter()
                .GetResult();
        }
        catch (ArgumentException exception)
        {
            // A version that is not exact is a usage error, not a failure of the machine.
            errors.WriteLine(Usage);
            errors.WriteLine($"lunate: {exception.Message}");
            return ExitUsage;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return ExitFailure;
        }
    }

    private static async Task<int> ExecuteAsync(
        Request request,
        string? workingDirectory,
        TextWriter output,
        TextWriter errors,
        ExtensionSeams? seams,
        CancellationToken cancellationToken
    )
    {
        string working = workingDirectory ?? Environment.CurrentDirectory;
        var workspace = new Workspace(working);
        if (request.Project && workspace.RepoRoot is null)
        {
            errors.WriteLine(Usage);
            errors.WriteLine(
                $"lunate: --project needs a worktree, and '{workspace.WorktreeRoot}' is not inside a repository — run the install from the repository, or without --project to install for the user."
            );
            return ExitUsage;
        }

        // Every command that writes the store runs the guard: a LUNATE_HOME inside the repository
        // it governs could have its records written by that repository.
        string storeRoot =
            seams?.StorePath
            ?? LunateHome.StoreOutsideWorkspace(workspace.WorktreeRoot, workspace.RepoRoot);
        string? worktree = request.Project ? workspace.WorktreeRoot : null;
        PackageInstallTarget target = request.Project
            ? PackageInstallTarget.ForProject(workspace.WorktreeRoot, storeRoot, request.Id)
            : PackageInstallTarget.ForUser(storeRoot, request.Id);
        var installer = new PackageInstaller(
            new PackageInstallOptions { SettingsRoot = working, Offline = request.Offline }
        );
        var install = new PackageInstallRequest
        {
            Id = request.Id,
            Version = request.Version,
            Target = target,
            Worktree = workspace.WorktreeRoot,
            Force = request.Force,
        };

        if (request.DryRun)
        {
            PackageDescription description = await installer
                .DescribeAsync(install, cancellationToken)
                .ConfigureAwait(false);
            output.Write(
                request.Json
                    ? InstallView.Json(description, ScopeText(target), worktree) + "\n"
                    : InstallView.DryRun(description, ScopeText(target), worktree)
            );
            return ExitOk;
        }

        PackageInstallResult result = await installer
            .InstallAsync(install, cancellationToken)
            .ConfigureAwait(false);
        foreach (string warning in result.Warnings)
        {
            errors.WriteLine($"lunate: {warning}");
        }

        output.Write(
            request.Json
                ? InstallView.Json(result, ScopeText(target), worktree) + "\n"
                : InstallView.Text(result, ScopeText(target), target.ScopeRoot, worktree)
        );
        return ExitOk;
    }

    private static string ScopeText(PackageInstallTarget target) =>
        target.Scope == PackageScope.Project ? "project" : "user";

    /// <summary>
    /// The session gate: a command the bash tool runs carries <c>LUNATE_SESSION</c>, and an install
    /// started from there would put code on the machine that the user never saw in the command they
    /// approved. <c>--yes</c> is the explicit answer — the flag has to appear in the command line
    /// the approval panel showed. Outside a session nothing changes.
    /// </summary>
    private static bool RefuseInsideSession(
        Request request,
        TextWriter errors,
        Func<string, string?>? environment
    )
    {
        if (request.Yes)
        {
            return false;
        }

        string? marker = (environment ?? Environment.GetEnvironmentVariable)(
            LunateHome.SessionVariable
        );
        if (!string.Equals(marker, LunateHome.SessionMarker, StringComparison.Ordinal))
        {
            return false;
        }

        errors.WriteLine(
            "lunate: this install runs inside a Lunate session, so it needs --yes — an install "
                + "started from a command puts code on the machine that runs at the next load, and "
                + "that has to be visible in the command you approved. Run 'lunate install "
                + $"{request.Id}{VersionSuffix(request.Version)} --yes', or run the install outside "
                + "the session."
        );
        return true;
    }

    private static string VersionSuffix(string version) =>
        version.Length == 0 ? string.Empty : FormattableString.Invariant($"@{version}");

    /// <summary>
    /// Splits <c>&lt;id&gt;[@&lt;version&gt;]</c> and refuses every other input shape before a feed
    /// is asked (design D1, D11): a URL is not supported in this version and a path is not a
    /// distribution unit.
    /// </summary>
    private static (string Id, string Version) Split(string spec)
    {
        if (spec.Contains("://", StringComparison.Ordinal) || IsScpUrl(spec))
        {
            throw new ArgumentException(
                $"'{spec}' is a URL; install takes '<package-id>[@<version>]' from a package feed — "
                    + "git installs are not supported in this version."
            );
        }

        if (
            spec.Contains('/', StringComparison.Ordinal)
            || spec.Contains('\\', StringComparison.Ordinal)
            || spec.StartsWith('.')
            || spec.StartsWith('~')
            || spec.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new ArgumentException(
                $"'{spec}' is a path; install takes '<package-id>[@<version>]' from a package feed "
                    + "— a directory is not a distribution unit, so copy it into the extensions "
                    + "folder instead."
            );
        }

        int at = spec.LastIndexOf('@');
        string id = at < 0 ? spec : spec[..at];
        string version = at < 0 ? string.Empty : spec[(at + 1)..];
        if (id.Length == 0)
        {
            throw new ArgumentException(
                $"'{spec}' names no package; install takes '<package-id>[@<version>]'."
            );
        }

        if (at >= 0 && version.Length == 0)
        {
            throw new ArgumentException(
                $"'{spec}' names no version after '@'; write '<package-id>@<version>' or leave the "
                    + "version off to install the latest stable one."
            );
        }

        return (id, version);
    }

    /// <summary>Whether the argument is scp-style <c>user@host:path</c> rather than a package.</summary>
    private static bool IsScpUrl(string spec)
    {
        int at = spec.LastIndexOf('@');
        if (at <= 0 || at == spec.Length - 1)
        {
            return false;
        }

        string rest = spec[(at + 1)..];
        return rest.Contains(':', StringComparison.Ordinal)
            || rest.Contains('/', StringComparison.Ordinal);
    }

    private static bool TryParse(string[] args, TextWriter errors, out Request request)
    {
        request = new Request(string.Empty, string.Empty, false, false, false, false, false, false);
        string? spec = null;
        var project = false;
        var dryRun = false;
        var json = false;
        var offline = false;
        var force = false;
        var yes = false;
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--project" when !project:
                    project = true;
                    break;
                case "--dry-run" when !dryRun:
                    dryRun = true;
                    break;
                case "--json" when !json:
                    json = true;
                    break;
                case "--offline" when !offline:
                    offline = true;
                    break;
                case "--force" when !force:
                    force = true;
                    break;
                case "--yes" when !yes:
                    yes = true;
                    break;
                default:
                    if (spec is not null || arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        errors.WriteLine(Usage);
                        return false;
                    }

                    spec = arg;
                    break;
            }
        }

        if (spec is null)
        {
            errors.WriteLine(Usage);
            return false;
        }

        try
        {
            (string id, string version) = Split(spec);
            request = new Request(id, version, project, dryRun, json, offline, force, yes);
            return true;
        }
        catch (ArgumentException exception)
        {
            errors.WriteLine(Usage);
            errors.WriteLine($"lunate: {exception.Message}");
            return false;
        }
    }

    private sealed record Request(
        string Id,
        string Version,
        bool Project,
        bool DryRun,
        bool Json,
        bool Offline,
        bool Force,
        bool Yes
    );
}
