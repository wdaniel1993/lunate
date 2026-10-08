namespace Lunate.Coding.Tests;

internal sealed class FakeShellProbe
{
    public bool IsWindows { get; set; }

    public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> PathLookups { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string?> EnvironmentVariables { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> FileExistsCalls { get; } = [];

    public List<string> FindOnPathCalls { get; } = [];

    public ShellProbe Build() => new(IsWindows, FileExists, FindOnPath, GetEnvironmentVariable);

    private bool FileExists(string path)
    {
        FileExistsCalls.Add(path);
        return ExistingFiles.Contains(path);
    }

    private string? FindOnPath(string name)
    {
        FindOnPathCalls.Add(name);
        return PathLookups.GetValueOrDefault(name);
    }

    private string? GetEnvironmentVariable(string name) =>
        EnvironmentVariables.GetValueOrDefault(name);
}

public sealed class ShellResolverTests
{
    [Fact]
    public void Unix_prefers_bash_on_the_path()
    {
        var probe = new FakeShellProbe { IsWindows = false };
        probe.PathLookups["bash"] = "/usr/local/bin/bash";

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.NotNull(shell);
        Assert.Equal(ShellKind.Bash, shell.Kind);
        Assert.Equal("/usr/local/bin/bash", shell.ExecutablePath);
        Assert.Equal("bash", shell.DisplayName);
        Assert.Equal(["-c"], shell.ArgumentsPrefix);
    }

    [Fact]
    public void Unix_falls_back_to_bin_bash()
    {
        var probe = new FakeShellProbe { IsWindows = false };
        probe.ExistingFiles.Add("/bin/bash");

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.NotNull(shell);
        Assert.Equal(ShellKind.Bash, shell.Kind);
        Assert.Equal("/bin/bash", shell.ExecutablePath);
        Assert.Equal("bash", shell.DisplayName);
        Assert.Equal(["-c"], shell.ArgumentsPrefix);
    }

    [Fact]
    public void Unix_falls_back_to_bin_sh()
    {
        var probe = new FakeShellProbe { IsWindows = false };
        probe.ExistingFiles.Add("/bin/sh");

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.NotNull(shell);
        Assert.Equal(ShellKind.Sh, shell.Kind);
        Assert.Equal("/bin/sh", shell.ExecutablePath);
        Assert.Equal("/bin/sh", shell.DisplayName);
        Assert.Equal(["-c"], shell.ArgumentsPrefix);
    }

    [Fact]
    public void Unix_without_any_shell_resolves_to_null()
    {
        var probe = new FakeShellProbe { IsWindows = false };

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.Null(shell);
    }

    [Theory]
    [InlineData("ProgramFiles", @"C:\Program Files", @"C:\Program Files\Git\bin\bash.exe")]
    [InlineData(
        "ProgramFiles(x86)",
        @"C:\Program Files (x86)",
        @"C:\Program Files (x86)\Git\bin\bash.exe"
    )]
    [InlineData(
        "LOCALAPPDATA",
        @"C:\Users\dev\AppData\Local",
        @"C:\Users\dev\AppData\Local\Programs\Git\bin\bash.exe"
    )]
    public void Windows_prefers_git_bash_install_paths(string variable, string root, string bash)
    {
        var probe = new FakeShellProbe { IsWindows = true };
        probe.EnvironmentVariables[variable] = root;
        probe.ExistingFiles.Add(bash);

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.NotNull(shell);
        Assert.Equal(ShellKind.GitBash, shell.Kind);
        Assert.Equal(bash, shell.ExecutablePath);
        Assert.Equal("Git Bash", shell.DisplayName);
        Assert.Equal(["-c"], shell.ArgumentsPrefix);
    }

    [Fact]
    public void Windows_install_path_wins_over_git_on_the_path()
    {
        var probe = new FakeShellProbe { IsWindows = true };
        probe.EnvironmentVariables["ProgramFiles"] = @"C:\Program Files";
        probe.ExistingFiles.Add(@"C:\Program Files\Git\bin\bash.exe");
        probe.PathLookups["git"] = @"C:\PortableGit\cmd\git.exe";
        probe.ExistingFiles.Add(@"C:\PortableGit\bin\bash.exe");

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.NotNull(shell);
        Assert.Equal(@"C:\Program Files\Git\bin\bash.exe", shell.ExecutablePath);
    }

    [Fact]
    public void Windows_derives_git_bash_from_git_on_the_path()
    {
        var probe = new FakeShellProbe { IsWindows = true };
        probe.PathLookups["git"] = @"C:\Program Files\Git\cmd\git.exe";
        probe.ExistingFiles.Add(@"C:\Program Files\Git\bin\bash.exe");

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.NotNull(shell);
        Assert.Equal(ShellKind.GitBash, shell.Kind);
        Assert.Equal(@"C:\Program Files\Git\bin\bash.exe", shell.ExecutablePath);
        Assert.Equal("Git Bash", shell.DisplayName);
    }

    [Fact]
    public void Windows_never_picks_the_system32_wsl_bash()
    {
        var probe = new FakeShellProbe { IsWindows = true };
        probe.ExistingFiles.Add(@"C:\Windows\System32\bash.exe");

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.Null(shell);
        Assert.DoesNotContain(
            probe.FileExistsCalls,
            path => path.Contains(@"System32\bash.exe", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void Windows_falls_through_to_pwsh7_on_the_path()
    {
        var probe = new FakeShellProbe { IsWindows = true };
        probe.PathLookups["pwsh"] = @"C:\Program Files\PowerShell\7\pwsh.exe";

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.NotNull(shell);
        Assert.Equal(ShellKind.Pwsh7, shell.Kind);
        Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe", shell.ExecutablePath);
        Assert.Equal("pwsh 7", shell.DisplayName);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-Command"], shell.ArgumentsPrefix);
    }

    [Fact]
    public void Windows_falls_through_to_the_pwsh7_install_path()
    {
        var probe = new FakeShellProbe { IsWindows = true };
        probe.EnvironmentVariables["ProgramFiles"] = @"C:\Program Files";
        probe.ExistingFiles.Add(@"C:\Program Files\PowerShell\7\pwsh.exe");

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.NotNull(shell);
        Assert.Equal(ShellKind.Pwsh7, shell.Kind);
        Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe", shell.ExecutablePath);
        Assert.Equal("pwsh 7", shell.DisplayName);
    }

    [Fact]
    public void Windows_falls_through_to_windows_powershell()
    {
        var probe = new FakeShellProbe { IsWindows = true };
        probe.PathLookups["powershell.exe"] =
            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.NotNull(shell);
        Assert.Equal(ShellKind.WindowsPowerShell, shell.Kind);
        Assert.Equal("Windows PowerShell", shell.DisplayName);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-Command"], shell.ArgumentsPrefix);
    }

    [Fact]
    public void Windows_falls_through_to_cmd()
    {
        var probe = new FakeShellProbe { IsWindows = true };
        probe.PathLookups["cmd.exe"] = @"C:\Windows\System32\cmd.exe";

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.NotNull(shell);
        Assert.Equal(ShellKind.Cmd, shell.Kind);
        Assert.Equal("cmd", shell.DisplayName);
        Assert.Equal(["/c"], shell.ArgumentsPrefix);
    }

    [Fact]
    public void Windows_without_any_shell_resolves_to_null()
    {
        var probe = new FakeShellProbe { IsWindows = true };

        var shell = new ShellResolver(null, probe.Build()).Resolve();

        Assert.Null(shell);
    }

    [Theory]
    [InlineData(true, @"C:\tools\bash.exe", ShellKind.GitBash, "Git Bash")]
    [InlineData(false, "/opt/shells/bash", ShellKind.Bash, "bash")]
    [InlineData(false, "/bin/sh", ShellKind.Sh, "/bin/sh")]
    [InlineData(true, @"C:\Program Files\PowerShell\7\pwsh.exe", ShellKind.Pwsh7, "pwsh 7")]
    [InlineData(
        true,
        @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
        ShellKind.WindowsPowerShell,
        "Windows PowerShell"
    )]
    [InlineData(true, @"C:\Windows\System32\cmd.exe", ShellKind.Cmd, "cmd")]
    public void An_override_wins_even_when_no_probe_finds_anything(
        bool isWindows,
        string overridePath,
        ShellKind kind,
        string displayName
    )
    {
        var probe = new FakeShellProbe { IsWindows = isWindows };

        var shell = new ShellResolver(overridePath, probe.Build()).Resolve();

        Assert.NotNull(shell);
        Assert.Equal(kind, shell.Kind);
        Assert.Equal(overridePath, shell.ExecutablePath);
        Assert.Equal(displayName, shell.DisplayName);
    }

    [Fact]
    public void A_blank_override_is_ignored()
    {
        var probe = new FakeShellProbe { IsWindows = false };
        probe.PathLookups["bash"] = "/usr/bin/bash";

        var shell = new ShellResolver("  ", probe.Build()).Resolve();

        Assert.NotNull(shell);
        Assert.Equal("/usr/bin/bash", shell.ExecutablePath);
    }

    [Fact]
    public void Resolution_is_cached()
    {
        var probe = new FakeShellProbe { IsWindows = false };
        probe.PathLookups["bash"] = "/usr/bin/bash";
        var resolver = new ShellResolver(null, probe.Build());

        var first = resolver.Resolve();
        var second = resolver.Resolve();

        Assert.Same(first, second);
        Assert.Single(probe.FindOnPathCalls);
    }

    [Fact]
    public void A_missing_shell_is_cached_as_missing()
    {
        var probe = new FakeShellProbe { IsWindows = false };
        var resolver = new ShellResolver(null, probe.Build());

        var first = resolver.Resolve();
        var callsAfterFirst = probe.FileExistsCalls.Count;
        var second = resolver.Resolve();

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(callsAfterFirst, probe.FileExistsCalls.Count);
    }

    [Fact]
    public void The_real_probe_finds_a_shell_on_this_machine()
    {
        var shell = new ShellResolver().Resolve();

        Assert.NotNull(shell);
        Assert.True(File.Exists(shell.ExecutablePath));
    }
}
