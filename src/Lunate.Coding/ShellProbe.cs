namespace Lunate.Coding;

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
