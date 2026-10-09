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
