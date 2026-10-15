using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

/// <summary>
/// The file-change bus seam of the backend: marks are lock-free and merged with the stamp scan
/// (one changed-file count), a project/build-file change triggers exactly one reload on the next
/// load, and the reload path re-runs the restore check.
/// </summary>
[Collection(RoslynIntegrationCollection.Name)]
public sealed class RoslynBackendSyncTests
{
    [Fact]
    public async Task A_mark_completes_while_the_gate_is_held_by_a_load()
    {
        using var temp = new TempDirectory();
        CancellationToken ct = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var backend = new RoslynBackend(
            temp.Root,
            () =>
            {
                entered.TrySetResult();
                release.Task.Wait(TimeSpan.FromSeconds(30));
                return new BootstrapResult(false, "no sdk");
            }
        );

        Task<WorkspaceLoadResult> load = Task.Run(() => backend.LoadAsync(ct).AsTask(), ct);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);

            // The load holds the backend gate: the mark must complete without waiting for it.
            Task mark = Task.Run(() => backend.NotifyFileChanged(temp.File("a.cs")), ct);
            await mark.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        finally
        {
            release.TrySetResult();
            await load.WaitAsync(TimeSpan.FromSeconds(30), ct);
        }
    }

    [Fact]
    public async Task A_bus_mark_reports_a_stamp_invisible_rewrite()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

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
        // Same length and the timestamp reset: the content stamp is unchanged, so only the mark
        // can carry the change.
        File.SetLastWriteTimeUtc(calculator, timestamp);
        backend.NotifyFileChanged(calculator);

        var diagnostics = await backend.GetDiagnosticsAsync(
            DiagnosticsScope.ChangedFiles,
            CancellationToken.None
        );

        Assert.Contains(
            "1 file(s) changed since the last check",
            diagnostics.ScopeDescription,
            StringComparison.Ordinal
        );
        Assert.Contains(
            diagnostics.Items,
            item =>
                item.Id == "CS0103" && item.File.EndsWith("Calculator.cs", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_bus_mark_and_the_stamp_scan_report_one_changed_file()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var calculator = Path.Combine(fixture.Root, "src", "ConsoleApp", "Calculator.cs");
        var broken = File.ReadAllText(calculator) + "\nclass Broken { int X => Missing4; }\n";
        File.WriteAllText(calculator, broken);
        // Both paths flag the file: the stamp scan sees the rewrite, the bus delivers the mark.
        backend.NotifyFileChanged(calculator);

        var diagnostics = await backend.GetDiagnosticsAsync(
            DiagnosticsScope.ChangedFiles,
            CancellationToken.None
        );

        Assert.Contains(
            "1 file(s) changed since the last check",
            diagnostics.ScopeDescription,
            StringComparison.Ordinal
        );
        Assert.Contains(
            diagnostics.Items,
            item =>
                item.Id == "CS0103" && item.File.EndsWith("Calculator.cs", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_mark_for_a_non_document_path_lingers_without_being_reported()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        // A path that is no document of the solution: its mark is never drained into a refresh
        // (the same lingering semantics the dirty set always had).
        backend.NotifyFileChanged(Path.Combine(fixture.Root, "notes.cs"));

        var diagnostics = await backend.GetDiagnosticsAsync(
            DiagnosticsScope.ChangedFiles,
            CancellationToken.None
        );

        Assert.Equal("no files changed since the last check", diagnostics.ScopeDescription);
    }

    [Fact]
    public async Task A_project_change_reloads_once_on_the_next_load()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);

        var first = await backend.LoadAsync(CancellationToken.None);
        Assert.Equal(WorkspaceStatus.Loaded, first.Status);
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
        backend.NotifyProjectChanged();

        var reloaded = await backend.LoadAsync(CancellationToken.None);

        Assert.Equal(WorkspaceStatus.Loaded, reloaded.Status);
        Assert.Equal(2, backend.LoadCount);

        var cached = await backend.LoadAsync(CancellationToken.None);
        Assert.Equal(WorkspaceStatus.Loaded, cached.Status);
        Assert.Equal(2, backend.LoadCount);
    }

    [Fact]
    public async Task A_reload_after_a_project_change_surfaces_the_restore_hint_when_restore_output_is_missing()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

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
        var assets = Path.Combine(fixture.Root, "src", "ConsoleApp", "obj", "project.assets.json");
        File.Delete(assets);
        backend.NotifyProjectChanged();

        var reload = await backend.LoadAsync(CancellationToken.None);

        Assert.Equal(WorkspaceStatus.RestoreRequired, reload.Status);
        Assert.Contains("dotnet restore", reload.Message, StringComparison.Ordinal);
        Assert.Equal(2, backend.LoadCount);
    }

    [Fact]
    public async Task A_reload_after_a_package_reference_change_surfaces_the_restore_hint_when_restore_output_is_stale()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        // The package is added after the restore: the assets file still exists but does not
        // cover the reference, so a silent load would ignore the package.
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
        backend.NotifyProjectChanged();

        var reload = await backend.LoadAsync(CancellationToken.None);

        Assert.Equal(WorkspaceStatus.RestoreRequired, reload.Status);
        Assert.Contains("dotnet restore", reload.Message, StringComparison.Ordinal);
        Assert.Equal(2, backend.LoadCount);
    }
}
