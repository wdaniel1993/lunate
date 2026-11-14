using System.Diagnostics;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

/// <summary>
/// The container path against a real engine, on the one platform where it is proven: Linux with a
/// reachable Docker or Podman engine and an image. A command runs inside the session's container at
/// the host's absolute workspace path, its write is visible on the host, the session marker arrives,
/// nothing outside the workspace is reachable, and a timed-out command leaves no container-side
/// process behind. Every other host skips with the reason named.
/// </summary>
public sealed class ContainerExecutionEndToEndTests
{
    private const string EngineVariable = "LUNATE_CONTAINER_E2E_ENGINE";

    private const string ImageVariable = "LUNATE_CONTAINER_E2E_IMAGE";

    private const string DefaultImage = "ubuntu:24.04";

    [Fact]
    public async Task A_real_command_runs_in_the_container_at_the_host_path_and_the_workspace_is_shared()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip(
                "the container path is proven on Linux; macOS with Podman and Windows WSL2 are not CI legs of this test"
            );
        }

        (string engine, string image) = await ReachableEngineAsync();
        using var temp = new TempDirectory();
        using var outside = new TempDirectory();
        string secret = outside.File("state-root-secret.txt");
        File.WriteAllText(secret, "not reachable");
        string container = $"lunate-e2e-{Guid.NewGuid().ToString("N")[..10]}";
        await using var environment = new ContainerExecutionEnvironment(
            new ExecutionEnvironmentSelection(engine, image),
            temp.Root,
            guardEnabled: false,
            containerName: container
        );
        BashTool tool = Tool(temp.Root, TimeSpan.FromSeconds(90), environment);

        ToolResult result = await tool.ExecuteAsync(
            Command(
                $"printf '%s' \"${LunateHome.SessionVariable}\" > marker.txt; "
                    + "cat /etc/hostname > hostname.txt; "
                    + "printf shared > visible.txt; "
                    + $"if test -e {Quote(secret)}; then printf present > state.txt; "
                    + "else printf absent > state.txt; fi"
            ),
            Context(temp.Root),
            TestContext.Current.CancellationToken
        );

        Assert.False(result.IsError, result.Output);
        Assert.Equal(LunateHome.SessionMarker, ReadBack(temp.Root, "marker.txt").Trim());
        Assert.Equal("absent", ReadBack(temp.Root, "state.txt").Trim());
        Assert.Equal("shared", ReadBack(temp.Root, "visible.txt").Trim());

        // The container's own hostname proves the command ran there, not on the host.
        (int exitCode, string inspect) = Run(engine, ["inspect", "--format", "{{.Id}}", container]);
        Assert.Equal(0, exitCode);
        string expected = inspect.Trim()[..12];
        Assert.Equal(expected, ReadBack(temp.Root, "hostname.txt").Trim());
    }

    [Fact]
    public async Task A_timed_out_command_leaves_no_container_side_process_behind()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip(
                "the container path is proven on Linux; macOS with Podman and Windows WSL2 are not CI legs of this test"
            );
        }

        (string engine, string image) = await ReachableEngineAsync();
        using var temp = new TempDirectory();
        string container = $"lunate-e2e-{Guid.NewGuid().ToString("N")[..10]}";
        await using var environment = new ContainerExecutionEnvironment(
            new ExecutionEnvironmentSelection(engine, image),
            temp.Root,
            guardEnabled: false,
            containerName: container
        );
        BashTool tool = Tool(temp.Root, TimeSpan.FromSeconds(2), environment);

        ToolResult timedOut = await tool.ExecuteAsync(
            Command("sleep 60"),
            Context(temp.Root),
            TestContext.Current.CancellationToken
        );

        Assert.True(timedOut.IsError, timedOut.Output);
        Assert.Contains("timed out", timedOut.Output, StringComparison.OrdinalIgnoreCase);

        ToolResult after = await tool.ExecuteAsync(
            Command(
                "if ps -e -o args 2>/dev/null | grep -q 'sleep 60'; then printf left > left.txt; "
                    + "else printf gone > left.txt; fi"
            ),
            Context(temp.Root),
            TestContext.Current.CancellationToken
        );

        Assert.False(after.IsError, after.Output);
        Assert.Equal("gone", ReadBack(temp.Root, "left.txt").Trim());
    }

    /// <summary>
    /// The engine and image this host can actually run: the variable's engine, else Docker, else
    /// Podman; the image is pulled once when it is missing. A host without either skips visibly
    /// instead of passing, and a pull that cannot reach a registry skips with that reason too.
    /// </summary>
    private static async Task<(string Engine, string Image)> ReachableEngineAsync()
    {
        string image = Environment.GetEnvironmentVariable(ImageVariable) ?? DefaultImage;
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable(EngineVariable) is { Length: > 0 } selected)
        {
            candidates.Add(selected);
        }
        else
        {
            candidates.Add("docker");
            candidates.Add("podman");
        }

        foreach (string engine in candidates)
        {
            (int version, _) = Run(engine, ["version"], TimeSpan.FromSeconds(20));
            if (version != 0)
            {
                continue;
            }

            (int inspect, _) = Run(engine, ["image", "inspect", image], TimeSpan.FromSeconds(20));
            if (inspect == 0)
            {
                return (engine, image);
            }

            (int pull, string output) = Run(engine, ["pull", image], TimeSpan.FromMinutes(3));
            if (pull != 0)
            {
                Assert.Skip(
                    $"{engine} cannot pull the container E2E image '{image}': {FirstLine(output)}"
                );
            }

            return (engine, image);
        }

        Assert.Skip(
            $"no reachable container engine ({string.Join(", ", candidates)}); the container E2E needs Linux with Docker or Podman"
        );
        return ("", "");
    }

    private static BashTool Tool(
        string root,
        TimeSpan timeout,
        ContainerExecutionEnvironment environment
    ) =>
        new(
            new Workspace(root),
            resolver: null,
            timeout: timeout,
            log: null,
            new BashGuard(),
            changeScanTimeout: null,
            guardEnabled: false,
            environment
        );

    private static ToolContext Context(string root) => new(root, new NullAgentEvents());

    private static JsonElement Command(string command) =>
        JsonSerializer.SerializeToElement(new { command });

    private static string ReadBack(string root, string name) =>
        File.ReadAllText(Path.Combine(root, name));

    /// <summary>One POSIX shell word, so a path with spaces stays one operand.</summary>
    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()
        ?? string.Empty;

    private static (int ExitCode, string Output) Run(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null
    )
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception)
        {
            return (-1, exception.Message);
        }

        using (process)
        {
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(timeout ?? TimeSpan.FromSeconds(30)))
            {
                process.Kill(entireProcessTree: true);
                return (-1, "timed out");
            }

            return (process.ExitCode, stdout + stderr);
        }
    }
}
