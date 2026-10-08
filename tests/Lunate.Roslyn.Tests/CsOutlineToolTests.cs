using System.Text.Json;
using Lunate.Agent;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class CsOutlineToolTests
{
    private static readonly ToolContext Context = new("/tmp", new NoopAgentEvents());

    [Fact]
    public void Declares_the_pinned_tool_shape()
    {
        var tool = new CsOutlineTool(() => new FakeCSharpBackend());

        Assert.Equal("cs_outline", tool.Name);
        Assert.Equal(
            "List a file's types and member signatures without bodies — cheap context for big files",
            tool.Description
        );
        Assert.True(tool.Annotations!.ReadOnly);
        Assert.False(tool.Annotations.Destructive);
        Assert.Equal(ToolRisk.ReadOnly, ((ITool)tool).Risk);
        var schema = tool.ParametersSchema.GetRawText();
        Assert.Contains("\"file\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"required\"", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructing_the_tool_does_not_resolve_the_backend()
    {
        var tool = new CsOutlineTool(() =>
            throw new InvalidOperationException("must not be called")
        );

        Assert.NotNull(tool.Name);
    }

    [Fact]
    public async Task An_outline_never_loads_a_solution()
    {
        var backend = new FakeCSharpBackend();
        var tool = new CsOutlineTool(() => backend);

        await tool.ExecuteAsync(
            Args("""{"file":"src/App/Thing.cs"}"""),
            Context,
            CancellationToken.None
        );

        Assert.Equal(0, backend.LoadCalls);
        Assert.Equal(1, backend.OutlineCalls);
        Assert.Equal("src/App/Thing.cs", backend.LastFile);
    }

    [Fact]
    public async Task The_backend_factory_runs_once_and_is_reused()
    {
        var calls = 0;
        var backend = new FakeCSharpBackend();
        var tool = new CsOutlineTool(() =>
        {
            calls++;
            return backend;
        });

        await tool.ExecuteAsync(Args("""{"file":"A.cs"}"""), Context, CancellationToken.None);
        await tool.ExecuteAsync(Args("""{"file":"B.cs"}"""), Context, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(2, backend.OutlineCalls);
    }

    [Fact]
    public async Task A_missing_file_argument_is_an_argument_error()
    {
        var backend = new FakeCSharpBackend();
        var tool = new CsOutlineTool(() => backend);

        var result = await tool.ExecuteAsync(Args("{}"), Context, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("file", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, backend.OutlineCalls);
    }

    [Fact]
    public async Task Items_are_indented_by_container_depth()
    {
        var backend = new FakeCSharpBackend
        {
            Outline = new OutlineResult(
                "found 3 declarations in 'src/App.cs'",
                [
                    new OutlineItem("namespace", null, "App", "namespace App", 1),
                    new OutlineItem("class", "App", "Thing", "public class Thing", 3),
                    new OutlineItem("method", "App.Thing", "Run", "public void Run()", 5),
                ],
                3
            ),
        };
        var tool = new CsOutlineTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"file":"src/App.cs"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("namespace App (line 1)", result.Output, StringComparison.Ordinal);
        Assert.Contains("\n  public class Thing (line 3)", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "\n    public void Run() (line 5)",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Same(backend.Outline, result.Details);
    }

    [Fact]
    public async Task Only_the_first_forty_items_are_inline()
    {
        var items = Enumerable
            .Range(0, 42)
            .Select(index => new OutlineItem(
                "class",
                "App",
                $"Type{index}",
                $"class Type{index}",
                index + 1
            ))
            .ToList();
        var backend = new FakeCSharpBackend
        {
            Outline = new OutlineResult("found 42 declarations in 'src/Many.cs'", items, 42),
        };
        var tool = new CsOutlineTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"file":"src/Many.cs"}"""),
            Context,
            CancellationToken.None
        );

        Assert.Contains("class Type39 (line 40)", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("class Type40", result.Output, StringComparison.Ordinal);
        Assert.Contains("2 more", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_truncated_result_says_so()
    {
        var backend = new FakeCSharpBackend
        {
            Outline = new OutlineResult(
                "found 250 declarations in 'src/Many.cs'",
                [new OutlineItem("class", null, "One", "class One", 1)],
                250
            )
            {
                Truncated = true,
            },
        };
        var tool = new CsOutlineTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"file":"src/Many.cs"}"""),
            Context,
            CancellationToken.None
        );

        Assert.Contains("truncated at 200 items", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_file_message_is_not_an_error()
    {
        var backend = new FakeCSharpBackend
        {
            Outline = new OutlineResult(
                "file not found: 'src/Nope.cs'; check the path relative to the worktree root",
                [],
                0
            ),
        };
        var tool = new CsOutlineTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"file":"src/Nope.cs"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("not found", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_outline_failure_is_an_error_result_not_an_exception()
    {
        var backend = new FakeCSharpBackend { OutlineThrow = true };
        var tool = new CsOutlineTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"file":"src/App.cs"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("Outline failed", result.Output, StringComparison.Ordinal);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
