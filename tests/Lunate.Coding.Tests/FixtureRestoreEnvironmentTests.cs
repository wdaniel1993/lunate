using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>
/// The environment a fixture restore hands its child: self-contained, so a runner whose home is
/// missing or read-only cannot fail a fixture with an error that names nothing, and a child's
/// failure can never be reported as silence.
/// </summary>
public sealed class FixtureRestoreEnvironmentTests
{
    [Fact]
    public void The_child_gets_its_own_cli_home_and_package_folders()
    {
        using var temp = new TempDirectory();

        Dictionary<string, string?> environment = FixtureRestore.ChildEnvironment(temp.Root);

        Assert.Equal(Path.Combine(temp.Root, ".dotnet-home"), environment["DOTNET_CLI_HOME"]);
        Assert.Equal(Path.Combine(temp.Root, ".packages"), environment["NUGET_PACKAGES"]);
        Assert.Equal(
            Path.Combine(temp.Root, ".nuget-http-cache"),
            environment["NUGET_HTTP_CACHE_PATH"]
        );
        Assert.DoesNotContain("HOME", environment.Keys);
    }

    [Fact]
    public void The_child_gets_a_private_temp_and_no_shared_build_server()
    {
        using var temp = new TempDirectory();

        Dictionary<string, string?> environment = FixtureRestore.ChildEnvironment(temp.Root);

        // Every folder the child can write to is inside the fixture: NuGet's scratch and MSBuild's
        // response files, like the package folders, must not be a place another restore can see.
        Assert.Equal(Path.Combine(temp.Root, ".tmp"), environment["TMPDIR"]);
        Assert.Equal(Path.Combine(temp.Root, ".tmp"), environment["TMP"]);
        Assert.Equal(Path.Combine(temp.Root, ".tmp"), environment["TEMP"]);
        // The build server is a long-lived process shared by everything on the machine. A fixture
        // restore is a few seconds of work; it does not need one.
        Assert.Equal("0", environment["MSBUILDUSESERVER"]);
        Assert.Equal("false", environment["DOTNET_CLI_USE_MSBUILD_SERVER"]);
    }

    [Fact]
    public void The_restore_is_one_node_with_a_private_package_folder()
    {
        using var temp = new TempDirectory();
        string solution = Path.Combine(temp.Root, "App.slnx");

        string[] arguments = FixtureRestore.RestoreArguments(
            temp.Root,
            solution,
            Path.Combine(temp.Root, "NuGet.config")
        );

        Assert.Contains(solution, arguments);
        Assert.Contains("-maxcpucount:1", arguments);
        Assert.Contains("-nodeReuse:false", arguments);
        int packages = Array.IndexOf(arguments, "--packages");
        Assert.InRange(packages, 0, arguments.Length - 2);
        Assert.Equal(Path.Combine(temp.Root, ".packages"), arguments[packages + 1]);
    }

    [Fact]
    public void A_failing_child_reports_what_it_printed()
    {
        using var temp = new TempDirectory();

        ChildProcessResult result = DotnetCli.Run(
            temp.Root,
            ["restore", "--definitely-not-a-real-option"],
            new Dictionary<string, string?>(),
            TimeSpan.FromSeconds(60)
        );

        Assert.False(result.Succeeded);
        // dotnet writes an MSBuild error on stdout here, so the contract is "something survived",
        // not "stderr survived" — what must never happen is a failure that reads as silence.
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput + result.StandardError));
        Assert.DoesNotContain(
            "printed nothing",
            result.Describe("dotnet restore failed"),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void A_stream_that_does_not_close_is_not_reported_as_silence()
    {
        // The pipe, not the child, was silent: the child exited but something it started — a build
        // server, a node — still holds the stream open. An empty string here would accuse the child
        // of a silent crash that never happened.
        var stalled = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        string output = DotnetCli.Drain(stalled.Task, TimeSpan.FromMilliseconds(100));

        Assert.Contains("had not closed", output, StringComparison.Ordinal);

        var result = new ChildProcessResult(
            Command: "dotnet restore App.slnx",
            WorkingDirectory: "/tmp/fixture",
            ExitCode: 1,
            Duration: TimeSpan.FromSeconds(3),
            StandardOutput: output,
            StandardError: string.Empty
        );

        string message = result.Describe("dotnet restore failed");

        Assert.DoesNotContain("printed nothing", message, StringComparison.Ordinal);
        Assert.Contains("had not closed", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_that_reported_no_error_names_the_target_it_was_in()
    {
        // The words CI printed for a disturbed solution restore, verbatim: the metaproject was
        // entered, no project restore line followed, and MSBuild reported zero errors. The target is
        // the only fact in that output that says where the restore gave up.
        var result = new ChildProcessResult(
            Command: "dotnet restore LibWithTests.slnx",
            WorkingDirectory: "/tmp/lunate-roslyn-tests/abc",
            ExitCode: 1,
            Duration: TimeSpan.FromSeconds(3.1),
            StandardOutput: """
            Build started 10/28/2026 11:34:02.
             1>Project "/tmp/lunate-roslyn-tests/abc/LibWithTests.slnx" on node 1 (Restore target(s)).
             1>ValidateSolutionConfiguration:
             1>Done Building Project "/tmp/lunate-roslyn-tests/abc/LibWithTests.slnx" (Restore target(s)) -- FAILED.
            Build FAILED.
             0 Warning(s)
             0 Error(s)
            """,
            StandardError: string.Empty
        );

        string message = result.Describe("the retry");

        Assert.Contains(
            "last target: ValidateSolutionConfiguration",
            message,
            StringComparison.Ordinal
        );
        Assert.Contains("0 Error(s)", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_successful_child_names_no_failing_target()
    {
        var result = new ChildProcessResult(
            Command: "dotnet restore App.slnx",
            WorkingDirectory: "/tmp/fixture",
            ExitCode: 0,
            Duration: TimeSpan.FromSeconds(1),
            StandardOutput: """
            Build started 10/28/2026 11:34:02.
            ValidateSolutionConfiguration:
            Build succeeded.
            """,
            StandardError: string.Empty
        );

        Assert.DoesNotContain(
            "last target",
            result.Describe("dotnet restore failed"),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void The_machine_report_names_the_cores_and_the_disk()
    {
        using var temp = new TempDirectory();

        string description = ChildProcessDiagnostics.Describe(temp.Root);

        Assert.Contains("processors:", description, StringComparison.Ordinal);
        Assert.Contains("free space", description, StringComparison.Ordinal);
        Assert.Contains("HOME=", description, StringComparison.Ordinal);
        // The report is about the child that failed, not the test host: it must name the folders the
        // restore was actually told to use, or a reader cannot tell isolation from inheritance.
        Assert.Contains(
            $"NUGET_PACKAGES={Path.Combine(temp.Root, ".packages")}",
            description,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"TMPDIR={Path.Combine(temp.Root, ".tmp")}",
            description,
            StringComparison.Ordinal
        );
    }
}
