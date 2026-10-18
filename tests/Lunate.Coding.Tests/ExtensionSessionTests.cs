using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The composed host contract: a session start discovers the store, loads every extension with
/// per-extension isolation, exposes the contributions and stops cleanly.
/// </summary>
public sealed class ExtensionSessionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_empty_store_composes_a_quiet_host()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        var log = new RecordingExtensionLog();

        await using var host = await ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams { StorePath = temp.File("store"), Log = log },
            Ct
        );

        Assert.Empty(host.Descriptors);
        Assert.Empty(host.Errors);
        Assert.Null(host.McpServers);
        ExtensionMcpServers servers = host.ComposeMcpServers(configuredServers: null);
        Assert.Empty(await servers.ListToolsAsync(Ct));
        Assert.Empty(log.Messages);
    }

    [Fact]
    public async Task A_composed_session_exposes_the_registered_extension_tools()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        ToolFixture.Install(store);

        await using var host = await ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            Ct
        );

        ITool tool = Assert.Single(host.Tools);
        Assert.Equal("tools__echo", tool.Name);
        Assert.Equal("tools", tool.Namespace?.Name);
    }

    [Fact]
    public async Task The_workspace_identity_is_reachable_in_a_composed_session()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        var workspace = new Workspace(worktree);

        await using var host = await ExtensionSession.StartAsync(
            workspace.Identity,
            new ExtensionSeams
            {
                StorePath = temp.File("store"),
                Log = new RecordingExtensionLog(),
            },
            Ct
        );

        Assert.True(
            host.Services.TryGetCore<WorkspaceInfo>("core/workspace", out WorkspaceInfo? found)
        );
        Assert.Equal(workspace.Identity, found);
    }

    [Fact]
    public async Task A_composed_session_exposes_the_three_core_handles()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);

        await using var host = await ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams
            {
                StorePath = temp.File("store"),
                Log = new RecordingExtensionLog(),
            },
            Ct
        );

        Assert.NotNull(host.Services.FindCore("core/workspace"));
        Assert.Same(host.FileChanges, host.Services.FindCore("core/file-bus"));
        Assert.True(
            host.Services.TryGetCore<IMutationQueue>("core/mutation-queue", out IMutationQueue? _)
        );
    }

    [Fact]
    public async Task A_change_enqueued_just_before_disposal_still_reaches_a_loaded_subscriber()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        var log = new RecordingExtensionLog();

        var host = await ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams { StorePath = store, Log = log },
            Ct
        );
        string path = Path.Combine(worktree, "late.cs");
        host.FileChanges.Notify(
            new FileChangeEvent(path, "late.cs", worktree, "run_drain", FileChangeKind.Created)
        );

        await host.DisposeAsync();

        Assert.True(
            log.Contains($"hello file changed: path={path}"),
            "the drain must deliver a change enqueued just before disposal; log:\n"
                + string.Join("\n", log.Messages)
        );

        // A notification after disposal is dropped, not thrown.
        host.FileChanges.Notify(
            new FileChangeEvent(path, "late.cs", worktree, "run_drain", FileChangeKind.Modified)
        );
    }

    [Fact]
    public async Task The_mutation_queue_handle_serializes_with_a_core_write_on_one_path()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);

        await using var host = await ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams
            {
                StorePath = temp.File("store"),
                Log = new RecordingExtensionLog(),
            },
            Ct
        );

        Assert.True(
            host.Services.TryGetCore<IMutationQueue>(
                "core/mutation-queue",
                out IMutationQueue? queue
            ) && queue is not null
        );
        var workspace = new Workspace(worktree);
        string path = Path.Combine(workspace.WorktreeRoot, "shared.txt");
        File.WriteAllText(path, "start\n");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // An extension schedules its read-modify-write through the handle it looked up.
        Task<int> scheduled = queue.RunAsync(
            path,
            async _ =>
            {
                entered.SetResult();
                await release.Task;
                File.WriteAllText(path, File.ReadAllText(path) + "ext\n");
                return 0;
            },
            Ct
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Task<ToolResult> write = new WriteTool(workspace).ExecuteAsync(
            Args("""{"path":"shared.txt","content":"core\n"}"""),
            new ToolContext(workspace.WorktreeRoot, new NullAgentEvents()),
            Ct
        );

        Assert.False(
            write.IsCompleted,
            "the core write must wait behind the extension's queued mutation"
        );
        Assert.Equal("start\n", File.ReadAllText(path));

        release.SetResult();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        ToolResult result = await write.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.False(result.IsError);
        Assert.Equal("core\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task A_global_extension_loads_exposes_its_contributions_and_stops_on_dispose()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        var log = new RecordingExtensionLog();

        var host = await ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams { StorePath = store, Log = log },
            Ct
        );
        try
        {
            Assert.Single(host.Descriptors);
            Assert.Empty(host.Errors);
            Assert.True(log.Contains("created hello"));
            Assert.True(log.Contains("hello session started"));

            Assert.True(
                host.ToolResultRenderers.TryRender(
                    "hello-tool",
                    new ToolResultView("hello-tool", "call-1", "out", false),
                    out IReadOnlyList<TextSegment> segments
                )
            );
            Assert.Equal("hello-rendered out", Assert.Single(segments).Text);

            IReadOnlyList<TextSegment> widgets = host.StatusWidgets.Segments(
                new StatusView("model", 0, 0, worktree, null)
            );
            Assert.Contains(widgets, segment => segment.Text == "hello-widget");
        }
        finally
        {
            await host.DisposeAsync();
        }

        Assert.True(log.Contains("hello session ended"));
        await host.DisposeAsync();
        Assert.Equal(
            1,
            log.Messages.Count(message =>
                message.Contains("hello session ended", StringComparison.Ordinal)
            )
        );
    }

    [Fact]
    public async Task Every_SessionStarted_handler_observes_the_session_with_the_full_extension_set()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        ServiceConsumerFixture.Install(Path.Combine(store, "extensions", "consumer"));
        var log = new RecordingExtensionLog();

        await using var host = await ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams { StorePath = store, Log = log },
            Ct
        );

        // The consumer sorts before hello: its handler can only resolve the provider's service
        // when the event fires after every extension has loaded, and hello's handler (registered
        // by the later load) can only run at all under the same rule.
        Assert.True(
            log.Contains("consumer session-started lookups: provider=True, absent=False"),
            "the consumer's SessionStarted handler did not observe the full extension set; log:\n"
                + string.Join("\n", log.Messages)
        );
        Assert.True(
            log.Contains("hello session started"),
            "the later-loaded extension's SessionStarted handler did not run; log:\n"
                + string.Join("\n", log.Messages)
        );
    }

    [Fact]
    public async Task A_session_start_cancelled_during_the_dispatch_stops_what_already_started()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        ServiceConsumerFixture.Install(
            Path.Combine(store, "extensions", "consumer"),
            slowSessionStartedMs: 30_000
        );
        var log = new RecordingExtensionLog();
        using var cancellation = new CancellationTokenSource();

        Task<ExtensionSession> compose = ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams { StorePath = store, Log = log },
            cancellation.Token
        );

        // The consumer's SessionStarted handler parks on a 30 s cancellable delay and runs first in
        // the dispatch (discovery sorts by directory name, both handlers Priority 0, so registration
        // order = load order — the sibling test's mechanism); cancel while it is in flight — the
        // compose must stop what already started (hello's background service, started at its load)
        // and release the bus before surfacing, exactly as a cancelled load does. The bus's dispose
        // is not separately asserted: no change is enqueued here, so the pump never starts and a
        // missing Dispose() would be invisible; the observable leak is the background service.
        await WaitUntilAsync(() => log.Contains("consumer slow session-started entered"));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compose);
        Assert.True(
            log.Contains("hello service stopped"),
            "the started background service was not stopped after a cancelled session-start dispatch; log:\n"
                + string.Join("\n", log.Messages)
        );
        Assert.False(
            log.Contains("consumer slow session-started completed"),
            "the cancelled handler resumed; log:\n" + string.Join("\n", log.Messages)
        );
        Assert.False(
            log.Contains("hello session started"),
            "handlers kept dispatching after cancellation; log:\n" + string.Join("\n", log.Messages)
        );
        // The cancelled dispatch still counts as fired (the flag is set before it), so the
        // cleanup's session-ending pass runs — hello's ending handler fires even though its
        // started handler never got its turn. Pre-existing rule (ending follows a begun session);
        // only the roster grew, since every extension now loads before the dispatch.
        Assert.True(
            log.Contains("hello session ended"),
            "the session-ending pass did not run after a cancelled dispatch; log:\n"
                + string.Join("\n", log.Messages)
        );
    }

    [Fact]
    public async Task One_broken_extension_is_reported_and_the_session_continues()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        WriteBrokenManifest(Path.Combine(store, "extensions", "broken"), "broken");
        var log = new RecordingExtensionLog();

        await using var host = await ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams { StorePath = store, Log = log },
            Ct
        );

        Assert.Equal(2, host.Descriptors.Count);
        ExtensionSessionError error = Assert.Single(host.Errors);
        Assert.Equal("broken", error.ExtensionId);
        Assert.Contains("Broken.dll", error.Message, StringComparison.Ordinal);
        Assert.True(log.Contains("Broken.dll"));
        Assert.True(
            log.Contains("created hello"),
            "the healthy extension must still load when a sibling fails"
        );
    }

    [Fact]
    public async Task A_denied_project_trust_is_reported_and_the_session_continues()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        string extensionDirectory = Path.Combine(worktree, ".lunate", "extensions", "hello");
        HelloFixture.Install(extensionDirectory);
        string store = temp.File("store");
        var log = new RecordingExtensionLog();
        var prompt = new ScriptedTrustPrompt(false);

        await using var host = await ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams
            {
                StorePath = store,
                Prompt = prompt,
                Log = log,
            },
            Ct
        );

        Assert.Single(host.Descriptors);
        ExtensionSessionError error = Assert.Single(host.Errors);
        Assert.Equal("hello", error.ExtensionId);
        Assert.Contains("was not trusted", error.Message, StringComparison.Ordinal);
        Assert.True(log.Contains("was not trusted"));
        Assert.False(log.Contains("created hello"));
        Assert.False(File.Exists(Path.Combine(store, "trust.json")));
    }

    [Fact]
    public async Task The_default_prompt_refuses_project_extensions_and_logs_why()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        string extensionDirectory = Path.Combine(worktree, ".lunate", "extensions", "hello");
        HelloFixture.Install(extensionDirectory);
        var log = new RecordingExtensionLog();

        await using var host = await ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams { StorePath = temp.File("store"), Log = log },
            Ct
        );

        ExtensionSessionError error = Assert.Single(host.Errors);
        Assert.Equal("hello", error.ExtensionId);
        Assert.True(
            log.Contains(
                "project extension 'hello' was not trusted; run an interactive session or 'lunate trust' to approve it."
            )
        );
        Assert.False(log.Contains("created hello"));
    }

    [Fact]
    public async Task Project_trust_recorded_outside_a_repository_keys_on_the_worktree_root()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(worktree, ".lunate", "extensions", "hello"));
        var workspace = new Workspace(worktree);
        var prompt = new ScriptedTrustPrompt(true);
        var log = new RecordingExtensionLog();

        await using (
            await ExtensionSession.StartAsync(
                workspace.Identity,
                new ExtensionSeams
                {
                    StorePath = store,
                    Prompt = prompt,
                    Log = log,
                },
                Ct
            )
        ) { }

        Assert.Equal(1, prompt.Calls);
        Assert.Equal([Workspace.Canonicalize(worktree)], TrustedRepositories(store));

        var secondPrompt = new ScriptedTrustPrompt();
        var secondLog = new RecordingExtensionLog();
        await using (
            await ExtensionSession.StartAsync(
                workspace.Identity,
                new ExtensionSeams
                {
                    StorePath = store,
                    Prompt = secondPrompt,
                    Log = secondLog,
                },
                Ct
            )
        ) { }

        Assert.Equal(0, secondPrompt.Calls);
        Assert.True(secondLog.Contains("created hello"));
    }

    [Fact]
    public async Task Project_trust_inside_a_repository_keys_on_the_git_common_dir()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(worktree, ".lunate", "extensions", "hello"));
        var workspace = new Workspace(worktree);
        var prompt = new ScriptedTrustPrompt(true);

        await using (
            await ExtensionSession.StartAsync(
                workspace.Identity,
                new ExtensionSeams
                {
                    StorePath = store,
                    Prompt = prompt,
                    Log = new RecordingExtensionLog(),
                },
                Ct
            )
        ) { }

        Assert.NotNull(workspace.GitCommonDir);
        Assert.Equal(
            [Workspace.Canonicalize(Path.Combine(worktree, ".git"))],
            TrustedRepositories(store)
        );
    }

    [Fact]
    public async Task A_composed_session_records_one_load_result_per_descriptor()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        BrokenFixture.Install(store);
        HelloFixture.Install(
            Path.Combine(worktree, ".lunate", "extensions", "hello-project"),
            id: "hello-project"
        );

        await using var host = await ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams
            {
                StorePath = store,
                Prompt = new ScriptedTrustPrompt(false),
                Log = new RecordingExtensionLog(),
            },
            Ct
        );

        Assert.Equal(["broken", "hello", "hello-project"], host.LoadResults.Select(r => r.Id));
        Assert.Equal(ExtensionLoadState.Failed, host.LoadResults[0].State);
        Assert.Contains("Broken.dll", host.LoadResults[0].Message, StringComparison.Ordinal);
        Assert.Equal(ExtensionLoadState.Loaded, host.LoadResults[1].State);
        Assert.Null(host.LoadResults[1].Message);
        Assert.Equal(ExtensionLoadState.TrustDenied, host.LoadResults[2].State);
        Assert.All(
            host.LoadResults,
            result =>
            {
                Assert.True(result.Elapsed >= TimeSpan.Zero);
                Assert.True(
                    result.Elapsed < TimeSpan.FromMinutes(1),
                    "the recorded load time must be this load's elapsed time"
                );
            }
        );
    }

    [Fact]
    public async Task Inspect_mode_loads_and_registers_but_starts_nothing_and_ends_cleanly()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        var log = new RecordingExtensionLog();

        var host = await ExtensionSession.StartAsync(
            Identity(worktree),
            new ExtensionSeams
            {
                StorePath = store,
                Log = log,
                Inspect = true,
            },
            Ct
        );
        try
        {
            Assert.True(log.Contains("created hello"));
            Assert.Equal(ExtensionLoadState.Loaded, Assert.Single(host.LoadResults).State);
            Assert.False(log.Contains("hello service started"), "no background service may start");
            Assert.False(log.Contains("hello session started"), "no session-start hook may run");
            Assert.Equal(new ExtensionCounts(0, 0, 3, 1), host.RegisteredCounts("hello"));

            await host.UnloadAsync("hello");
            Assert.Equal(new ExtensionCounts(0, 0, 0, 0), host.RegisteredCounts("hello"));
        }
        finally
        {
            await host.DisposeAsync();
        }

        // EndSessionAsync must no-op when the session never started.
        Assert.False(log.Contains("hello service stopped"), "no background service may stop");
        Assert.False(log.Contains("hello session ended"), "no session-ending hook may run");
    }

    private static WorkspaceInfo Identity(string worktree) =>
        new(Workspace.Canonicalize(worktree), null, null);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50, Ct);
        }

        Assert.Fail("condition not reached within 30s");
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IReadOnlyList<string> TrustedRepositories(string storePath)
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(storePath, "trust.json"))
        );
        return
        [
            .. document
                .RootElement.GetProperty("repositories")
                .EnumerateObject()
                .Select(p => p.Name),
        ];
    }

    private static void WriteBrokenManifest(string directory, string id)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "extension.json"),
            $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"^1.5.0","entryAssembly":"Broken.dll"}"""
        );
    }

    [Fact]
    public async Task Cancellation_mid_compose_stops_what_already_started()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello-one"), id: "hello-one");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello-two"), id: "hello-two");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        // The first extension's background service is up when its start line is logged; cancel
        // right there so the second extension's load observes cancellation while the first is
        // still running. The host must stop it before surfacing the cancellation.
        var log = new CancelOnMessageLog(cts, "hello service started");

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ExtensionSession.StartAsync(
                Identity(worktree),
                new ExtensionSeams { StorePath = store, Log = log },
                cts.Token
            )
        );

        Assert.True(log.Contains("hello service started"));
        Assert.True(log.Contains("hello service stopped"));
    }

    /// <summary>Cancels a source the moment a matching log line is recorded.</summary>
    private sealed class CancelOnMessageLog(CancellationTokenSource source, string trigger)
        : IExtensionLog
    {
        private readonly object _gate = new();
        private readonly List<string> _messages = [];

        public void Info(string message) => Add(message);

        public void Warn(string message) => Add(message);

        public void Error(string message) => Add(message);

        public bool Contains(string text)
        {
            lock (_gate)
            {
                return _messages.Any(message => message.Contains(text, StringComparison.Ordinal));
            }
        }

        private void Add(string message)
        {
            lock (_gate)
            {
                _messages.Add(message);
            }

            if (message.Contains(trigger, StringComparison.Ordinal))
            {
                source.Cancel();
            }
        }
    }
}
