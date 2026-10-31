using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Coding;
using Lunate.Extensibility.Testing;

namespace LspDiagnosticsExtension.Tests;

internal static class TestSupport
{
    public static string FakeServerDll { get; } =
        Path.Combine(AppContext.BaseDirectory, "FakeLspServer.dll");

    public static ExtensionTestHost CreateHost(TempDirectory temp)
    {
        var host = new ExtensionTestHost(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "lsp-diagnostics",
                ExtensionDirectory = Path.Combine(
                    AppContext.BaseDirectory,
                    "LspDiagnosticsExtension"
                ),
            }
        );
        var workspace = new Workspace(host.WorkingDirectory);
        // The loaded extension's manifest-matched verifiers gate the real write pipeline.
        host.Tools.Add(new WriteTool(workspace, host.Runner.WriteVerifiers.Find));
        host.Tools.Add(new EditTool(workspace, host.Runner.WriteVerifiers.Find));
        host.Tools.Add(new ReadTool(workspace));
        return host;
    }

    public static string MarkerPath(ExtensionTestHost host) =>
        Path.Combine(host.TempDirectory, "fake-lsp-marker.txt");

    /// <summary>Points the extension at the fake server; write before the host loads the extension.</summary>
    public static void WriteSettings(
        ExtensionTestHost host,
        int timeoutMs = 4000,
        int maxDiagnostics = 5,
        string serverCommand = "dotnet",
        params string[] serverArgs
    )
    {
        string directory = Path.Combine(host.StorePath, "extensions-settings");
        Directory.CreateDirectory(directory);
        string[] args = [FakeServerDll, .. serverArgs];
        File.WriteAllText(
            Path.Combine(directory, "lsp-diagnostics.json"),
            JsonSerializer.Serialize(
                new
                {
                    serverCommand,
                    serverArgs = args,
                    timeoutMs,
                    maxDiagnostics,
                }
            )
        );
    }

    public static ToolCallResult SingleToolResult(ExtensionRunResult result) =>
        result.Events.OfType<ToolCallResult>().Single();

    public static string[] MarkerLines(string markerPath) =>
        File.Exists(markerPath) ? File.ReadAllLines(markerPath) : [];

    public static int MarkerPid(string markerPath) =>
        int.Parse(MarkerLines(markerPath)[0], CultureInfo.InvariantCulture);

    public static bool LogContains(ExtensionTestHost host, string text) =>
        host.Log.Messages.Any(message => message.Contains(text, StringComparison.Ordinal));

    public static int LogCount(
        ExtensionTestHost host,
        ExtensionLogLevel level,
        string? contains = null
    ) =>
        host.Log.Entries.Count(entry =>
            entry.Level == level
            && (contains is null || entry.Message.Contains(contains, StringComparison.Ordinal))
        );
}
