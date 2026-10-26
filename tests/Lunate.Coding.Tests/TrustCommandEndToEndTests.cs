using System.Text.Json;
using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// The trust command end to end through the CLI: a print session refuses an untrusted project
/// extension, the command records it, and the next session loads it; the digest pinning flow binds
/// CI's recording to the inspected pending set.
/// </summary>
public sealed class TrustCommandEndToEndTests
{
    [Fact]
    public void A_print_session_refuses_until_the_command_records_then_loads_the_extension()
    {
        using var temp = new TempDirectory();
        string worktree = SetupProjectExtension(temp, out string store, out string marker);

        (int firstExit, string firstOutput, RecordingExtensionLog firstLog) = RunPrint(
            temp,
            worktree,
            store
        );

        Assert.Equal(0, firstExit);
        Assert.Equal("ok", firstOutput);
        Assert.False(File.Exists(marker), "the untrusted extension must not load");
        Assert.True(
            firstLog.Contains("'lunate trust'"),
            "the refusal hint must name the command; log:\n" + string.Join("\n", firstLog.Messages)
        );

        (int approved, string approveOutput, string approveErrors) = RunTrust(
            ["trust", "--yes"],
            worktree,
            store
        );

        Assert.Equal(TrustCommand.ExitOk, approved);
        Assert.Empty(approveErrors);
        Assert.Contains("trust: recorded extensions", approveOutput, StringComparison.Ordinal);

        (int secondExit, string secondOutput, RecordingExtensionLog secondLog) = RunPrint(
            temp,
            worktree,
            store
        );

        Assert.Equal(0, secondExit);
        Assert.Equal("ok", secondOutput);
        Assert.True(File.Exists(marker), "the trusted project extension must load");
        Assert.False(secondLog.Contains("was not trusted"));
    }

    [Fact]
    public void The_pinning_flow_binds_recording_to_the_inspected_digest()
    {
        using var temp = new TempDirectory();
        string worktree = SetupProjectExtension(temp, out string store, out _);

        (int inspectExit, string inspected, _) = RunTrust(["trust", "--json"], worktree, store);
        Assert.Equal(TrustCommand.ExitPending, inspectExit);
        string digest = JsonDocument
            .Parse(inspected)
            .RootElement.GetProperty("digest")
            .GetString()!;

        File.WriteAllText(
            Path.Combine(worktree, ".lunate", "extensions", "hello", "changed.txt"),
            "changed"
        );

        (int staleExit, string staleOutput, _) = RunTrust(
            ["trust", "--yes", "--digest", digest],
            worktree,
            store
        );

        Assert.Equal(TrustCommand.ExitDigestMismatch, staleExit);
        Assert.Contains("trust: nothing was recorded", staleOutput, StringComparison.Ordinal);
        Assert.Empty(Records(worktree, store));

        (int freshExit, string freshJson, _) = RunTrust(["trust", "--json"], worktree, store);
        Assert.Equal(TrustCommand.ExitPending, freshExit);
        string fresh = JsonDocument.Parse(freshJson).RootElement.GetProperty("digest").GetString()!;
        Assert.NotEqual(digest, fresh);

        (int approved, _, string approveErrors) = RunTrust(
            ["trust", "--yes", "--digest", fresh],
            worktree,
            store
        );

        Assert.Equal(TrustCommand.ExitOk, approved);
        Assert.Empty(approveErrors);
        Assert.Single(Records(worktree, store));
    }

    private static string SetupProjectExtension(
        TempDirectory temp,
        out string store,
        out string marker
    )
    {
        string worktree = Workspace.Canonicalize(temp.File("worktree"));
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        HelloFixture.Install(Path.Combine(worktree, ".lunate", "extensions", "hello"));
        store = temp.File("store");
        marker = temp.File("load-marker.txt");
        HelloFixture.WriteSettings(store, "hello", marker);
        return worktree;
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

    private static IReadOnlyList<TrustRecord> Records(string worktree, string store) =>
        new ExtensionTrustStore(new ExtensionHostOptions { StorePath = store }).Records(
            new Workspace(worktree).GitCommonDir ?? worktree
        );
}
