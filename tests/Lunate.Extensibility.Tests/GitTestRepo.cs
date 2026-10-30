using System.Diagnostics;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// Real git for tests that need a repository or a linked worktree (git is executed; nothing is
/// stubbed beyond the temp tree).
/// </summary>
internal static class GitTestRepo
{
    /// <summary>Initializes a real repository at <paramref name="root"/> with one commit, so
    /// <c>git worktree add</c> has a stable HEAD.</summary>
    internal static void Init(string root)
    {
        Git(root, "init", "-q", ".");
        File.WriteAllText(Path.Combine(root, "README.md"), "readme");
        Git(root, "add", "-A");
        Git(
            root,
            "-c",
            "user.email=tests@lunate.local",
            "-c",
            "user.name=Lunate Tests",
            "commit",
            "-qm",
            "init"
        );
    }

    /// <summary>Runs one git command in <paramref name="root"/>; a non-zero exit fails the test.</summary>
    internal static void Git(string root, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {stderr}"
        );
    }
}
