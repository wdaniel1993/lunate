using System.Diagnostics;
using System.Globalization;
using Lunate.Agent;

namespace Lunate.Protocols.Tests;

internal sealed class NullAgentEvents : IAgentEvents
{
    public void Emit(AgentEvent agentEvent) { }
}

internal static class McpTestEnvironment
{
    public static ToolContext Context { get; } = new("unused", new NullAgentEvents());

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

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

    public static string DotnetPath { get; } =
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path
        && File.Exists(path)
            ? path
            : "dotnet";

    public static McpServerOptions Options(
        string markerPath,
        string name = "server",
        TimeSpan? callTimeout = null
    ) =>
        new(
            name,
            DotnetPath,
            ["exec", ServerDll],
            new Dictionary<string, string> { ["LUNATE_TEST_MCP_MARKER"] = markerPath },
            WorkingDirectory: null,
            callTimeout
        );

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

    public static int StartedPid(string markerPath)
    {
        const string Prefix = "started pid=";
        var line = MarkerLines(markerPath)
            .First(l => l.StartsWith(Prefix, StringComparison.Ordinal));
        return int.Parse(line[Prefix.Length..], CultureInfo.InvariantCulture);
    }

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

    private static string Configuration
    {
        get
        {
            var frameworkDirectory = new DirectoryInfo(
                AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
            );
            return frameworkDirectory.Parent?.Name ?? "Debug";
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
