using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

[Collection(RoslynIntegrationCollection.Name)]
public sealed class CsRenameGateTests(ITestOutputHelper output)
{
    [Fact]
    public async Task A_rename_is_planned_for_both_projects_and_disk_stays_untouched()
    {
        using var fixture = Fixtures.CopySolution("lib-with-tests");
        Fixtures.Restore(fixture, "LibWithTests.sln");
        using var backend = new RoslynBackend(fixture.Root);
        var load = await backend.LoadAsync(CancellationToken.None);
        Assert.Equal(WorkspaceStatus.Loaded, load.Status);
        var before = HashTree(fixture.Root);

        var first = await backend.PlanRenameAsync("Divide", "Quotient", CancellationToken.None);
        var after = HashTree(fixture.Root);
        var second = await backend.PlanRenameAsync("Divide", "Quotient", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.Loaded, first.Status);
        Assert.Equal(
            new object[]
            {
                new
                {
                    File = "src/CalculatorLib/Calculator.cs",
                    Line = 9,
                    OldText = "public static int Divide(int left, int right) =>",
                    NewText = "public static int Quotient(int left, int right) =>",
                },
                new
                {
                    File = "src/CalculatorLib/Usage.cs",
                    Line = 5,
                    OldText = "public static int Value => Calculator.Divide(10, 5);",
                    NewText = "public static int Value => Calculator.Quotient(10, 5);",
                },
                new
                {
                    File = "src/CalculatorLib/Usage.cs",
                    Line = 7,
                    OldText = "public static int Half(int value) => Calculator.Divide(value, 2);",
                    NewText = "public static int Half(int value) => Calculator.Quotient(value, 2);",
                },
                new
                {
                    File = "tests/CalculatorLib.Tests/Program.cs",
                    Line = 4,
                    OldText = "return Calculator.Add(1, 2) == 3 && Calculator.Divide(6, 2) == 3 ? 0 : 1;",
                    NewText = "return Calculator.Add(1, 2) == 3 && Calculator.Quotient(6, 2) == 3 ? 0 : 1;",
                },
            },
            Flatten(first).ToArray()
        );
        Assert.Equal(3, first.TotalFileCount);
        Assert.Equal(4, first.TotalChangeCount);
        Assert.False(first.Truncated);
        Assert.Contains("nothing was changed", first.Message, StringComparison.Ordinal);
        Assert.Contains("apply via edit/write", first.Message, StringComparison.Ordinal);
        Assert.Equal(before, after);
        Assert.Equal(Flatten(first), Flatten(second));
        Assert.Equal(first.TotalFileCount, second.TotalFileCount);
        Assert.Equal(first.TotalChangeCount, second.TotalChangeCount);
    }

    [Fact]
    public async Task An_invalid_new_name_is_rejected_with_guidance()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.PlanRenameAsync("Calculator", "1nvalid", CancellationToken.None);

        Assert.Empty(result.Changes);
        Assert.Equal(0, result.TotalFileCount);
        Assert.Equal(0, result.TotalChangeCount);
        Assert.Contains("identifier", result.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_metadata_target_produces_no_plan()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.PlanRenameAsync("System.String", "Text", CancellationToken.None);

        Assert.Empty(result.Changes);
        Assert.Contains("metadata symbol", result.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_ambiguous_name_lists_candidates_and_no_plan()
    {
        using var fixture = Fixtures.CopySolution("lib-with-tests");
        Fixtures.Restore(fixture, "LibWithTests.sln");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.PlanRenameAsync("Value", "Amount", CancellationToken.None);

        Assert.Equal(2, result.Candidates.Count);
        Assert.Empty(result.Changes);
        Assert.Contains("dotted path", result.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_restore_required_status_is_passed_through()
    {
        using var fixture = Fixtures.CopySolution("lib-with-tests");
        using var backend = new RoslynBackend(fixture.Root);
        var load = await backend.LoadAsync(CancellationToken.None);
        Assert.Equal(WorkspaceStatus.RestoreRequired, load.Status);

        var result = await backend.PlanRenameAsync("Divide", "Quotient", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.RestoreRequired, result.Status);
        Assert.Empty(result.Changes);
        Assert.Contains("restore", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_missing_sdk_status_is_passed_through()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        using var backend = new RoslynBackend(
            fixture.Root,
            () => new BootstrapResult(false, "install the .NET SDK")
        );
        var load = await backend.LoadAsync(CancellationToken.None);
        Assert.Equal(WorkspaceStatus.NoSdk, load.Status);

        var result = await backend.PlanRenameAsync("Calculator", "Counter", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.NoSdk, result.Status);
        Assert.Contains("install the .NET SDK", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_solution_is_reported_instead_of_throwing()
    {
        using var backend = new RoslynBackend(
            Path.Combine(Path.GetTempPath(), "lunate-roslyn-tests", Guid.NewGuid().ToString("N"))
        );
        await backend.LoadAsync(CancellationToken.None);

        var result = await backend.PlanRenameAsync("Calculator", "Counter", CancellationToken.None);

        Assert.Equal(SymbolSearchStatus.NoSolution, result.Status);
        Assert.Empty(result.Changes);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task The_tool_plans_a_rename_and_says_nothing_changed()
    {
        using var fixture = Fixtures.CopySolution("lib-with-tests");
        Fixtures.Restore(fixture, "LibWithTests.sln");
        var tool = new CsRenameTool(() => new RoslynBackend(fixture.Root));

        var result = await tool.ExecuteAsync(
            System
                .Text.Json.JsonDocument.Parse("""{"name":"Divide","newName":"Quotient"}""")
                .RootElement.Clone(),
            new Lunate.Agent.ToolContext(fixture.Root, new NoopAgentEvents()),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Contains("nothing was changed", result.Output, StringComparison.Ordinal);
        Assert.Contains("apply via edit/write", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "src/CalculatorLib/Calculator.cs:9",
            result.Output,
            StringComparison.Ordinal
        );
        var plan = Assert.IsType<RenamePlanResult>(result.Details);
        Assert.Equal(3, plan.TotalFileCount);
        Assert.Equal(4, plan.TotalChangeCount);
    }

    [Fact]
    public async Task A_warm_rename_plan_is_fast()
    {
        using var fixture = Fixtures.CopySolution("lib-with-tests");
        Fixtures.Restore(fixture, "LibWithTests.sln");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);
        await backend.PlanRenameAsync("Divide", "Quotient", CancellationToken.None);

        var watch = Stopwatch.StartNew();
        var warm = await backend.PlanRenameAsync("Helper", "Utility", CancellationToken.None);
        watch.Stop();

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"warm rename plan: {watch.ElapsedMilliseconds} ms"
            )
        );

        Assert.NotEmpty(warm.Changes);
        Assert.True(watch.ElapsedMilliseconds < 5_000, "the warm rename plan was not fast");
    }

    private static IEnumerable<object> Flatten(RenamePlanResult plan) =>
        plan.Changes.SelectMany(change =>
            change.Entries.Select(entry =>
                (object)
                    new
                    {
                        change.File,
                        entry.Line,
                        entry.OldText,
                        entry.NewText,
                    }
            )
        );

    private static string HashTree(string root)
    {
        var builder = new StringBuilder();
        foreach (
            var path in Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(
                    file => Path.GetRelativePath(root, file).Replace('\\', '/'),
                    StringComparer.Ordinal
                )
        )
        {
            builder
                .Append(Path.GetRelativePath(root, path).Replace('\\', '/'))
                .Append('|')
                .Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
                .Append('\n');
        }

        return builder.ToString();
    }
}
