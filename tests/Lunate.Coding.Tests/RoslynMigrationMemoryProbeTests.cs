using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

[Collection("roslyn-workspaces")]
public sealed class RoslynMigrationMemoryProbeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public Task Hold_after_reference_solution_load() => RunAsync(editCycles: false);

    [Fact]
    public Task Hold_after_ten_edit_to_diagnostics_cycles() => RunAsync(editCycles: true);

    private static async Task RunAsync(bool editCycles)
    {
        string? root = Environment.GetEnvironmentVariable("LUNATE_ROSLYN_MEMORY_ROOT");
        Assert.SkipUnless(
            !string.IsNullOrWhiteSpace(root) && File.Exists(Path.Combine(root, "Polly.slnx")),
            "set LUNATE_ROSLYN_MEMORY_ROOT to a restored reference solution"
        );

        string solutionRoot = GitIdentity.Canonicalize(root!);
        string relativeFile = "src/Polly.Core/Retry/RetryStrategyOptions.cs";
        string file = Path.Combine(solutionRoot, relativeFile);
        Assert.True(File.Exists(file), $"the reference edit file was not found: {file}");

        string original = await File.ReadAllTextAsync(file, Ct);
        using var services = new WorkspaceServices(2);
        var changes = new FileChangeBus(solutionRoot);
        services.Connect(changes);
        ToolRegistry tools = HarnessFactory.CreateTools(
            new Workspace(solutionRoot),
            services: services
        );
        var context = new ToolContext(solutionRoot, new NullAgentEvents());
        try
        {
            ToolResult loaded = await RunDiagnosticsAsync(
                tools,
                context,
                """{"scope":"solution"}"""
            );
            Assert.False(loaded.IsError, loaded.Output);

            if (editCycles)
            {
                for (int iteration = 1; iteration <= 10; iteration++)
                {
                    string changed = string.Concat(
                        original,
                        "\n// lunate memory cycle ",
                        iteration.ToString(CultureInfo.InvariantCulture),
                        "\n"
                    );
                    await File.WriteAllTextAsync(file, changed, Ct);
                    changes.Notify(
                        new FileChangeEvent(
                            file,
                            relativeFile,
                            solutionRoot,
                            null,
                            FileChangeKind.Modified
                        )
                    );
                    await changes.DrainAsync(Ct);

                    ToolResult diagnostics = await RunDiagnosticsAsync(tools, context, "{}");
                    Assert.False(diagnostics.IsError, diagnostics.Output);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(4), Ct);
        }
        finally
        {
            await File.WriteAllTextAsync(file, original, Ct);
        }
    }

    private static Task<ToolResult> RunDiagnosticsAsync(
        ToolRegistry tools,
        ToolContext context,
        string arguments
    )
    {
        using JsonDocument document = JsonDocument.Parse(arguments);
        return tools
            .Find("cs_diagnostics")!
            .ExecuteAsync(document.RootElement.Clone(), context, Ct)
            .WaitAsync(WaitBudgets.Diagnostics, Ct);
    }
}
