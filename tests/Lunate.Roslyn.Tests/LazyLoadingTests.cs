using System.Diagnostics;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

[Collection(RoslynIntegrationCollection.Name)]
public sealed class LazyLoadingTests
{
    private const string ToolVariable = "LUNATE_LAZY_PROBE";
    private const string MarkerVariable = "LUNATE_LAZY_PROBE_MARKER";
    private const string DiagnosticsTool = "diagnostics";
    private const string FindSymbolTool = "find_symbol";

    [Fact]
    public Task The_diagnostics_tool_does_not_touch_roslyn_or_msbuild_before_the_first_call() =>
        RunProbeAsync(
            DiagnosticsTool,
            nameof(The_diagnostics_tool_does_not_touch_roslyn_or_msbuild_before_the_first_call)
        );

    [Fact]
    public Task The_find_symbol_tool_does_not_touch_roslyn_or_msbuild_before_the_first_call() =>
        RunProbeAsync(
            FindSymbolTool,
            nameof(The_find_symbol_tool_does_not_touch_roslyn_or_msbuild_before_the_first_call)
        );

    private static async Task RunProbeAsync(string tool, string methodName)
    {
        if (Environment.GetEnvironmentVariable(ToolVariable) == tool)
        {
            await ProbeAsync(tool);
            File.WriteAllText(Environment.GetEnvironmentVariable(MarkerVariable)!, "probe ran");
            return;
        }

        var marker = Path.Combine(
            Path.GetTempPath(),
            string.Concat("lunate-lazy-probe-", tool, "-", Guid.NewGuid().ToString("N"), ".marker")
        );
        try
        {
            using var child = LaunchProbe(methodName, tool, marker);
            var output = child.StandardOutput.ReadToEnd() + child.StandardError.ReadToEnd();
            Assert.True(
                child.WaitForExit(TimeSpan.FromSeconds(120)),
                "the probe process timed out"
            );
            Assert.True(
                child.ExitCode == 0,
                $"the probe failed (exit code {child.ExitCode}):\n{output}"
            );
            Assert.True(
                File.Exists(marker),
                $"the probe test never ran in the child process:\n{output}"
            );
        }
        finally
        {
            if (File.Exists(marker))
            {
                File.Delete(marker);
            }
        }
    }

    private static async Task ProbeAsync(string tool)
    {
        Assert.Empty(LoadedRoslynOrMsBuildAssemblies());

        using var fixture = Fixtures.CopySolution("console-app");
        var context = new ToolContext(fixture.Root, new NoopAgentEvents());
        ToolResult result;

        if (tool == DiagnosticsTool)
        {
            var diagnostics = new CsDiagnosticsTool(() => new RoslynBackend(fixture.Root));
            Assert.Empty(LoadedRoslynOrMsBuildAssemblies());

            result = await diagnostics.ExecuteAsync(
                JsonDocument.Parse("{}").RootElement.Clone(),
                context,
                CancellationToken.None
            );
        }
        else
        {
            var findSymbol = new CsFindSymbolTool(() => new RoslynBackend(fixture.Root));
            Assert.Empty(LoadedRoslynOrMsBuildAssemblies());

            result = await findSymbol.ExecuteAsync(
                JsonDocument.Parse("""{"name":"Calculator"}""").RootElement.Clone(),
                context,
                CancellationToken.None
            );
        }

        // The fixture is never restored here, so the first call must report restore-required.
        Assert.True(result.IsError);
        Assert.NotEmpty(LoadedRoslynOrMsBuildAssemblies());
    }

    private static IReadOnlyList<string> LoadedRoslynOrMsBuildAssemblies() =>
        AppDomain
            .CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name ?? string.Empty)
            .Where(name =>
                name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.Build", StringComparison.Ordinal)
            )
            .ToList();

    private static Process LaunchProbe(string methodName, string tool, string markerPath)
    {
        var assembly = typeof(LazyLoadingTests).Assembly.Location;
        var method = string.Concat(typeof(LazyLoadingTests).FullName, ".", methodName);

        var processPath = Environment.ProcessPath;
        var direct =
            processPath is not null
            && string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "Lunate.Roslyn.Tests",
                StringComparison.Ordinal
            );
        var fileName = direct ? processPath! : DotNetPath();

        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (!direct)
        {
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add(assembly);
        }

        startInfo.ArgumentList.Add("-method");
        startInfo.ArgumentList.Add(method);
        startInfo.ArgumentList.Add("-noLogo");
        startInfo.Environment[ToolVariable] = tool;
        startInfo.Environment[MarkerVariable] = markerPath;

        return Process.Start(startInfo)!;
    }

    private static string DotNetPath()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return !string.IsNullOrEmpty(host) && File.Exists(host) ? host : "dotnet";
    }
}
