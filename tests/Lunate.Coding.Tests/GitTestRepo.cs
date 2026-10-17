using System.Diagnostics;

namespace Lunate.Coding.Tests;

/// <summary>A real git repository in a temp directory for the bash change-tracker tests.</summary>
internal sealed class GitTestRepo : IDisposable
{
    private readonly TempDirectory _temp = new();

    internal GitTestRepo()
    {
        Root = _temp.Root;
        Init(Root);
    }

    internal string Root { get; }

    /// <summary>Initializes an existing directory as a repository (used by fixture-based tests).</summary>
    internal static void Init(string root)
    {
        Git(root, "init", "-q", ".");
        Git(root, "config", "user.email", "tests@lunate.local");
        Git(root, "config", "user.name", "Lunate Tests");
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

    /// <summary>Writes a file under the repo root (creating directories), returns its absolute path.</summary>
    internal string Write(string relativePath, string content)
    {
        string absolute = Path.Combine(
            Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)
        );
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, content);
        return absolute;
    }

    /// <summary>Runs one git command in the repo root; a non-zero exit fails the test.</summary>
    internal void Run(params string[] arguments) => Git(Root, arguments);

    internal void CommitAll(string message = "commit")
    {
        Run("add", "-A");
        Run("commit", "-qm", message);
    }

    public void Dispose() => _temp.Dispose();
}
