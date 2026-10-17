using System.Text.Json;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;
using Lunate.Extensibility.Testing;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// Remote extensions through the real loader and hook runner: discovery and trust, lazy load
/// with declaration-driven proxies, background startup after the UI is ready, failed extensions
/// contained, the trust digest covering the bundled files, and the manual restart path.
/// </summary>
public sealed class LoaderRemoteTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_remote_extension_loads_lazily_and_answers_through_the_hook_runner()
    {
        using var workspace = new TempWorkspace();
        ScriptedChild child = workspace.Child;
        child.HookAnswers = (_, _, _) =>
            ValueTask.FromResult<JsonElement?>(
                HookResultCodec.EncodeResult(
                    HookContracts.Find("ToolCalling")!,
                    new ToolCallingResult.Block("held")
                )
            );
        ExtensionLoader loader = workspace.CreateLoader();

        LoadedExtension loaded = await workspace.LoadAsync(loader);

        Assert.Equal("remote-sample", loaded.Id);
        Assert.Equal(0, child.StartCount);
        Assert.Equal(1, loader.Hooks.Count("remote-sample"));

        ToolCallingResult result = await loader.Hooks.RunToolCallingAsync(
            new ToolCallingPayload(
                "r1",
                "c1",
                "read",
                JsonDocument.Parse("{}").RootElement.Clone()
            ),
            Ct
        );

        Assert.Equal(new ToolCallingResult.Block("held"), result);
        Assert.Equal(1, child.StartCount);
    }

    [Fact]
    public async Task Background_mode_starts_after_the_explicit_trigger_only()
    {
        using var workspace = new TempWorkspace(startup: "background");
        ExtensionLoader loader = workspace.CreateLoader();

        await workspace.LoadAsync(loader);

        Assert.Equal(0, workspace.Child.StartCount);

        await loader.StartBackgroundExtensionsAsync(Ct);

        Assert.Equal(1, workspace.Child.StartCount);
    }

    [Fact]
    public async Task A_refused_remote_extension_is_contained_and_the_session_continues()
    {
        using var workspace = new TempWorkspace();
        workspace.Child.Handshake = request => new InitializeResult(99, request.Hooks, []);
        ExtensionLoader loader = workspace.CreateLoader();
        await workspace.LoadAsync(loader);

        ToolCallingResult result = await loader.Hooks.RunToolCallingAsync(
            new ToolCallingPayload(
                "r1",
                "c1",
                "read",
                JsonDocument.Parse("{}").RootElement.Clone()
            ),
            Ct
        );

        // The failure policy applies: ToolCalling blocks fail-safe; nothing throws into the run.
        Assert.IsType<ToolCallingResult.Block>(result);
        Assert.Contains(
            workspace.Log.Errors,
            entry => entry.Contains("was refused", StringComparison.Ordinal)
        );
        Assert.Equal(RemoteExtensionState.Refused, loader.RemoteStatus("remote-sample")!.State);
    }

    [Fact]
    public async Task The_trust_digest_covers_the_extension_directory_contents()
    {
        using var workspace = new TempWorkspace(project: true);
        ExtensionLoader first = workspace.CreateLoader();
        var prompt = new ScriptedTrustPrompt(true);

        await workspace.LoadAsync(first, prompt);

        Assert.Equal(1, prompt.Calls);

        // Swapping a bundled file after trust re-prompts (the digest covers the directory).
        workspace.RewriteBundledScript("// changed");

        ExtensionLoader second = workspace.CreateLoader();
        var secondPrompt = new ScriptedTrustPrompt(true);
        await workspace.LoadAsync(second, secondPrompt);

        Assert.Equal(1, secondPrompt.Calls);
        Assert.True(secondPrompt.Approvals >= 1);
    }

    [Fact]
    public async Task A_manual_restart_through_the_loader_starts_a_fresh_child()
    {
        using var workspace = new TempWorkspace();
        ExtensionLoader loader = workspace.CreateLoader();
        await workspace.LoadAsync(loader);
        await loader.Hooks.RunToolCallingAsync(
            new ToolCallingPayload(
                "r1",
                "c1",
                "read",
                JsonDocument.Parse("{}").RootElement.Clone()
            ),
            Ct
        );
        Assert.Equal(1, workspace.Child.StartCount);

        Assert.True(await loader.RestartRemoteAsync("remote-sample", Ct));

        Assert.Equal(2, workspace.Child.StartCount);
        Assert.False(await loader.RestartRemoteAsync("absent", Ct));
    }

    private sealed class TempWorkspace : IDisposable
    {
        private readonly string _root;
        private readonly string _extensionDirectory;
        private readonly string _scriptPath;

        public TempWorkspace(bool project = false, string? startup = null)
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "lunate-remote-loader-tests",
                Guid.NewGuid().ToString("N")
            );
            string worktree = Path.Combine(_root, "worktree");
            System.IO.Directory.CreateDirectory(worktree);
            string storePath = Path.Combine(_root, "store");
            System.IO.Directory.CreateDirectory(storePath);
            _extensionDirectory = project
                ? Path.Combine(worktree, ".lunate", "extensions", "remote-sample")
                : Path.Combine(storePath, "extensions", "remote-sample");
            System.IO.Directory.CreateDirectory(_extensionDirectory);
            _scriptPath = Path.Combine(_extensionDirectory, "index.js");
            File.WriteAllText(_scriptPath, "// script");
            string runtime = startup is null
                ? "\"runtime\":{\"command\":\"node\",\"args\":[\"index.js\"]}"
                : $"\"runtime\":{{\"command\":\"node\",\"args\":[\"index.js\"],\"startup\":\"{startup}\"}}";
            File.WriteAllText(
                Path.Combine(_extensionDirectory, "extension.json"),
                $$"""{"id":"remote-sample","version":"0.1.0","apiVersion":"^1.0.0","hooks":["tool-calling"],{{runtime}}}"""
            );
            StorePath = storePath;
            Worktree = worktree;
            Log = new RecordingLog();
            Child = new ScriptedChild();
        }

        public string StorePath { get; }

        public string Worktree { get; }

        public RecordingLog Log { get; }

        public ScriptedChild Child { get; }

        public ExtensionLoader CreateLoader()
        {
            var loader = new ExtensionLoader(
                new ExtensionHostOptions { StorePath = StorePath, Log = Log }
            );
            loader.Services.RegisterCore("core/process", Child.Runner);
            loader.Services.RegisterCore("core/workspace", new WorkspaceInfo(Worktree, null, null));
            loader.Discover(Worktree, "repo");
            return loader;
        }

        public Task<LoadedExtension> LoadAsync(
            ExtensionLoader loader,
            ScriptedTrustPrompt? prompt = null
        ) =>
            loader.Load(
                "remote-sample",
                Worktree,
                "repo",
                prompt ?? new ScriptedTrustPrompt(true),
                Ct
            );

        public void RewriteBundledScript(string content) => File.WriteAllText(_scriptPath, content);

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
