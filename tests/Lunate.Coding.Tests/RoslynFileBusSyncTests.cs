using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Roslyn;

namespace Lunate.Coding.Tests;

/// <summary>
/// The file-change bus end to end with the real Roslyn backend and the fixture solution: a
/// bus-delivered stamp-invisible edit refreshes diagnostics without a reload, project and
/// build-configuration changes reload once, an edit during a load is not lost, a file flagged by
/// both the mark and the stamp scan is reported once, and a write through a composed session
/// reaches fresh diagnostics.
/// </summary>
[Collection("roslyn-workspaces")]
public sealed class RoslynFileBusSyncTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_bus_delivered_stamp_invisible_edit_refreshes_without_reloading()
    {
        using var fixture = RoslynFixtureSupport.CopySolution("console-app");
        RoslynFixtureSupport.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        using var services = new WorkspaceServices(1, root => backend);
        var bus = new FileChangeBus(fixture.Root);
        services.Connect(bus);
        ToolRegistry tools = HarnessFactory.CreateTools(
            new Workspace(fixture.Root),
            services: services
        );

        ToolResult first = await RunDiagnosticsAsync(tools, fixture.Root);
        Assert.False(first.IsError, first.Output);
        Assert.Equal(1, backend.LoadCount);

        var calculator = Path.Combine(fixture.Root, "src", "ConsoleApp", "Calculator.cs");
        var original = File.ReadAllText(calculator);
        var timestamp = File.GetLastWriteTimeUtc(calculator);
        var broken = original.Replace(
            "left + right + third",
            "left + right + other",
            StringComparison.Ordinal
        );
        Assert.NotEqual(original, broken);
        Assert.Equal(original.Length, broken.Length);
        File.WriteAllText(calculator, broken);
        // Same length and the timestamp reset: only the bus mark can carry the change.
        File.SetLastWriteTimeUtc(calculator, timestamp);
        Notify(bus, fixture.Root, calculator, "src/ConsoleApp/Calculator.cs");
        await bus.DrainAsync(Ct);

        ToolResult second = await RunDiagnosticsAsync(tools, fixture.Root);

        Assert.False(second.IsError, second.Output);
        Assert.Contains("CS0103", second.Output, StringComparison.Ordinal);
        Assert.Contains(
            "1 file(s) changed since the last check",
            second.Output,
            StringComparison.Ordinal
        );
        Assert.Equal(1, backend.LoadCount);
    }

    [Fact]
    public async Task A_bus_delivered_project_change_reloads_once()
    {
        using var fixture = RoslynFixtureSupport.CopySolution("console-app");
        RoslynFixtureSupport.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        using var services = new WorkspaceServices(1, root => backend);
        var bus = new FileChangeBus(fixture.Root);
        services.Connect(bus);
        ToolRegistry tools = HarnessFactory.CreateTools(
            new Workspace(fixture.Root),
            services: services
        );
        await RunDiagnosticsAsync(tools, fixture.Root);
        Assert.Equal(1, backend.LoadCount);

        var project = Path.Combine(fixture.Root, "src", "ConsoleApp", "ConsoleApp.csproj");
        File.WriteAllText(
            project,
            File.ReadAllText(project)
                .Replace(
                    "</Project>",
                    "  <PropertyGroup>\n    <Company>Lunate</Company>\n  </PropertyGroup>\n</Project>",
                    StringComparison.Ordinal
                )
        );
        Notify(bus, fixture.Root, project, "src/ConsoleApp/ConsoleApp.csproj");
        await bus.DrainAsync(Ct);

        ToolResult reloaded = await RunDiagnosticsAsync(tools, fixture.Root);

        Assert.False(reloaded.IsError, reloaded.Output);
        Assert.Equal(2, backend.LoadCount);

        ToolResult cached = await RunDiagnosticsAsync(tools, fixture.Root);
        Assert.False(cached.IsError, cached.Output);
        Assert.Equal(2, backend.LoadCount);
    }

    [Fact]
    public async Task A_bus_delivered_directory_build_props_change_reloads()
    {
        using var fixture = RoslynFixtureSupport.CopySolution("console-app");
        RoslynFixtureSupport.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        using var services = new WorkspaceServices(1, root => backend);
        var bus = new FileChangeBus(fixture.Root);
        services.Connect(bus);
        ToolRegistry tools = HarnessFactory.CreateTools(
            new Workspace(fixture.Root),
            services: services
        );
        await RunDiagnosticsAsync(tools, fixture.Root);
        Assert.Equal(1, backend.LoadCount);

        var props = Path.Combine(fixture.Root, "Directory.Build.props");
        File.WriteAllText(
            props,
            File.ReadAllText(props)
                .Replace(
                    "</PropertyGroup>",
                    "    <Company>Lunate</Company>\n    </PropertyGroup>",
                    StringComparison.Ordinal
                )
        );
        Notify(bus, fixture.Root, props, "Directory.Build.props");
        await bus.DrainAsync(Ct);

        ToolResult reloaded = await RunDiagnosticsAsync(tools, fixture.Root);

        Assert.False(reloaded.IsError, reloaded.Output);
        Assert.Equal(2, backend.LoadCount);
    }

    [Fact]
    public async Task A_bus_delivered_package_reference_change_surfaces_the_restore_hint()
    {
        using var fixture = RoslynFixtureSupport.CopySolution("console-app");
        RoslynFixtureSupport.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        using var services = new WorkspaceServices(1, root => backend);
        var bus = new FileChangeBus(fixture.Root);
        services.Connect(bus);
        ToolRegistry tools = HarnessFactory.CreateTools(
            new Workspace(fixture.Root),
            services: services
        );
        await RunDiagnosticsAsync(tools, fixture.Root);

        // The package is added after the restore: the stale assets file would silently ignore it.
        var project = Path.Combine(fixture.Root, "src", "ConsoleApp", "ConsoleApp.csproj");
        File.WriteAllText(
            project,
            File.ReadAllText(project)
                .Replace(
                    "</Project>",
                    "  <ItemGroup>\n    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\n  </ItemGroup>\n</Project>",
                    StringComparison.Ordinal
                )
        );
        Notify(bus, fixture.Root, project, "src/ConsoleApp/ConsoleApp.csproj");
        await bus.DrainAsync(Ct);

        ToolResult reloaded = await RunDiagnosticsAsync(tools, fixture.Root);

        Assert.True(reloaded.IsError, reloaded.Output);
        Assert.Contains("dotnet restore", reloaded.Output, StringComparison.Ordinal);
        Assert.Equal(2, backend.LoadCount);
    }

    [Fact]
    public async Task An_edit_during_the_load_is_not_lost()
    {
        using var fixture = RoslynFixtureSupport.CopySolution("console-app");
        RoslynFixtureSupport.Restore(fixture, "ConsoleApp.slnx");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var backend = new RoslynBackend(
            fixture.Root,
            () =>
            {
                entered.TrySetResult();
                release.Task.Wait(TimeSpan.FromSeconds(30));
                return MsBuildBootstrap.EnsureInitialized();
            }
        );
        using var services = new WorkspaceServices(1, root => backend);
        var bus = new FileChangeBus(fixture.Root);
        services.Connect(bus);
        ToolRegistry tools = HarnessFactory.CreateTools(
            new Workspace(fixture.Root),
            services: services
        );

        Task<ToolResult> running = Task.Run(() => RunDiagnosticsAsync(tools, fixture.Root), Ct);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

            // The load holds the backend gate: the bus mark must land without waiting for it.
            var calculator = Path.Combine(fixture.Root, "src", "ConsoleApp", "Calculator.cs");
            var original = File.ReadAllText(calculator);
            var timestamp = File.GetLastWriteTimeUtc(calculator);
            var broken = original.Replace(
                "left + right + third",
                "left + right + other",
                StringComparison.Ordinal
            );
            File.WriteAllText(calculator, broken);
            File.SetLastWriteTimeUtc(calculator, timestamp);
            Notify(bus, fixture.Root, calculator, "src/ConsoleApp/Calculator.cs");
            await bus.DrainAsync(Ct);
        }
        finally
        {
            release.TrySetResult();
        }

        ToolResult result = await running.WaitAsync(TimeSpan.FromMinutes(2), Ct);

        Assert.False(result.IsError, result.Output);
        Assert.Contains("CS0103", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, backend.LoadCount);
    }

    [Fact]
    public async Task A_file_flagged_by_both_paths_is_reported_once()
    {
        using var fixture = RoslynFixtureSupport.CopySolution("console-app");
        RoslynFixtureSupport.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        using var services = new WorkspaceServices(1, root => backend);
        var bus = new FileChangeBus(fixture.Root);
        services.Connect(bus);
        ToolRegistry tools = HarnessFactory.CreateTools(
            new Workspace(fixture.Root),
            services: services
        );
        await RunDiagnosticsAsync(tools, fixture.Root);

        // Both paths flag the file: the stamp scan sees the rewrite, the bus delivers the mark.
        var calculator = Path.Combine(fixture.Root, "src", "ConsoleApp", "Calculator.cs");
        File.WriteAllText(
            calculator,
            File.ReadAllText(calculator) + "\nclass Broken { int X => Missing4; }\n"
        );
        Notify(bus, fixture.Root, calculator, "src/ConsoleApp/Calculator.cs");
        await bus.DrainAsync(Ct);

        ToolResult result = await RunDiagnosticsAsync(tools, fixture.Root);

        Assert.False(result.IsError, result.Output);
        Assert.Contains(
            "1 file(s) changed since the last check",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Contains("CS0103", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, backend.LoadCount);
    }

    [Fact]
    public async Task A_composed_session_write_keeps_diagnostics_fresh_with_one_load()
    {
        using var temp = new TempDirectory();
        using var fixture = RoslynFixtureSupport.CopySolution("console-app");
        RoslynFixtureSupport.Restore(fixture, "ConsoleApp.slnx");
        var backend = new RoslynBackend(fixture.Root);
        using var services = new WorkspaceServices(1, root => backend);
        var workspace = new Workspace(fixture.Root);
        await using ExtensionSession host = await ExtensionSession.StartAsync(
            workspace.Identity,
            TestExtensionSeams.Empty(temp),
            Ct
        );
        services.Connect(host.FileChanges);
        ToolRegistry tools = HarnessFactory.CreateTools(workspace, services: services);
        var context = new ToolContext(fixture.Root, new NullAgentEvents())
        {
            FileChanges = host.FileChanges,
        };

        ToolResult initial = await tools
            .Find("cs_diagnostics")!
            .ExecuteAsync(Args("{}"), context, Ct);
        Assert.False(initial.IsError, initial.Output);
        Assert.Equal(1, backend.LoadCount);

        var calculator = Path.Combine(fixture.Root, "src", "ConsoleApp", "Calculator.cs");
        var broken = File.ReadAllText(calculator) + "\nclass Broken { int X => Missing4; }\n";
        ToolResult written = await tools
            .Find("write")!
            .ExecuteAsync(
                JsonSerializer.SerializeToElement(
                    new { path = "src/ConsoleApp/Calculator.cs", content = broken }
                ),
                context,
                Ct
            );
        Assert.False(written.IsError, written.Output);
        await host.FileChanges.DrainAsync(Ct);

        ToolResult result = await tools
            .Find("cs_diagnostics")!
            .ExecuteAsync(Args("{}"), context, Ct);

        Assert.False(result.IsError, result.Output);
        Assert.Contains("CS0103", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, backend.LoadCount);
    }

    [Fact]
    public async Task A_bash_calls_stamp_invisible_edit_is_marked_and_refreshes_without_reloading()
    {
        using var temp = new TempDirectory();
        using var fixture = RoslynFixtureSupport.CopySolution("console-app");
        GitTestRepo.Init(fixture.Root);
        GitTestRepo.Git(fixture.Root, "add", "-A");
        GitTestRepo.Git(fixture.Root, "commit", "-qm", "fixture");
        RoslynFixtureSupport.Restore(fixture, "ConsoleApp.slnx");
        var backend = new RoslynBackend(fixture.Root);
        using var services = new WorkspaceServices(1, root => backend);
        var workspace = new Workspace(fixture.Root);
        await using ExtensionSession host = await ExtensionSession.StartAsync(
            workspace.Identity,
            TestExtensionSeams.Empty(temp),
            Ct
        );
        services.Connect(host.FileChanges);
        ToolRegistry tools = HarnessFactory.CreateTools(workspace, services: services);
        var context = new ToolContext(fixture.Root, new NullAgentEvents())
        {
            FileChanges = host.FileChanges,
            RunId = "run_1",
        };

        ToolResult first = await RunDiagnosticsAsync(tools, fixture.Root);
        Assert.False(first.IsError, first.Output);
        Assert.Equal(1, backend.LoadCount);

        // The first bash call of the run only baselines.
        ToolResult warm = await tools
            .Find("bash")!
            .ExecuteAsync(Args("""{"command":"echo warm"}"""), context, Ct);
        Assert.False(warm.IsError, warm.Output);

        // A bash call edits a file with the same length and the original timestamp restored: the
        // content stamp cannot see it, so only the bash mark can carry the change.
        var calculator = Path.Combine(fixture.Root, "src", "ConsoleApp", "Calculator.cs");
        var original = File.ReadAllText(calculator);
        var broken = original.Replace(
            "left + right + third",
            "left + right + other",
            StringComparison.Ordinal
        );
        Assert.NotEqual(original, broken);
        Assert.Equal(original.Length, broken.Length);
        File.WriteAllText(Path.Combine(fixture.Root, ".edit.tmp"), broken);
        ToolResult edited = await tools
            .Find("bash")!
            .ExecuteAsync(
                Args(
                    """{"command":"cp -p src/ConsoleApp/Calculator.cs .keep.tmp && cp .edit.tmp src/ConsoleApp/Calculator.cs && touch -r .keep.tmp src/ConsoleApp/Calculator.cs"}"""
                ),
                context,
                Ct
            );
        Assert.False(edited.IsError, edited.Output);
        await host.FileChanges.DrainAsync(Ct);

        ToolResult second = await RunDiagnosticsAsync(tools, fixture.Root);

        Assert.False(second.IsError, second.Output);
        Assert.Contains("CS0103", second.Output, StringComparison.Ordinal);
        Assert.Contains(
            "1 file(s) changed since the last check",
            second.Output,
            StringComparison.Ordinal
        );
        Assert.Equal(1, backend.LoadCount);
    }

    [Fact]
    public async Task A_new_file_in_an_untracked_directory_reaches_the_check_after_a_bash_project_change()
    {
        using var temp = new TempDirectory();
        using var fixture = RoslynFixtureSupport.CopySolution("console-app");
        GitTestRepo.Init(fixture.Root);
        GitTestRepo.Git(fixture.Root, "add", "-A");
        GitTestRepo.Git(fixture.Root, "commit", "-qm", "fixture");
        RoslynFixtureSupport.Restore(fixture, "ConsoleApp.slnx");
        var backend = new RoslynBackend(fixture.Root);
        using var services = new WorkspaceServices(1, root => backend);
        var workspace = new Workspace(fixture.Root);
        await using ExtensionSession host = await ExtensionSession.StartAsync(
            workspace.Identity,
            TestExtensionSeams.Empty(temp),
            Ct
        );
        services.Connect(host.FileChanges);
        ToolRegistry tools = HarnessFactory.CreateTools(workspace, services: services);
        var context = new ToolContext(fixture.Root, new NullAgentEvents())
        {
            FileChanges = host.FileChanges,
            RunId = "run_1",
        };

        ToolResult first = await RunDiagnosticsAsync(tools, fixture.Root);
        Assert.False(first.IsError, first.Output);
        Assert.Equal(1, backend.LoadCount);

        ToolResult warm = await tools
            .Find("bash")!
            .ExecuteAsync(Args("""{"command":"echo warm"}"""), context, Ct);
        Assert.False(warm.IsError, warm.Output);

        // A new source file inside a new untracked directory (the scan skips untracked
        // directories) plus a project-file change in the same call: the project change is marked
        // and the reload brings the new file in fresh.
        ToolResult made = await tools
            .Find("bash")!
            .ExecuteAsync(
                Args(
                    """{"command":"mkdir src/ConsoleApp/newdir && printf 'class Bad { int X => Missing9; }\n' > src/ConsoleApp/newdir/Bad.cs && printf '\n' >> src/ConsoleApp/ConsoleApp.csproj"}"""
                ),
                context,
                Ct
            );
        Assert.False(made.IsError, made.Output);
        await host.FileChanges.DrainAsync(Ct);

        // The reload re-baselines every document, so the changed scope reports nothing; the
        // solution scope shows the reloaded project's diagnostics — the new file included.
        ToolResult second = await tools
            .Find("cs_diagnostics")!
            .ExecuteAsync(Args("""{"scope":"solution"}"""), context, Ct);

        Assert.False(second.IsError, second.Output);
        Assert.Equal(2, backend.LoadCount);
        Assert.Contains("CS0103", second.Output, StringComparison.Ordinal);
    }

    private static Task<ToolResult> RunDiagnosticsAsync(ToolRegistry tools, string root) =>
        tools
            .Find("cs_diagnostics")!
            .ExecuteAsync(Args("{}"), new ToolContext(root, new NullAgentEvents()), Ct);

    private static void Notify(FileChangeBus bus, string root, string absolute, string relative) =>
        bus.Notify(new FileChangeEvent(absolute, relative, root, null, FileChangeKind.Modified));

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class NullAgentEvents : IAgentEvents
    {
        public void Emit(AgentEvent agentEvent) { }
    }
}
