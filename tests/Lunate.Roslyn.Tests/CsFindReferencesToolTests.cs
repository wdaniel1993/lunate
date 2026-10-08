using System.Text.Json;
using Lunate.Agent;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class CsFindReferencesToolTests
{
    private static readonly ToolContext Context = new("/tmp", new NoopAgentEvents());

    [Fact]
    public void Declares_the_pinned_tool_shape()
    {
        var tool = new CsFindReferencesTool(() => new FakeCSharpBackend());

        Assert.Equal("cs_find_references", tool.Name);
        Assert.Equal(
            "Find every source reference to a type or member with this name — checks the impact of API changes without grepping",
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
        var tool = new CsFindReferencesTool(() =>
            throw new InvalidOperationException("must not be called")
        );

        Assert.NotNull(tool.Name);
    }

    [Fact]
    public async Task An_empty_name_asks_for_a_name()
    {
        var backend = new FakeCSharpBackend
        {
            References = new ReferencesResult(
                SymbolSearchStatus.Loaded,
                "provide a symbol name to search for",
                null,
                [],
                0
            ),
        };
        var tool = new CsFindReferencesTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"   "}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("symbol name", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, backend.ReferencesCalls);
    }

    [Fact]
    public async Task The_backend_factory_runs_once_and_is_reused()
    {
        var calls = 0;
        var backend = new FakeCSharpBackend();
        var tool = new CsFindReferencesTool(() =>
        {
            calls++;
            return backend;
        });

        await tool.ExecuteAsync(Args("""{"name":"Divide"}"""), Context, CancellationToken.None);
        await tool.ExecuteAsync(Args("""{"name":"Helper"}"""), Context, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(2, backend.LoadCalls);
        Assert.Equal(2, backend.ReferencesCalls);
    }

    [Fact]
    public async Task The_name_is_passed_through_to_the_backend()
    {
        var backend = new FakeCSharpBackend();
        var tool = new CsFindReferencesTool(() => backend);

        await tool.ExecuteAsync(Args("""{"name":"  Divide  "}"""), Context, CancellationToken.None);

        Assert.Equal("  Divide  ", backend.LastName);
    }

    [Fact]
    public async Task A_resolved_symbol_renders_its_definition_and_usages()
    {
        var references = new ReferencesResult(
            SymbolSearchStatus.Loaded,
            "found 2 references to 'Divide'",
            new SymbolMatch(
                "method",
                "Divide",
                "CalculatorLib.Calculator",
                "src/CalculatorLib/Calculator.cs",
                9,
                23,
                "int Calculator.Divide(int left, int right)",
                false
            ),
            [
                new ReferenceLocation("src/CalculatorLib/Usage.cs", 5, 43),
                new ReferenceLocation("tests/CalculatorLib.Tests/Program.cs", 4, 48),
            ],
            2
        );
        var backend = new FakeCSharpBackend { References = references };
        var tool = new CsFindReferencesTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Divide"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains(
            "definition: src/CalculatorLib/Calculator.cs:9 — int Calculator.Divide(int left, int right)",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "tests/CalculatorLib.Tests/Program.cs:4:48",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Same(references, result.Details);
    }

    [Fact]
    public async Task Only_the_first_ten_usages_are_inline()
    {
        var usages = Enumerable
            .Range(0, 12)
            .Select(index => new ReferenceLocation($"src/File{index}.cs", index + 1, 1))
            .ToList();
        var backend = new FakeCSharpBackend
        {
            References = new ReferencesResult(
                SymbolSearchStatus.Loaded,
                "found 12 references to 'Thing'",
                new SymbolMatch("class", "Thing", "App", "src/Thing.cs", 1, 1, "Thing", false),
                usages,
                12
            ),
        };
        var tool = new CsFindReferencesTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Thing"}"""),
            Context,
            CancellationToken.None
        );

        Assert.Contains("src/File9.cs:10:1", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("src/File10.cs", result.Output, StringComparison.Ordinal);
        Assert.Contains("2 more", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_truncated_result_says_so()
    {
        var backend = new FakeCSharpBackend
        {
            References = new ReferencesResult(
                SymbolSearchStatus.Loaded,
                "found 250 references to 'Thing'",
                new SymbolMatch("class", "Thing", "App", "src/Thing.cs", 1, 1, "Thing", false),
                [new ReferenceLocation("src/A.cs", 1, 1)],
                250
            )
            {
                Truncated = true,
            },
        };
        var tool = new CsFindReferencesTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Thing"}"""),
            Context,
            CancellationToken.None
        );

        Assert.Contains("truncated at 200 references", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_ambiguous_name_renders_candidates_and_the_hint()
    {
        var backend = new FakeCSharpBackend
        {
            References = new ReferencesResult(
                SymbolSearchStatus.Loaded,
                "'Value' matches 2 symbols; references need a unique symbol — use a dotted path to disambiguate",
                null,
                [],
                0
            )
            {
                Candidates =
                [
                    new SymbolMatch(
                        "property",
                        "Value",
                        "App.Outer.Inner",
                        "src/Outer.cs",
                        7,
                        27,
                        "int Inner.Value",
                        false
                    ),
                    new SymbolMatch(
                        "property",
                        "Value",
                        "App.Usage",
                        "src/Usage.cs",
                        5,
                        23,
                        "int Usage.Value",
                        false
                    ),
                ],
            },
        };
        var tool = new CsFindReferencesTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Value"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("dotted path", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/Outer.cs:7", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_metadata_symbol_is_rendered_without_a_file()
    {
        var backend = new FakeCSharpBackend
        {
            References = new ReferencesResult(
                SymbolSearchStatus.Loaded,
                "metadata symbol — no source references",
                new SymbolMatch("class", "String", "System", null, 0, 0, "string", true),
                [],
                0
            ),
        };
        var tool = new CsFindReferencesTool(() => backend);

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
    public async Task No_sdk_is_an_actionable_error_result()
    {
        var backend = new FakeCSharpBackend
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
        var tool = new CsFindReferencesTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Divide"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("install the .NET SDK", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, backend.ReferencesCalls);
    }

    [Fact]
    public async Task Restore_required_is_an_actionable_error_result()
    {
        var backend = new FakeCSharpBackend
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
        var tool = new CsFindReferencesTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Divide"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("dotnet restore", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, backend.ReferencesCalls);
    }

    [Fact]
    public async Task A_partial_load_is_noted_and_still_searches()
    {
        var backend = new FakeCSharpBackend
        {
            LoadResult = new WorkspaceLoadResult(
                WorkspaceStatus.Partial,
                "loaded 1 projects; 2 workspace failures (first: boom)",
                "/tmp/App.slnx",
                1,
                2,
                [new WorkspaceFailure(null, "boom")]
            ),
            References = new ReferencesResult(
                SymbolSearchStatus.Partial,
                "found 1 references to 'Divide'",
                new SymbolMatch(
                    "method",
                    "Divide",
                    "CalculatorLib.Calculator",
                    "src/CalculatorLib/Calculator.cs",
                    9,
                    23,
                    "int Calculator.Divide(int left, int right)",
                    false
                ),
                [new ReferenceLocation("src/CalculatorLib/Usage.cs", 5, 43)],
                1
            ),
        };
        var tool = new CsFindReferencesTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Divide"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("Partial load", result.Output, StringComparison.Ordinal);
        Assert.Contains("boom", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/CalculatorLib/Usage.cs:5:43", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_failures_are_surfaced()
    {
        var backend = new FakeCSharpBackend
        {
            References = new ReferencesResult(
                SymbolSearchStatus.Partial,
                "found 1 references to 'Divide'",
                null,
                [],
                0
            )
            {
                Failures = [new WorkspaceFailure(null, "deleted file src/Gone.cs")],
                TotalFailureCount = 1,
            },
        };
        var tool = new CsFindReferencesTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Divide"}"""),
            Context,
            CancellationToken.None
        );

        Assert.Contains("deleted file src/Gone.cs", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_search_failure_is_an_error_result_not_an_exception()
    {
        var backend = new FakeCSharpBackend { ReferencesThrow = true };
        var tool = new CsFindReferencesTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Divide"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("Reference search failed", result.Output, StringComparison.Ordinal);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
