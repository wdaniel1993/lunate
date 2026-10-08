using System.Diagnostics;
using System.Globalization;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

[Collection(RoslynIntegrationCollection.Name)]
public sealed class CsOutlineGateTests(ITestOutputHelper output)
{
    [Fact]
    public async Task An_outline_has_body_free_signatures_and_nesting()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var outline = await backend.OutlineAsync(
            "src/ConsoleApp/Geometry.cs",
            CancellationToken.None
        );

        Assert.Equal(
            [
                new OutlineItem("namespace", null, "ConsoleApp", "namespace ConsoleApp", 1),
                new OutlineItem("class", "ConsoleApp", "Geometry", "public class Geometry", 3),
                new OutlineItem(
                    "field",
                    "ConsoleApp.Geometry",
                    "_sides",
                    "private readonly int _sides",
                    5
                ),
                new OutlineItem(
                    "constructor",
                    "ConsoleApp.Geometry",
                    "Geometry",
                    "public Geometry(int sides)",
                    7
                ),
                new OutlineItem("enum", "ConsoleApp.Geometry", "Kind", "public enum Kind", 12),
                new OutlineItem(
                    "event",
                    "ConsoleApp.Geometry",
                    "Changed",
                    "public event EventHandler? Changed",
                    18
                ),
                new OutlineItem("property", "ConsoleApp.Geometry", "Sides", "public int Sides", 20),
                new OutlineItem(
                    "property",
                    "ConsoleApp.Geometry",
                    "Shape",
                    "public Kind Shape",
                    22
                ),
                new OutlineItem(
                    "method",
                    "ConsoleApp.Geometry",
                    "Perimeter",
                    "public int Perimeter(int length)",
                    24
                ),
                new OutlineItem(
                    "method",
                    "ConsoleApp.Geometry",
                    "RaiseChanged",
                    "public void RaiseChanged()",
                    26
                ),
            ],
            outline.Items
        );
        Assert.Equal(10, outline.TotalItemCount);
        Assert.False(outline.Truncated);
        Assert.DoesNotContain(
            outline.Items,
            item => item.Signature.Contains("_sides = sides", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            outline.Items,
            item => item.Signature.Contains("Invoke", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task An_absolute_path_inside_the_root_works()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        using var backend = new RoslynBackend(fixture.Root);

        var outline = await backend.OutlineAsync(
            Path.Combine(fixture.Root, "src", "ConsoleApp", "Geometry.cs"),
            CancellationToken.None
        );

        Assert.Equal(10, outline.TotalItemCount);
    }

    [Fact]
    public async Task A_syntax_error_file_still_outlines()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        var broken = Path.Combine(fixture.Root, "src", "ConsoleApp", "Broken.cs");
        File.WriteAllText(
            broken,
            "namespace ConsoleApp;\n\npublic class Broken\n{\n    public int Value = ;\n"
        );
        using var backend = new RoslynBackend(fixture.Root);

        var outline = await backend.OutlineAsync(
            "src/ConsoleApp/Broken.cs",
            CancellationToken.None
        );

        Assert.Contains(
            outline.Items,
            item => item.Kind == "namespace" && item.Name == "ConsoleApp"
        );
        Assert.Contains(outline.Items, item => item.Kind == "class" && item.Name == "Broken");
        Assert.Contains(outline.Items, item => item.Kind == "field" && item.Name == "Value");
    }

    [Fact]
    public async Task A_missing_file_is_an_actionable_message()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        using var backend = new RoslynBackend(fixture.Root);

        var outline = await backend.OutlineAsync("src/ConsoleApp/Nope.cs", CancellationToken.None);

        Assert.Empty(outline.Items);
        Assert.Contains("not found", outline.Message, StringComparison.Ordinal);
        Assert.Contains("src/ConsoleApp/Nope.cs", outline.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_outside_the_root_is_refused()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        using var backend = new RoslynBackend(fixture.Root);
        var outside = Path.Combine(
            Path.GetTempPath(),
            "lunate-roslyn-tests",
            "outside",
            "Loose.cs"
        );

        var outline = await backend.OutlineAsync(outside, CancellationToken.None);

        Assert.Empty(outline.Items);
        Assert.Contains("outside", outline.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_outline_works_without_a_loaded_solution()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(directory.File("Loose.cs"), "public class Loose { }\n");
        var bootstrapCalls = 0;
        using var backend = new RoslynBackend(
            directory.Root,
            () =>
            {
                bootstrapCalls++;
                return new BootstrapResult(false, "no .NET SDK could be located");
            }
        );

        var outline = await backend.OutlineAsync("Loose.cs", CancellationToken.None);

        Assert.Equal("public class Loose", Assert.Single(outline.Items).Signature);
        Assert.Equal(0, bootstrapCalls);

        var load = await backend.LoadAsync(CancellationToken.None);
        Assert.Equal(WorkspaceStatus.NoSdk, load.Status);
        Assert.Equal(1, bootstrapCalls);

        var afterLoad = await backend.OutlineAsync("Loose.cs", CancellationToken.None);
        Assert.Single(afterLoad.Items);
    }

    [Fact]
    public async Task An_empty_file_has_an_empty_outline_with_a_note()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        File.WriteAllText(Path.Combine(fixture.Root, "src", "ConsoleApp", "Empty.cs"), "");
        using var backend = new RoslynBackend(fixture.Root);

        var outline = await backend.OutlineAsync("src/ConsoleApp/Empty.cs", CancellationToken.None);

        Assert.Empty(outline.Items);
        Assert.Equal(0, outline.TotalItemCount);
        Assert.Contains("empty", outline.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_path_asks_for_a_file()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        using var backend = new RoslynBackend(fixture.Root);

        var outline = await backend.OutlineAsync("   ", CancellationToken.None);

        Assert.Empty(outline.Items);
        Assert.Contains("file", outline.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_tool_outlines_the_fixture_file()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        var tool = new CsOutlineTool(() => new RoslynBackend(fixture.Root));

        var result = await tool.ExecuteAsync(
            System
                .Text.Json.JsonDocument.Parse("""{"file":"src/ConsoleApp/Calculator.cs"}""")
                .RootElement.Clone(),
            new Lunate.Agent.ToolContext(fixture.Root, new NoopAgentEvents()),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("public static int Add(int left, int right)", result.Output);
        Assert.Contains("public static class Calculator", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_warm_outline_is_fast()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.OutlineAsync("src/ConsoleApp/Geometry.cs", CancellationToken.None);

        var watch = Stopwatch.StartNew();
        var warm = await backend.OutlineAsync(
            "src/ConsoleApp/Calculator.cs",
            CancellationToken.None
        );
        watch.Stop();

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"warm outline: {watch.ElapsedMilliseconds} ms"
            )
        );

        Assert.NotEmpty(warm.Items);
        Assert.True(watch.ElapsedMilliseconds < 5_000, "the warm outline was not fast");
    }
}
