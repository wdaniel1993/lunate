using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

[Collection(RoslynIntegrationCollection.Name)]
public sealed class CsFindSymbolGateTests(ITestOutputHelper output)
{
    [Fact]
    public async Task A_type_is_found_with_file_position_and_signature()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindSymbolAsync("Calculator", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.Loaded, result.Status);
        var match = Assert.Single(result.Matches, candidate => !candidate.FromMetadata);
        Assert.Equal(
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
            match
        );
    }

    [Fact]
    public async Task Overloads_return_one_match_per_signature()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindSymbolAsync("Add", CancellationToken.None);

        var source = result.Matches.Where(candidate => !candidate.FromMetadata).ToList();
        Assert.Equal(2, source.Count);
        Assert.Equal(
            new SymbolMatch(
                "method",
                "Add",
                "ConsoleApp.Calculator",
                "src/ConsoleApp/Calculator.cs",
                5,
                23,
                "int Calculator.Add(int left, int right)",
                false
            ),
            source[0]
        );
        Assert.Equal(
            new SymbolMatch(
                "method",
                "Add",
                "ConsoleApp.Calculator",
                "src/ConsoleApp/Calculator.cs",
                7,
                23,
                "int Calculator.Add(int left, int right, int third)",
                false
            ),
            source[1]
        );
    }

    [Fact]
    public async Task A_partial_type_lists_every_declaration()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindSymbolAsync("Widget", CancellationToken.None);

        var source = result.Matches.Where(candidate => !candidate.FromMetadata).ToList();
        Assert.Equal(2, source.Count);
        Assert.Equal(
            new SymbolMatch(
                "class",
                "Widget",
                "ConsoleApp",
                "src/ConsoleApp/Widget.Extra.cs",
                3,
                22,
                "Widget",
                false
            ),
            source[0]
        );
        Assert.Equal(
            new SymbolMatch(
                "class",
                "Widget",
                "ConsoleApp",
                "src/ConsoleApp/Widget.cs",
                3,
                22,
                "Widget",
                false
            ),
            source[1]
        );
    }

    [Fact]
    public async Task A_nested_type_is_found_by_its_dotted_path()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindSymbolAsync(
            "ConsoleApp.Outer.Inner",
            CancellationToken.None
        );

        var match = Assert.Single(result.Matches, candidate => candidate.Name == "Inner");
        Assert.Equal(
            new SymbolMatch(
                "class",
                "Inner",
                "ConsoleApp.Outer",
                "src/ConsoleApp/Outer.cs",
                5,
                25,
                "Inner",
                false
            ),
            match
        );
    }

    [Fact]
    public async Task A_namespace_is_found_by_its_dotted_path()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindSymbolAsync("ConsoleApp.Tools", CancellationToken.None);

        var match = Assert.Single(result.Matches, candidate => candidate.Kind == "namespace");
        Assert.Equal(
            new SymbolMatch(
                "namespace",
                "Tools",
                "ConsoleApp",
                "src/ConsoleApp/Tools/Helper.cs",
                1,
                11,
                "Tools",
                false
            ),
            match
        );
    }

    [Fact]
    public async Task An_unknown_name_is_empty_with_a_hint()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindSymbolAsync("NoSuchSymbolZzz", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.Loaded, result.Status);
        Assert.Empty(result.Matches);
        Assert.Equal(0, result.TotalMatchCount);
        Assert.Contains("no definition", result.Message, StringComparison.Ordinal);
        Assert.Contains("NoSuchSymbolZzz", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_case_mismatch_is_empty_with_a_casing_hint()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindSymbolAsync("calculator", CancellationToken.None);

        Assert.Empty(result.Matches);
        Assert.Contains("case-sensitive", result.Message, StringComparison.Ordinal);
        Assert.Contains("Calculator", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_metadata_type_is_listed_without_a_source_location()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindSymbolAsync("String", CancellationToken.None);

        var match = Assert.Single(result.Matches, candidate => candidate.Name == "String");
        Assert.True(match.FromMetadata);
        Assert.Equal("class", match.Kind);
        Assert.Equal("System", match.Container);
        Assert.Null(match.File);
        Assert.Equal(0, match.Line);
        Assert.Equal(0, match.Column);
        Assert.Equal("string", match.Signature);
        Assert.Contains("metadata-only", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Results_are_stable_across_runs()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var first = await backend.FindSymbolAsync("Widget", CancellationToken.None);
        var second = await backend.FindSymbolAsync("Widget", CancellationToken.None);

        Assert.Equal(first.Matches, second.Matches);
        Assert.Equal(first.TotalMatchCount, second.TotalMatchCount);
    }

    [Fact]
    public async Task An_empty_name_asks_for_a_name()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindSymbolAsync("   ", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.Loaded, result.Status);
        Assert.Empty(result.Matches);
        Assert.Contains("symbol name", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_solution_is_reported_instead_of_throwing()
    {
        using var backend = new RoslynBackend(
            Path.Combine(Path.GetTempPath(), "lunate-roslyn-tests", Guid.NewGuid().ToString("N"))
        );
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.FindSymbolAsync("Calculator", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.NoSolution, result.Status);
        Assert.Empty(result.Matches);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task A_partial_load_still_finds_symbols()
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
        var load = await backend.LoadAsync(CancellationToken.None);
        Assert.Equal(WorkspaceStatus.Partial, load.Status);

        var result = await backend.FindSymbolAsync("Calculator", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.Partial, result.Status);
        Assert.Contains(
            result.Matches,
            candidate =>
                candidate.Name == "Calculator" && candidate.File == "src/ConsoleApp/Calculator.cs"
        );
    }

    [Fact]
    public async Task The_signature_format_is_pinned()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var calculator = await backend.FindSymbolAsync("Calculator", CancellationToken.None);
        var count = await backend.FindSymbolAsync(
            "ConsoleApp.Widget.Count",
            CancellationToken.None
        );
        var inner = await backend.FindSymbolAsync("ConsoleApp.Outer.Inner", CancellationToken.None);
        var helper = await backend.FindSymbolAsync(
            "ConsoleApp.Tools.Helper",
            CancellationToken.None
        );

        Assert.Equal(
            "Calculator",
            Assert.Single(calculator.Matches, candidate => !candidate.FromMetadata).Signature
        );
        Assert.Equal("int Widget.Count", Assert.Single(count.Matches).Signature);
        Assert.Equal(
            "Inner",
            Assert.Single(inner.Matches, candidate => !candidate.FromMetadata).Signature
        );
        Assert.Equal(
            "Helper",
            Assert.Single(helper.Matches, candidate => !candidate.FromMetadata).Signature
        );
    }

    [Fact]
    public async Task A_file_edited_since_load_is_searched_as_changed()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var program = Path.Combine(fixture.Root, "src", "ConsoleApp", "Program.cs");
        File.AppendAllText(program, "\npublic static class FreshlyAdded { }\n");
        backend.NotifyFileChanged(program);

        var result = await backend.FindSymbolAsync("FreshlyAdded", CancellationToken.None);

        var match = Assert.Single(result.Matches, candidate => !candidate.FromMetadata);
        Assert.Equal("src/ConsoleApp/Program.cs", match.File);
        Assert.Equal("class", match.Kind);
        Assert.Equal(5, match.Line);
    }

    [Fact]
    public async Task The_tool_finds_a_symbol_on_the_fixture()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        var tool = new CsFindSymbolTool(() => new RoslynBackend(fixture.Root));

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"name":"Calculator"}""").RootElement.Clone(),
            new ToolContext(fixture.Root, new NoopAgentEvents()),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains(
            "src/ConsoleApp/Calculator.cs:3 — Calculator",
            result.Output,
            StringComparison.Ordinal
        );
        var search = Assert.IsType<SymbolSearchResult>(result.Details);
        Assert.Equal(SymbolSearchStatus.Loaded, search.Status);
    }

    [Fact]
    public async Task A_warm_search_is_fast()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);
        await backend.FindSymbolAsync("Widget", CancellationToken.None);

        var watch = Stopwatch.StartNew();
        var warm = await backend.FindSymbolAsync("Calculator", CancellationToken.None);
        watch.Stop();

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"warm symbol search: {watch.ElapsedMilliseconds} ms"
            )
        );

        Assert.NotEmpty(warm.Matches);
        Assert.True(watch.ElapsedMilliseconds < 5_000, "the warm search was not fast");
    }
}
