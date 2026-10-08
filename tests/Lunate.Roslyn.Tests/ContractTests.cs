using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class ContractTests
{
    [Fact]
    public void Changed_files_is_the_default_diagnostics_scope()
    {
        Assert.Equal(DiagnosticsScope.ChangedFiles, default(DiagnosticsScope));
    }

    [Fact]
    public void Workspace_load_result_defaults_the_total_failure_count_to_the_bounded_list()
    {
        var failures = new List<WorkspaceFailure> { new("Fixture.Tests", "boom") };

        var result = new WorkspaceLoadResult(
            WorkspaceStatus.Partial,
            "partial load",
            "/tmp/Fixture.sln",
            3,
            16,
            failures
        );

        Assert.Equal(WorkspaceStatus.Partial, result.Status);
        Assert.Equal(1, result.TotalFailureCount);
        Assert.Same(failures, result.Failures);
    }

    [Fact]
    public void Workspace_load_result_reports_more_failures_than_the_bounded_list()
    {
        var failures = new List<WorkspaceFailure> { new("Fixture.Tests", "boom") };

        var result = new WorkspaceLoadResult(
            WorkspaceStatus.Partial,
            "partial load",
            "/tmp/Fixture.sln",
            3,
            16,
            failures
        )
        {
            TotalFailureCount = 25,
        };

        Assert.Equal(25, result.TotalFailureCount);
    }

    [Fact]
    public void Diagnostics_result_carries_counts_truncation_and_scope()
    {
        var item = new DiagnosticsItem(
            "src/Fixture.App/Program.cs",
            12,
            9,
            DiagnosticsSeverity.Error,
            "CS0103",
            "The name 'Missing' does not exist in the current context"
        );

        var result = new DiagnosticsResult(
            [item],
            ErrorCount: 3,
            WarningCount: 1,
            Truncated: true,
            ScopeDescription: "files changed since the last check"
        );

        Assert.Single(result.Items);
        Assert.Equal(3, result.ErrorCount);
        Assert.Equal(1, result.WarningCount);
        Assert.True(result.Truncated);
        Assert.Equal(DiagnosticsSeverity.Error, result.Items[0].Severity);
    }
}
