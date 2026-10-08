using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

[Collection(RoslynIntegrationCollection.Name)]
public sealed class RoslynBackendTests
{
    [Fact]
    public async Task A_missing_root_is_no_solution()
    {
        using var backend = new RoslynBackend(
            Path.Combine(Path.GetTempPath(), "lunate-roslyn-tests", Guid.NewGuid().ToString("N"))
        );

        var result = await backend.LoadAsync(CancellationToken.None);

        Assert.Equal(WorkspaceStatus.NoSolution, result.Status);
        Assert.Null(result.SolutionPath);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task An_unrestored_solution_reports_restore_required()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        using var backend = new RoslynBackend(fixture.Root);

        var result = await backend.LoadAsync(CancellationToken.None);

        Assert.Equal(WorkspaceStatus.RestoreRequired, result.Status);
        Assert.NotNull(result.SolutionPath);
        Assert.Contains("dotnet restore", result.Message, StringComparison.Ordinal);
        Assert.Contains("ConsoleApp", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_restored_console_app_loads_clean()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);

        var result = await backend.LoadAsync(CancellationToken.None);

        Assert.Equal(WorkspaceStatus.Loaded, result.Status);
        Assert.Equal(1, result.ProjectCount);
        Assert.True(result.DocumentCount >= 2);
        Assert.Equal(0, result.TotalFailureCount);
        Assert.NotNull(result.SolutionPath);
        Assert.EndsWith("ConsoleApp.slnx", result.SolutionPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Loading_twice_is_idempotent()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);

        var first = await backend.LoadAsync(CancellationToken.None);
        var second = await backend.LoadAsync(CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task A_library_with_a_test_project_loads_both_projects()
    {
        using var fixture = Fixtures.CopySolution("lib-with-tests");
        Fixtures.Restore(fixture, "LibWithTests.sln");
        using var backend = new RoslynBackend(fixture.Root);

        var result = await backend.LoadAsync(CancellationToken.None);

        Assert.Equal(WorkspaceStatus.Loaded, result.Status);
        Assert.Equal(2, result.ProjectCount);

        var diagnostics = await backend.GetDiagnosticsAsync(
            DiagnosticsScope.Solution,
            CancellationToken.None
        );
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public async Task A_disposed_backend_loads_again_from_scratch()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        var backend = new RoslynBackend(fixture.Root);

        var first = await backend.LoadAsync(CancellationToken.None);
        backend.Dispose();
        var second = await backend.LoadAsync(CancellationToken.None);
        backend.Dispose();

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.SolutionPath, second.SolutionPath);
        Assert.Equal(first.ProjectCount, second.ProjectCount);
    }

    [Fact]
    public async Task An_injected_locator_failure_returns_no_sdk_and_never_throws()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        using var backend = new RoslynBackend(
            fixture.Root,
            () => new BootstrapResult(false, "install the .NET SDK")
        );

        var load = await backend.LoadAsync(CancellationToken.None);
        Assert.Equal(WorkspaceStatus.NoSdk, load.Status);
        Assert.Contains("install the .NET SDK", load.Message, StringComparison.Ordinal);

        var diagnostics = await backend.GetDiagnosticsAsync(
            DiagnosticsScope.ChangedFiles,
            CancellationToken.None
        );
        Assert.Empty(diagnostics.Items);
        Assert.NotEmpty(diagnostics.ScopeDescription);

        backend.NotifyFileChanged("not a real path\u0000");
        backend.NotifyFileChanged(string.Empty);
    }

    [Fact]
    public async Task Diagnostics_before_a_load_are_empty_instead_of_throwing()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        using var backend = new RoslynBackend(fixture.Root);

        var diagnostics = await backend.GetDiagnosticsAsync(
            DiagnosticsScope.ChangedFiles,
            CancellationToken.None
        );

        Assert.Empty(diagnostics.Items);
        Assert.Equal(0, diagnostics.ErrorCount);
    }

    [Fact]
    public async Task A_broken_project_reference_is_a_partial_load()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        var project = Path.Combine(fixture.Root, "src", "ConsoleApp", "ConsoleApp.csproj");
        File.WriteAllText(
            project,
            File.ReadAllText(project)
                .Replace(
                    "</Project>",
                    """
                      <ItemGroup>
                        <ProjectReference Include="..\Missing\Missing.csproj" />
                      </ItemGroup>
                    </Project>
                    """,
                    StringComparison.Ordinal
                )
        );
        File.AppendAllText(
            Path.Combine(fixture.Root, "src", "ConsoleApp", "Program.cs"),
            "\n_ = typeof(MissingNamespace.MissingType);\n"
        );
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);

        var result = await backend.LoadAsync(CancellationToken.None);

        Assert.Equal(WorkspaceStatus.Partial, result.Status);
        Assert.True(result.TotalFailureCount >= 1);
        Assert.NotEmpty(result.Failures);
        Assert.NotEmpty(result.Failures[0].Message);

        var diagnostics = await backend.GetDiagnosticsAsync(
            DiagnosticsScope.Solution,
            CancellationToken.None
        );
        Assert.NotEmpty(diagnostics.Items);
        Assert.True(diagnostics.ErrorCount >= 1);
    }

    [Fact]
    public async Task A_deleted_document_is_a_failure_and_the_workspace_survives()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);
        var calculator = Path.Combine(fixture.Root, "src", "ConsoleApp", "Calculator.cs");

        File.Delete(calculator);
        backend.NotifyFileChanged(calculator);

        var diagnostics = await backend.GetDiagnosticsAsync(
            DiagnosticsScope.ChangedFiles,
            CancellationToken.None
        );
        Assert.NotEmpty(diagnostics.Failures);
        Assert.Contains(
            diagnostics.Failures,
            failure => failure.Message.Contains("Calculator.cs", StringComparison.Ordinal)
        );

        var reload = await backend.LoadAsync(CancellationToken.None);
        Assert.Equal(WorkspaceStatus.Loaded, reload.Status);
    }
}
