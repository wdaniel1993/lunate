using System.Text.Json;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;
using Lunate.Extensibility.Testing;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// One remote extension installed in a throwaway store, with the real loader over it: the manifest
/// declares the caller's hooks, services and commands, the child is a <see cref="ScriptedChild"/>
/// speaking the real wire, and the loader's log and one-line notices are recorded.
/// </summary>
internal sealed class RemoteTestWorkspace : IDisposable
{
    private readonly string _root;
    private readonly string _extensionDirectory;
    private readonly string _scriptPath;

    public RemoteTestWorkspace(
        bool project = false,
        string? startup = null,
        IReadOnlyList<string>? hooks = null,
        IReadOnlyList<string>? commands = null,
        IReadOnlyList<string>? services = null
    )
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "lunate-remote-loader-tests",
            Guid.NewGuid().ToString("N")
        );
        string worktree = Path.Combine(_root, "worktree");
        Directory.CreateDirectory(worktree);
        string storePath = Path.Combine(_root, "store");
        Directory.CreateDirectory(storePath);
        _extensionDirectory = project
            ? Path.Combine(worktree, ".lunate", "extensions", "remote-sample")
            : Path.Combine(storePath, "extensions", "remote-sample");
        Directory.CreateDirectory(_extensionDirectory);
        _scriptPath = Path.Combine(_extensionDirectory, "index.js");
        File.WriteAllText(_scriptPath, "// script");
        string runtime = startup is null
            ? "\"runtime\":{\"command\":\"node\",\"args\":[\"index.js\"]}"
            : $"\"runtime\":{{\"command\":\"node\",\"args\":[\"index.js\"],\"startup\":\"{startup}\"}}";
        File.WriteAllText(
            Path.Combine(_extensionDirectory, "extension.json"),
            $$"""
            {
                "id": "remote-sample",
                "version": "0.1.0",
                "apiVersion": "^1.0.0",
                "hooks": [{{Declarations(hooks ?? ["tool-calling"])}}],
                "services": [{{Declarations(services ?? [])}}],
                "commands": [{{Declarations(commands ?? [])}}],
                {{runtime}}
            }
            """
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

    /// <summary>Every one-line notice the host raised (an unload cancelling a command).</summary>
    public List<string> Notices { get; } = [];

    public ExtensionLoader CreateLoader()
    {
        var loader = new ExtensionLoader(
            new ExtensionHostOptions
            {
                StorePath = StorePath,
                Log = Log,
                Notify = Notices.Add,
            }
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
            TestContext.Current.CancellationToken
        );

    public void RewriteBundledScript(string content) => File.WriteAllText(_scriptPath, content);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Declarations(IReadOnlyList<string> names) =>
        string.Join(",", names.Select(name => JsonSerializer.Serialize(name)));
}
