using System.Globalization;
using System.Reactive.Concurrency;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;

namespace Lunate.Coding;

/// <summary>How a finished run ended; decides the leftover-steering rule.</summary>
internal enum RunOutcome
{
    Unknown,
    Completed,
    Cancelled,
    Failed,
    StepLimit,
    Length,
}

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

    /// <summary>Where composition errors are reported; null uses stderr.</summary>
    public TextWriter? Diagnostics { get; init; }

    /// <summary>Adjusts the composed harness options; tests pin budgets and retry delays here.</summary>
    public Func<AgentHarnessOptions, AgentHarnessOptions>? ConfigureHarness { get; init; }
}

/// <summary>
/// The interactive frontend: it consumes the harness's event stream into scrollback and the live
/// area, wires keys through the router, history and quit window, turns approvals into the live-area
/// prompt and steers a running turn through the queue. The pinned interaction rules - Esc returns
/// queued steering unsent; a normal finish auto-runs leftovers; an error or step limit returns them
/// to the input line - live here.
/// </summary>
internal sealed partial class InteractiveSession : IDisposable
{
    private readonly InteractiveSessionOptions _options;
    private readonly IConsoleIO _console;
    private readonly TerminalCapabilities _capabilities;
    private readonly LiveArea _live;
    private InputPipeline _pipeline;
    private readonly InputHistory _history;
    private readonly CtrlCQuitWindow _quitWindow;
    private readonly IUserInteraction _interaction;
    private readonly SteeringQueue _steering = new();
    private readonly object _steeringGate = new();
    private readonly List<string> _pendingSteering = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ToolBlockRenderer _toolBlocks = new();
    private readonly MarkdownRenderer _markdown;
    private readonly Dictionary<string, string> _toolNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _toolArgs = new(StringComparer.Ordinal);
    private readonly List<string> _activeCalls = [];
    private readonly HashSet<string> _alwaysApproved = new(StringComparer.Ordinal);
    private AgentSettings _settings = new();
    private ModelCatalog _catalog = null!;
    private IChatClientFactory _factory = null!;
    private Workspace _workspace = null!;
    private ToolRegistry _tools = null!;
    private Session _session = null!;
    private string _sessionDirectory = string.Empty;
    private ModelInfo _model = null!;
    private AgentHarness? _harness;
    private SessionApprover? _approver;
    private InputLineState _input = InputLine.Empty;
    private string? _historyDraft;
    private Task? _run;
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
    private List<string> _pickerValues = [];
    private FileIndex? _fileIndex;
    private string _tail = string.Empty;
    private RunOutcome _outcome;
    private long _inputTokens;
    private long _outputTokens;
    private string _modelId = "unknown";
    private long _contextWindow;
    private string _workingDirectory = string.Empty;
    private string? _branch;
    private ExtensionSession? _extensions;
    private McpToolSet? _mcp;
    private readonly List<UserNotice> _pendingNotices = [];
    private bool _composed;
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
        _pipeline = new InputPipeline(options.Hooks);
        _history = new InputHistory(options.HistoryPath ?? InputHistory.DefaultPath);
        _quitWindow = new CtrlCQuitWindow(options.Scheduler);
        _interaction = new TuiUserInteraction(_live);
    }

    /// <summary>Runs the input loop until the terminal's key stream ends or the quit window quits.</summary>
    public async Task RunAsync(CancellationToken ct = default)
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
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // A quit (or the caller's token) during composition: never surface it as an error.
        }
        catch (Exception exception)
        {
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

            await DisposeExtensionsAsync();
            _live.Dispose();
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

    private async Task ComposeAsync(CancellationToken ct)
    {
        _settings = SettingsStore.Resolve(_options.SettingsPath, _options.Environment);
        _catalog = ModelCatalog.Load(_options.ModelsPath);
        _model = HarnessFactory.ResolveModel(_settings.Model, _catalog);
        _factory =
            _options.Factory
            ?? HarnessFactory.CreateFactory(_options.AuthPath, _options.Environment);
        _workspace = new Workspace(_options.WorkingDirectory ?? Environment.CurrentDirectory);
        _tools = HarnessFactory.CreateTools(_workspace);
        _sessionDirectory =
            _options.SessionDirectory ?? HarnessFactory.DefaultSessionDirectory(_workspace);
        _session = HarnessFactory.CreateSession(_sessionDirectory, _workspace);
        _modelId = _model.Id;
        _contextWindow = _model.ContextWindow;
        _workingDirectory = DisplayDirectory(_workspace.WorktreeRoot);
        _branch = GitBranchReader.Read(_workspace.WorktreeRoot);
        _trustTotal = CountPendingTrustQuestions();
        _approver = new SessionApprover(this, _settings.Approval);
        await ComposeExtensionsAsync(ct);
        await ComposeMcpAsync(ct);
        _harness = BuildHarness(_model);
        _pipeline = new InputPipeline(_options.Hooks ?? _extensions?.HookRunner);
    }

    /// <summary>
    /// Composes the session's extension host as the session starts: the workspace is resolved, so
    /// trust keys on the worktree root (or the git common dir inside a repository). A composition
    /// failure is reported and the session runs without extensions; a per-extension failure is
    /// already isolated by the host. The host's MCP tools are listed once, here. With
    /// <c>--no-extensions</c> the host is not composed at all.
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
            };
            _extensions = await ExtensionSession
                .StartAsync(
                    _workspace.WorktreeRoot,
                    _workspace.GitCommonDir ?? _workspace.WorktreeRoot,
                    seams,
                    ct
                )
                .ConfigureAwait(false);
            foreach (
                ITool tool in await _extensions.McpServers.ListToolsAsync(ct).ConfigureAwait(false)
            )
            {
                _tools.Add(tool);
            }
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
    /// Composes the configured MCP servers at session start, next to the extension host: the global
    /// mcp.json plus the trusted project file, tools listed once and added to the registry. Config
    /// errors and failed servers become notices; a failure here never aborts the session.
    /// </summary>
    private async Task ComposeMcpAsync(CancellationToken ct)
    {
        TextWriter diagnostics = _options.Diagnostics ?? Console.Error;
        try
        {
            IExtensionLog log = _options.Extensions?.Log ?? new StderrExtensionLog(diagnostics);
            _mcp = await McpComposition
                .ComposeAsync(
                    _workspace,
                    _options.McpConfigPath,
                    _options.Extensions,
                    new TuiMcpConfigTrustPrompt(AskTrustAsync),
                    log,
                    Notify,
                    ct
                )
                .ConfigureAwait(false);
            if (_mcp is null)
            {
                return;
            }

            foreach (ITool tool in await _mcp.ListToolsAsync(ct).ConfigureAwait(false))
            {
                _tools.Add(tool);
            }

            McpComposition.NotifyFailures(_mcp, Notify);
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
                [.. _tools.Tools.Select(tool => tool.Name)]
            ),
            WorkingDirectory = _workspace.WorktreeRoot,
            ToolOutputLimit = _settings.ToolOutputLimit,
            Approver = _approver,
            Hooks = _extensions?.Hooks,
            Session = _session,
            Steering = _steering,
            ModelCatalog = _catalog,
            ModelId = model.Id,
        };
        if (_options.ConfigureHarness is { } configure)
        {
            harnessOptions = configure(harnessOptions);
        }

        return new AgentHarness(_factory.Create(model), _tools, harnessOptions);
    }

    /// <summary>The pending approval as shown in the live area; null when none is open.</summary>
    internal ApprovalPromptModel? PendingApproval => _approvalPrompt;

    /// <summary>The pending trust question as shown in the live area; null when none is open.</summary>
    internal TrustPromptModel? PendingTrustPrompt => _trustPrompt;

    /// <summary>Whether composition finished; false while the session composes.</summary>
    internal bool IsComposed => _composed;

    /// <summary>Whether a turn (or its auto-run chain) is currently active.</summary>
    internal bool IsRunning => _turnActive;

    /// <summary>The queued steering messages not yet drained; the leftovers Esc returns.</summary>
    internal int QueuedSteeringCount => _steering.Count;

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

    private async Task HandleKeyAsync(KeyEvent key, long sequence, CancellationToken ct)
    {
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
            HandlePickerKey(key);
            return;
        }

        switch (KeyRouter.Route(key))
        {
            case RoutedKey.Edit:
                _input = InputLine.Apply(_input, key);
                _historyDraft = null;
                _live.SetInput(_input);
                break;
            case RoutedKey.Submit:
                await SubmitAsync(ct);
                break;
            case RoutedKey.Cancel:
                _runCancellation?.Cancel();
                break;
            case RoutedKey.ClearOrQuit:
                HandleCtrlC();
                break;
            case RoutedKey.HistoryPrevious:
                NavigatePrevious();
                break;
            case RoutedKey.HistoryNext:
                NavigateNext();
                break;
            case RoutedKey.ModelPicker:
                OpenModelPicker();
                break;
            case RoutedKey.Complete:
                ApplyCompletion();
                break;
        }
    }

    private async Task SubmitAsync(CancellationToken ct)
    {
        string submitted = _input.Text;
        if (string.IsNullOrWhiteSpace(submitted))
        {
            return;
        }

        SetInput(string.Empty);
        if (Commands.IsCommand(submitted))
        {
            _history.Add(submitted);
            await DispatchCommandAsync(submitted, ct);
            return;
        }

        InputPipelineResult processed = await _pipeline.ProcessAsync(submitted, ct);
        if (processed.Consumed)
        {
            Notify("input consumed by an extension hook");
            return;
        }

        _history.Add(processed.Text);
        _live.SetNotice(null);
        if (_turnActive)
        {
            EnqueueSteering(processed.Text);
            return;
        }

        _turnActive = true;
        _run = RunTurnsAsync(processed.Text, ct);
    }

    private async Task RunTurnsAsync(string text, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                _outcome = RunOutcome.Unknown;
                await RunOnceAsync(text, ct);
                List<string> leftover = DrainSteering();
                DiscardPendingSteering(leftover.Count);
                if (_outcome == RunOutcome.Completed && leftover.Count > 0)
                {
                    text = string.Join("\n", leftover);
                    continue;
                }

                if (leftover.Count > 0)
                {
                    SetInput(string.Join("\n", leftover));
                }

                return;
            }
        }
        finally
        {
            _turnActive = false;
            _run = null;
            _live.SetTool(null);
        }
    }

    private async Task RunOnceAsync(string text, CancellationToken ct)
    {
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runCancellation = runCts;
        try
        {
            await foreach (AgentEvent agentEvent in _harness!.RunAsync(text, runCts.Token))
            {
                HandleEvent(agentEvent);
            }
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested)
        {
            _outcome = RunOutcome.Cancelled;
        }
        finally
        {
            _runCancellation = null;
            _live.SetTool(null);
        }
    }

    private List<string> DrainSteering()
    {
        List<string> leftover = [];
        while (_steering.TryDequeue(out string? message))
        {
            if (message is not null)
            {
                leftover.Add(message);
            }
        }

        return leftover;
    }

    /// <summary>Enqueues steering and mirrors it into the pending FIFO the echo reads.</summary>
    private void EnqueueSteering(string text)
    {
        lock (_steeringGate)
        {
            _steering.Enqueue(text);
            _pendingSteering.Add(text);
        }
    }

    /// <summary>The leftovers drained at run end were never injected; they must not echo and are
    /// removed from the front of the pending FIFO (injection pops from the front too).</summary>
    private void DiscardPendingSteering(int count)
    {
        lock (_steeringGate)
        {
            int remove = Math.Min(count, _pendingSteering.Count);
            if (remove > 0)
            {
                _pendingSteering.RemoveRange(0, remove);
            }
        }
    }

    /// <summary>Pops one pending text for a <c>SteeringInjected</c> event and commits its echo.</summary>
    private void EchoSteering()
    {
        string? text;
        lock (_steeringGate)
        {
            text = _pendingSteering.Count > 0 ? _pendingSteering[0] : null;
            if (text is not null)
            {
                _pendingSteering.RemoveAt(0);
            }
        }

        if (text is null)
        {
            return;
        }

        _live.WriteScrollback(console =>
            console.Write(
                new Markup($"[dim]{(_capabilities.Unicode ? "»" : ">")} {Markup.Escape(text)}[/]\n")
            )
        );
    }

    private void HandleCtrlC()
    {
        switch (_quitWindow.Press(_input.Text.Length == 0))
        {
            case CtrlCAction.ClearInput:
                SetInput(string.Empty);
                _live.SetNotice(null);
                break;
            case CtrlCAction.Arm:
            case CtrlCAction.ReArm:
                Notify(CtrlCQuitWindow.Hint, NoticeLevel.Warning);
                break;
            case CtrlCAction.Quit:
                Quit();
                break;
        }
    }

    /// <summary>The one quit path: double Ctrl+C and <c>/quit</c> both end the session loop.</summary>
    private void Quit()
    {
        _live.SetNotice(null);
        _lifetime.Cancel();
    }

    private void NavigatePrevious()
    {
        _historyDraft ??= _input.Text;
        if (_history.TryPrevious(out string text))
        {
            SetHistoryInput(text);
        }
    }

    private void NavigateNext()
    {
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
        _input = new InputLineState(text, text.Length);
        _live.SetInput(_input);
    }

    private void SetInput(string text)
    {
        _input = new InputLineState(text, text.Length);
        _historyDraft = null;
        _live.SetInput(_input);
    }

    private async ValueTask<bool> RequestApprovalAsync(
        string toolName,
        string args,
        CancellationToken ct
    )
    {
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
        _approvalPrompt = new ApprovalPromptModel(toolName, args);
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
            _modelId,
            _inputTokens + _outputTokens,
            _contextWindow,
            _workingDirectory,
            _branch
        );
        IReadOnlyList<TextSegment> widgets = _statusWidgets?.Segments(view) ?? [];
        _interaction.UpdateStatus(
            new UserStatus(
                view.Model,
                view.TokensUsed,
                view.ContextWindow,
                view.WorkingDirectory,
                view.GitBranch,
                widgets
            )
        );
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

    private void MapOutcome(string stopReason) =>
        _outcome = stopReason switch
        {
            StopReasons.Stop => RunOutcome.Completed,
            StopReasons.Cancelled => RunOutcome.Cancelled,
            StopReasons.StepLimit => RunOutcome.StepLimit,
            StopReasons.Length => RunOutcome.Length,
            _ => RunOutcome.Unknown,
        };

    private static string DisplayDirectory(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }

    /// <summary>The approval seam: policy-allowed calls run silently; the rest prompt in the live
    /// area, with "always" remembered per tool name for the session.</summary>
    private sealed class SessionApprover(InteractiveSession session, ApprovalPolicy policy)
        : IToolApprover
    {
        public ValueTask<bool> ApproveAsync(ITool tool, JsonElement args, CancellationToken ct) =>
            NonInteractiveApprover.IsAllowed(policy, tool.Risk)
                ? ValueTask.FromResult(true)
                : session.RequestApprovalAsync(tool.Name, args.GetRawText(), ct);
    }
}
