using System.Diagnostics;
using System.Globalization;

namespace Lunate.Coding.Tests;

/// <summary>Locates the shared TestMcpServer and reads its marker file conventions.</summary>
internal static class McpTestServerSupport
{
    internal static string DotnetPath { get; } =
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path
        && File.Exists(path)
            ? path
            : "dotnet";

    internal static string ServerDll { get; } =
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

    /// <summary>The mcp.json server entry for TestMcpServer, optionally writing a start marker.</summary>
    internal static string EntryJson(string? markerPath = null) =>
        markerPath is null
            ? $$"""{ "command": "{{Escape(DotnetPath)}}", "args": ["exec", "{{Escape(ServerDll)}}"] }"""
            : $$"""{ "command": "{{Escape(DotnetPath)}}", "args": ["exec", "{{Escape(ServerDll)}}"], "env": { "LUNATE_TEST_MCP_MARKER": "{{Escape(markerPath)}}" } }""";

    /// <summary>One complete temp mcp.json document configured with one TestMcpServer.</summary>
    internal static string ConfigJson(string name = "test", string? markerPath = null) =>
        $$"""{ "mcpServers": { "{{Escape(name)}}": {{EntryJson(markerPath)}} } }""";

    internal static int StartedPid(string markerPath)
    {
        const string Prefix = "started pid=";
        string line = MarkerLines(markerPath)
            .First(entry => entry.StartsWith(Prefix, StringComparison.Ordinal));
        return int.Parse(line[Prefix.Length..], CultureInfo.InvariantCulture);
    }

    internal static async Task WaitForProcessExitAsync(int pid, TimeSpan timeout)
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

    private static bool IsProcessAlive(int pid)
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

    private static IReadOnlyList<string> MarkerLines(string markerPath)
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

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string Configuration =>
        new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))
            .Parent
            ?.Name
        ?? "Debug";

    private static string RepositoryRoot => FindRepositoryRoot();

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
