using System.ComponentModel;
using System.Diagnostics;

namespace Lunate.Coding;

/// <summary>One mechanism probe: an actual run, its exit code and combined output.</summary>
internal delegate (int ExitCode, string Output) GuardRun(string fileName, string[] arguments);

/// <summary>
/// The platform facts behind <see cref="BashGuard"/>, injectable so every branch is unit-testable
/// on any OS (the same shape as <see cref="ShellProbe"/>); the constructor is internal, tests
/// substitute the record.
/// </summary>
internal sealed record GuardProbe(
    bool IsWindows,
    bool IsMacOs,
    bool IsLinux,
    Func<string, string?> FindOnPath,
    Func<string, bool> FileExists,
    Func<string, bool> DirectoryExists,
    Func<string, string?> GetEnvironmentVariable,
    Func<string, string?> ReadTextFile,
    Func<string, IReadOnlyList<string>> ListDirectories,
    GuardRun Run
)
{
    internal static GuardProbe Current { get; } =
        new(
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            OperatingSystem.IsLinux(),
            SearchPath,
            File.Exists,
            Directory.Exists,
            Environment.GetEnvironmentVariable,
            ReadTextFileAtPath,
            ListDirectoriesAt,
            RunMechanism
        );

    private static string? ReadTextFileAtPath(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ListDirectoriesAt(string path)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(path).Order(StringComparer.Ordinal)];
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or DirectoryNotFoundException)
        {
            return [];
        }
    }

    private static string? SearchPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (
            var directory in path.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs one mechanism once: it is the probe that separates "looks installed" from "works".
    /// Output is trimmed and newlines collapsed, so a notice can quote it on one line.
    /// </summary>
    private static (int ExitCode, string Output) RunMechanism(
        string fileName,
        string[] arguments
    )
    {
        try
        {
            using var process = Process.Start(
                new ProcessStartInfo(fileName)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            );
            if (process is null)
            {
                return (-1, "the probe process did not start");
            }

            string output = process.StandardOutput.ReadToEnd();
            output += process.StandardError.ReadToEnd();
            if (process.WaitForExit(TimeSpan.FromSeconds(5)))
            {
                return (process.ExitCode, Collapse(output));
            }

            process.Kill();
            return (-1, "the mechanism probe timed out after 5s");
        }
        catch (Exception exception)
            when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            return (-1, Collapse(exception.Message));
        }
    }

    private static string Collapse(string text) =>
        string.Concat(
            text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(1)
        );
}
