using System.Text.Json;
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
            worktree,
            worktree,
            new ExtensionSeams { StorePath = temp.File("store"), Log = log },
            Ct
        );

        Assert.Empty(host.Descriptors);
        Assert.Empty(host.Errors);
        Assert.Empty(await host.McpServers.ListToolsAsync(Ct));
        Assert.Empty(log.Messages);
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
            worktree,
            worktree,
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
            worktree,
            worktree,
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
            worktree,
            worktree,
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
            worktree,
            worktree,
            new ExtensionSeams { StorePath = temp.File("store"), Log = log },
            Ct
        );

        ExtensionSessionError error = Assert.Single(host.Errors);
        Assert.Equal("hello", error.ExtensionId);
        Assert.True(
            log.Contains(
                "project extension 'hello' was not trusted; run an interactive session to approve it."
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
                workspace.WorktreeRoot,
                workspace.GitCommonDir ?? workspace.WorktreeRoot,
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
                workspace.WorktreeRoot,
                workspace.GitCommonDir ?? workspace.WorktreeRoot,
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
                workspace.WorktreeRoot,
                workspace.GitCommonDir ?? workspace.WorktreeRoot,
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
                worktree,
                worktree,
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
