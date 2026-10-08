using System.Diagnostics;

namespace Lunate.Roslyn.Tests;

/// <summary>A temp copy of a fixture solution; deleting it cleans up everything including restore output.</summary>
internal sealed class FixtureSolution(string root) : IDisposable
{
    public string Root { get; } = root;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal static class Fixtures
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static FixtureSolution CopySolution(string name)
    {
        var source = Path.Combine(RepositoryRoot, "tests", "fixtures", "solutions", name);
        var root = Path.Combine(
            Path.GetTempPath(),
            "lunate-roslyn-tests",
            Guid.NewGuid().ToString("N")
        );
        CopyDirectory(source, root);
        // Canonicalize the temp root: on macOS Path.GetTempPath() returns the /var symlink alias,
        // and a restore graph that mixes /var and /private/var spellings restores the same project
        // twice, racing on its obj files.
        return new FixtureSolution(PathIdentity.Canonicalize(root));
    }

    /// <summary>Restores the copied solution offline: package-free fixtures and a cleared source list.</summary>
    public static void Restore(FixtureSolution fixture, string solutionFileName)
    {
        var configPath = Path.Combine(fixture.Root, "NuGet.config");
        File.WriteAllText(
            configPath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
              </packageSources>
            </configuration>
            """
        );

        var solutionPath = Path.Combine(fixture.Root, solutionFileName);
        var (exitCode, output) = RunDotnet(
            fixture.Root,
            ["restore", solutionPath, "--configfile", configPath, "-p:NuGetAudit=false", "--nologo"]
        );

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet restore failed with exit code {exitCode}:\n{output}"
            );
        }
    }

    private static (int ExitCode, string Output) RunDotnet(
        string workingDirectory,
        IReadOnlyList<string> arguments
    )
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnet) || !File.Exists(dotnet))
        {
            dotnet = "dotnet";
        }

        var startInfo = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["NUGET_PACKAGES"] = Path.Combine(workingDirectory, ".packages");
        startInfo.Environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(
            workingDirectory,
            ".nuget-http-cache"
        );
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} timed out");
        }

        return (process.ExitCode, output);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name is "bin" or "obj")
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }

    private static string FindRepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find lunate.sln above {AppContext.BaseDirectory}."
        );
    }
}
