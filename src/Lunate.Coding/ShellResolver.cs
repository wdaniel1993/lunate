namespace Lunate.Coding;

/// <summary>The shell dialect the resolved executable speaks.</summary>
public enum ShellKind
{
    /// <summary>bash on PATH or /bin/bash.</summary>
    Bash,

    /// <summary>/bin/sh.</summary>
    Sh,

    /// <summary>Git Bash on Windows.</summary>
    GitBash,

    /// <summary>PowerShell 7+ (`pwsh`).</summary>
    Pwsh7,

    /// <summary>Windows PowerShell 5.1 (`powershell.exe`).</summary>
    WindowsPowerShell,

    /// <summary>The Windows command processor.</summary>
    Cmd,
}

/// <summary>The executable, model-facing name and fixed argument prefix of the resolved shell.</summary>
public sealed record ResolvedShell(
    ShellKind Kind,
    string ExecutablePath,
    string DisplayName,
    IReadOnlyList<string> ArgumentsPrefix
);

/// <summary>
/// Platform probes behind <see cref="ShellResolver"/>, injectable so every Windows branch is
/// unit-testable on any OS. The constructor is internal: only tests substitute it.
/// </summary>
internal sealed record ShellProbe(
    bool IsWindows,
    Func<string, bool> FileExists,
    Func<string, string?> FindOnPath,
    Func<string, string?> GetEnvironmentVariable
)
{
    public static ShellProbe Current { get; } =
        new(
            OperatingSystem.IsWindows(),
            File.Exists,
            SearchPath,
            Environment.GetEnvironmentVariable
        );

    private static string? SearchPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var extensions = OperatingSystem.IsWindows() ? new[] { "", ".exe", ".cmd", ".bat" } : [""];
        foreach (
            var directory in path.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, name + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Resolves the shell the <c>bash</c> tool runs commands in, exactly per the guide's platform
/// table: Unix bash then /bin/sh; Windows Git Bash (install paths, then derived from <c>git</c> on
/// PATH), then pwsh 7, then Windows PowerShell, then cmd. <c>C:\Windows\System32\bash.exe</c> (WSL)
/// is never selected. An explicit override path wins; the result is cached.
/// </summary>
public sealed class ShellResolver
{
    private static readonly IReadOnlyList<string> BashArguments = ["-c"];

    private static readonly IReadOnlyList<string> PowerShellArguments =
    [
        "-NoProfile",
        "-NonInteractive",
        "-Command",
    ];

    private static readonly IReadOnlyList<string> CmdArguments = ["/c"];

    private readonly string? _overridePath;
    private readonly ShellProbe _probe;
    private bool _resolved;
    private ResolvedShell? _shell;

    /// <summary>Creates a resolver; <paramref name="overridePath"/> is the settings/public seam.</summary>
    public ShellResolver(string? overridePath = null)
        : this(overridePath, ShellProbe.Current) { }

    internal ShellResolver(string? overridePath, ShellProbe probe)
    {
        _overridePath = overridePath;
        _probe = probe;
    }

    /// <summary>The resolved shell, or null when no shell exists on this machine; cached.</summary>
    public ResolvedShell? Resolve()
    {
        if (!_resolved)
        {
            _shell = ResolveOnce();
            _resolved = true;
        }

        return _shell;
    }

    private ResolvedShell? ResolveOnce()
    {
        if (!string.IsNullOrWhiteSpace(_overridePath))
        {
            return FromOverride(_overridePath.Trim());
        }

        return _probe.IsWindows ? ResolveWindows() : ResolveUnix();
    }

    private ResolvedShell? ResolveUnix()
    {
        var bash =
            _probe.FindOnPath("bash") ?? (_probe.FileExists("/bin/bash") ? "/bin/bash" : null);
        if (bash is not null)
        {
            return new ResolvedShell(ShellKind.Bash, bash, "bash", BashArguments);
        }

        if (_probe.FileExists("/bin/sh"))
        {
            return new ResolvedShell(ShellKind.Sh, "/bin/sh", "/bin/sh", BashArguments);
        }

        return null;
    }

    private ResolvedShell? ResolveWindows()
    {
        var gitBash = GitBashInstallPath() ?? GitBashFromPath();
        if (gitBash is not null)
        {
            return new ResolvedShell(ShellKind.GitBash, gitBash, "Git Bash", BashArguments);
        }

        var pwsh = _probe.FindOnPath("pwsh") ?? PwshInstallPath();
        if (pwsh is not null)
        {
            return new ResolvedShell(ShellKind.Pwsh7, pwsh, "pwsh 7", PowerShellArguments);
        }

        var powershell = _probe.FindOnPath("powershell.exe");
        if (powershell is not null)
        {
            return new ResolvedShell(
                ShellKind.WindowsPowerShell,
                powershell,
                "Windows PowerShell",
                PowerShellArguments
            );
        }

        var cmd = _probe.FindOnPath("cmd.exe");
        return cmd is null ? null : new ResolvedShell(ShellKind.Cmd, cmd, "cmd", CmdArguments);
    }

    private string? GitBashInstallPath()
    {
        foreach (var variable in new[] { "ProgramFiles", "ProgramFiles(x86)" })
        {
            var root = _probe.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var candidate = JoinWindows(root, @"Git\bin\bash.exe");
            if (_probe.FileExists(candidate))
            {
                return candidate;
            }
        }

        var localAppData = _probe.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var candidate = JoinWindows(localAppData, @"Programs\Git\bin\bash.exe");
            if (_probe.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string? GitBashFromPath()
    {
        var git = _probe.FindOnPath("git");
        if (git is null)
        {
            return null;
        }

        // git.exe lives in <root>\cmd or <root>\bin; either way bash sits in <root>\bin.
        var directory = WindowsDirectoryName(git);
        if (directory is null)
        {
            return null;
        }

        var parent = WindowsDirectoryName(directory);
        foreach (var candidate in new[] { directory, parent })
        {
            if (candidate is null)
            {
                continue;
            }

            var bash = JoinWindows(candidate, @"bin\bash.exe");
            if (_probe.FileExists(bash))
            {
                return bash;
            }
        }

        return null;
    }

    private string? PwshInstallPath()
    {
        var programFiles = _probe.GetEnvironmentVariable("ProgramFiles");
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            return null;
        }

        var candidate = JoinWindows(programFiles, @"PowerShell\7\pwsh.exe");
        return _probe.FileExists(candidate) ? candidate : null;
    }

    private ResolvedShell FromOverride(string path)
    {
        var name = FileName(path);
        if (
            name.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)
        )
        {
            return new ResolvedShell(ShellKind.Pwsh7, path, "pwsh 7", PowerShellArguments);
        }

        if (
            name.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)
        )
        {
            return new ResolvedShell(
                ShellKind.WindowsPowerShell,
                path,
                "Windows PowerShell",
                PowerShellArguments
            );
        }

        if (
            name.Equals("cmd", StringComparison.OrdinalIgnoreCase)
            || name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
        )
        {
            return new ResolvedShell(ShellKind.Cmd, path, "cmd", CmdArguments);
        }

        if (
            name.Equals("sh", StringComparison.OrdinalIgnoreCase)
            || name.Equals("sh.exe", StringComparison.OrdinalIgnoreCase)
        )
        {
            return new ResolvedShell(ShellKind.Sh, path, "/bin/sh", BashArguments);
        }

        return _probe.IsWindows
            ? new ResolvedShell(ShellKind.GitBash, path, "Git Bash", BashArguments)
            : new ResolvedShell(ShellKind.Bash, path, "bash", BashArguments);
    }

    private static string JoinWindows(string directory, string child) =>
        directory.TrimEnd('\\', '/') + "\\" + child;

    private static string? WindowsDirectoryName(string path)
    {
        var index = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return index > 0 ? path[..index] : null;
    }

    private static string FileName(string path)
    {
        var index = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return index >= 0 ? path[(index + 1)..] : path;
    }
}
