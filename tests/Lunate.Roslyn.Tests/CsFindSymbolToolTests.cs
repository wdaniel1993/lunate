using System.Text.Json;
using Lunate.Agent;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class CsFindSymbolToolTests
{
    private static readonly ToolContext Context = new("/tmp", new NoopAgentEvents());

    [Fact]
    public void Declares_the_pinned_tool_shape()
    {
        var tool = new CsFindSymbolTool(() => new FakeBackend());

        Assert.Equal("cs_find_symbol", tool.Name);
        Assert.Equal(
            "Find where a type or member with this name is defined and its signature — navigates large solutions without grepping",
            tool.Description
        );
        Assert.True(tool.Annotations!.ReadOnly);
        Assert.False(tool.Annotations.Destructive);
        Assert.Equal(ToolRisk.ReadOnly, ((ITool)tool).Risk);
        var schema = tool.ParametersSchema.GetRawText();
        Assert.Contains("\"name\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"required\"", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructing_the_tool_does_not_resolve_the_backend()
    {
        var tool = new CsFindSymbolTool(() =>
            throw new InvalidOperationException("must not be called")
        );

        Assert.NotNull(tool.Name);
    }

    [Fact]
    public async Task A_missing_name_is_an_instructing_error()
    {
        var backend = new FakeBackend();
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(Args("{}"), Context, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("name", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, backend.LoadCalls);
    }

    [Fact]
    public async Task An_empty_name_is_an_instructing_error()
    {
        var backend = new FakeBackend();
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"   "}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("name", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, backend.LoadCalls);
    }

    [Fact]
    public async Task The_backend_factory_runs_once_and_is_reused()
    {
        var calls = 0;
        var backend = new FakeBackend();
        var tool = new CsFindSymbolTool(() =>
        {
            calls++;
            return backend;
        });

        await tool.ExecuteAsync(Args("""{"name":"Calculator"}"""), Context, CancellationToken.None);
        await tool.ExecuteAsync(Args("""{"name":"Widget"}"""), Context, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(2, backend.LoadCalls);
        Assert.Equal(2, backend.SearchCalls);
    }

    [Fact]
    public async Task The_name_is_passed_through_trimmed()
    {
        var backend = new FakeBackend();
        var tool = new CsFindSymbolTool(() => backend);

        await tool.ExecuteAsync(
            Args("""{"name":"  Calculator  "}"""),
            Context,
            CancellationToken.None
        );

        Assert.Equal("Calculator", backend.LastQuery);
    }

    [Fact]
    public async Task A_found_symbol_is_rendered_with_location_and_signature()
    {
        var search = new SymbolSearchResult(
            SymbolSearchStatus.Loaded,
            "found 1 definition for 'Calculator'",
            [
                new SymbolMatch(
                    "class",
                    "Calculator",
                    "ConsoleApp",
                    "src/ConsoleApp/Calculator.cs",
                    3,
                    21,
                    "Calculator",
                    false
                ),
            ],
            1
        );
        var backend = new FakeBackend { Search = search };
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Calculator"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains(
            "src/ConsoleApp/Calculator.cs:3 — Calculator",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Same(search, result.Details);
    }

    [Fact]
    public async Task A_metadata_match_is_rendered_without_a_file()
    {
        var backend = new FakeBackend
        {
            Search = new SymbolSearchResult(
                SymbolSearchStatus.Loaded,
                "found 1 definition for 'String'; 1 metadata-only (no source file)",
                [new SymbolMatch("class", "String", "System", null, 0, 0, "string", true)],
                1
            ),
        };
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"String"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains(
            "(metadata) System.String — string",
            result.Output,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Only_the_first_ten_matches_are_inline()
    {
        var matches = Enumerable
            .Range(0, 12)
            .Select(index => new SymbolMatch(
                "class",
                $"Type{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                "App",
                $"src/File{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}.cs",
                index + 1,
                1,
                $"Type{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                false
            ))
            .ToList();
        var backend = new FakeBackend
        {
            Search = new SymbolSearchResult(
                SymbolSearchStatus.Loaded,
                "found 12 definitions for 'Type'",
                matches,
                12
            ),
        };
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Type"}"""),
            Context,
            CancellationToken.None
        );

        Assert.Contains("src/File0.cs:1 — Type0", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/File9.cs:10 — Type9", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("src/File10.cs", result.Output, StringComparison.Ordinal);
        Assert.Contains("2 more", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_truncated_result_says_so()
    {
        var backend = new FakeBackend
        {
            Search = new SymbolSearchResult(
                SymbolSearchStatus.Loaded,
                "found 60 definitions for 'Many'; showing the first 50",
                [new SymbolMatch("class", "Many", "App", "src/Many.cs", 1, 1, "Many", false)],
                60
            )
            {
                Truncated = true,
            },
        };
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Many"}"""),
            Context,
            CancellationToken.None
        );

        Assert.Contains("truncated at 50", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_name_is_not_an_error()
    {
        var backend = new FakeBackend
        {
            Search = new SymbolSearchResult(
                SymbolSearchStatus.Loaded,
                "no definition found for 'Zzz'; check the spelling or search for the simple name",
                [],
                0
            ),
        };
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Zzz"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("no definition found", result.Output, StringComparison.Ordinal);
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
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Calculator"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("install the .NET SDK", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, backend.SearchCalls);
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
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Calculator"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("dotnet restore", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, backend.SearchCalls);
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
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Calculator"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("no solution found", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, backend.SearchCalls);
    }

    [Fact]
    public async Task A_partial_load_is_noted_and_still_searches()
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
            Search = new SymbolSearchResult(
                SymbolSearchStatus.Partial,
                "found 1 definition for 'Calculator'",
                [
                    new SymbolMatch(
                        "class",
                        "Calculator",
                        "ConsoleApp",
                        "src/ConsoleApp/Calculator.cs",
                        3,
                        21,
                        "Calculator",
                        false
                    ),
                ],
                1
            ),
        };
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Calculator"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("Partial load", result.Output, StringComparison.Ordinal);
        Assert.Contains("boom", result.Output, StringComparison.Ordinal);
        Assert.Contains("Calculator.cs:3", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_failures_are_surfaced()
    {
        var backend = new FakeBackend
        {
            Search = new SymbolSearchResult(
                SymbolSearchStatus.Partial,
                "found 1 definition for 'Calculator'",
                [],
                1
            )
            {
                Failures = [new WorkspaceFailure(null, "deleted file src/Gone.cs")],
                TotalFailureCount = 1,
            },
        };
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Calculator"}"""),
            Context,
            CancellationToken.None
        );

        Assert.Contains("deleted file src/Gone.cs", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_search_failure_is_an_error_result_not_an_exception()
    {
        var backend = new FakeBackend { SearchThrows = true };
        var tool = new CsFindSymbolTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Calculator"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("Symbol search failed", result.Output, StringComparison.Ordinal);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class FakeBackend : ICSharpBackend
    {
        public WorkspaceLoadResult LoadResult { get; set; } =
            new(WorkspaceStatus.Loaded, "loaded 1 projects", "/tmp/App.slnx", 1, 1, []);

        public SymbolSearchResult Search { get; set; } =
            new(SymbolSearchStatus.Loaded, "found 1 definition", [], 1);

        public bool SearchThrows { get; set; }

        public int LoadCalls { get; private set; }

        public int SearchCalls { get; private set; }

        public string? LastQuery { get; private set; }

        public ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct)
        {
            LoadCalls++;
            return ValueTask.FromResult(LoadResult);
        }

        public ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
            DiagnosticsScope scope,
            CancellationToken ct
        ) => throw new InvalidOperationException("diagnostics are not used by this tool");

        public ValueTask<SymbolSearchResult> FindSymbolAsync(string name, CancellationToken ct)
        {
            SearchCalls++;
            LastQuery = name;
            if (SearchThrows)
            {
                throw new InvalidOperationException("boom");
            }

            return ValueTask.FromResult(Search);
        }

        public void NotifyFileChanged(string absolutePath) { }
    }
}
