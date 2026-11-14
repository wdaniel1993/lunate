using System.Diagnostics;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The container environment without an engine: the mapped argv, the forwarded session marker, the
/// one-container lifecycle, cancel by the recorded container-side process group, the readiness the
/// header and <c>/context</c> read, and the model-readable refusal a failed creation produces.
/// </summary>
public sealed class ContainerExecutionEnvironmentTests
{
    private const string Workspace = "/work/tree";

    private static ExecutionEnvironmentSelection Docker =>
        new(ExecutionEnvironmentSelection.Docker, "alpine");

    [Fact]
    public void A_command_maps_to_the_engine_exec_at_the_host_path_with_the_group_wrapper()
    {
        ContainerExecutionEnvironment environment = Environment(
            new FakeHost(),
            containerName: "lunate-test"
        );

        ExecutionCommand mapped = environment.Map(
            new ExecutionCommand("/bin/bash", ["-c", "echo hi"], Workspace, null)
        );

        Assert.Equal("docker", mapped.Executable);
        Assert.Equal(
            ["exec", "-i", "-w", Workspace, "lunate-test", "/bin/sh", "-c"],
            mapped.Arguments.Take(7)
        );
        string wrapper = mapped.Arguments[7];
        Assert.Contains("/tmp/lunate-test-1.pgid", wrapper, StringComparison.Ordinal);
        Assert.Contains("ps -o pgid=", wrapper, StringComparison.Ordinal);
        Assert.Contains("exec \"$@\"", wrapper, StringComparison.Ordinal);
        Assert.Equal(
            [ContainerExecutionEnvironment.WrapperName, "/bin/sh", "-c", "echo hi"],
            mapped.Arguments.Skip(8)
        );
        Assert.Equal(Workspace, mapped.WorkingDirectory);
        // The engine client inherits the host environment; the container gets no host environment.
        Assert.Null(mapped.Environment);
        Assert.Null(mapped.ExtraEnvironment);
    }

    [Fact]
    public void The_explicit_pairs_are_forwarded_sorted_and_the_session_marker_is_one_of_them()
    {
        ContainerExecutionEnvironment environment = Environment(
            new FakeHost(),
            containerName: "lunate-test"
        );

        ExecutionCommand mapped = environment.Map(
            new ExecutionCommand(
                "/bin/bash",
                ["-c", "true"],
                Workspace,
                new Dictionary<string, string> { ["B"] = "2", ["A"] = "1" },
                new Dictionary<string, string> { [LunateHome.SessionVariable] = "1" }
            )
        );

        Assert.Equal(
            ["-e", "A=1", "-e", "B=2", "-e", "LUNATE_SESSION=1", "lunate-test"],
            mapped.Arguments.Skip(4).Take(7)
        );
    }

    [Fact]
    public void The_two_accepted_engines_differ_only_in_the_executable()
    {
        ContainerExecutionEnvironment docker = Environment(new FakeHost(), containerName: "c");
        ContainerExecutionEnvironment podman = Environment(
            new FakeHost(),
            containerName: "c",
            selection: new ExecutionEnvironmentSelection(ExecutionEnvironmentSelection.Podman, "x")
        );
        var command = new ExecutionCommand("/bin/sh", ["-c", "true"], Workspace, null);

        ExecutionCommand dockerMapped = docker.Map(command);
        ExecutionCommand podmanMapped = podman.Map(command);

        Assert.Equal("docker", dockerMapped.Executable);
        Assert.Equal("podman", podmanMapped.Executable);
        Assert.Equal(dockerMapped.Arguments, podmanMapped.Arguments);
    }

    [Fact]
    public async Task Two_commands_share_one_container_and_disposal_removes_it()
    {
        var host = new FakeHost();
        await using ContainerExecutionEnvironment environment = Environment(
            host,
            containerName: "lunate-test"
        );

        Assert.Equal("starting", environment.Status.Readiness);

        await environment.RunAsync(Request("echo one"), TestContext.Current.CancellationToken);
        await environment.RunAsync(Request("echo two"), TestContext.Current.CancellationToken);

        Assert.Equal("ready", environment.Status.Readiness);
        Assert.Equal("docker:alpine", environment.Status.Description);
        Assert.Single(host.Requests, request => IsCreate(request));
        Assert.Equal(2, host.Requests.Count(IsToolCommand));
        Assert.All(
            host.Requests.Where(IsToolCommand),
            request => Assert.Contains("lunate-test", request.Arguments)
        );

        await environment.DisposeAsync();

        Assert.Contains(host.Requests, request => IsRemove(request, "lunate-test"));
    }

