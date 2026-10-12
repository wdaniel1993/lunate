using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Testing.Tests;

public sealed class McpFromExtensionEndToEndTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_extension_registered_mcp_server_round_trips_its_tools_through_the_harness()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        await using ExtensionTestHost host = CreateHost(temp, marker);
        await host.StartAsync(Ct);

        RegisteredMcpServer registered = Assert.Single(host.Runner.McpServers.List());
        Assert.Equal("test", registered.Name);
        Assert.Equal("mcp", registered.ExtensionId);

        Assert.False(File.Exists(marker), "registration must start nothing");

        IReadOnlyList<ExtensionMcpServerStatus> statuses = await host.ComposeMcpServersAsync(Ct);

        ExtensionMcpServerStatus status = Assert.Single(statuses);
        Assert.Equal("test", status.Name);
        Assert.Equal(ExtensionMcpServerState.Started, status.State);
        Assert.Equal(5, status.ToolCount);
        Assert.Equal("extension 'mcp'", status.Source);

        var pid = StartedPid(marker);
        Assert.True(IsProcessAlive(pid));
        Assert.Equal(
            ["test__add", "test__boom", "test__echo", "test__slow", "test__spawn_tool"],
            host.Tools.Tools.Select(tool => tool.Name).Order(StringComparer.Ordinal)
        );
        Assert.All(host.Tools.Tools, tool => Assert.Equal("test", tool.Namespace?.Name));
        Assert.Contains(host.Tools.Declarations, declaration => declaration.Name == "test__add");
        Assert.Contains(host.Tools.Tools, tool => tool.Name == "test__echo");
        Assert.DoesNotContain(
            host.Tools.Declarations,
            declaration => declaration.Name == "test__echo"
        );

        IReadOnlyList<ExtensionMcpServerStatus> again = await host.ComposeMcpServersAsync(Ct);

        Assert.Equal(ExtensionMcpServerState.Started, Assert.Single(again).State);
        Assert.Single(
            File.ReadAllLines(marker),
            line => line.StartsWith("started pid=", StringComparison.Ordinal)
        );

        await host.RunAsync(
            new ToolCallingChatClient(
                "test__add",
                new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 }
            ),
            "add 2 and 3",
            Ct
        );

        ToolCallResult result = Assert.Single(host.Events.Of<ToolCallResult>());
        Assert.False(result.IsError);
        Assert.Contains("5", result.Output, StringComparison.Ordinal);
        Assert.Contains(host.Events.Of<ToolCallStart>(), start => start.ToolName == "test__add");

        await host.StopAsync(Ct);
        await WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(10));
    }

    private static ExtensionTestHost CreateHost(TempDirectory temp, string marker)
    {
        var host = new ExtensionTestHost(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "mcp",
                ExtensionDirectory = Path.Combine(
                    AppContext.BaseDirectory,
                    "TestExtensions",
                    "McpExtension"
                ),
                SourceFiles = new Dictionary<string, string>
                {
                    ["extension.json"] =
                        """{"id":"mcp","version":"0.1.0","apiVersion":"^1.5.0","entryAssembly":"McpExtension.dll"}""",
                },
            }
        );
        string settingsDirectory = Path.Combine(host.StorePath, "extensions-settings");
        Directory.CreateDirectory(settingsDirectory);
        File.WriteAllText(
            Path.Combine(settingsDirectory, "mcp.json"),
            JsonSerializer.Serialize(
                new
                {
                    command = DotnetPath,
                    arguments = new[] { "exec", ServerDll },
                    marker,
                    serverName = "test",
                }
            )
        );
        return host;
    }

    private static string DotnetPath { get; } =
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path
        && File.Exists(path)
            ? path
            : "dotnet";

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string ServerDll { get; } =
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

    private static int StartedPid(string markerPath)
    {
        const string Prefix = "started pid=";
        string line = ReadMarkerLines(markerPath)
            .First(entry => entry.StartsWith(Prefix, StringComparison.Ordinal));
        return int.Parse(line[Prefix.Length..], CultureInfo.InvariantCulture);
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

    private static async Task WaitForProcessExitAsync(int pid, TimeSpan timeout)
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

    private static IReadOnlyList<string> ReadMarkerLines(string markerPath)
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
}
