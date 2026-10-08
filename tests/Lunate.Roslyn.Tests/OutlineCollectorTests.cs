using Lunate.Roslyn;
using Microsoft.CodeAnalysis.CSharp;

namespace Lunate.Roslyn.Tests;

public sealed class OutlineCollectorTests
{
    [Fact]
    public void The_cap_keeps_two_hundred_items_and_counts_the_rest()
    {
        var source = string.Join(
            '\n',
            Enumerable.Range(0, 250).Select(index => $"public class Type{index} {{ }}")
        );
        var tree = CSharpSyntaxTree.ParseText(
            source,
            path: "/src/Many.cs",
            cancellationToken: TestContext.Current.CancellationToken
        );

        var outlined = OutlineCollector.Walk(tree);

        Assert.Equal(200, outlined.Items.Count);
        Assert.Equal(250, outlined.Total);
        Assert.True(outlined.Truncated);
    }

    [Fact]
    public void Signatures_are_body_free_headers_in_source_order()
    {
        var source = """
            namespace App;

            public class Sample
            {
                public int Value => 42;

                public int Add(int left, int right) => left + right;
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(
            source,
            path: "/src/Sample.cs",
            cancellationToken: TestContext.Current.CancellationToken
        );

        var outlined = OutlineCollector.Walk(tree);

        Assert.Equal(
            ["namespace", "class", "property", "method"],
            outlined.Items.Select(item => item.Kind)
        );
        Assert.Equal("namespace App", outlined.Items[0].Signature);
        Assert.Equal("public class Sample", outlined.Items[1].Signature);
        Assert.Equal("public int Value", outlined.Items[2].Signature);
        Assert.Equal("public int Add(int left, int right)", outlined.Items[3].Signature);
        Assert.DoesNotContain(
            outlined.Items,
            item => item.Signature.Contains("42", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            outlined.Items,
            item => item.Signature.Contains("left + right", StringComparison.Ordinal)
        );
        Assert.Equal(1, outlined.Items[0].Line);
        Assert.Equal(3, outlined.Items[1].Line);
    }
}
