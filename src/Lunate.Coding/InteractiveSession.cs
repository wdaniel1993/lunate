using System.Globalization;
using System.Reactive.Concurrency;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;

namespace Lunate.Coding;

/// <summary>The injectable seams of an interactive session; production leaves paths null.</summary>
internal sealed record InteractiveSessionOptions
{
    public IChatClientFactory? Factory { get; init; }

    public string? SettingsPath { get; init; }

    public string? ModelsPath { get; init; }

    public string? AuthPath { get; init; }

    public string? SessionDirectory { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? HistoryPath { get; init; }

    public Func<string, string?>? Environment { get; init; }

    /// <summary>The terminal the session reads keys from and paints the live area through.</summary>
    public required IConsoleIO Console { get; init; }

    /// <summary>The workspace file seam behind the lazy path index; null walks the real workspace.</summary>
    public IWorkspaceFiles? WorkspaceFiles { get; init; }

    /// <summary>The scheduler for the live area and the Ctrl+C window.</summary>
    public required IScheduler Scheduler { get; init; }

    /// <summary>The scrollback console; null renders through the terminal (plain when not interactive).</summary>
    public IAnsiConsole? Scrollback { get; init; }

    /// <summary>The extension hook runner behind the input pipeline; null passes input through.</summary>
    public HookRunner? Hooks { get; init; }

    /// <summary>The tool-result renderers the extension host registered; null renders no extension blocks.</summary>
    public ToolResultRendererRegistry? ToolResultRenderers { get; init; }

    /// <summary>The status widgets the extension host registered; null contributes no widget segments.</summary>
    public StatusWidgetRegistry? StatusWidgets { get; init; }

    /// <summary>The extension host seams; null takes the store, prompt and log defaults.</summary>
    public ExtensionSeams? Extensions { get; init; }

    /// <summary>The global mcp.json path; null uses <c>~/.lunate/mcp.json</c>.</summary>
    public string? McpConfigPath { get; init; }

    /// <summary>The standard user-scope mcp.json path; null uses <c>~/.agents/mcp.json</c>.</summary>
    public string? McpAgentsConfigPath { get; init; }

    /// <summary>The user-profile root the skill user scope resolves under; null uses the profile.</summary>
    public string? HomeDirectory { get; init; }

    /// <summary>The clipboard source behind <c>@clipboard</c>; null uses the platform helper chain.</summary>
    public IClipboardImageSource? Clipboard { get; init; }

    /// <summary>The text clipboard behind <c>/copy</c>; null composes the platform transport.</summary>
    public ITextClipboard? TextClipboard { get; init; }

    /// <summary>The wall clock the export file name reads; null uses the local time.</summary>
    public Func<DateTimeOffset>? Clock { get; init; }

    /// <summary>Where composition errors are reported; null uses stderr.</summary>
    public TextWriter? Diagnostics { get; init; }

    /// <summary>Observes each event of a run before the session renders it; null observes nothing.</summary>
    public Action<AgentEvent>? EventObserver { get; init; }

    /// <summary>Adjusts the composed harness options; tests pin budgets and retry delays here.</summary>
    public Func<AgentHarnessOptions, AgentHarnessOptions>? ConfigureHarness { get; init; }

    /// <summary>The launch flags parsed once by the CLI; defaults stand in for direct calls.</summary>
    public LaunchOptions Launch { get; init; } = new();

    /// <summary>
    /// The masked secret prompt behind <c>/login</c>; null uses the live-area prompt. Print and ACP
    /// modes never construct an interactive session.
    /// </summary>
    public ISecretPrompt? SecretPrompt { get; init; }

    /// <summary>The session file to start from; null creates a fresh session.</summary>
    public string? StartSession { get; init; }

    /// <summary>Open the session picker once composition finished; the CLI's <c>--resume</c>.</summary>
    public bool StartWithPicker { get; init; }

    /// <summary>The session store root for id and <c>--continue</c> lookup; null uses the profile.</summary>
    public string? SessionsRoot { get; init; }
}

/// <summary>
/// The interactive frontend: it consumes the harness's event stream into scrollback and the live
/// area, wires keys through the router, history and quit window, and drains the follow-up queue
/// after every turn. The pinned interaction rules - Enter queues while a turn runs, Esc interrupts
/// and the queue then runs next, and Up takes the whole queue back - live here.
/// </summary>
internal sealed partial class InteractiveSession : IDisposable
{
    private readonly InteractiveSessionOptions _options;
    private readonly IConsoleIO _console;
    private readonly TerminalCapabilities _capabilities;
    private readonly LiveArea _live;
    private InputPipeline _pipeline;
    private ImageInputResolver _imageResolver = null!;
    private readonly InputHistory _history;
    private readonly Stack<InputLineState> _undo = new();
    private readonly QuitWindow _quitWindow;
    private HistorySearch? _historySearch;
    private string _killRing = string.Empty;
    private string? _stashedDraft;
    private readonly QuitWindow _exitWindow;
    private readonly IUserInteraction _interaction;
    private readonly ISecretPrompt _secretPrompt;
    private readonly SteeringQueue _steering = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ToolBlockRenderer _toolBlocks;
    private readonly ThinkingCommitRenderer _thinkingCommit = new();
    private readonly UserEchoRenderer _userEcho = new();
    private readonly MarkdownRenderer _markdown;
    private readonly Dictionary<(string RunId, string CallId), string> _toolNames = [];
    private readonly Dictionary<(string RunId, string CallId), string> _toolArgs = [];

    /// <summary>When each in-flight call started, on the injected scheduler's clock.</summary>
    private readonly Dictionary<(string RunId, string CallId), DateTimeOffset> _toolStartedAt = [];
    private readonly List<(string RunId, string CallId)> _activeCalls = [];

    /// <summary>The active calls that spawned a live child run; their carried result indents.</summary>
    private readonly HashSet<(string RunId, string CallId)> _spawningCalls = [];
    private int _liveChildren;

    /// <summary>The viewer's store: every committed item, in commit order.</summary>
    private readonly List<TranscriptEntry> _transcript = [];

    /// <summary>The full-screen transcript viewer; lazy until ctrl+o opens it.</summary>
    private readonly TranscriptViewer _viewer;
    private readonly HashSet<string> _alwaysApproved = new(StringComparer.Ordinal);
    private AgentSettings _settings = new();
    private ModelCatalog _catalog = null!;
    private AuthStore _auth = null!;
    private Func<string, string?> _environment = Environment.GetEnvironmentVariable;
    private IChatClientFactory _factory = null!;
    private Workspace _workspace = null!;
    private ToolRegistry _tools = null!;
    private WorkspaceServices? _workspaceServices;
    private Func<string, ToolRegistry>? _workspaceTools;
    private readonly List<ITool> _inheritedTools = [];
    private Session? _session;
    private bool _fileless;
    private SkillCatalog? _skills;
    private CommandRegistry _commands = CommandRegistry.Empty;
    private string? _skillSection;
    private readonly SkillReadAllowance _skillAllowance = new();
    private string _sessionDirectory = string.Empty;
    private ModelInfo _model = null!;
    private ThinkingLevel _thinkingLevel;
    private AgentHarness? _harness;
    private SessionApprover? _approver;
    private int _reloadGate;
    private InputLineState _input = InputLine.Empty;
    private string? _historyDraft;
    private Task? _run;
    private KeyHelpModel? _keyHelp;
    private bool _keyHelpFiltering;
    private volatile bool _turnActive;
    private CancellationTokenSource? _runCancellation;
    private ApprovalPromptModel? _approvalPrompt;
    private TaskCompletionSource<ApprovalChoice>? _approvalDecision;
    private TrustPromptModel? _trustPrompt;
    private TaskCompletionSource<TrustChoice>? _trustDecision;
    private long _keySequence;
    private long _trustArmSequence;
    private int _trustAsked;
    private int _trustTotal;
    private SelectListModel? _picker;
    private PickerKind _pickerKind;
    private List<string> _pickerLabels = [];
    private List<string> _pickerValues = [];
    private List<string> _pickerVisibleValues = [];
    private string _pickerTitlePrefix = string.Empty;
    private string _pickerFilter = string.Empty;
    private FileIndex? _fileIndex;
    private string _tail = string.Empty;
    private string _thinking = string.Empty;

