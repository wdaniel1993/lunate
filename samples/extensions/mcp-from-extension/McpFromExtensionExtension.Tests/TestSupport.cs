using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Lunate.Extensibility.Testing;

namespace McpFromExtensionExtension.Tests;

internal static class TestSupport
{
    /// <summary>The extension directory the test build copies under the output root.</summary>
    public static string ExtensionDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "McpFromExtensionExtension");

    /// <summary>
    /// Creates a host over the built sample extension; the marker settings are written before the
    /// host loads the extension, so the fake server's environment carries them.
    /// </summary>
    public static ExtensionTestHost CreateHost(
        TempDirectory temp,
        string markerPath,
        ScriptedApprover? approver = null
    )
    {
        var host = new ExtensionTestHost(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "mcp-from-extension",
                ExtensionDirectory = ExtensionDirectory,
                Approver = approver,
            }
        );
        WriteSettings(host, markerPath);
        return host;
    }

    public static void WriteSettings(ExtensionTestHost host, string markerPath)
    {
        string directory = Path.Combine(host.StorePath, "extensions-settings");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "mcp-from-extension.json"),
            JsonSerializer.Serialize(new { markerPath })
        );
    }

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

    public static int StartedPid(string markerPath)
    {
        const string Prefix = "started pid=";
        string line = MarkerLines(markerPath)
            .First(entry => entry.StartsWith(Prefix, StringComparison.Ordinal));
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
}
