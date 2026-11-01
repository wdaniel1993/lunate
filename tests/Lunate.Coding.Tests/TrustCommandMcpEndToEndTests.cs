using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// The project mcp.json trust flow end to end through the CLI: a print session refuses an untrusted
/// project server, inspect and approval start nothing, and after the command records the trust the
/// next session starts the server.
/// </summary>
[Collection("mcp-processes")]
public sealed class TrustCommandMcpEndToEndTests
{
    [Fact]
    public void An_untrusted_project_server_is_refused_until_the_command_trusts_it()
    {
        using var temp = new TempDirectory();
        string worktree = Workspace.Canonicalize(temp.File("worktree"));
        Directory.CreateDirectory(worktree);
        string marker = temp.File("server-marker.txt");
        string config = McpComposition.ProjectConfigPath(worktree);
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, McpTestServerSupport.ConfigJson("srv", marker));
        string store = temp.File("store");

        (int firstExit, string firstOutput, RecordingExtensionLog firstLog) = RunPrint(
            temp,
            worktree,
            store
        );

        Assert.Equal(0, firstExit);
        Assert.Equal("ok", firstOutput);
        Assert.False(File.Exists(marker), "the untrusted project server must not start");
        Assert.True(
            firstLog.Contains("interactive") && firstLog.Contains("'lunate trust'"),
            "the refusal hint must name both approval paths; log:\n"
                + string.Join("\n", firstLog.Messages)
        );

        (int inspectExit, string inspectOutput, string inspectErrors) = RunTrust(
            ["trust"],
            worktree,
            store
        );
        Assert.Equal(TrustCommand.ExitPending, inspectExit);
        Assert.Empty(inspectErrors);
        Assert.Contains(
            $"Run project MCP server 'srv', which sees your shell environment, from '{config}'",
            inspectOutput,
            StringComparison.Ordinal
        );
        Assert.False(File.Exists(marker), "inspect must start nothing");

        (int approveExit, string _, string approveErrors) = RunTrust(
            ["trust", "--yes"],
            worktree,
            store
        );
        Assert.Equal(TrustCommand.ExitOk, approveExit);
        Assert.Empty(approveErrors);
        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            new ExtensionTrustStore(
                new ExtensionHostOptions { StorePath = store }
            ).EvaluateMcpServer(worktree, McpConfigTrustHash.ComputeDefinitions(config)["srv"])
        );
        Assert.False(File.Exists(marker), "recording trust must start nothing");

        (int secondExit, string secondOutput, RecordingExtensionLog secondLog) = RunPrint(
            temp,
            worktree,
            store
        );

        Assert.Equal(0, secondExit);
        Assert.Equal("ok", secondOutput);
        Assert.True(File.Exists(marker), "the trusted project server must start");
        Assert.False(secondLog.Contains("was not trusted"));
    }

    private static (int ExitCode, string Output, RecordingExtensionLog Log) RunPrint(
        TempDirectory temp,
        string worktree,
        string store
    )
    {
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            WorkingDirectory = worktree,
            McpConfigPath = temp.File("global-mcp.json"),
        };
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["-p", "Hi"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            printOptions: options,
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );

        return (exitCode, output.ToString(), log);
    }

    private static (int ExitCode, string Output, string Errors) RunTrust(
        string[] args,
        string worktree,
        string store
    )
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            args,
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store },
            workingDirectory: worktree,
            stdinIsTerminal: true
        );

        return (exitCode, output.ToString(), errors.ToString());
    }
}
