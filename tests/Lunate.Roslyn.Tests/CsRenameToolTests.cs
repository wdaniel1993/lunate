using System.Text.Json;
using Lunate.Agent;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class CsRenameToolTests
{
    private static readonly ToolContext Context = new("/tmp", new NoopAgentEvents());

    [Fact]
    public void Declares_the_pinned_tool_shape()
    {
        var tool = new CsRenameTool(() => new FakeCSharpBackend());

        Assert.Equal("cs_rename", tool.Name);
        Assert.Equal(
            "Plan a solution-wide rename of a type or member as file edits — nothing is changed until you apply them with edit/write",
            tool.Description
        );
        Assert.True(tool.Annotations!.ReadOnly);
        Assert.False(tool.Annotations.Destructive);
        Assert.Equal(ToolRisk.ReadOnly, ((ITool)tool).Risk);
        var schema = tool.ParametersSchema.GetRawText();
        Assert.Contains("\"name\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"newName\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"required\"", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructing_the_tool_does_not_resolve_the_backend()
    {
        var tool = new CsRenameTool(() =>
            throw new InvalidOperationException("must not be called")
        );

        Assert.NotNull(tool.Name);
    }

    [Fact]
    public async Task A_missing_new_name_is_an_argument_error()
    {
        var backend = new FakeCSharpBackend();
        var tool = new CsRenameTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Divide"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("newName", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, backend.RenameCalls);
    }

    [Fact]
    public async Task The_names_are_passed_through_to_the_backend()
    {
        var backend = new FakeCSharpBackend();
        var tool = new CsRenameTool(() => backend);

        await tool.ExecuteAsync(
            Args("""{"name":"  Divide  ","newName":" Quotient "}"""),
            Context,
            CancellationToken.None
        );

        Assert.Equal("  Divide  ", backend.LastName);
        Assert.Equal(" Quotient ", backend.LastNewName);
    }

    [Fact]
    public async Task A_plan_renders_file_lines_and_the_nothing_changed_sentence()
    {
        var plan = new RenamePlanResult(
            SymbolSearchStatus.Loaded,
            "planned 2 change(s) in 2 file(s); nothing was changed — apply via edit/write",
            [
                new RenameChange("src/A.cs", [new RenameLineChange(1, "int a;", "int b;")]),
                new RenameChange("src/B.cs", [new RenameLineChange(3, "a = 1;", "b = 1;")]),
            ],
            2,
            2
        );
        var backend = new FakeCSharpBackend { RenamePlan = plan };
        var tool = new CsRenameTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"a","newName":"b"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("nothing was changed", result.Output, StringComparison.Ordinal);
        Assert.Contains("apply via edit/write", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/A.cs:1: int a; → int b;", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/B.cs:3: a = 1; → b = 1;", result.Output, StringComparison.Ordinal);
        Assert.Same(plan, result.Details);
    }

    [Fact]
    public async Task Only_the_first_ten_changes_are_inline()
    {
        var entries = Enumerable
            .Range(0, 12)
            .Select(index => new RenameLineChange(index + 1, $"old{index}", $"new{index}"))
            .ToList();
        var backend = new FakeCSharpBackend
        {
            RenamePlan = new RenamePlanResult(
                SymbolSearchStatus.Loaded,
                "planned 12 change(s) in 1 file(s); nothing was changed — apply via edit/write",
                [new RenameChange("src/A.cs", entries)],
                1,
                12
            ),
        };
        var tool = new CsRenameTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"old","newName":"new"}"""),
            Context,
            CancellationToken.None
        );

        Assert.Contains("src/A.cs:10: old9 → new9", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("src/A.cs:11", result.Output, StringComparison.Ordinal);
        Assert.Contains("2 more changes", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invalid_new_name_explains_without_an_error()
    {
        var backend = new FakeCSharpBackend
        {
            RenamePlan = new RenamePlanResult(
                SymbolSearchStatus.Loaded,
                "'1nvalid' is not a valid C# identifier: it must start with a letter or underscore, contain only letters, digits or underscores, and not be a C# keyword; nothing was changed — apply via edit/write",
                [],
                0,
                0
            ),
        };
        var tool = new CsRenameTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Divide","newName":"1nvalid"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("identifier", result.Output, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_nothing_changed_sentence_is_always_present()
    {
        var backend = new FakeCSharpBackend
        {
            RenamePlan = new RenamePlanResult(
                SymbolSearchStatus.NoSolution,
                "no solution is loaded",
                [],
                0,
                0
            ),
        };
        var tool = new CsRenameTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Divide","newName":"Quotient"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("no solution is loaded", result.Output, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_solution_is_an_actionable_error_result()
    {
        var backend = new FakeCSharpBackend
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
        var tool = new CsRenameTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Divide","newName":"Quotient"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("no solution found", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, backend.RenameCalls);
    }

    [Fact]
    public async Task A_partial_load_is_noted_and_still_plans()
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
            RenamePlan = new RenamePlanResult(
                SymbolSearchStatus.Partial,
                "planned 1 change(s) in 1 file(s); nothing was changed — apply via edit/write",
                [new RenameChange("src/A.cs", [new RenameLineChange(1, "int a;", "int b;")])],
                1,
                1
            ),
        };
        var tool = new CsRenameTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"a","newName":"b"}"""),
            Context,
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("Partial load", result.Output, StringComparison.Ordinal);
        Assert.Contains("boom", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/A.cs:1", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rename_failure_is_an_error_result_not_an_exception()
    {
        var backend = new FakeCSharpBackend { RenameThrow = true };
        var tool = new CsRenameTool(() => backend);

        var result = await tool.ExecuteAsync(
            Args("""{"name":"Divide","newName":"Quotient"}"""),
            Context,
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains("Rename planning failed", result.Output, StringComparison.Ordinal);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
