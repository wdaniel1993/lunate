using System.Diagnostics;
using System.Text;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Testing;
using Lunate.TestSupport;

namespace Lunate.Extensibility.Testing.Tests;

/// <summary>
/// #464: the template README says "copy it, rename it, replace the handlers", and a copy outside
/// this repository must keep that promise. A copy does not see <c>Directory.Build.props</c>, so
/// the template's own project file carries the properties every SDK project needs, restores the
/// contract pair as packages (<c>LunateUseNuGetContracts=true</c>) from a feed of freshly packed
/// packages, and the built copy loads through the real loader and replays the fixture.
/// </summary>
/// <remarks>
/// This exercises the prove path outside the repository; the verify gate keeps proving the same
/// package shape in-repository, and the solution keeps compiling the template projects.
/// </remarks>
public sealed class ExtensionTemplateCopyEndToEndTests
{
    [Fact]
    public async Task Template_copy_builds_and_loads_outside_the_repository()
    {
        string repository = RepositoryRoot.Find();
        using var temp = new TempDirectory();

        string feed = temp.Subdirectory("contract-feed");
        Pack(repository, feed);

        string copy = CopyTemplate(repository, temp.Subdirectory("my-extension"));
        // Both projects a copy carries: the extension a host loads and the test project the
        // README keeps as the place where behavior is proven.
        Build(
            Path.Combine(copy, "TemplateExtension", "TemplateExtension.csproj"),
            feed,
            ReleaseVersions.Contract
        );
        Build(
            Path.Combine(copy, "TemplateExtension.Tests", "TemplateExtension.Tests.csproj"),
            feed,
            ReleaseVersions.Contract,
            ReleaseVersions.Preview
        );

        string extensionOutput = Path.Combine(
            copy,
            "TemplateExtension",
            "bin",
            Configuration,
            TargetFramework
        );
        using var hostTemp = new TempDirectory();
        var approver = new ScriptedApprover(true);
        await using var host = new ExtensionTestHost(
            new ExtensionTestHostOptions
            {
                TempDirectory = hostTemp.Root,
                ExtensionId = "template",
                ExtensionDirectory = extensionOutput,
                Approver = approver,
            }
        );
        host.Tools.Add(new EchoTool());

        ExtensionRunResult result = await host.RunAsync(
            new ReplayChatClient(FixturePath(repository)),
            "call the echo tool",
            TestContext.Current.CancellationToken
        );
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Contains(
            host.Log.Entries,
            entry =>
                entry.Message.Contains("template", StringComparison.Ordinal)
                && entry.Message.Contains("tool-calling", StringComparison.Ordinal)
        );
        Assert.Single(
            host.Log.Entries,
            entry => entry.Message.Contains("service started", StringComparison.Ordinal)
        );
        Assert.Single(
            host.Log.Entries,
            entry => entry.Message.Contains("service stopped", StringComparison.Ordinal)
        );
        Assert.Equal(1, approver.Calls);
        Assert.Equal(1, approver.Approvals);
        Assert.Contains(result.Events.OfType<ToolCallResult>(), toolResult => !toolResult.IsError);
        Assert.Contains(
            result.Events.OfType<RunFinished>(),
            finished => finished.StopReason == StopReasons.Stop
        );
    }

    private const string Configuration = "Release";
    private const string TargetFramework = "net10.0";

    private static void Pack(string repository, string feed)
    {
        string[] packables =
        [
            "src/Lunate.Contracts/Lunate.Contracts.csproj",
            "src/Lunate.Extensibility.Abstractions/Lunate.Extensibility.Abstractions.csproj",
            "src/Lunate.Agent/Lunate.Agent.csproj",
            "src/Lunate.Extensibility.Testing/Lunate.Extensibility.Testing.csproj",
        ];
        foreach (string project in packables)
        {
            Dotnet(
                $"""pack "{Path.Combine(repository, project)}" -c {Configuration} -o "{feed}" --nologo -nodeReuse:false"""
            );
        }
    }

    private static void Build(
        string project,
        string feed,
        string contractVersion,
        string? previewVersion = null
    )
    {
        string testing = previewVersion is null ? string.Empty : $" -p:LunateTestingVersion={previewVersion}";
        Dotnet(
            $"""build "{project}" -c {Configuration} -p:LunateUseNuGetContracts=true """
                + $"""-p:LunateContractsVersion={contractVersion}{testing} """
                + $"""-p:RestoreAdditionalProjectSources="{feed}" --nologo -nodeReuse:false"""
        );
    }

    private static void Dotnet(string arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1";
        var buffer = new StringBuilder();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (buffer)
                {
                    _ = buffer.AppendLine(args.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (buffer)
                {
                    _ = buffer.AppendLine(args.Data);
                }
            }
        };
        Assert.True(process.Start(), $"dotnet did not start: {arguments}");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Assert.True(
            process.WaitForExit(300_000),
            $"dotnet did not finish within five minutes: {arguments}"
        );
        Assert.True(process.ExitCode == 0, $"dotnet failed ({process.ExitCode}):\n{buffer}");
    }

    private static string CopyTemplate(string repository, string destination)
    {
        CopyDirectory(
            Path.Combine(repository, "templates", "extension"),
            destination,
            skip: ["bin", "obj"]
        );
        return destination;
    }

    private static void CopyDirectory(string source, string destination, HashSet<string> skip)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            if (!skip.Contains(Path.GetFileName(directory)))
            {
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), skip);
            }
        }
    }

    private static string FixturePath(string repository) =>
        Path.Combine(
            repository,
            "tests",
            "fixtures",
            "streams",
            "extension-template-tool-call.jsonl"
        );

    /// <summary>The agent-side echo tool the template's fixture replay calls through the copy.</summary>
    private sealed class EchoTool : ITool
    {
        public string Name => "echo";

        public string Description => "Echoes the given text back.";

        public JsonElement ParametersSchema { get; } =
            JsonDocument
                .Parse(
                    """{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}"""
                )
                .RootElement.Clone();

        public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
        {
            string text = args.TryGetProperty("text", out JsonElement value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
            return Task.FromResult(new ToolResult($"echo: {text}", IsError: false));
        }
    }
}
