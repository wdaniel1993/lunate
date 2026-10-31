using System.Diagnostics;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class RoslynBuiltInStartupTests
{
    private const string ProbeVariable = "LUNATE_ROSLYN_BUILTIN_PROBE";
    private const string MarkerVariable = "LUNATE_ROSLYN_BUILTIN_MARKER";

    [Fact]
    public async Task The_builtin_keeps_roslyn_and_msbuild_unloaded_until_first_csharp_use()
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) == "1")
        {
            await ProbeAsync();
            File.WriteAllText(Environment.GetEnvironmentVariable(MarkerVariable)!, "probe ran");
            return;
        }

        string marker = Path.Combine(
            Path.GetTempPath(),
            string.Concat("lunate-roslyn-builtin-", Guid.NewGuid().ToString("N"), ".marker")
        );
        try
        {
            using Process child = LaunchProbe(marker);
            string output = child.StandardOutput.ReadToEnd() + child.StandardError.ReadToEnd();
            Assert.True(child.WaitForExit(WaitBudgets.ChildRun), "probe timed out:\n" + output);
            Assert.True(child.ExitCode == 0, $"probe failed (exit {child.ExitCode}):\n{output}");
            Assert.True(File.Exists(marker), "probe did not run:\n" + output);
        }
        finally
        {
            if (File.Exists(marker))
            {
                File.Delete(marker);
            }
        }
    }

    private static async Task ProbeAsync()
    {
        Assert.Empty(RoslynOrMsBuildAssemblies());
        using var temp = new TempDirectory();
        string root = temp.File("workspace");
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "Program.cs");
        await File.WriteAllTextAsync(source, "namespace Sample; public class Example { }");
        await File.WriteAllTextAsync(Path.Combine(root, "Fixture.slnx"), "<Solution />");
        Workspace workspace = new(root);

        await using ExtensionSession host = await ExtensionSession.StartAsync(
            workspace.Identity,
            new ExtensionSeams { StorePath = temp.File("store"), Log = new RecordingExtensionLog() }
        );
        IReadOnlyList<ITool> tools = host.ComposeTools(workspace);

        Assert.Equal(
            ["cs_diagnostics", "cs_find_symbol", "cs_find_references", "cs_outline", "cs_rename"],
            tools.Select(tool => tool.Name)
        );
        Assert.Empty(RoslynOrMsBuildAssemblies());

        using var arguments = System.Text.Json.JsonDocument.Parse("{\"file\":\"Program.cs\"}");
        ToolResult result = await tools
            .Single(tool => tool.Name == "cs_outline")
            .ExecuteAsync(
                arguments.RootElement.Clone(),
                new ToolContext(root, new NullAgentEvents()),
                CancellationToken.None
            );

        Assert.False(result.IsError, result.Output);
        Assert.Contains(
            RoslynOrMsBuildAssemblies(),
            name => name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
        );
    }

    private static string[] RoslynOrMsBuildAssemblies() =>
        [
            .. AppDomain
                .CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetName().Name ?? string.Empty)
                .Where(name =>
                    name.StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal)
                    || name.StartsWith("Microsoft.Build.", StringComparison.Ordinal)
                ),
        ];

    private static Process LaunchProbe(string markerPath)
    {
        string assembly = typeof(RoslynBuiltInStartupTests).Assembly.Location;
        string method = string.Concat(
            typeof(RoslynBuiltInStartupTests).FullName,
            ".",
            nameof(The_builtin_keeps_roslyn_and_msbuild_unloaded_until_first_csharp_use)
        );
        string? processPath = Environment.ProcessPath;
        bool direct =
            processPath is not null
            && string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "Lunate.Coding.Tests",
                StringComparison.Ordinal
            );
        var startInfo = new ProcessStartInfo(direct ? processPath! : DotNetPath())
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
        startInfo.Environment[MarkerVariable] = markerPath;
        return Process.Start(startInfo)!;
    }

    private static string DotNetPath()
    {
        string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return !string.IsNullOrEmpty(host) && File.Exists(host) ? host : "dotnet";
    }
}
