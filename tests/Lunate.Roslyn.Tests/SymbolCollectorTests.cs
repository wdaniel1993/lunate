using Lunate.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lunate.Roslyn.Tests;

public sealed class SymbolCollectorTests
{
    private static readonly MetadataReference CoreLibrary = MetadataReference.CreateFromFile(
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .First(path => Path.GetFileName(path) == "System.Private.CoreLib.dll")
    );

    [Fact]
    public void The_cap_keeps_fifty_matches_and_counts_the_rest()
    {
        var source = string.Join(
            '\n',
            Enumerable.Range(1, 60).Select(_ => "public partial class Many { }")
        );
        var compilation = Compile(("/src/Many.cs", source));

        var collected = SymbolCollector.Collect(
            [compilation],
            "Many",
            "/src",
            CancellationToken.None
        );

        Assert.Equal(50, collected.Matches.Count);
        Assert.Equal(60, collected.TotalMatchCount);
        Assert.True(collected.Truncated);
    }

    [Fact]
    public void Metadata_matches_are_deduplicated_across_compilations()
    {
        var first = Compile(("/first/Empty.cs", "namespace First { }"));
        var second = Compile(("/second/Empty.cs", "namespace Second { }"));

        var collected = SymbolCollector.Collect(
            [first, second],
            "String",
            "/src",
            CancellationToken.None
        );

        var match = Assert.Single(collected.Matches);
        Assert.True(match.FromMetadata);
        Assert.Equal("String", match.Name);
        Assert.Equal(1, collected.TotalMatchCount);
        Assert.Equal(1, collected.MetadataMatchCount);
    }

    [Fact]
    public void Deduplicated_metadata_matches_do_not_depend_on_project_order()
    {
        var first = Compile(("/first/Empty.cs", "namespace First { }"));
        var second = Compile(("/second/Empty.cs", "namespace Second { }"));

        var forward = SymbolCollector.Collect(
            [first, second],
            "String",
            "/src",
            CancellationToken.None
        );
        var reverse = SymbolCollector.Collect(
            [second, first],
            "String",
            "/src",
            CancellationToken.None
        );

        Assert.Equal(forward.Matches, reverse.Matches);
        Assert.Equal(forward.TotalMatchCount, reverse.TotalMatchCount);
    }

    [Fact]
    public void Metadata_count_covers_matches_dropped_by_the_cap()
    {
        var source = string.Join(
            '\n',
            Enumerable.Range(1, 60).Select(_ => "public partial class Object { }")
        );
        var compilation = Compile(("/src/Object.cs", source));

        var collected = SymbolCollector.Collect(
            [compilation],
            "Object",
            "/src",
            CancellationToken.None
        );

        Assert.Equal(50, collected.Matches.Count);
        Assert.Equal(61, collected.TotalMatchCount);
        Assert.True(collected.Truncated);
        Assert.Equal(1, collected.MetadataMatchCount);
    }

    [Fact]
    public void A_dotted_query_finds_a_nested_metadata_type()
    {
        var compilation = Compile(("/src/Empty.cs", "namespace Source { }"));

        var collected = SymbolCollector.Collect(
            [compilation],
            "Environment.SpecialFolder",
            "/src",
            CancellationToken.None
        );

        var match = Assert.Single(collected.Matches);
        Assert.True(match.FromMetadata);
        Assert.Equal("enum", match.Kind);
        Assert.Equal("SpecialFolder", match.Name);
        Assert.Equal("System.Environment", match.Container);
        Assert.Null(match.File);
        Assert.Equal(0, match.Line);
        Assert.Equal(0, match.Column);
    }

    [Fact]
    public void A_full_dotted_path_finds_a_nested_metadata_type()
    {
        var compilation = Compile(("/src/Empty.cs", "namespace Source { }"));

        var collected = SymbolCollector.Collect(
            [compilation],
            "System.Environment.SpecialFolder",
            "/src",
            CancellationToken.None
        );

        var match = Assert.Single(collected.Matches);
        Assert.True(match.FromMetadata);
        Assert.Equal("enum", match.Kind);
        Assert.Equal("System.Environment", match.Container);
    }

    [Fact]
    public void A_simple_name_still_does_not_find_nested_metadata_types()
    {
        var compilation = Compile(("/src/Empty.cs", "namespace Source { }"));

        var collected = SymbolCollector.Collect(
            [compilation],
            "SpecialFolder",
            "/src",
            CancellationToken.None
        );

        Assert.Empty(collected.Matches);
    }

    [Fact]
    public void A_dotted_query_keeps_only_the_matching_container()
    {
        var compilation = Compile(
            ("/src/A.cs", "namespace A { public class Item { } }"),
            ("/src/B.cs", "namespace B { public class Item { } }")
        );

        var collected = SymbolCollector.Collect(
            [compilation],
            "A.Item",
            "/src",
            CancellationToken.None
        );

        var match = Assert.Single(collected.Matches);
        Assert.Equal("A", match.Container);
        Assert.Equal("A.cs", match.File);
    }

    [Fact]
    public void Metadata_types_are_marked_and_have_no_source_location()
    {
        var compilation = Compile(("/src/Empty.cs", "namespace Source { }"));

        var collected = SymbolCollector.Collect(
            [compilation],
            "String",
            "/src",
            CancellationToken.None
        );

        var match = Assert.Single(collected.Matches, candidate => candidate.Name == "String");
        Assert.True(match.FromMetadata);
        Assert.Null(match.File);
        Assert.Equal(0, match.Line);
        Assert.Equal(0, match.Column);
        Assert.Equal("class", match.Kind);
        Assert.Equal("System", match.Container);
    }

    [Fact]
    public void A_case_mismatch_reports_the_correctly_cased_candidate()
    {
        var compilation = Compile(("/src/Thing.cs", "namespace App { public class Thing { } }"));

        var collected = SymbolCollector.Collect(
            [compilation],
            "app.thing",
            "/src",
            CancellationToken.None
        );

        Assert.Empty(collected.Matches);
        Assert.Equal("App.Thing", collected.CaseInsensitiveCandidate);
    }

    private static CSharpCompilation Compile(params (string Path, string Text)[] files)
    {
        var trees = files
            .Select(file => CSharpSyntaxTree.ParseText(file.Text, path: file.Path))
            .ToArray();
        return CSharpCompilation.Create("collector-tests", trees, [CoreLibrary]);
    }
}
