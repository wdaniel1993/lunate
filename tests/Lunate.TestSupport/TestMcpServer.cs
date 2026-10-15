using System.Diagnostics;
using System.Globalization;

namespace Lunate.TestSupport;

/// <summary>Locates the shared TestMcpServer and reads its marker file conventions.</summary>
public static class TestMcpServer
{
    /// <summary>The repository root, found by walking up to the solution file.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The build configuration the tests run under (their output directory's parent).</summary>
    public static string Configuration { get; } =
        new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))
            .Parent
            ?.Name
        ?? "Debug";

    /// <summary>The shared TestMcpServer DLL for the current configuration.</summary>
    public static string ServerDll { get; } =
        Path.Combine(
            RepositoryRoot,
            "tests",
            "tools",
            "TestMcpServer",
            "bin",
            Configuration,
            "net10.0",
            "TestMcpServer.dll"
        );

    /// <summary>The dotnet host to spawn TestMcpServer with; the test host's own when known.</summary>
    public static string DotnetPath { get; } =
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path
        && File.Exists(path)
            ? path
            : "dotnet";

    /// <summary>The marker file's lines; retries while the server process still holds it open.</summary>
    public static IReadOnlyList<string> MarkerLines(string markerPath)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.Exists(markerPath) ? File.ReadAllLines(markerPath) : [];
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(20);
            }
        }
    }

    /// <summary>Waits until the marker file has a line with the prefix; throws on timeout.</summary>
    public static async Task WaitForMarkerLineAsync(
        string markerPath,
        string prefix,
        TimeSpan timeout
    )
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (
                MarkerLines(markerPath)
                    .Any(line => line.StartsWith(prefix, StringComparison.Ordinal))
            )
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException(
            $"Marker line '{prefix}' did not appear within {timeout}. Contents: {string.Join(" | ", MarkerLines(markerPath))}"
        );
    }

    /// <summary>The pid the server recorded in its marker's <c>started pid=</c> line.</summary>
    public static int StartedPid(string markerPath)
    {
        const string Prefix = "started pid=";
        string line = MarkerLines(markerPath)
            .First(entry => entry.StartsWith(Prefix, StringComparison.Ordinal));
        return int.Parse(line[Prefix.Length..], CultureInfo.InvariantCulture);
    }

    /// <summary>Whether the process exists and has not exited.</summary>
    public static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Waits for the process to exit; throws on timeout.</summary>
    public static async Task WaitForProcessExitAsync(int pid, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsProcessAlive(pid))
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Process {pid} is still alive after {timeout}.");
    }

    /// <summary>Kills the server process and waits for it to exit.</summary>
    public static async Task KillAsync(int pid)
    {
        using var process = Process.GetProcessById(pid);
        process.Kill();
        await process.WaitForExitAsync();
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
