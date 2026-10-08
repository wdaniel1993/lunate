using System.Text.Json;
using Lunate.Agent;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class CsDiagnosticsToolTests
{
    private static readonly ToolContext Context = new("/tmp", new NoopAgentEvents());

    [Fact]
    public void Declares_the_pinned_tool_shape()
    {
        var tool = new CsDiagnosticsTool(() => new FakeBackend());

        Assert.Equal("cs_diagnostics", tool.Name);
        Assert.Equal(
            "Compiler errors and warnings for files changed since the last check (or the whole solution) — fast compile check after edits without a full dotnet build",
            tool.Description
        );
        Assert.True(tool.Annotations!.ReadOnly);
        Assert.False(tool.Annotations.Destructive);
        Assert.Equal(ToolRisk.ReadOnly, ((ITool)tool).Risk);
        Assert.Contains(
            "\"changed\"",
            tool.ParametersSchema.GetRawText(),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "\"solution\"",
            tool.ParametersSchema.GetRawText(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task The_default_scope_is_changed_files()
    {
        var backend = new FakeBackend();
        var tool = new CsDiagnosticsTool(() => backend);

        var result = await tool.ExecuteAsync(Args("{}"), Context, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(DiagnosticsScope.ChangedFiles, backend.LastScope);
    }

    [Fact]
    public async Task An_explicit_solution_scope_is_passed_through()
    {
        var backend = new FakeBackend();
        var tool = new CsDiagnosticsTool(() => backend);

        await tool.ExecuteAsync(Args("""{"scope":"solution"}"""), Context, CancellationToken.None);

        Assert.Equal(DiagnosticsScope.Solution, backend.LastScope);
    }

    [Fact]
    public async Task An_invalid_scope_is_an_instructing_error()
    {
        var backend = new FakeBackend();
        var tool = new CsDiagnosticsTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"scope":"banana"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("changed", result.Output, StringComparison.Ordinal);
        Assert.Contains("solution", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, backend.LoadCalls);
    }

    [Fact]
    public void Constructing_the_tool_does_not_resolve_the_backend()
    {
        var tool = new CsDiagnosticsTool(() =>
            throw new InvalidOperationException("must not be called")
        );

        Assert.NotNull(tool.Name);
    }

    [Fact]
    public async Task The_backend_factory_runs_once_and_is_reused()
    {
        var calls = 0;
        var backend = new FakeBackend();
        var tool = new CsDiagnosticsTool(() =>
        {
            calls++;
            return backend;
        });

        await tool.ExecuteAsync(Args("{}"), Context, CancellationToken.None);
        await tool.ExecuteAsync(Args("{}"), Context, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(2, backend.LoadCalls);
    }

    [Fact]
    public async Task No_sdk_is_an_actionable_error_result()
    {
        var backend = new FakeBackend
        {
            LoadResult = new WorkspaceLoadResult(
                WorkspaceStatus.NoSdk,
                "no .NET SDK could be located; install the .NET SDK",
                null,
                0,
                0,
                []
            ),
        };
        var tool = new CsDiagnosticsTool(() => backend);

        var result = await tool.ExecuteAsync(Args("{}"), Context, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("install the .NET SDK", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, backend.DiagnosticsCalls);
    }

    [Fact]
    public async Task Restore_required_is_an_actionable_error_result()
    {
        var backend = new FakeBackend
        {
            LoadResult = new WorkspaceLoadResult(
                WorkspaceStatus.RestoreRequired,
                "restore required: 1 project needs `dotnet restore` (in /tmp/app)",
                "/tmp/app/App.slnx",
                1,
                0,
                []
            ),
        };
        var tool = new CsDiagnosticsTool(() => backend);

        var result = await tool.ExecuteAsync(Args("{}"), Context, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("dotnet restore", result.Output, StringComparison.Ordinal);
        Assert.Contains("/tmp/app", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_solution_is_an_actionable_error_result()
    {
        var backend = new FakeBackend
        {
            LoadResult = new WorkspaceLoadResult(
                WorkspaceStatus.NoSolution,
                "no solution found: no .sln or .slnx under '/tmp/work'",
                null,
                0,
                0,
                []
            ),
        };
        var tool = new CsDiagnosticsTool(() => backend);

        var result = await tool.ExecuteAsync(Args("{}"), Context, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("no solution found", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clean_run_says_so()
    {
        var backend = new FakeBackend
        {
            Diagnostics = new DiagnosticsResult(
                [],
                0,
                0,
                false,
                "1 file(s) changed since the last check"
            ),
        };
        var tool = new CsDiagnosticsTool(() => backend);

        var result = await tool.ExecuteAsync(Args("{}"), Context, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("No compiler errors or warnings", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Counts_and_the_first_ten_items_are_inline()
    {
        var items = Enumerable
            .Range(0, 12)
            .Select(index => new DiagnosticsItem(
                $"src/File{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}.cs",
                1,
                1,
                DiagnosticsSeverity.Error,
                "CS0103",
                $"missing name {index.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            ))
            .ToList();
        var backend = new FakeBackend
        {
            Diagnostics = new DiagnosticsResult(items, 12, 0, false, "the whole solution"),
        };
        var tool = new CsDiagnosticsTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"scope":"solution"}"""),
            Context,
            CancellationToken.None
        );

        Assert.Contains("12 errors", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/File0.cs(1,1): error CS0103", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/File9.cs(1,1): error CS0103", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("src/File10.cs(1,1)", result.Output, StringComparison.Ordinal);
        Assert.Contains("2 more", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_full_list_travels_in_details()
    {
        var items = new List<DiagnosticsItem>
        {
            new("src/App.cs", 3, 4, DiagnosticsSeverity.Warning, "CS0168", "unused variable"),
        };
        var diagnostics = new DiagnosticsResult(items, 0, 1, false, "the whole solution");
        var backend = new FakeBackend { Diagnostics = diagnostics };
        var tool = new CsDiagnosticsTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"scope":"solution"}"""),
            Context,
            CancellationToken.None
        );

        Assert.Same(diagnostics, result.Details);
        Assert.Contains("1 warning", result.Output, StringComparison.Ordinal);
        Assert.Contains("warning CS0168", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_partial_load_is_noted_and_still_reports_diagnostics()
    {
        var backend = new FakeBackend
        {
            LoadResult = new WorkspaceLoadResult(
                WorkspaceStatus.Partial,
                "loaded 1 projects; 2 workspace failures (first: boom)",
                "/tmp/App.slnx",
                1,
                2,
                [new WorkspaceFailure(null, "boom")]
            ),
            Diagnostics = new DiagnosticsResult([], 0, 0, false, "the whole solution"),
        };
        var tool = new CsDiagnosticsTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"scope":"solution"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("Partial load", result.Output, StringComparison.Ordinal);
        Assert.Contains("boom", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_failures_are_surfaced()
    {
        var backend = new FakeBackend
        {
            Diagnostics = new DiagnosticsResult(
                [],
                0,
                0,
                false,
                "1 file(s) changed since the last check"
            )
            {
                Failures = [new WorkspaceFailure(null, "deleted file src/Gone.cs")],
                TotalFailureCount = 1,
            },
        };
        var tool = new CsDiagnosticsTool(() => backend);

        var result = await tool.ExecuteAsync(Args("{}"), Context, CancellationToken.None);

        Assert.Contains("deleted file src/Gone.cs", result.Output, StringComparison.Ordinal);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class FakeBackend : ICSharpBackend
    {
        public WorkspaceLoadResult LoadResult { get; set; } =
            new(WorkspaceStatus.Loaded, "loaded 1 projects", "/tmp/App.slnx", 1, 1, []);

        public DiagnosticsResult Diagnostics { get; set; } =
            new([], 0, 0, false, "no files changed since the last check");

        public int LoadCalls { get; private set; }

        public int DiagnosticsCalls { get; private set; }

        public DiagnosticsScope? LastScope { get; private set; }

        public ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct)
        {
            LoadCalls++;
            return ValueTask.FromResult(LoadResult);
        }

        public ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
            DiagnosticsScope scope,
            CancellationToken ct
        )
        {
            DiagnosticsCalls++;
            LastScope = scope;
            return ValueTask.FromResult(Diagnostics);
        }

        public ValueTask<SymbolSearchResult> FindSymbolAsync(string name, CancellationToken ct) =>
            ValueTask.FromResult(
                new SymbolSearchResult(
                    SymbolSearchStatus.NoSolution,
                    "no solution is loaded",
                    [],
                    0
                )
            );

        public ValueTask<ReferencesResult> FindReferencesAsync(string name, CancellationToken ct) =>
            throw new InvalidOperationException("references are not used by this tool");

        public ValueTask<OutlineResult> OutlineAsync(string file, CancellationToken ct) =>
            throw new InvalidOperationException("outline is not used by this tool");

        public ValueTask<RenamePlanResult> PlanRenameAsync(
            string name,
            string newName,
            CancellationToken ct
        ) => throw new InvalidOperationException("rename is not used by this tool");

        public void NotifyFileChanged(string absolutePath) { }
    }
}
