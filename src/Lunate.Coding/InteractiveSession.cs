using System.Globalization;
using System.Reactive.Concurrency;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
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

    /// <summary>The scheduler for the live area and the Ctrl+C window.</summary>
    public required IScheduler Scheduler { get; init; }

    /// <summary>The scrollback console; null renders through the terminal (plain when not interactive).</summary>
    public IAnsiConsole? Scrollback { get; init; }

    /// <summary>The extension hook runner behind the input pipeline; null passes input through.</summary>
    public HookRunner? Hooks { get; init; }

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
    private readonly LiveArea _live;
    private readonly InputPipeline _pipeline;
    private readonly InputHistory _history;
    private readonly CtrlCQuitWindow _quitWindow;
    private readonly SteeringQueue _steering = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ToolBlockRenderer _toolBlocks = new();
    private readonly MarkdownRenderer _markdown = new();
    private readonly Dictionary<string, string> _toolNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _toolArgs = new(StringComparer.Ordinal);
    private readonly List<string> _activeCalls = [];
    private readonly HashSet<string> _alwaysApproved = new(StringComparer.Ordinal);
    private AgentHarness? _harness;
    private SessionApprover? _approver;
    private InputLineState _input = InputLine.Empty;
    private string? _historyDraft;
    private Task? _run;
    private volatile bool _turnActive;
    private CancellationTokenSource? _runCancellation;
    private ApprovalPromptModel? _approvalPrompt;
    private TaskCompletionSource<ApprovalChoice>? _approvalDecision;
    private string _tail = string.Empty;
    private RunOutcome _outcome;
    private long _inputTokens;
    private long _outputTokens;
    private string _modelId = "unknown";
    private long _contextWindow;
    private string _workingDirectory = string.Empty;
    private string? _branch;
    private bool _disposed;

    public InteractiveSession(InteractiveSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _console = options.Console;
        _live = new LiveArea(
            options.Console,
            options.Scheduler,
            readKeys: false,
            scrollback: options.Scrollback
        );
        _pipeline = new InputPipeline(options.Hooks);
        _history = new InputHistory(options.HistoryPath ?? InputHistory.DefaultPath);
        _quitWindow = new CtrlCQuitWindow(options.Scheduler);
    }

    /// <summary>Runs the input loop until the terminal's key stream ends or the quit window quits.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            Compose();
        }
        catch (Exception exception)
        {
            (_options.Diagnostics ?? Console.Error).WriteLine($"lunate: {exception.Message}");
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        _live.Start();
        _live.SetFooter(Footer());
        try
        {
            await PumpKeysAsync(linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
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

    private void Compose()
    {
        AgentSettings settings = SettingsStore.Resolve(_options.SettingsPath, _options.Environment);
        ModelCatalog catalog = ModelCatalog.Load(_options.ModelsPath);
        ModelInfo model = HarnessFactory.ResolveModel(settings.Model, catalog);
        IChatClientFactory factory =
            _options.Factory
            ?? HarnessFactory.CreateFactory(_options.AuthPath, _options.Environment);
        var workspace = new Workspace(_options.WorkingDirectory ?? Environment.CurrentDirectory);
        ToolRegistry tools = HarnessFactory.CreateTools(workspace);
        Session session = HarnessFactory.CreateSession(
            _options.SessionDirectory ?? HarnessFactory.DefaultSessionDirectory(workspace),
            workspace
        );
        _modelId = model.Id;
        _contextWindow = model.ContextWindow;
        _workingDirectory = DisplayDirectory(workspace.WorktreeRoot);
        _branch = GitBranchReader.Read(workspace.WorktreeRoot);
        _approver = new SessionApprover(this, settings.Approval);
        var harnessOptions = new AgentHarnessOptions
        {
            SystemPrompt = SystemPrompt.Compose(
                workspace,
                [.. tools.Tools.Select(tool => tool.Name)]
            ),
            WorkingDirectory = workspace.WorktreeRoot,
            ToolOutputLimit = settings.ToolOutputLimit,
            Approver = _approver,
            Session = session,
            Steering = _steering,
            ModelCatalog = catalog,
            ModelId = model.Id,
        };
        if (_options.ConfigureHarness is { } configure)
        {
            harnessOptions = configure(harnessOptions);
        }

        _harness = new AgentHarness(factory.Create(model), tools, harnessOptions);
    }

    /// <summary>The pending approval as shown in the live area; null when none is open.</summary>
    internal ApprovalPromptModel? PendingApproval => _approvalPrompt;

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

            await HandleKeyAsync(key, ct);
        }
    }

    private async Task HandleKeyAsync(KeyEvent key, CancellationToken ct)
    {
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
        InputPipelineResult processed = await _pipeline.ProcessAsync(submitted, ct);
        if (processed.Consumed)
        {
            _live.SetNotice("input consumed by an extension hook");
            return;
        }

        _history.Add(processed.Text);
        _live.SetNotice(null);
        if (_turnActive)
        {
            _steering.Enqueue(processed.Text);
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
                _live.SetNotice(CtrlCQuitWindow.Hint);
                break;
            case CtrlCAction.Quit:
                _live.SetNotice(null);
                _lifetime.Cancel();
                break;
        }
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

    private StatusFooterModel Footer() =>
        new(_modelId, _inputTokens + _outputTokens, _contextWindow, _workingDirectory, _branch);

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
