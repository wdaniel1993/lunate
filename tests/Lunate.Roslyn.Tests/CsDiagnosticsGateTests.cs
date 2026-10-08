using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Roslyn;
using Xunit;

namespace Lunate.Roslyn.Tests;

[Collection(RoslynIntegrationCollection.Name)]
public sealed class CsDiagnosticsGateTests(ITestOutputHelper output)
{
    [Fact]
    public async Task An_error_introduced_by_an_edit_is_reported_and_the_fix_is_clean_again()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        RoslynBackend? backend = null;
        var tool = new CsDiagnosticsTool(() => backend = new RoslynBackend(fixture.Root));
        var context = new ToolContext(fixture.Root, new NoopAgentEvents());

        var first = await tool.ExecuteAsync(Args("{}"), context, CancellationToken.None);
        Assert.False(first.IsError);
        Assert.Contains("No compiler errors or warnings", first.Output, StringComparison.Ordinal);
        Assert.NotNull(backend);

        var calculator = Path.Combine(fixture.Root, "src", "ConsoleApp", "Calculator.cs");
        var original = File.ReadAllText(calculator);
        var originalTimestamp = File.GetLastWriteTimeUtc(calculator);
        var broken =
            original + "public static class Broken { public static int X => MissingName; }\n";

        File.WriteAllText(calculator, broken);
        File.SetLastWriteTimeUtc(calculator, originalTimestamp);
        backend.NotifyFileChanged(calculator);

        var editWatch = Stopwatch.StartNew();
        var reported = await tool.ExecuteAsync(Args("{}"), context, CancellationToken.None);
        editWatch.Stop();
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"edit -> diagnostics: {editWatch.ElapsedMilliseconds} ms"
            )
        );

        var brokenLine = original.Split('\n').Length;
        Assert.False(reported.IsError);
        Assert.Contains("Calculator.cs", reported.Output, StringComparison.Ordinal);
        Assert.Contains("CS0103", reported.Output, StringComparison.Ordinal);
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"({brokenLine},"),
            reported.Output,
            StringComparison.Ordinal
        );
        Assert.True(editWatch.ElapsedMilliseconds < 5_000, "the edit cycle was not warm");

        File.WriteAllText(calculator, original);
        var fixedResult = await tool.ExecuteAsync(Args("{}"), context, CancellationToken.None);
        Assert.False(fixedResult.IsError);
        Assert.Contains(
            "No compiler errors or warnings",
            fixedResult.Output,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task The_warm_no_change_cycle_is_fast()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        await backend.LoadAsync(CancellationToken.None);
        await backend.GetDiagnosticsAsync(DiagnosticsScope.ChangedFiles, CancellationToken.None);

        var watch = Stopwatch.StartNew();
        var warm = await backend.GetDiagnosticsAsync(
            DiagnosticsScope.ChangedFiles,
            CancellationToken.None
        );
        watch.Stop();
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"warm no-change diagnostics: {watch.ElapsedMilliseconds} ms"
            )
        );

        Assert.Empty(warm.Items);
        Assert.True(watch.ElapsedMilliseconds < 5_000, "the warm cycle was not fast");
    }

    [Fact]
    public async Task The_first_call_fits_the_ci_tripwire()
    {
        using var fixture = Fixtures.CopySolution("console-app");
        Fixtures.Restore(fixture, "ConsoleApp.slnx");

        var watch = Stopwatch.StartNew();
        using var backend = new RoslynBackend(fixture.Root);
        var load = await backend.LoadAsync(CancellationToken.None);
        var diagnostics = await backend.GetDiagnosticsAsync(
            DiagnosticsScope.ChangedFiles,
            CancellationToken.None
        );
        watch.Stop();

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"first call (construct -> load -> diagnostics): {watch.ElapsedMilliseconds} ms; "
                    + $"status {load.Status}, projects {load.ProjectCount}, documents {load.DocumentCount}"
            )
        );

        Assert.Equal(WorkspaceStatus.Loaded, load.Status);
        Assert.Empty(diagnostics.Items);
        Assert.True(
            watch.ElapsedMilliseconds < 10_000,
            string.Create(
                CultureInfo.InvariantCulture,
                $"first call took {watch.ElapsedMilliseconds} ms; the CI tripwire is 10 s"
            )
        );
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
