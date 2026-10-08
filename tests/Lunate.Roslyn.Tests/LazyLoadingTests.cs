using System.Diagnostics;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

[Collection(RoslynIntegrationCollection.Name)]
public sealed class LazyLoadingTests
{
    private const string ProbeVariable = "LUNATE_LAZY_PROBE";
    private const string MarkerVariable = "LUNATE_LAZY_PROBE_MARKER";

    [Fact]
    public async Task The_tool_does_not_touch_roslyn_or_msbuild_before_the_first_call()
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) == "1")
        {
            await ProbeAsync();
            File.WriteAllText(Environment.GetEnvironmentVariable(MarkerVariable)!, "probe ran");
            return;
        }

        try
        {
            using var child = LaunchProbe();
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
                File.Exists(MarkerPath),
                $"the probe test never ran in the child process:\n{output}"
            );
        }
        finally
        {
            if (File.Exists(MarkerPath))
            {
                File.Delete(MarkerPath);
            }
        }
    }

    private static async Task ProbeAsync()
    {
        Assert.Empty(LoadedRoslynOrMsBuildAssemblies());

        using var fixture = Fixtures.CopySolution("console-app");
        var tool = new CsDiagnosticsTool(() => new RoslynBackend(fixture.Root));

        Assert.Empty(LoadedRoslynOrMsBuildAssemblies());

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("{}").RootElement.Clone(),
            new ToolContext(fixture.Root, new NoopAgentEvents()),
            CancellationToken.None
        );

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

    private static Process LaunchProbe()
    {
        var assembly = typeof(LazyLoadingTests).Assembly.Location;
        var method = string.Concat(
            typeof(LazyLoadingTests).FullName,
            ".",
            nameof(The_tool_does_not_touch_roslyn_or_msbuild_before_the_first_call)
        );

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
        startInfo.Environment[ProbeVariable] = "1";
        startInfo.Environment[MarkerVariable] = MarkerPath;

        return Process.Start(startInfo)!;
    }

    private static string MarkerPath { get; } =
        Path.Combine(
            Path.GetTempPath(),
            string.Concat("lunate-lazy-probe-", Guid.NewGuid().ToString("N"), ".marker")
        );

    private static string DotNetPath()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return !string.IsNullOrEmpty(host) && File.Exists(host) ? host : "dotnet";
    }
}
