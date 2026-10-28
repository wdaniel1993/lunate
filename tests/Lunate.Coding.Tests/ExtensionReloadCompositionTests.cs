using Lunate.Extensibility;
using Lunate.Extensibility.Compile;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// <c>/extensions reload &lt;id&gt;</c> as a session command: it is refused while a turn runs (the
/// reload marks the session busy for its own stretch, so a running turn must not be able to have
/// that flag cleared under it), and a successful reload recomposes the command registry — the new
/// build's slash command is dispatchable in the same session instead of the old build's set
/// staying bound to code that is no longer loaded.
/// </summary>
[Collection("mcp-processes")]
public sealed class ExtensionReloadCompositionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>A single-file extension whose build contributes one command, <c>/ping</c>.</summary>
    private const string Source = """
        #:extension acme-tools@1.0.0
        #:command ping
        #:package Refit@8.0.0

        public static class Acme
        {
            public static void Register() { }
        }
        """;

    [Fact]
    public async Task Reload_is_refused_while_a_turn_runs()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        WriteSource(worktree);
        using var host = CreateHost(temp, worktree);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        host.Client.Gate(1);
        host.Console.SendText("hello");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await host.WaitUntilAsync(() => host.Session.IsRunning);

        await host.Session.DispatchCommandAsync("/extensions reload acme-tools", Ct);
        host.Advance(50);

        Assert.Contains("a turn is running", host.Frames, StringComparison.Ordinal);
        // The turn is still the session's active turn: the refusal happens before anything claims
        // or clears the busy flag, so the key pump keeps queueing instead of taking a new prompt.
        Assert.True(host.Session.IsRunning);
        Assert.DoesNotContain("reloaded — build", host.Frames, StringComparison.Ordinal);

        host.Client.Release(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_reloaded_extension_command_is_registered_in_the_session()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        WriteSource(worktree);
        string store = temp.File("store");
        Directory.CreateDirectory(Path.Combine(store, "extensions-settings"));
        File.WriteAllText(
            Path.Combine(store, "extensions-settings", "acme-tools.json"),
            """{"commands":[{"name":"ping"}]}"""
        );
        using var host = CreateHost(temp, worktree, store);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // Before the reload the extension is not loaded, so the command is not a command.
        host.Console.SendText("/ping");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("unknown command");

        host.Console.SendText("/extensions reload acme-tools");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("Trust the single-file extension 'acme-tools'");
        host.Console.SendText("y");
        await host.WaitForFrameAsync("extension 'acme-tools' reloaded — build ");

        // The reloaded build's command source is in the registry the session dispatches against —
        // the half that made a newly live command invisible until the next session.
        host.Console.SendText("/ping");
        host.Console.SendEnter();
        await host.WaitForFrameAsync("ping acme-tools:");

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static InteractiveSessionHost CreateHost(
        TempDirectory temp,
        string worktree,
        string? store = null
    )
    {
        var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams
            {
                StorePath = store ?? temp.File("store"),
                Log = new RecordingExtensionLog(),
                RestoreEngine = () => new OnePackageEngine(),
                Compiler = () => new FixtureCompiler(),
            },
            workingDirectory: worktree
        );
        host.Console.Size = new ConsoleSize(600, 24);
        return host;
    }

    private static void WriteSource(string worktree)
    {
        string extensionsRoot = Path.Combine(worktree, ".lunate", "extensions");
        Directory.CreateDirectory(extensionsRoot);
        File.WriteAllText(Path.Combine(extensionsRoot, "acme.cs"), Source);
    }

    /// <summary>One package, no assets: the lock binds, and the compile is the fixture's.</summary>
    private sealed class OnePackageEngine : ISingleFileRestoreEngine
    {
        public Task<SingleFileRestoreResult> RestoreAsync(
            SingleFileRestoreRequest request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new SingleFileRestoreResult
                {
                    Packages =
                    [
                        new ExtensionLockPackage
                        {
                            Id = "Refit",
                            Version = "8.0.0",
                            Sha512 = "ZmFrZQ==",
                            Requested = "Refit@8.0.0",
                            Direct = true,
                            Files = ["lib/net10.0/Refit.dll"],
                            Compile = [],
                            Runtime = [],
                        },
                    ],
                    ReferencePack = new ExtensionLockReferencePack
                    {
                        Id = "Microsoft.NETCore.App.Ref",
                        Version = "10.0.13",
                        Rule = ReferencePackRules.Exact,
                        Assets = ["ref/net10.0/System.Runtime.dll"],
                    },
                    Sources = ["local"],
                    GlobalPackagesFolder = Path.Combine(Path.GetTempPath(), "unused-packages"),
                }
            );
        }
    }

    /// <summary>
    /// The build's bytes: the command fixture's assembly, which registers the command source the
    /// settings describe — a real managed build, so the reload path loads and activates it.
    /// </summary>
    private sealed class FixtureCompiler : ISingleFileCompiler
    {
        public SingleFileCompileResult Compile(
            SingleFileCompileRequest request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            string assembly = Path.Combine(
                AppContext.BaseDirectory,
                "TestExtensions",
                "CommandExtension",
                "CommandExtension.dll"
            );
            return SingleFileCompileResult.Ok(File.ReadAllBytes(assembly));
        }
    }
}
