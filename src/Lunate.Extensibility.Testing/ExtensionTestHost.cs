using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility.Testing;

/// <summary>
/// Hosts one extension for tests: it installs the extension under the caller's temp directory,
/// discovers and loads it through the real <see cref="ExtensionLoader"/> (real manifest, real load
/// context, real trust flow with a <see cref="ScriptedTrustPrompt"/>), starts the session and its
/// background services, runs the real harness against a caller-provided <see cref="IChatClient"/>,
/// and stops idempotently. Disposal unloads the extension. All state stays under the caller's temp
/// directory; the real <c>~/.lunate</c> is never touched.
/// </summary>
public sealed class ExtensionTestHost : IAsyncDisposable
{
    private readonly ExtensionTestHostOptions _options;
    private readonly FileChangeBus _fileChanges;
    private LoadedExtension? _loaded;
    private Session? _session;
    private bool _stopped;
    private bool _disposed;

    public ExtensionTestHost(ExtensionTestHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TempDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ExtensionId);
        if (options.ExtensionDirectory is null && options.SourceFiles is null)
        {
            throw new ArgumentException(
                "ExtensionTestHostOptions needs an ExtensionDirectory or SourceFiles to install.",
                nameof(options)
            );
        }

        _options = options;
        TempDirectory = Path.GetFullPath(options.TempDirectory);
        Directory.CreateDirectory(TempDirectory);
        WorkingDirectory = Path.Combine(TempDirectory, "worktree");
        Directory.CreateDirectory(WorkingDirectory);
        StorePath = Path.Combine(TempDirectory, "store");
        Directory.CreateDirectory(StorePath);
        SessionPath = Path.Combine(TempDirectory, "sessions", "session.jsonl");
        ExtensionDirectory = Path.Combine(
            WorkingDirectory,
            ".lunate",
            "extensions",
            options.ExtensionId
        );
        Log = new RecordingExtensionLog();
        Trust = new ScriptedTrustPrompt(options.TrustDecisions?.ToArray() ?? [true]);
        Events = new EventRecorder();
        Tools = new ToolRegistry();
        Runner = new ExtensionLoader(new ExtensionHostOptions { StorePath = StorePath, Log = Log });
        _fileChanges = new FileChangeBus(WorkingDirectory, Log);
        Runner.Services.RegisterCore("core/file-bus", _fileChanges);
        InstallExtension(options);
    }

    /// <summary>The caller-owned temp directory all host state lives under.</summary>
    public string TempDirectory { get; }

    /// <summary>The extension store (settings, secrets, trust) under the temp directory.</summary>
    public string StorePath { get; }

    /// <summary>The worktree the session runs in, under the temp directory.</summary>
    public string WorkingDirectory { get; }

    /// <summary>The installed project extension directory (<c>.lunate/extensions/&lt;id&gt;</c>).</summary>
    public string ExtensionDirectory { get; }

    /// <summary>The session file the runs append to, under the temp directory.</summary>
    public string SessionPath { get; }

    /// <summary>The real loader; its hook runner is wired into every run.</summary>
    public ExtensionLoader Runner { get; }

    /// <summary>Every line the loaded extension logged, in emission order.</summary>
    public RecordingExtensionLog Log { get; }

    /// <summary>The trust decisions and counts for the load.</summary>
    public ScriptedTrustPrompt Trust { get; }

    /// <summary>Every event of every run on this host, in emission order.</summary>
    public EventRecorder Events { get; }

    /// <summary>The tool registry runs execute against; add tools before running.</summary>
    public ToolRegistry Tools { get; }

    /// <summary>The loaded extension; throws before the first <see cref="StartAsync"/>.</summary>
    public LoadedExtension LoadedExtension =>
        _loaded
        ?? throw new InvalidOperationException(
            "The extension is not loaded yet; call StartAsync or RunAsync first."
        );

    /// <summary>Discovers and loads the extension and starts the session; later calls do nothing.</summary>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loaded is not null)
        {
            return;
        }

        Runner.Discover(WorkingDirectory, _options.RepositoryIdentity);
        _loaded = await Runner
            .Load(
                _options.ExtensionId,
                WorkingDirectory,
                _options.RepositoryIdentity,
                Trust,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Starts the session, runs one prompt through the real harness and drains file changes.</summary>
    public async Task<ExtensionRunResult> RunAsync(
        IChatClient client,
        string prompt,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        await StartAsync(cancellationToken).ConfigureAwait(false);

        Session session = EnsureSession();
        var harness = new AgentHarness(
            client,
            Tools,
            new AgentHarnessOptions
            {
                MaxSteps = _options.MaxSteps,
                SystemPrompt = _options.SystemPrompt,
                WorkingDirectory = WorkingDirectory,
                Approver = _options.Approver,
                Hooks = new AgentHookAdapter(Runner.Hooks),
                Session = session,
                FileChanges = _fileChanges,
            }
        );

        List<AgentEvent> events = [];
        await foreach (
            AgentEvent agentEvent in harness
                .RunAsync(prompt, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            Events.Emit(agentEvent);
            events.Add(agentEvent);
        }

        await _fileChanges.DrainAsync(cancellationToken).ConfigureAwait(false);
        return new ExtensionRunResult(events, session.Path);
    }

    /// <summary>Ends the session through the real lifecycle; later calls do nothing.</summary>
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        await Runner.EndSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops the session, unloads the extension and releases the file change bus.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        if (_loaded is not null)
        {
            await Runner.Unload(_loaded.Id).ConfigureAwait(false);
        }

        _fileChanges.Dispose();
    }

    private Session EnsureSession()
    {
        if (_session is not null)
        {
            return _session;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
        _session = File.Exists(SessionPath)
            ? Session.Load(SessionPath)
            : Session.Create(
                SessionPath,
                WorkingDirectory,
                _options.RepositoryIdentity,
                WorkingDirectory
            );
        return _session;
    }

    private void InstallExtension(ExtensionTestHostOptions options)
    {
        Directory.CreateDirectory(ExtensionDirectory);
        if (
            options.ExtensionDirectory is { } source
            && !PathsEqual(Path.GetFullPath(source), ExtensionDirectory)
        )
        {
            CopyDirectory(Path.GetFullPath(source), ExtensionDirectory);
        }

        if (options.SourceFiles is { } files)
        {
            foreach ((string relativePath, string contents) in files)
            {
                string target = Path.Combine(ExtensionDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, contents);
            }
        }

        if (!File.Exists(Path.Combine(ExtensionDirectory, "extension.json")))
        {
            throw new ArgumentException(
                $"The extension to test has no extension.json under '{ExtensionDirectory}'; provide one through ExtensionDirectory or SourceFiles.",
                nameof(options)
            );
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (
            string directory in Directory.EnumerateDirectories(
                source,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            Directory.CreateDirectory(
                Path.Combine(destination, Path.GetRelativePath(source, directory))
            );
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );
}