    /// <summary>When the open reasoning span began, on the injected scheduler's clock.</summary>
    private DateTimeOffset _thinkingStartedAt;
    private long _inputTokens;
    private long _outputTokens;

    /// <summary>The current turn's usage records: the cost aggregator's per-record input.</summary>
    private readonly List<(string? ModelId, UsageLanes Lanes)> _turnUsage = [];

    /// <summary>The current turn's fully priced cost; null while unpriced or before its usage.</summary>
    private decimal? _turnCost;

    /// <summary>Accumulated prompt-cache reads; null until a provider reports one (display-only).</summary>
    private long? _cacheReadTokens;

    /// <summary>Accumulated prompt-cache writes; null until a provider reports one (display-only).</summary>
    private long? _cacheWriteTokens;

    /// <summary>The provider's additional-count key for cache-creation tokens.</summary>
    private const string CacheCreationInputTokensKey = "CacheCreationInputTokens";

    /// <summary>The cumulative token counts when the current run started (its deltas' baseline).</summary>
    private long _runInputBaseline;
    private long _runOutputBaseline;
    private string _modelId = "unknown";
    private string? _extensionModelId;
    private string? _modelRoute;
    private long _contextWindow;
    private string _workingDirectory = string.Empty;
    private string? _branch;
    private ExtensionSession? _extensions;
    private McpToolSet? _mcp;
    private readonly List<UserNotice> _pendingNotices = [];
    private bool _composed;
    private bool _promptEchoed;
    private bool _turnWroteOutput;

    /// <summary>
    /// Whether the most recent turn ended with an answer (stop or length) rather than an error or
    /// a cancellation. <c>/copy</c> reads it to skip a failed tail; a turn in flight is not a
    /// completed answer either.
    /// </summary>
    private bool _lastTurnCompleted;
    private bool _headerCountsPending;
    private ToolResultRendererRegistry? _toolResultRenderers;
    private StatusWidgetRegistry? _statusWidgets;
    private bool _disposed;

    public InteractiveSession(InteractiveSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _console = options.Console;
        _capabilities = TerminalCapabilities.Detect(
            options.Console.IsInteractive,
            OperatingSystem.IsWindows(),
            options.Environment
        );
        _live = new LiveArea(
            options.Console,
            options.Scheduler,
            readKeys: false,
            scrollback: options.Scrollback,
            capabilities: _capabilities
        );
        _markdown = new MarkdownRenderer(_capabilities);
        _toolBlocks = new ToolBlockRenderer(_capabilities);
        _viewer = new TranscriptViewer(_console, _capabilities, options.HomeDirectory);
        _pipeline = new InputPipeline(options.Hooks);
        _history = new InputHistory(options.HistoryPath ?? InputHistory.DefaultPath);
        _quitWindow = QuitWindow.ForCtrlC(options.Scheduler);
        _exitWindow = QuitWindow.ForExit(options.Scheduler);
        _interaction = new TuiUserInteraction(_live);
        _secretPrompt = options.SecretPrompt ?? new TuiSecretPrompt(_live);
    }

    /// <summary>Runs the input loop until the terminal's key stream ends or the quit window quits.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        // The tui capability requires raw mode through the platform APIs: in a canonical
        // terminal Enter arrives as LF and can never submit. A terminal that cannot switch
        // modes fails soft with the platform's actionable message instead of an exception.
        IDisposable rawMode;
        try
        {
            rawMode = _console.EnterRawMode();
        }
        catch (InvalidOperationException exception)
        {
            _console.Write(exception.Message + Environment.NewLine);
            return;
        }

        using (rawMode)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            _live.Start();
            _live.SetLoading(_capabilities.Unicode ? "loading project…" : "loading project...");
            Task pump = PumpKeysAsync(linked.Token);
            try
            {
                await ComposeAsync(linked.Token);
                _composed = true;
                _live.SetLoading(null);
                FlushPendingNotices();
                UpdateStatus();
                // --resume (or the injected seam) opens the existing picker once composition
                // finished: the listing needs the session directory and the live area is ready.
                if (_options.StartWithPicker || _options.Launch.Resume)
                {
                    OpenSessionPicker();
                }
                // Background remote extensions start only now, after the UI is ready; the startup
                // path and its budget never touch them.
                if (_extensions is { } backgroundHost)
                {
                    _ = StartBackgroundExtensionsAsync(backgroundHost, linked.Token);
                }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                // A quit (or the caller's token) during composition: never surface it as an error.
            }
            catch (Exception exception)
            {
                // An unknown --tools name or an unknown --model is a usage error (design D1),
                // exactly as in print mode; every other composition failure is a run error.
                ExitCode = exception switch
                {
                    ToolFilterException => 2,
                    UnknownModelException when _options.Launch.Model is not null => 2,
                    _ => 1,
                };
                _lifetime.Cancel();
                (_options.Diagnostics ?? Console.Error).WriteLine($"lunate: {exception.Message}");
            }

