using System.Diagnostics;
using System.Globalization;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

[Collection(RoslynIntegrationCollection.Name)]
public sealed class CsReferencesGateTests(ITestOutputHelper output)
{
    [Fact]
    public async Task References_across_projects_exclude_the_declaration()
    {
        using var fixture = Fixtures.CopySolution("lib-with-tests");
        Fixtures.Restore(fixture, "LibWithTests.sln");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var first = await backend.FindReferencesAsync("Divide", CancellationToken.None);
        var second = await backend.FindReferencesAsync("Divide", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.Loaded, first.Status);
        Assert.Equal(
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
            first.Resolved
        );
        Assert.Equal(
            [
                new ReferenceLocation("src/CalculatorLib/Usage.cs", 5, 43),
                new ReferenceLocation("src/CalculatorLib/Usage.cs", 7, 53),
                new ReferenceLocation("tests/CalculatorLib.Tests/Program.cs", 4, 48),
            ],
            first.References
        );
        Assert.Equal(3, first.TotalReferenceCount);
        Assert.False(first.Truncated);
        Assert.DoesNotContain(
            first.References,
            reference => reference.File == "src/CalculatorLib/Calculator.cs" && reference.Line == 9
        );

        Assert.Equal(first.Resolved, second.Resolved);
        Assert.Equal(first.References, second.References);
        Assert.Equal(first.TotalReferenceCount, second.TotalReferenceCount);
    }

    [Fact]
    public async Task An_ambiguous_simple_name_lists_candidates_instead_of_guessing()
    {
        using var fixture = Fixtures.CopySolution("lib-with-tests");
        Fixtures.Restore(fixture, "LibWithTests.sln");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindReferencesAsync("Value", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.Loaded, result.Status);
        Assert.Null(result.Resolved);
        Assert.Empty(result.References);
        Assert.Equal(0, result.TotalReferenceCount);
        Assert.Contains("dotted path", result.Message, StringComparison.Ordinal);
        Assert.Equal(
            [
                new SymbolMatch(
                    "property",
                    "Value",
                    "CalculatorLib.Outer.Inner",
                    "src/CalculatorLib/Outer.cs",
                    7,
                    27,
                    "int Inner.Value",
                    false
                ),
                new SymbolMatch(
                    "property",
                    "Value",
                    "CalculatorLib.Usage",
                    "src/CalculatorLib/Usage.cs",
                    5,
                    23,
                    "int Usage.Value",
                    false
                ),
            ],
            result.Candidates
        );
    }

    [Fact]
    public async Task A_dotted_path_disambiguates_a_name()
    {
        using var fixture = Fixtures.CopySolution("lib-with-tests");
        Fixtures.Restore(fixture, "LibWithTests.sln");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindReferencesAsync(
            "CalculatorLib.Outer.Inner.Value",
            CancellationToken.None
        );

        Assert.Equal(SymbolSearchStatus.Loaded, result.Status);
        Assert.NotNull(result.Resolved);
        Assert.Equal("CalculatorLib.Outer.Inner", result.Resolved.Container);
        Assert.Empty(result.References);
        Assert.Equal(0, result.TotalReferenceCount);
        Assert.Contains("no references", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_symbol_without_usages_is_empty_with_a_note()
    {
        using var fixture = Fixtures.CopySolution("lib-with-tests");
        Fixtures.Restore(fixture, "LibWithTests.sln");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindReferencesAsync("Helper", CancellationToken.None);

        Assert.NotNull(result.Resolved);
        Assert.Equal("CalculatorLib.Tools", result.Resolved.Container);
        Assert.Empty(result.References);
        Assert.Equal(0, result.TotalReferenceCount);
        Assert.Contains("no references", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_metadata_symbol_has_a_message_not_an_error()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindReferencesAsync("System.String", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.Loaded, result.Status);
        Assert.NotNull(result.Resolved);
        Assert.True(result.Resolved.FromMetadata);
        Assert.Empty(result.References);
        Assert.Equal(0, result.TotalReferenceCount);
        Assert.Contains("metadata symbol", result.Message, StringComparison.Ordinal);
        Assert.Contains("no source references", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_name_is_empty_with_a_hint()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindReferencesAsync("NoSuchSymbolZzz", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.Loaded, result.Status);
        Assert.Null(result.Resolved);
        Assert.Empty(result.References);
        Assert.Contains("no definition", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_solution_is_reported_instead_of_throwing()
    {
        using var backend = new RoslynBackend(
            Path.Combine(Path.GetTempPath(), "lunate-roslyn-tests", Guid.NewGuid().ToString("N"))
        );
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindReferencesAsync("Calculator", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.NoSolution, result.Status);
        Assert.Empty(result.References);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task The_tool_finds_references_on_the_fixture()
    {
        using var fixture = Fixtures.CopySolution("lib-with-tests");
        Fixtures.Restore(fixture, "LibWithTests.sln");
        var tool = new CsFindReferencesTool(() => new RoslynBackend(fixture.Root));

        var result = await tool.ExecuteAsync(
            System.Text.Json.JsonDocument.Parse("""{"name":"Divide"}""").RootElement.Clone(),
            new Lunate.Agent.ToolContext(fixture.Root, new NoopAgentEvents()),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains(
            "definition: src/CalculatorLib/Calculator.cs:9",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "tests/CalculatorLib.Tests/Program.cs:4:48",
            result.Output,
            StringComparison.Ordinal
        );
        var references = Assert.IsType<ReferencesResult>(result.Details);
        Assert.Equal(3, references.TotalReferenceCount);
    }

    [Fact]
    public async Task A_warm_reference_search_is_fast()
    {
        using var fixture = Fixtures.CopySolution("lib-with-tests");
        Fixtures.Restore(fixture, "LibWithTests.sln");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);
        await backend.FindReferencesAsync("Divide", CancellationToken.None);

        var watch = Stopwatch.StartNew();
        var warm = await backend.FindReferencesAsync("Helper", CancellationToken.None);
        watch.Stop();

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"warm reference search: {watch.ElapsedMilliseconds} ms"
            )
        );

        Assert.NotNull(warm.Resolved);
        Assert.True(watch.ElapsedMilliseconds < 5_000, "the warm reference search was not fast");
    }
}