    [Fact]
    public async Task A_creation_failure_is_model_readable_and_never_runs_on_the_host()
    {
        var host = new FakeHost
        {
            Create = _ => new ProcessRunResult(
                1,
                string.Empty,
                "Cannot connect to the Docker daemon at unix:///var/run/docker.sock",
                TimedOut: false
            ),
        };
        await using ContainerExecutionEnvironment environment = Environment(
            host,
            containerName: "lunate-test"
        );

        ExecutionEnvironmentUnavailableException exception =
            await Assert.ThrowsAsync<ExecutionEnvironmentUnavailableException>(() =>
                environment.RunAsync(Request("echo hi"), TestContext.Current.CancellationToken)
            );

        Assert.Contains(
            "could not create its container",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Cannot connect to the Docker daemon",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("docker", exception.Message, StringComparison.Ordinal);
        Assert.Contains("alpine", exception.Message, StringComparison.Ordinal);
        Assert.Contains("did not run on the host", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(host.Requests, IsToolCommand);
        Assert.StartsWith("failed: ", environment.Status.Readiness, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_creation_failure_reaches_the_model_as_a_tool_error()
    {
        using var temp = new TempDirectory();
        var host = new FakeHost
        {
            Create = _ => new ProcessRunResult(1, string.Empty, "engine is not running", false),
        };
        await using ContainerExecutionEnvironment environment = new(
            Docker,
            temp.Root,
            host,
            guardEnabled: false,
            containerName: "lunate-test"
        );
        var tool = new BashTool(
            new Workspace(temp.Root),
            resolver: null,
            timeout: null,
            log: null,
            new BashGuard(),
            changeScanTimeout: null,
            guardEnabled: false,
            environment
        );

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { command = "echo hi" }),
            new ToolContext(temp.Root, new NullAgentEvents()),
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsError);
        Assert.Contains("could not create its container", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timeout_kills_the_recorded_group_inside_the_container()
    {
        var host = new FakeHost { CancelToolCommand = true };
        await using ContainerExecutionEnvironment environment = Environment(
            host,
            containerName: "lunate-test"
        );

        ProcessRunResult result = await environment.RunAsync(
            Request("sleep 60"),
            TestContext.Current.CancellationToken
        );

        Assert.True(result.TimedOut);
        ProcessRunRequest command = Assert.Single(host.Requests, IsToolCommand);
        string pgidPath = PgidPathOf(command);
        ProcessRunRequest kill = Assert.Single(host.Requests, IsKill);
        string script = kill.Arguments[^1];
        Assert.Contains(pgidPath, script, StringComparison.Ordinal);
        Assert.Contains("kill -s TERM", script, StringComparison.Ordinal);
        Assert.Contains("kill -s KILL", script, StringComparison.Ordinal);
        // A proven-dead group keeps the container; only an unprovable one recreates it.
        Assert.DoesNotContain(host.Requests, request => IsRemove(request, "lunate-test"));
    }

    [Fact]
    public async Task An_unprovable_group_recreates_the_container_for_the_next_command()
    {
        var host = new FakeHost
        {
            CancelToolCommand = true,
            Kill = _ => new ProcessRunResult(3, "", "", false),
        };
        await using ContainerExecutionEnvironment environment = Environment(
            host,
            containerName: "lunate-test"
        );

        await environment.RunAsync(Request("sleep 60"), TestContext.Current.CancellationToken);

        Assert.Contains(host.Requests, request => IsRemove(request, "lunate-test"));
        Assert.Equal("starting", environment.Status.Readiness);

        await environment.RunAsync(Request("echo again"), TestContext.Current.CancellationToken);

        Assert.Equal(2, host.Requests.Count(IsCreate));
        Assert.Equal(2, host.Requests.Count(IsToolCommand));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("not-a-number")]
    public void A_recorded_group_that_is_empty_root_or_not_a_number_is_never_killed(string recorded)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("the cancel script is POSIX sh; the Windows legs do not run it");
        }

        using var temp = new TempDirectory();
        string path = temp.File("cancel.pgid");
        File.WriteAllText(path, recorded);

        (int exitCode, _) = RunSh(ContainerExecutionEnvironment.KillScript(path));

        Assert.Equal(3, exitCode);
    }

    [Fact]
    public void The_guard_wraps_the_engine_client_not_the_command_inside()
    {
        using var temp = new TempDirectory();
        string state = Path.Combine(temp.Root, "state");
        var guard = new BashGuard(
            new GuardTarget(state, Path.Combine(state, "auth.json"), temp.Root, temp.Root),
            Fake.Unix(macOs: true, sandboxExec: true)
        );
        ContainerExecutionEnvironment environment = Environment(
            new FakeHost(),
            containerName: "lunate-test",
            guard: guard
        );

        ExecutionCommand mapped = environment.Map(
            new ExecutionCommand("/bin/bash", ["-c", "echo hi"], temp.Root, null)
        );

        Assert.Equal("/usr/bin/sandbox-exec", mapped.Executable);
        Assert.Equal("-p", mapped.Arguments[0]);
        // The guard prefix ends at the engine client; the container command stays unguarded.
        Assert.Equal("docker", mapped.Arguments[2]);
        Assert.Equal("exec", mapped.Arguments[3]);
        Assert.Contains("/bin/sh", mapped.Arguments);
    }

    [Fact]
    public async Task The_only_mount_is_the_workspace_at_its_host_path()
    {
        var host = new FakeHost();
        await using ContainerExecutionEnvironment environment = Environment(
            host,
            containerName: "lunate-test"
        );

        await environment.RunAsync(Request("true"), TestContext.Current.CancellationToken);

        ProcessRunRequest create = Assert.Single(host.Requests, IsCreate);
        Assert.Equal(
            [
                "run",
                "-d",
                "--name",
                "lunate-test",
                "-v",
                $"{Workspace}:{Workspace}",
                "-w",
                Workspace,
            ],
            create.Arguments.Take(8)
        );
        // One mount, the workspace, at the host's own absolute path. The state root, the install
        // prefix, auth.json and the engine socket are therefore absent inside the container.
        Assert.DoesNotContain("-v", create.Arguments.Skip(5));
        Assert.DoesNotContain("--volume", create.Arguments);
        Assert.DoesNotContain("--mount", create.Arguments);
        Assert.DoesNotContain("--privileged", create.Arguments);
        Assert.DoesNotContain(
            create.Arguments,
            argument =>
                argument.Contains("auth.json", StringComparison.Ordinal)
                || argument.Contains("docker.sock", StringComparison.Ordinal)
                || argument.Contains("podman.sock", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void The_environment_announces_that_it_starts_a_host_client()
    {
        ContainerExecutionEnvironment environment = Environment(new FakeHost());

        Assert.True(environment.StartsHostClient);
        Assert.False(((IExecutionEnvironment)LocalExecutionEnvironment.Instance).StartsHostClient);
        Assert.Equal("docker:alpine", environment.Description);
    }

    private static ContainerExecutionEnvironment Environment(
        FakeHost host,
        string? containerName = null,
        ExecutionEnvironmentSelection? selection = null,
        BashGuard? guard = null
    ) =>
        new(
            selection ?? Docker,
            Workspace,
            host,
            guard: guard,
            guardEnabled: guard is not null,
            containerName: containerName
        );

    private static ProcessRunRequest Request(string command) =>
        new("/bin/bash", ["-c", command], Workspace);

    private static bool IsCreate(ProcessRunRequest request) =>
        request.Arguments.Count > 0 && request.Arguments[0] == "run";

    private static bool IsRemove(ProcessRunRequest request, string container) =>
        request.Arguments.Count >= 3
        && request.Arguments[0] == "rm"
        && request.Arguments[2] == container;

    private static bool IsToolCommand(ProcessRunRequest request) =>
        request.Arguments.Count > 0
        && request.Arguments[0] == "exec"
        && request.Arguments.Contains("-i");

    private static bool IsKill(ProcessRunRequest request) =>
        request.Arguments.Count > 0
        && request.Arguments[0] == "exec"
        && !request.Arguments.Contains("-i");

    private static string PgidPathOf(ProcessRunRequest command) =>
        command
            .Arguments.Where(argument => argument.Contains(".pgid", StringComparison.Ordinal))
            .Select(argument =>
            {
                int start = argument.IndexOf("/tmp/", StringComparison.Ordinal);
                int end = argument.IndexOf(".pgid", StringComparison.Ordinal) + ".pgid".Length;
                return argument[start..end];
            })
            .Single();

    private static (int ExitCode, string Output) RunSh(string script)
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    /// <summary>
    /// The host half without an engine: records every request, and can simulate the cancel of a
    /// tool command so the container-side kill path runs without a container.
    /// </summary>
    private sealed class FakeHost : IHostCommandRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];

        public Func<ProcessRunRequest, ProcessRunResult>? Create { get; init; }

        public Func<ProcessRunRequest, ProcessRunResult>? Kill { get; init; }

        public bool CancelToolCommand { get; init; }

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken,
            Func<Task>? afterCancel
        ) => RunCoreAsync(request, afterCancel);

        private async Task<ProcessRunResult> RunCoreAsync(
            ProcessRunRequest request,
            Func<Task>? afterCancel
        )
        {
            Requests.Add(request);
            if (IsCreate(request))
            {
                return Create is { } create
                    ? create(request)
                    : new ProcessRunResult(0, "container-id", string.Empty, TimedOut: false);
            }

            if (IsToolCommand(request))
            {
                if (CancelToolCommand && afterCancel is not null)
                {
                    await afterCancel();
                    return new ProcessRunResult(null, "partial", string.Empty, TimedOut: true);
                }

                return new ProcessRunResult(0, "ok", string.Empty, TimedOut: false);
            }

            if (IsKill(request))
            {
                return Kill is { } kill
                    ? kill(request)
                    : new ProcessRunResult(0, string.Empty, string.Empty, TimedOut: false);
            }

            return new ProcessRunResult(0, string.Empty, string.Empty, TimedOut: false);
        }

        public Task<IProcessSession> StartSessionAsync(
            ProcessSessionRequest request,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("no interactive tool command in this test");
    }
}