            try
            {
                await pump;
            }
            catch (OperationCanceledException) { }
            finally
            {
                _lifetime.Cancel();
                if (_run is { } run)
                {
                    try
                    {
                        await run;
                    }
                    catch (OperationCanceledException) { }
                }

                if (_bashRun is { } bashRun)
                {
                    try
                    {
                        await bashRun;
                    }
                    catch (OperationCanceledException) { }
                }

                await DisposeExtensionsAsync();
                _workspaceServices?.Dispose();
                _workspaceServices = null;
                // Every exit path leaves the viewer's alternate screen, exceptions included.
                _viewer.Close();
                _live.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    /// <summary>
    /// Fires the background startup without blocking the UI; per-extension failures are already
    /// contained and logged by the host.
    /// </summary>
    private static async Task StartBackgroundExtensionsAsync(
        ExtensionSession host,
        CancellationToken ct
    )
    {
        try
        {
            await host.StartBackgroundAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            host.Log.Warn($"background extension startup failed: {exception.Message}");
        }
    }

    private async Task ComposeAsync(CancellationToken ct)
    {
        _settings = SettingsStore.Resolve(_options.SettingsPath, _options.Environment);
        _settings = SettingsStore.ApplyLaunch(_settings, _options.Launch);
        _thinkingLevel = _settings.Thinking;
        _catalog = ModelCatalog.Load(_options.ModelsPath);
        _environment = _options.Environment ?? Environment.GetEnvironmentVariable;
        _auth = AuthStore.Load(_options.AuthPath, _options.Environment);
        _factory = _options.Factory ?? HarnessFactory.CreateFactory(_auth);
        _workspace = new Workspace(_options.WorkingDirectory ?? Environment.CurrentDirectory);
        // Before anything of this session's own state is created: a store inside the tree this
        // session governs could have its trust records written by that tree.
        LunateHome.StoreOutsideWorkspace(_workspace.WorktreeRoot, _workspace.RepoRoot);
        _workspaceServices = new WorkspaceServices(_settings.Roslyn.MaxLoadedWorkspaces);
        _sessionDirectory =
            _options.SessionDirectory ?? HarnessFactory.DefaultSessionDirectory(_workspace);
        StartInitialSession();
        _workingDirectory = DisplayDirectory(_workspace.WorktreeRoot);
        _branch = GitBranchReader.Read(_workspace.WorktreeRoot);
        // The welcome header prints here, on the first frame and before any composition output:
        // the loading line keeps running underneath and nothing blocks on it (design D1).
        PrintWelcomeHeader(counts: null);
        _trustTotal = CountPendingTrustQuestions();
        _approver = new SessionApprover(this, _settings.Approval);
        await ComposeExtensionsAsync(ct);
        // One dim notice per first slow-hook crossing; the log keeps the warning, and modes
        // without a subscriber here (print, ACP, the CLI) stay log-only, never stdout.
        foreach (
            HookRunner runner in new[] { _options.Hooks, _extensions?.HookRunner }
                .OfType<HookRunner>()
                .Distinct()
        )
        {
            runner.SlowHook += OnSlowHook;
        }

        // The bus exists only when the extension host composed; scan-only without extensions.
        if (_extensions is { } extensionHost)
        {
            _workspaceServices.Connect(extensionHost.FileChanges);
        }

        // Skills compose after the extension host (its sources contribute) and before the tools
        // (the read tool carries their allowance); discovery and the trust question run here.
        await ComposeSkillsAsync(ct);
        _tools = HarnessFactory.CreateTools(
            _workspace,
            services: _workspaceServices,
            skillReadAllowance: _skillAllowance,
            log: _extensions?.Log,
            extraTools: _settings.Tools.Extra
        );
        await ComposeMcpConfigAsync(ct);
        await ComposeMcpToolsAsync(ct);
        // The launch tool selection narrows the fully composed registry (base, extension and MCP
        // tools) and the inherited list the child workspace factory offers.
        if (
            !HarnessFactory.TryFilterTools(
                _tools,
                _inheritedTools,
                _options.Launch,
                out string? toolError
            )
        )
        {
            throw new ToolFilterException(toolError!);
        }

        _workspaceTools = HarnessFactory.CreateWorkspaceTools(
            _workspaceServices,
            _inheritedTools,
            root => _skills?.ReadRootsFor(root) ?? [],
            log: _extensions?.Log,
            extraTools: _settings.Tools.Extra
        );
        // The model resolves after the extension host: the configured id may name a virtual or
        // extension-registered model, which only the registry knows.
        ResolveSessionModel();
        _imageResolver = CreateImageResolver(_model);
        _harness = BuildHarness(_model);
        // Composition is done: the placeholder counts fill in with exactly one update line, never a
        // redraw of the block (design D1).
        PrintHeaderCounts();
        _pipeline = new InputPipeline(_options.Hooks ?? _extensions?.HookRunner);
    }

    /// <summary>
    /// The image resolver for a model. It is rebuilt on every model switch so its vision gate and
    /// refusal text always name the model that will actually receive the next request.
    /// </summary>
    private ImageInputResolver CreateImageResolver(ModelInfo? model) =>
        new(
            _workspace.WorktreeRoot,
            model,
            _options.Clipboard ?? new PlatformClipboardImageSource()
        );

    /// <summary>
    /// Resolves the configured model: an extension-registered id (virtual models included) comes
    /// from the registry with its derived capabilities; everything else stays a catalog lookup.
    /// With <c>--no-extensions</c> the registry never exists, so extension ids fail as unknown.
    /// </summary>
    private void ResolveSessionModel()
    {
        string? configured = _settings.Model;
        ModelCapabilities? capabilities = null;
        if (!string.IsNullOrWhiteSpace(configured) && _extensions is not null)
        {
            try
            {
                capabilities = _extensions.ModelRegistry.FindCapabilities(configured);
            }
            catch (InvalidOperationException exception)
            {
                // A registered extension model can be broken at resolve time (unknown backend,
                // cycle, bad capability declaration). There is no catalog default to fall back
                // to — a session cannot start without a model — so surface one clear,
                // actionable line instead of the raw teaching error mid-composition.
                throw new InvalidOperationException(
                    $"The configured model '{configured}' did not resolve: {exception.Message}",
                    exception
                );
            }
        }

        if (!string.IsNullOrWhiteSpace(configured) && capabilities is { } resolved)
        {
            _extensionModelId = configured;
            _modelRoute = null;
            _model = new ModelInfo(
                configured,
                "extension",
                Endpoint: null,
                resolved.ContextWindow,
                resolved.SupportsTools
            );
        }
        else
        {
            _extensionModelId = null;
            _modelRoute = null;
            _model = HarnessFactory.ResolveModel(configured, _catalog);
        }

        _modelId = _model.Id;
        _contextWindow = _model.ContextWindow;
    }

    /// <summary>
    /// Composes the session's extension host as the session starts: the workspace is resolved, so
    /// trust keys on the worktree root (or the git common dir inside a repository). A composition
    /// failure is reported and the session runs without extensions; a per-extension failure is
    /// already isolated by the host. The configured MCP servers are already composed, so a
    /// registration sharing a configured server's name is skipped before listing and shown as
    /// shadowed by `/mcp`. The host's MCP tools are listed once, here, and a server whose process
    /// exits later fires one notice. With <c>--no-extensions</c> the host is not composed at all.
    /// </summary>
    private async Task ComposeExtensionsAsync(CancellationToken ct)
    {
        TextWriter diagnostics = _options.Diagnostics ?? Console.Error;
        if (_options.Extensions?.NoExtensions is true)
        {
            _toolResultRenderers = _options.ToolResultRenderers;
            _statusWidgets = _options.StatusWidgets;
            return;
        }

        try
        {
            ExtensionSeams seams = (_options.Extensions ?? new ExtensionSeams()) with
            {
                Log = _options.Extensions?.Log ?? new StderrExtensionLog(diagnostics),
                Prompt = _options.Extensions?.Prompt ?? new TuiExtensionTrustPrompt(AskTrustAsync),
                Notice =
                    _options.Extensions?.Notice
                    ?? (message => Notify(message, NoticeLevel.Warning)),
                ModelCatalog = _catalog,
                ClientFactory = _factory,
            };
            _extensions = await ExtensionSession
                .StartAsync(_workspace.Identity, seams, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.WriteLine(
                $"lunate: the extension host was not composed: {exception.Message}"
            );
        }

        _toolResultRenderers = _options.ToolResultRenderers ?? _extensions?.ToolResultRenderers;
        _statusWidgets = _options.StatusWidgets ?? _extensions?.StatusWidgets;
    }

    /// <summary>
    /// The slow-hook notice: one per first crossing, naming the extension, the hook, the elapsed
    /// time and the threshold and pointing at <c>/extensions</c>. Dim (info level); later slow
    /// dispatches are counted in the view, not shown again.
    /// </summary>
    private void OnSlowHook(SlowHookEvent slow)
    {
        Notify(
            FormattableString.Invariant(
                $"hook {slow.Hook} from extension '{slow.ExtensionId}' took {slow.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} ms, above the slow threshold of {slow.Threshold.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} ms — see /extensions; later slow dispatches are counted, not shown."
            )
        );
    }

    /// <summary>
    /// Composes the configured MCP servers at session start, next to the extension host: the global
    /// mcp.json plus the trusted project file. Nothing starts before listing; config errors and a
    /// failure here never abort the session.
    /// </summary>
    private async Task ComposeMcpConfigAsync(CancellationToken ct)
    {
        TextWriter diagnostics = _options.Diagnostics ?? Console.Error;
        try
        {
            IExtensionLog log = _options.Extensions?.Log ?? new StderrExtensionLog(diagnostics);
            _mcp = await McpComposition
                .ComposeAsync(
                    _workspace,
                    _options.McpConfigPath,
                    _options.McpAgentsConfigPath,
                    _options.Extensions,
                    new TuiMcpConfigTrustPrompt(AskTrustAsync),
                    new TuiMcpServerApprovalPrompt(AskTrustAsync),
                    log,
                    Notify,
                    ct,
                    skippedServerQuestions: skipped =>
                        _trustTotal = Math.Max(_trustAsked, _trustTotal - skipped)
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Notify("the MCP servers were not composed: " + exception.Message, NoticeLevel.Error);
        }
    }

    /// <summary>
    /// The listing half of MCP composition, after both sets are known: the extension mapping is
    /// built with the configured names as the collision baseline (the configured server wins, the
    /// registration is skipped before listing and shown shadowed), then the extension tools and the
    /// configured tools are listed once and added; a server whose process exits later fires one
    /// notice, and failed configured servers become notices. A failure here never aborts the
    /// session.
    /// </summary>
    private async Task ComposeMcpToolsAsync(CancellationToken ct)
    {
        try
        {
            if (_extensions is { } extensions)
            {
                // Extension tools first, in registration order, then the MCP tools (design D7).
                foreach (ITool tool in extensions.Tools)
                {
                    _tools.Add(tool);
                    _inheritedTools.Add(tool);
                }

                ExtensionMcpServers servers = extensions.ComposeMcpServers(
                    McpNameCollisions.ConfiguredServers(_mcp)
                );
                servers.OnServerExited(status => Notify(McpComposition.ServerExitedNotice(status)));
                ToolComposition.AddSkippingTaken(
                    _tools,
                    await servers.ListToolsAsync(ct).ConfigureAwait(false),
                    "an extension-registered MCP server",
                    message => Notify(message, NoticeLevel.Warning)
                );
            }

            if (_mcp is { } mcp)
            {
                mcp.OnServerExited(status => Notify(McpComposition.ServerExitedNotice(status)));
                ToolComposition.AddSkippingTaken(
                    _tools,
                    await mcp.ListToolsAsync(ct).ConfigureAwait(false),
                    "mcp.json",
                    message => Notify(message, NoticeLevel.Warning)
                );

                // A child run in another worktree may be offered the tools of servers explicitly
                // marked workspace-independent (and only those); skipped collisions stay skipped.
                foreach (ITool tool in mcp.ListedToolsFrom(options => options.WorkspaceIndependent))
                {
                    if (_tools.Tools.Contains(tool))
                    {
                        _inheritedTools.Add(tool);
                    }
                }

                McpComposition.NotifyFailures(mcp, Notify);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Notify("the MCP servers were not composed: " + exception.Message, NoticeLevel.Error);
        }
    }

    /// <summary>The session end of the composition lifecycle: MCP servers and the extension host stop.</summary>
    private async ValueTask DisposeExtensionsAsync()
    {
        if (_mcp is { } mcp)
        {
            _mcp = null;
            try
            {
                await mcp.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (_options.Diagnostics ?? Console.Error).WriteLine(
                    $"lunate: the MCP servers failed to stop cleanly: {exception.Message}"
                );
            }
        }

        if (_extensions is not { } host)
        {
            return;
        }

        _extensions = null;
        try
        {
            await host.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (_options.Diagnostics ?? Console.Error).WriteLine(
                $"lunate: the extension host failed to stop cleanly: {exception.Message}"
            );
        }
    }

    /// <summary>
    /// Builds a harness for the given model against the current session; the constructor restores
    /// the conversation from the session, so this is the rebuild path of <c>/model</c>,
    /// <c>/new</c> and <c>/resume</c>.
    /// </summary>
    private AgentHarness BuildHarness(ModelInfo model)
    {
        var harnessOptions = new AgentHarnessOptions
        {
            SystemPrompt = SystemPrompt.Compose(
                _workspace,
                [.. _tools.Tools.Select(tool => tool.Name)],
                SystemPrompt.Directives(_options.Launch, _session)
            ),
            PromptSections = _skillSection is { } skills
                ? [new AgentPromptSection("skills", skills)]
                : null,
            WorkingDirectory = _workspace.WorktreeRoot,
            ToolOutputLimit = _settings.ToolOutputLimit,
            SubagentLimits = _settings.Subagents,
            Approver = _approver,
            Hooks = _extensions?.Hooks,
            Session = _session,
            Steering = _steering,
            FileChanges = (IFileChangeSink?)_extensions?.FileChanges ?? NullFileChangeSink.Instance,
            Model = model,
            ModelCatalog = _catalog,
            ModelId = model.Id,
            ThinkingLevel = _thinkingLevel,
            ModelRegistry = _extensions?.ModelRegistry,
            WorkspaceTools = _workspaceTools,
        };
        if (_options.ConfigureHarness is { } configure)
        {
            harnessOptions = configure(harnessOptions);
        }

        IChatClient client = _extensionModelId is { } extensionModelId
            ? _extensions!.ModelRegistry.Resolve(extensionModelId)
            : _factory.Create(model);
        return new AgentHarness(client, _tools, harnessOptions);
    }

    /// <summary>
    /// The footer's model segment: for an extension-registered session model it shortens the
    /// namespaced id and appends the last routed resolution (<c>auto → deep</c>); a catalog model
    /// renders exactly as before.
    /// </summary>
    private string DisplayModel()
    {
        if (_extensionModelId is null)
        {
            return _modelId;
        }

        string model = ShortModelName(_modelId);
        return _modelRoute is { } route ? $"{model} → {ShortModelName(route)}" : model;
    }

    /// <summary>The last path segment: <c>ext/model-router/auto</c> shows as <c>auto</c>.</summary>
    private static string ShortModelName(string modelId)
    {
        int slash = modelId.LastIndexOf('/');
        return slash >= 0 ? modelId[(slash + 1)..] : modelId;
    }

    /// <summary>The pending approval as shown in the live area; null when none is open.</summary>
    internal ApprovalPromptModel? PendingApproval => _approvalPrompt;

    /// <summary>The session's approval seam, composed at session start; null before composition.</summary>
    internal IToolApprover? Approver => _approver;

    /// <summary>The pending trust question as shown in the live area; null when none is open.</summary>
    internal TrustPromptModel? PendingTrustPrompt => _trustPrompt;

    /// <summary>Whether composition finished; false while the session composes.</summary>
    internal bool IsComposed => _composed;

    /// <summary>
    /// Reads the scrollback as one consistent snapshot: a commit is several line writes under
    /// the live area's paint gate, so an unguarded read can see a listing half-written.
    /// </summary>
    internal T ReadCommittedScrollback<T>(Func<T> read) => _live.ReadScrollback(read);

    /// <summary>The exit code composition's failure implies; 0 when the session ran normally.</summary>
    internal int ExitCode { get; private set; }

    /// <summary>Whether a turn (or its auto-run chain) is currently active.</summary>
    internal bool IsRunning => _turnActive;

    /// <summary>The session's token totals as the footer shows them, for the accounting tests.</summary>
    internal (long Input, long Output) TokenTotals => (_inputTokens, _outputTokens);

    /// <summary>The harness steering queue; nothing in the prompt writes to it (tests drive it directly).</summary>
    internal SteeringQueue Steering => _steering;

    /// <summary>
    /// The in-memory transcript the viewer renders: user and assistant text, thinking spans and
    /// full tool blocks (output and diff included), in commit order. A view over data the session
    /// already holds; nothing here is persisted, and the session file stays exactly as it is.
    /// </summary>
    internal IReadOnlyList<TranscriptEntry> Transcript => _transcript;

    private async Task PumpKeysAsync(CancellationToken ct)
    {
        await foreach (KeyEvent key in _console.ReadKeysAsync(ct))
        {
            if (_lifetime.IsCancellationRequested)
            {
                break;
            }

            // The read-time sequence stamps every key: keys read before a trust question armed
            // can never answer it (the type-ahead guard).
            await HandleKeyAsync(key, Interlocked.Increment(ref _keySequence), ct);
        }
    }

    internal async Task HandleKeyAsync(KeyEvent key, long sequence, CancellationToken ct)
    {
        // The viewer is modal: while it owns the terminal every key navigates or closes it, so
        // nothing else — the input line, a run's cancel, the model — sees them.
        if (_viewer.IsOpen)
        {
            if (_viewer.IsPicking)
            {
                HandleTreeKey(key);
            }
            else
            {
                HandleViewerKey(key);
            }

            return;
        }

        // The secret prompt is modal like the viewer: only Ctrl+C quits, every other key edits
        // the mask or completes it. Nothing reaches the input line, history or autocomplete.
        if (_secretPrompt.IsOpen)
        {
            if (KeyRouter.Route(key) == RoutedKey.ClearOrQuit)
            {
                HandleCtrlC();
            }
            else
            {
                _secretPrompt.HandleKey(key);
            }

            return;
        }

        if (_trustDecision is { } trustPending)
        {
            if (KeyRouter.Route(key) == RoutedKey.ClearOrQuit)
            {
                HandleCtrlC();
                return;
            }

            if (
                sequence > Interlocked.Read(ref _trustArmSequence)
                && _trustPrompt?.Decide(key) is { } trustChoice
            )
            {
                ResolveTrust(trustPending, trustChoice);
            }

            return;
        }

        if (_approvalDecision is { } pending)
        {
            if (KeyRouter.Route(key) == RoutedKey.ClearOrQuit)
            {
                HandleCtrlC();
                return;
            }

            if (_approvalPrompt?.Decide(key) is { } decision)
            {
                ResolveApproval(pending, decision);
            }

            return;
        }

        if (!_composed)
        {
            // Composition is still running: ordinary input is inert; only the quit window acts.
            if (KeyRouter.Route(key) == RoutedKey.ClearOrQuit)
            {
                HandleCtrlC();
            }

            return;
        }

        if (_picker is not null)
        {
            await HandlePickerKey(key, ct);
            return;
        }

        if (HandleKeyHelpKey(key))
        {
            return;
        }

        if (HandleHistorySearchKey(key))
        {
            return;
        }

        switch (KeyRouter.Route(key, _turnActive, _followUps.Count > 0, _bashMode || _bashRunning))
        {
            case RoutedKey.Edit:
                EditInput(key);
                break;
            case RoutedKey.FollowUp:
                if (_bashMode)
                {
                    // Enter stays bash mode's run key; a bash line is never queued.
                    await SubmitAsync(ct);
                    break;
                }

                await QueueFollowUpAsync(ct);
                break;
            case RoutedKey.FollowUpTakeBack:
                if (_autocomplete is not null)
                {
                    MoveAutocomplete(-1);
                    break;
                }

                TakeFollowUpsBack();
                break;
            case RoutedKey.Submit:
                if (_autocomplete is not null && !IsExactAutocompleteMatch())
                {
                    AcceptAutocomplete();
                    break;
                }

                await SubmitAsync(ct);
                break;
            case RoutedKey.Cancel:
                if (_autocomplete is not null)
                {
                    DismissAutocomplete();
                    break;
                }

                if (_bashMode)
                {
                    LeaveBashMode(restore: true);
                    break;
                }

                if (_turnActive)
                {
                    _runCancellation?.Cancel();
                    _bashCancellation?.Cancel();
                    break;
                }

                if (_bashRunning)
                {
                    _bashCancellation?.Cancel();
                    break;
                }

                break;
            case RoutedKey.ClearOrQuit:
                HandleCtrlC();
                break;
            case RoutedKey.Interrupt:
                _runCancellation?.Cancel();
                _bashCancellation?.Cancel();
                break;
            case RoutedKey.Exit:
                HandleCtrlD();
                break;
            case RoutedKey.Redraw:
                _live.Redraw();
                break;
            case RoutedKey.HistoryPrevious:
                if (_autocomplete is not null)
                {
                    MoveAutocomplete(-1);
                    break;
                }

                NavigatePrevious();
                break;
            case RoutedKey.HistoryNext:
                if (_autocomplete is not null)
                {
                    MoveAutocomplete(1);
                    break;
                }

                NavigateNext();
                break;
            case RoutedKey.ModelPicker:
                OpenModelPicker();
                break;
            case RoutedKey.TranscriptViewer:
                OpenViewer();
                break;
            case RoutedKey.Copy:
                await CopyLastAnswerAsync(ct);
                break;
            case RoutedKey.Stash:
                ToggleStash();
                break;
            case RoutedKey.HistorySearch:
                BeginHistorySearch();
                break;
            case RoutedKey.LineStart:
                ApplyEdit(InputLine.MoveHome(_input));
                break;
            case RoutedKey.LineEnd:
                ApplyEdit(InputLine.MoveEnd(_input));
                break;
            case RoutedKey.WordLeft:
                ApplyEdit(InputLine.MoveWordLeft(_input));
                break;
            case RoutedKey.WordRight:
                ApplyEdit(InputLine.MoveWordRight(_input));
                break;
            case RoutedKey.KillToEnd:
                ApplyKill(InputLine.KillToEnd(_input));
                break;
            case RoutedKey.KillToStart:
                ApplyKill(InputLine.KillToStart(_input));
                break;
            case RoutedKey.KillWordBack:
                ApplyKill(InputLine.KillWordBack(_input));
                break;
            case RoutedKey.KillWordForward:
                ApplyKill(InputLine.KillWordForward(_input));
                break;
            case RoutedKey.Yank:
                ApplyEdit(InputLine.Insert(_input, _killRing));
                break;
            case RoutedKey.Undo:
                UndoInput();
                break;
            case RoutedKey.Complete:
                if (_autocomplete is not null)
                {
                    AcceptAutocomplete();
                }

                break;
        }
    }

    /// <summary>
    /// The ctrl+o binding: suspends the live area (so no frame paints over the alternate screen)
    /// and opens the viewer at the latest content. Refused while a prompt or picker owns input —
    /// those keys return before the binding routes.
    /// </summary>
    private void OpenViewer()
    {
        _live.SetSuspended(true);
        try
        {
            _viewer.Open(new TranscriptModel(_transcript));
        }
        catch
        {
            // The screen never became the viewer's: resume the live area before the failure
            // surfaces, so a broken console write does not leave the session suspended.
            _live.SetSuspended(false);
            throw;
        }
    }

    /// <summary>One key while the viewer is open; closing it resumes the live area.</summary>
    private void HandleViewerKey(KeyEvent key)
    {
        if (_viewer.HandleKey(key, new TranscriptModel(_transcript)))
        {
            return;
        }

        _live.SetSuspended(false);
    }

    /// <summary>The one edit path; every newline key and every other edit routes here.</summary>
    private void EditInput(KeyEvent key)
    {
        bool wasEmpty = _input.Text.Length == 0;
        InputLineState edited = InputLine.Apply(_input, key);
        if (edited != _input)
        {
            if (IsNewlineKey(key))
            {
                _undo.Clear();
            }
            else
            {
                PushUndo(_input);
            }
        }

        _input = edited;
        _historyDraft = null;
        if (TryEnterBashMode(wasEmpty, key))
        {
            return;
        }

        _live.SetInput(_input, _bashMode);
        if (_bashMode)
        {
            CloseAutocomplete();
        }
        else
        {
            RefreshAutocomplete(IsTextEdit(key));
        }
    }

    private async Task SubmitAsync(CancellationToken ct)
    {
        if (_bashMode)
        {
            await SubmitBashAsync(ct);
            return;
        }

        string submitted = _input.Text;
        if (string.IsNullOrWhiteSpace(submitted))
        {
            return;
        }

        SetInput(string.Empty);
        _undo.Clear();
        if (
            Commands.IsCommand(submitted)
            && !ImageInputResolver.IsDroppedImagePath(_workspace.WorktreeRoot, submitted)
        )
        {
            // A login line may carry a typed key; it never reaches the history file. Every other
            // command keeps its current history behaviour.
            if (Commands.Parse(submitted).Name != "/login")
            {
                _history.Add(submitted);
            }

            await DispatchCommandAsync(submitted, ct);
            return;
        }

        await SubmitPromptAsync(submitted, skill: null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The one prompt submission path shared by typed text and <c>/skill:&lt;name&gt;</c>: the
    /// input pipeline runs, history records the text and an idle session starts the run, carrying
    /// the skill metadata when the submission was a skill invocation.
    /// </summary>
    private async Task SubmitPromptAsync(
        string text,
        SessionSkillMetadata? skill,
        CancellationToken ct
    )
    {
        if (_bashRunning)
        {
            Notify("a bash command is running — wait for it to finish", NoticeLevel.Warning);
            return;
        }

        // The previous notice clears as the submission arrives, before the hook chain runs: a
        // slow-hook event posted while the input dispatches must survive (it would otherwise be
        // cleared in the same tick and never render).
        _live.SetNotice(null);
        InputPipelineResult processed = await _pipeline.ProcessAsync(text, ct);
        if (processed.Consumed)
        {
            Notify("input consumed by an extension hook");
            return;
        }

        ImageInputResult input = skill is null
            ? await _imageResolver.ResolveAsync(processed.Text, ct)
            : ImageInputResult.Ready(processed.Text, images: null);
        if (input.Outcome == ImageInputOutcome.Refused)
        {
            Notify(input.Error!, NoticeLevel.Error);
            return;
        }

        if (input.Outcome == ImageInputOutcome.Quiet)
        {
            return;
        }

        _history.Add(input.Text);

        var userInput = new UserInput(input.Text, input.Images);
        PrepareTurn(userInput);
        _run = RunTurnsAsync(userInput, skill, ct);
    }

    /// <summary>
    /// Opens a turn in the transcript and scrollback before its run starts. Shared by a typed
    /// submission and a drained follow-up, so both produce the identical user echo and cost reset.
    /// </summary>
    private void PrepareTurn(UserInput input)
    {
        // A picker launch has no session yet: the turn that starts now creates it, so quitting
        // after the picker leaves the store untouched.
        MaterializeDeferredSession();
        _transcript.Add(new TranscriptUserEntry(input.Text));
        // The prompt opens the turn in scrollback before the run starts; the same processed text
        // feeds the transcript entry, so the viewer and scrollback never disagree. Later turns
        // open with one blank line so the prompt never runs against the previous turn's output.
        _live.WriteScrollback(console =>
        {
            // The echo's own trailing blank already separates a turn that wrote nothing (for
            // example one cancelled before its first event); never add a second blank for it.
            if (_promptEchoed && _turnWroteOutput)
            {
                console.WriteLine();
            }

            console.Write(_userEcho.Render(input.Text, _console.Size.Columns, _capabilities.Color));
        });
        _promptEchoed = true;
        _turnWroteOutput = false;
        // A new user turn starts a fresh cost: the previous turn's records and total never leak.
        // Only a visible cost needs the immediate re-render; the turn's first usage record
        // re-renders the footer with its own total anyway.
        bool hadCost = _turnCost is not null;
        _turnUsage.Clear();
        _turnCost = null;
        _turnActive = true;
        // A turn in flight has no answer yet: /copy must not treat its streamed text as one.
        _lastTurnCompleted = false;
        if (hadCost)
        {
            UpdateStatus();
        }
    }

    private async Task RunTurnsAsync(
        UserInput input,
        SessionSkillMetadata? skill,
        CancellationToken ct
    )
    {
        try
        {
            while (true)
            {
                await RunOnceAsync(input, skill, ct);
                // A quit cancels the lifetime token: the queue is dropped, never drained.
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (!_followUps.TryDequeue(out QueuedFollowUp? followUp) || followUp is null)
                {
                    return;
                }

                _history.Add(followUp.Input.Text);
                PrepareTurn(followUp.Input);
                input = followUp.Input;
                skill = null;
                UpdateFollowUpIndicator();
            }
        }
        finally
        {
            _turnActive = false;
            _run = null;
            _live.SetTool(null);
        }
    }

    private async Task RunOnceAsync(
        UserInput input,
        SessionSkillMetadata? skill,
        CancellationToken ct
    )
    {
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runCancellation = runCts;
        _runInputBaseline = _inputTokens;
        _runOutputBaseline = _outputTokens;
        _live.SetRunUsage(null, null);
        _live.SetRunning(true);
        _live.SetCancellable(true);
        try
        {
            IAsyncEnumerable<AgentEvent> events = skill is { } metadata
                ? _harness!.RunSkillAsync(input.Text, metadata, runCts.Token)
                : _harness!.RunAsync(input, runCts.Token);
            await foreach (AgentEvent agentEvent in events)
            {
                _options.EventObserver?.Invoke(agentEvent);
                HandleEvent(agentEvent);
            }
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested)
        {
            // The interrupted turn still hands over to the queue below.
        }
        finally
        {
            _runCancellation = null;
            _live.SetRunning(false);
            _live.SetCancellable(false);
            _live.SetRunUsage(null, null);
            _live.SetTool(null);
        }
    }

    /// <summary>Commits the dim echo of one message the harness injected from its steering queue.</summary>
    private void EchoSteering(SteeringInjected injected)
    {
        // The event carries the text: the session entry can lag the event, and a lookup that misses
        // used to drop the echo without a trace.
        string? text = injected.Text;
        if (string.IsNullOrEmpty(text) && injected.EntryId is { } entryId)
        {
            text = _session
                ?.Entries.OfType<SessionMessageEntry>()
                .FirstOrDefault(entry => entry.Id == entryId)
                ?.Message.Text;
        }
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _transcript.Add(new TranscriptUserEntry(text));
        _turnWroteOutput = true;
        _live.WriteScrollback(console =>
            console.Write(
                new Markup(
                    $"[dim]{(_capabilities.Unicode ? "»" : ">>")} {Markup.Escape(text)}[/]\n"
                )
            )
        );
    }

    private void ApplyKill(InputLine.KillResult kill)
    {
        if (kill.Killed.Length == 0)
        {
            return;
        }

        _killRing = kill.Killed;
        ApplyEdit(kill.State);
    }

    private void ApplyEdit(InputLineState next)
    {
        if (next == _input)
        {
            return;
        }

        PushUndo(_input);
        _input = next;
        _historyDraft = null;
        _live.SetInput(_input, _bashMode);
        if (_bashMode)
        {
            CloseAutocomplete();
        }
        else
        {
            RefreshAutocomplete(edited: true);
        }
    }

    private void PushUndo(InputLineState state)
    {
        if (_undo.Count >= 64)
        {
            InputLineState[] kept = [.. _undo.Reverse().Take(63)];
            _undo.Clear();
            foreach (InputLineState entry in kept)
            {
                _undo.Push(entry);
            }
        }

        _undo.Push(state);
    }

    private void UndoInput()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        InputLineState previous = _undo.Pop();
        _input = previous;
        _historyDraft = null;
        _live.SetInput(_input, _bashMode);
        if (_bashMode)
        {
            CloseAutocomplete();
        }
        else
        {
            RefreshAutocomplete(edited: true);
        }
    }

    private static bool IsNewlineKey(KeyEvent key) =>
        key is { Kind: KeyKind.Enter, Alt: true }
        || key is { Kind: KeyKind.Enter, Shift: true }
        || key is { Kind: KeyKind.Character, Ctrl: true, Text: "j" };

    private sealed record HistorySearch(
        string Query,
        IReadOnlyList<string> Matches,
        int Index,
        string Draft
    );

    private IReadOnlyList<string> HistoryEntries() =>
        _bashMode ? _bashHistory.Entries : _history.Entries;

    private void BeginHistorySearch()
    {
        if (_historySearch is null)
        {
            _historySearch = new HistorySearch(
                string.Empty,
                HistoryEntries().Reverse().ToArray(),
                -1,
                _input.Text
            );
        }

        AdvanceHistorySearch();
    }

    /// <summary>Consumes the keys that belong to an open history search; anything else accepts it.</summary>
    private bool HandleHistorySearchKey(KeyEvent key)
    {
        if (_historySearch is not { } search)
        {
            return false;
        }

        switch (key)
        {
            case { Kind: KeyKind.Enter }:
                CloseHistorySearch(keepInput: true);
                return true;
            case { Kind: KeyKind.Escape }:
                CloseHistorySearch(keepInput: false);
                return true;
            case { Kind: KeyKind.Character, Ctrl: true, Text: "r" }:
                AdvanceHistorySearch();
                return true;
            case { Kind: KeyKind.Down }:
                StepHistorySearch(-1);
                return true;
            case { Kind: KeyKind.Backspace }:
                if (search.Query.Length == 0)
                {
                    CloseHistorySearch(keepInput: false);
                }
                else
                {
                    SearchHistory(search.Query[..^1]);
                }

                return true;
            case { Kind: KeyKind.Space, Ctrl: false, Alt: false }:
                SearchHistory(search.Query + " ");
                return true;
            case { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: not null }:
                SearchHistory(search.Query + key.Text);
                return true;
            default:
                CloseHistorySearch(keepInput: true);
                return false;
        }
    }

    private void SearchHistory(string query)
    {
        if (_historySearch is not { } search)
        {
            return;
        }

        string[] matches = HistoryEntries()
            .Reverse()
            .Where(entry => entry.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _historySearch = new HistorySearch(
            query,
            matches,
            matches.Length == 0 ? -1 : 0,
            search.Draft
        );
        ShowHistorySearch();
    }

    private void AdvanceHistorySearch()
    {
        if (_historySearch is not { } search)
        {
            return;
        }

        if (search.Matches.Count == 0)
        {
            ShowHistorySearch();
            return;
        }

        int next = Math.Min(search.Index + 1, search.Matches.Count - 1);
        _historySearch = search with { Index = next };
        ShowHistorySearch();
    }

    private void StepHistorySearch(int delta)
    {
        if (_historySearch is not { } search || search.Matches.Count == 0)
        {
            return;
        }

        int next = Math.Clamp(search.Index + delta, 0, search.Matches.Count - 1);
        _historySearch = search with { Index = next };
        ShowHistorySearch();
    }

    private void ShowHistorySearch()
    {
        if (_historySearch is not { } search)
        {
            return;
        }

        string position =
            search.Matches.Count == 0 ? "no match" : $"{search.Index + 1}/{search.Matches.Count}";
        string entry = search.Index >= 0 ? search.Matches[search.Index] : search.Draft;
        CloseAutocomplete();
        _input = new InputLineState(entry, entry.Length);
        _live.SetInput(_input, _bashMode);
        _live.SetNotice($"history: {search.Query} ({position}) — Enter accept, Esc cancel");
    }

    private void CloseHistorySearch(bool keepInput)
    {
        if (_historySearch is not { } search)
        {
            return;
        }

        _historySearch = null;
        _live.SetNotice(null);
        if (!keepInput)
        {
            _input = new InputLineState(search.Draft, search.Draft.Length);
            _live.SetInput(_input, _bashMode);
        }
    }

    private void ToggleStash()
    {
        if (_input.Text.Length > 0)
        {
            _stashedDraft = _input.Text;
            SetInput(string.Empty);
            _undo.Clear();
            _live.SetNotice("draft stashed — Ctrl+S restores it");
            return;
        }

        if (_stashedDraft is { } stash)
        {
            _stashedDraft = null;
            SetInput(stash);
            _live.SetNotice("draft restored");
            return;
        }

        _live.SetNotice("no stashed draft");
    }

    /// <summary>
    /// The key-help overlay. While it is open it owns the keyboard: the only keys that do anything
    /// are its own, and a stray Enter can never submit a turn from a read-only screen.
    /// </summary>
    private bool HandleKeyHelpKey(KeyEvent key)
    {
        if (_keyHelp is not { } help)
        {
            if (
                key is { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: "?" }
                && _input.Text.Length == 0
                && !_turnActive
                && !_bashMode
            )
            {
                _keyHelp = KeyHelpModel.For(KeyHelpContext());
                _keyHelpFiltering = false;
                _live.SetKeyHelp(_keyHelp);
                return true;
            }

            return false;
        }

        switch (key)
        {
            case { Kind: KeyKind.Escape } when _keyHelpFiltering:
                _keyHelpFiltering = false;
                _keyHelp = help.Filter(string.Empty);
                _live.SetKeyHelp(_keyHelp);
                return true;
            case { Kind: KeyKind.Escape } or { Kind: KeyKind.Character, Text: "?" }:
                CloseKeyHelp();
                return true;
            case { Kind: KeyKind.Tab }:
                _keyHelp = help.WithScope(
                    help.Scope == KeyHelpScope.Here ? KeyHelpScope.All : KeyHelpScope.Here
                );
                _live.SetKeyHelp(_keyHelp);
                return true;
            case { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: "/" }
                when !_keyHelpFiltering:
                _keyHelpFiltering = true;
                _keyHelp = help.Filter(string.Empty);
                _live.SetKeyHelp(_keyHelp);
                return true;
            case { Kind: KeyKind.Down }
            or { Kind: KeyKind.Character, Text: "j" } when !_keyHelpFiltering:
                _keyHelp = help.Move(1);
                _live.SetKeyHelp(_keyHelp);
                return true;
            case { Kind: KeyKind.Up }
            or { Kind: KeyKind.Character, Text: "k" } when !_keyHelpFiltering:
                _keyHelp = help.Move(-1);
                _live.SetKeyHelp(_keyHelp);
                return true;
            case { Kind: KeyKind.Backspace } when _keyHelpFiltering:
                _keyHelp = help.Filter(help.Query.Length == 0 ? string.Empty : help.Query[..^1]);
                _live.SetKeyHelp(_keyHelp);
                return true;
            case { Kind: KeyKind.Space } when _keyHelpFiltering:
                _keyHelp = help.Filter(help.Query + " ");
                _live.SetKeyHelp(_keyHelp);
                return true;
            case { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: not null }
                when _keyHelpFiltering:
                _keyHelp = help.Filter(help.Query + key.Text);
                _live.SetKeyHelp(_keyHelp);
                return true;
            default:
                // Read-only: every other key is swallowed on purpose.
                return true;
        }
    }

    private KeyContext KeyHelpContext()
    {
        KeyContext context = _bashMode ? KeyContext.Bash : KeyContext.Idle;
        if (_turnActive)
        {
            context |= KeyContext.Running;
        }

        if (_followUps.Count > 0)
        {
            context |= KeyContext.HasQueue;
        }

        return context;
    }

    private void CloseKeyHelp()
    {
        _keyHelp = null;
        _keyHelpFiltering = false;
        _live.SetKeyHelp(null);
    }

    private void HandleCtrlC()
    {
        switch (_quitWindow.Press(_input.Text.Length == 0))
        {
            case QuitAction.ClearInput:
                SetInput(string.Empty);
                _live.SetNotice(null);
                break;
            case QuitAction.Arm:
            case QuitAction.ReArm:
                Notify(QuitWindow.Hint, NoticeLevel.Warning);
                break;
            case QuitAction.Quit:
                Quit();
                break;
            case QuitAction.Ignore:
                break;
        }
    }

    /// <summary>
    /// Ctrl+D: quits on a second press within the window, but only from an empty prompt with nothing
    /// running — quitting mid-turn would discard work with no recovery path.
    /// </summary>
    private void HandleCtrlD()
    {
        bool eligible = _input.Text.Length == 0 && !_turnActive && !_bashRunning && _composed;
        switch (_exitWindow.PressWhen(eligible))
        {
            case QuitAction.Arm:
            case QuitAction.ReArm:
                Notify(QuitWindow.ExitHint, NoticeLevel.Warning);
                break;
            case QuitAction.Quit:
                Quit();
                break;
            case QuitAction.Ignore:
            case QuitAction.ClearInput:
                break;
        }
    }

    /// <summary>The one quit path: double Ctrl+C, double Ctrl+D and <c>/quit</c> all end the session loop.</summary>
    private void Quit()
    {
        _live.SetNotice(null);
        _lifetime.Cancel();
    }

    private void NavigatePrevious()
    {
        if (_bashMode)
        {
            _bashHistoryDraft ??= _input.Text;
            if (_bashHistory.TryPrevious(out string command))
            {
                SetHistoryInput(command);
            }

            return;
        }

        _historyDraft ??= _input.Text;
        if (_history.TryPrevious(out string text))
        {
            SetHistoryInput(text);
        }
    }

    private void NavigateNext()
    {
        if (_bashMode)
        {
            if (_bashHistory.TryNext(out string command))
            {
                SetHistoryInput(command);
                return;
            }

            if (_bashHistoryDraft is { } bashDraft)
            {
                _bashHistoryDraft = null;
                SetHistoryInput(bashDraft);
            }

            return;
        }

        if (_history.TryNext(out string text))
        {
            SetHistoryInput(text);
            return;
        }

        if (_historyDraft is { } draft)
        {
            _historyDraft = null;
            SetHistoryInput(draft);
        }
    }

    /// <summary>Shows a history entry without disturbing the navigation draft.</summary>
    private void SetHistoryInput(string text)
    {
        CloseAutocomplete();
        _input = new InputLineState(text, text.Length);
        _live.SetInput(_input, _bashMode);
    }

    private void SetInput(string text)
    {
        CloseAutocomplete();
        _bashMode = false;
        _bashSilent = false;
        _bashHistoryDraft = null;
        _input = new InputLineState(text, text.Length);
        _historyDraft = null;
        _live.SetInput(_input);
    }

    private async ValueTask<bool> RequestApprovalAsync(
        ApprovalRequest request,
        CancellationToken ct
    )
    {
        string toolName = request.Tool.Name;
        lock (_alwaysApproved)
        {
            if (_alwaysApproved.Contains(toolName))
            {
                return true;
            }
        }

        var decision = new TaskCompletionSource<ApprovalChoice>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        string? workspaceRoot =
            request.WorkspaceRoot is { Length: > 0 } root
            && !SameRoot(root, _workspace.WorktreeRoot)
                ? root
                : null;
        var detail = ApprovalDetail.Build(
            toolName,
            request.Args,
            request.WorkspaceRoot ?? _workspace.WorktreeRoot,
            _capabilities.Unicode,
            _console.Size.Columns
        );
        _approvalPrompt = new ApprovalPromptModel(
            toolName,
            request.Args.GetRawText(),
            Asker: AskerLabel(request),
            Detail: detail,
            WorkspaceRoot: workspaceRoot
        );
        _approvalDecision = decision;
        _live.SetApproval(_approvalPrompt);
        using CancellationTokenRegistration registration = ct.Register(() =>
            decision.TrySetCanceled(ct)
        );
        ApprovalChoice choice;
        try
        {
            choice = await decision.Task.ConfigureAwait(false);
        }
        finally
        {
            _approvalPrompt = null;
            _approvalDecision = null;
            _live.SetApproval(null);
        }

        if (choice is ApprovalChoice.Always)
        {
            lock (_alwaysApproved)
            {
                _alwaysApproved.Add(toolName);
            }
        }

        return choice is ApprovalChoice.Approve or ApprovalChoice.Always;
    }

    /// <summary>
    /// The asking-run label of an approval prompt: a subagent ask names its depth; the workspace
    /// root is a separate prompt line. Top-level asks carry no label.
    /// </summary>
    private string? AskerLabel(ApprovalRequest request) =>
        request.Depth <= 0
            ? null
            : FormattableString.Invariant($"subagent (depth {request.Depth})");

    private static bool SameRoot(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsLinux()
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase
        );

    /// <summary>"Always" is remembered per session; every rebuild (/new, any /resume) clears it.</summary>
    private void ResetAlwaysApproved()
    {
        lock (_alwaysApproved)
        {
            _alwaysApproved.Clear();
        }
    }

    private void ResolveApproval(
        TaskCompletionSource<ApprovalChoice> pending,
        ApprovalChoice choice
    )
    {
        _approvalPrompt = null;
        _approvalDecision = null;
        _live.SetApproval(null);
        pending.TrySetResult(choice);
    }

    private void UpdateStatus()
    {
        var view = new StatusView(
            DisplayModel(),
            _inputTokens + _outputTokens,
            _contextWindow,
            _workingDirectory,
            _branch
        );
        IReadOnlyList<TextSegment> widgets = _statusWidgets?.Segments(view) ?? [];
        var favoritePosition = -1;
        for (var index = 0; index < _settings.Favorites.Count; index++)
        {
            if (string.Equals(_settings.Favorites[index], _model.Id, StringComparison.Ordinal))
            {
                favoritePosition = index;
                break;
            }
        }

        _interaction.UpdateStatus(
            new UserStatus(
                view.Model,
                view.TokensUsed,
                view.ContextWindow,
                view.WorkingDirectory,
                view.GitBranch,
                _thinkingLevel == ThinkingLevel.Off ? null : ThinkingLiteral(_thinkingLevel),
                widgets
            )
            {
                FavoritePosition = favoritePosition < 0 ? null : favoritePosition + 1,
                FavoriteCount = _settings.Favorites.Count,
                TurnCost = _turnCost is decimal cost ? CostCalculator.Format(cost) : null,
            }
        );
        UpdateFollowUpIndicator();
    }

    private void Notify(string message, NoticeLevel level = NoticeLevel.Info)
    {
        if (!_composed)
        {
            _pendingNotices.Add(new UserNotice(level, message));
            return;
        }

        _interaction.Notify(new UserNotice(level, message));
    }

    private void Notify(UserNotice notice) => Notify(notice.Message, notice.Level);

    /// <summary>
    /// Composition-time notices are held until the live area starts (a notice posted before
    /// <see cref="LiveArea.Start"/> would be dropped); they replay as the joined message.
    /// </summary>
    private void FlushPendingNotices()
    {
        if (_pendingNotices.Count == 0)
        {
            return;
        }

        NoticeLevel level =
            _pendingNotices.Any(notice => notice.Level is NoticeLevel.Error) ? NoticeLevel.Error
            : _pendingNotices.Any(notice => notice.Level is NoticeLevel.Warning)
                ? NoticeLevel.Warning
            : NoticeLevel.Info;
        string message = string.Join("; ", _pendingNotices.Select(notice => notice.Message));
        _pendingNotices.Clear();
        _interaction.Notify(new UserNotice(level, message));
    }

    private static string DisplayDirectory(string path)
    {
        string home = LunateHome.Profile;
        return path.StartsWith(home, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }

    /// <summary>The approval seam: policy-allowed calls run silently; the rest prompt in the live
    /// area, with "always" remembered per tool name for the session — one grant covers the tool in
    /// every run of the tree, including subagents.</summary>
    private sealed class SessionApprover(InteractiveSession session, ApprovalPolicy policy)
        : IToolApprover
    {
        public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct) =>
            NonInteractiveApprover.IsAllowed(policy, request.Tool.Risk)
                ? ValueTask.FromResult(true)
                : session.RequestApprovalAsync(request, ct);
    }
}
