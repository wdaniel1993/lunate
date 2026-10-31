using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

[Collection("roslyn-workspaces")]
public sealed class RoslynMigrationBaselineTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task All_five_tool_results_match_the_frozen_migration_baseline()
    {
        using var fixture = RoslynFixtureSupport.CopySolution("console-app");
        RoslynFixtureSupport.Restore(fixture, "ConsoleApp.slnx");
        using var services = new WorkspaceServices(2);
        ToolRegistry tools = RoslynTestHarness.CreateTools(
            new Workspace(fixture.Root),
            services: services
        );
        Assert.Equal(
            ["cs_diagnostics", "cs_find_symbol", "cs_find_references", "cs_outline", "cs_rename"],
            tools
                .Tools.Select(tool => tool.Name)
                .Where(name => name.StartsWith("cs_", StringComparison.Ordinal))
        );

        var context = new ToolContext(fixture.Root, new NullAgentEvents());
        List<BaselineResult> actual = [];
        await CaptureAsync(tools, context, "cs_diagnostics", """{"scope":"solution"}""", actual);
        await CaptureAsync(tools, context, "cs_find_symbol", """{"name":"Calculator"}""", actual);
        await CaptureAsync(
            tools,
            context,
            "cs_find_references",
            """{"name":"Calculator"}""",
            actual
        );
        await CaptureAsync(
            tools,
            context,
            "cs_outline",
            """{"file":"src/ConsoleApp/Calculator.cs"}""",
            actual
        );
        await CaptureAsync(
            tools,
            context,
            "cs_rename",
            """{"name":"Calculator","newName":"Counter"}""",
            actual
        );

        string baselinePath = Path.Combine(
            RoslynFixtureSupport.RepositoryRoot,
            "tests",
            "fixtures",
            "roslyn-built-in-migration",
            "results.json"
        );
        BaselineResult[] expected = JsonSerializer
            .Deserialize<BaselineResult[]>(File.ReadAllText(baselinePath))!
            .Select(result => result with { Output = NormalizeNewlines(result.Output) })
            .ToArray();
        Assert.Equal(expected, actual);
    }

    private static async Task CaptureAsync(
        ToolRegistry tools,
        ToolContext context,
        string name,
        string arguments,
        List<BaselineResult> results
    )
    {
        using JsonDocument document = JsonDocument.Parse(arguments);
        ToolResult result = await tools
            .Find(name)!
            .ExecuteAsync(document.RootElement.Clone(), context, Ct)
            .WaitAsync(WaitBudgets.Diagnostics, Ct);
        // The tools compose their text with AppendLine, so it carries Environment.NewLine: CRLF on
        // Windows. The frozen baseline is one file with LF, and the comparison must not depend on the
        // platform that produced the run — normalise both sides (the line-ending rule, applied here
        // because this is a golden).
        results.Add(
            new BaselineResult(name, arguments, NormalizeNewlines(result.Output), result.IsError)
        );
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed record BaselineResult(
        string Name,
        string Arguments,
        string Output,
        bool IsError
    );
}
