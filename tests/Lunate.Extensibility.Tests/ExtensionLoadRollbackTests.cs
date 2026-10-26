using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// A failed or cancelled load leaves no trace: one teardown drops every registration the load made —
/// hooks, tools, models, renderers, widgets, MCP servers, skill sources, command sources, file
/// subscriptions and background services — and forgets that the extension loaded, so a retry is a
/// first load and nothing half-registered answers a hook, a call, a status line or a file change.
/// </summary>
public sealed class ExtensionLoadRollbackTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private const string LeakToolName = "leak_probe";
    private const string LeakModelId = "leak-model";

    /// <summary>
    /// A declared provider serving the model id the fixture's code already serves: the model
    /// registry refuses the declaration, which is a failure after every registration happened.
    /// </summary>
    private const string DeclaredProviderServingTheCodeModel = """
        [{"id":"declared","displayName":"Declared","endpoint":"https://example.test/v1","secretName":"token","modelIds":["leak-model"],"contextWindow":4096}]
        """;

    [Fact]
    public async Task A_failed_load_leaves_no_registration_behind()
    {
        using var temp = new TempDirectory();
        var log = new RecordingExtensionLog();
        TestExtensions.InstallLeakExtension(
            temp,
            "leak",
            modelProviders: DeclaredProviderServingTheCodeModel
        );
        ExtensionLoader loader = CreateLoader(temp, log, out FileChangeBus bus);
        using (bus)
        {
            await Assert.ThrowsAsync<ExtensionLoadException>(() =>
                loader.Load("leak", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct)
            );

            Assert.DoesNotContain(
                log.Messages,
                message => message.Contains("leak service started", StringComparison.Ordinal)
            );
            await AssertEverythingDroppedAsync(loader, log, bus);
        }
    }

    [Fact]
    public async Task A_cancelled_load_leaves_no_registration_behind()
    {
        using var temp = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        TestExtensions.InstallLeakExtension(temp, "leak");
        // The fixture's background service is the last place in the load that observes the token,
        // and it is reached only after every registration and the loaded marks: cancelling from its
        // line ends a load that got all the way to session start.
        var log = new RecordingExtensionLog
        {
            OnMessage = message =>
            {
                if (string.Equals(message, "leak service starting", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                }
            },
        };
        ExtensionLoader loader = CreateLoader(temp, log, out FileChangeBus bus);
        using (bus)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                loader.Load(
                    "leak",
                    temp.Root,
                    "repo",
                    new FakeExtensionTrustPrompt(true),
                    cancellation.Token
                )
            );

            await AssertEverythingDroppedAsync(loader, log, bus);

            // The failed load forgot itself: loading again is a first load, not "already loaded".
            LoadedExtension loaded = await loader.Load(
                "leak",
                temp.Root,
                "repo",
                new FakeExtensionTrustPrompt(true),
                Ct
            );

            Assert.Equal("leak", loaded.Id);
            Assert.Equal(1, loader.Hooks.Count("leak"));
        }
    }

    [Fact]
    public async Task A_failed_load_can_be_retried_and_loads_once_the_cause_is_gone()
    {
        using var temp = new TempDirectory();
        var log = new RecordingExtensionLog();
        // The code serves the id the manifest's declared provider also serves: the declaration is
        // refused after the factory registered everything.
        TestExtensions.InstallLeakExtension(
            temp,
            "leak",
            modelProviders: DeclaredProviderServingTheCodeModel
        );
        ExtensionLoader loader = CreateLoader(temp, log, out FileChangeBus bus);
        using (bus)
        {
            await Assert.ThrowsAsync<ExtensionLoadException>(() =>
                loader.Load("leak", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct)
            );

            // Nothing of the failed load says "already loaded": repairing the manifest and loading
            // again is a first load.
            TestExtensions.InstallLeakExtension(temp, "leak");
            loader.Discover(temp.Root, "repo");
            LoadedExtension loaded = await loader.Load(
                "leak",
                temp.Root,
                "repo",
                new FakeExtensionTrustPrompt(true),
                Ct
            );

            Assert.Equal("leak", loaded.Id);
            Assert.Equal(1, loader.Hooks.Count("leak"));
            Assert.Single(loader.Tools.List());
        }
    }

    private static ExtensionLoader CreateLoader(
        TempDirectory temp,
        RecordingExtensionLog log,
        out FileChangeBus bus
    )
    {
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");
        bus = new FileChangeBus(Path.GetFullPath(temp.Root), log);
        loader.Services.RegisterCore("core/file-bus", bus);
        return loader;
    }

    /// <summary>
    /// Every registry the failed load could have touched is empty, and neither a file change nor a
    /// status update reaches the extension's contributions.
    /// </summary>
    private static async Task AssertEverythingDroppedAsync(
        ExtensionLoader loader,
        RecordingExtensionLog log,
        FileChangeBus bus
    )
    {
        Assert.Empty(loader.Tools.List());
        Assert.Equal(0, loader.Hooks.Count("leak"));
        Assert.Empty(loader.ModelRegistry.List());
        Assert.Null(loader.ModelRegistry.FindCapabilities(LeakModelId));
        Assert.False(
            loader.ToolResultRenderers.TryRender(
                LeakToolName,
                new ToolResultView(LeakToolName, "call-1", "text", IsError: false),
                out _
            )
        );
        Assert.Empty(
            loader.StatusWidgets.Segments(new StatusView("model", 0, 0, "/work", GitBranch: null))
        );
        Assert.Empty(loader.McpServers.List());
        Assert.Empty(loader.SkillSources.List());
        Assert.Empty(loader.CommandSources.List());
        Assert.Equal(0, loader.Services.Count("leak"));
        Assert.False(loader.Services.TryGet("ext/leak/probe", out _));

        bus.Notify(Changes.Event("/work/leak.cs", "leak.cs"));
        await bus.DrainAsync(Ct);
        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("leak file changed", StringComparison.Ordinal)
        );

        // The session-started hook is gone too: dispatching it reaches no handler of the failed load.
        await loader.Hooks.RunSessionStartedAsync(new SessionStartedPayload("/work", "repo"), Ct);
        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("leak session started", StringComparison.Ordinal)
        );
    }
}
