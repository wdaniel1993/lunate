using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Tui;
using Microsoft.Extensions.AI;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Coding.Tests;

/// <summary>A channel-backed terminal: the test sends keys and records the painted writes.</summary>
internal sealed class ScriptedConsoleIO : IConsoleIO
{
    private readonly Channel<KeyEvent> _keys = Channel.CreateUnbounded<KeyEvent>();
    private readonly Subject<ConsoleSize> _resized = new();
    private readonly List<string> _writes = [];
    private readonly List<string> _typedAtChild = [];

    public bool IsInteractive { get; set; }

    public ConsoleSize Size { get; set; } = new(80, 24);

    public IObservable<ConsoleSize> Resized => _resized;

    public void PushResize(ConsoleSize size)
    {
        Size = size;
        _resized.OnNext(size);
    }

    public IReadOnlyList<string> Writes
    {
        get
        {
            lock (_writes)
            {
                return [.. _writes];
            }
        }
    }

    public void Send(KeyEvent key)
    {
        if (IsTerminalSuspended)
        {
            lock (_typedAtChild)
            {
                _typedAtChild.Add(key.Text ?? key.Kind.ToString());
            }

            return;
        }

        _keys.Writer.TryWrite(key);
    }

    public void SendText(string text)
    {
        foreach (char character in text)
        {
            Send(new KeyEvent(KeyKind.Character, character.ToString(), false, false, false));
        }
    }

    public void SendEnter() => Send(new KeyEvent(KeyKind.Enter, null, false, false, false));

    public void SendAltEnter() => Send(new KeyEvent(KeyKind.Enter, null, false, false, true));

    public void SendUp() => Send(new KeyEvent(KeyKind.Up, null, false, false, false));

    public void SendEscape() => Send(new KeyEvent(KeyKind.Escape, null, false, false, false));

    public void SendCtrlC() =>
        Send(new KeyEvent(KeyKind.Character, "c", Ctrl: true, Shift: false, Alt: false));

    public void SendCtrl(string text) =>
        Send(new KeyEvent(KeyKind.Character, text, Ctrl: true, Shift: false, Alt: false));

    public void SendAlt(string text) =>
        Send(new KeyEvent(KeyKind.Character, text, Ctrl: false, Shift: false, Alt: true));

    public void SendBackspace() => Send(new KeyEvent(KeyKind.Backspace, null, false, false, false));

    public void SendCtrlD() =>
        Send(new KeyEvent(KeyKind.Character, "d", Ctrl: true, Shift: false, Alt: false));

    public void SendCtrlL() =>
        Send(new KeyEvent(KeyKind.Character, "l", Ctrl: true, Shift: false, Alt: false));

    public void SendCtrlO() =>
        Send(new KeyEvent(KeyKind.Character, "o", Ctrl: true, Shift: false, Alt: false));

    public void Complete() => _keys.Writer.TryComplete();

    public async IAsyncEnumerable<KeyEvent> ReadKeysAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await foreach (KeyEvent key in _keys.Reader.ReadAllAsync(cancellationToken))
        {
            yield return key;
        }
    }

    public void Write(string text)
    {
        lock (_writes)
        {
            _writes.Add(text);
        }
    }

    public IDisposable EnterRawMode()
    {
        if (FailRawMode)
        {
            throw new InvalidOperationException(
                "the terminal attributes of stdin could not be changed; interactive input is unavailable."
            );
        }

        RawModeEntries++;
        InRawMode = true;
        return new RawModeScope(this);
    }

    /// <summary>Entering the alternate screen (the transcript viewer's surface); recorded like the frames.</summary>
    public IDisposable EnterAlternateScreen()
    {
        if (FailAlternateScreen)
        {
            throw new InvalidOperationException("the terminal is gone");
        }

        AlternateScreenEntries++;
        InAlternateScreen = true;
        Write("\u001b[?1049h");
        return new AlternateScreenScope(this);
    }

    /// <summary>When set, entering the alternate screen throws the way a failing console write does.</summary>
    public bool FailAlternateScreen { get; set; }

    /// <summary>True while a child owns the terminal (the editor handover).</summary>
    public bool IsTerminalSuspended { get; private set; }

    /// <summary>How many times the terminal was handed to a child.</summary>
    public int SuspendCount { get; private set; }

    /// <summary>Keys typed while a child owned the terminal; they never reached the reader.</summary>
    public IReadOnlyList<string> TypedAtChild
    {
        get
        {
            lock (_typedAtChild)
            {
                return [.. _typedAtChild];
            }
        }
    }

    /// <summary>
    /// Stands in for the real handover: the console goes cooked, the session's read loop sees no
    /// further keys, and anything typed meanwhile is recorded as the child's input.
    /// </summary>
    public IDisposable SuspendTerminal()
    {
        SuspendCount++;
        IsTerminalSuspended = true;
        InRawMode = false;
        return new SuspendScope(this);
    }

    /// <summary>Whether the alternate screen is entered right now.</summary>
    public bool InAlternateScreen { get; private set; }

    /// <summary>How many times the alternate screen was entered.</summary>
    public int AlternateScreenEntries { get; private set; }

    /// <summary>When set, <see cref="EnterRawMode"/> throws the platform's actionable failure.</summary>
    public bool FailRawMode { get; set; }

    /// <summary>How many times the session entered raw mode (the fix that restored the spec'd call).</summary>
    public int RawModeEntries { get; private set; }

    /// <summary>True between <see cref="EnterRawMode"/> and its disposal.</summary>
    public bool InRawMode { get; private set; }

    private sealed class RawModeScope(ScriptedConsoleIO console) : IDisposable
    {
        public void Dispose() => console.InRawMode = false;
    }

    private sealed class SuspendScope(ScriptedConsoleIO console) : IDisposable
    {
        private bool _resumed;

        public void Dispose()
        {
            if (_resumed)
            {
                return;
            }

            _resumed = true;
            console.IsTerminalSuspended = false;
            console.InRawMode = true;
        }
    }

    private sealed class AlternateScreenScope(ScriptedConsoleIO console) : IDisposable
    {
        private bool _left;

        public void Dispose()
        {
            if (_left)
            {
                return;
            }

            _left = true;
            console.InAlternateScreen = false;
            console.Write("\u001b[?1049l");
        }
    }
}

/// <summary>
/// A virtual-time scheduler the test advances by hand; no wall clock is ever read.
/// </summary>
/// <remarks>
/// Thread-safe by contract: the session posts stimuli (and the live area's sample/spinner timers
/// re-arm) on its own thread while the test thread advances the clock. The queue and the clock
/// share one lock — an unsynchronised <see cref="PriorityQueue{TElement,TPriority}"/> silently
/// drops scheduled work and throws from <see cref="Advance"/>, which surfaces as a live-area
/// state that never paints (a notice or a committed command that never shows up).
/// </remarks>
internal sealed class ManualScheduler : IScheduler
{
    private static readonly DateTimeOffset Base = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly object _gate = new();
    private readonly PriorityQueue<ScheduledWork, (long Due, long Sequence)> _queue = new();
    private long _sequence;
    private DateTimeOffset _now = Base;

    public DateTimeOffset Now
    {
        get
        {
            lock (_gate)
            {
                return _now;
            }
        }
    }

    public IDisposable Schedule<TState>(
        TState state,
        Func<IScheduler, TState, IDisposable> action
    ) => Schedule(state, TimeSpan.Zero, action);

    public IDisposable Schedule<TState>(
        TState state,
        TimeSpan dueTime,
        Func<IScheduler, TState, IDisposable> action
    ) => Enqueue(state, Now + dueTime, action);

    public IDisposable Schedule<TState>(
        TState state,
        DateTimeOffset dueTime,
        Func<IScheduler, TState, IDisposable> action
    ) => Enqueue(state, dueTime, action);

    /// <summary>
    /// How much work is still queued. A scheduled chain that re-arms itself (the live area's frame
    /// interval) must never reach zero while it is subscribed: zero means the chain is dead and the
    /// surface it paints will not change again.
    /// </summary>
    public int PendingWorkCount
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count;
            }
        }
    }

    public void Advance(TimeSpan by)
    {
        DateTimeOffset target = Now + by;
        while (true)
        {
            ScheduledWork work;
            lock (_gate)
            {
                if (
                    !_queue.TryPeek(
                        out ScheduledWork? candidate,
                        out (long Due, long Sequence) priority
                    )
                    || priority.Due > (target - Base).Ticks
                    || candidate is null
                )
                {
                    _now = target;
                    return;
                }

                _queue.Dequeue();
                _now = Base + TimeSpan.FromTicks(priority.Due);
                work = candidate;
            }

            // Run outside the lock: a work item may schedule (re-entering Enqueue) and a paint
            // may take the writer's lock; holding _gate across it would serialise the session
            // thread against every advance.
            work.Run(this);
        }
    }

    private IDisposable Enqueue<TState>(
        TState state,
        DateTimeOffset due,
        Func<IScheduler, TState, IDisposable> action
    )
    {
        var work = new ScheduledWork(scheduler => action(scheduler, state));
        lock (_gate)
        {
            _queue.Enqueue(work, ((due - Base).Ticks, _sequence++));
        }

        return work;
    }

    private sealed class ScheduledWork(Func<IScheduler, IDisposable> action) : IDisposable
    {
        private bool _disposed;

        public void Run(IScheduler scheduler)
        {
            if (!_disposed)
            {
                action(scheduler);
            }
        }

        public void Dispose() => _disposed = true;
    }
}

/// <summary>
/// A scripted provider whose calls can be gated: the test waits for a call to start and releases
/// it, so key timing versus run timing is deterministic.
/// </summary>
internal sealed class GatedChatClient : IChatClient
{
    private readonly Queue<Script> _scripts = new();
    private readonly Dictionary<int, TaskCompletionSource> _started = new();
    private readonly Dictionary<int, TaskCompletionSource> _release = new();
    private readonly List<IReadOnlyList<ChatMessage>> _requests = [];
    private readonly List<ChatOptions?> _requestOptions = [];
    private readonly List<IReadOnlyList<ChatMessage>> _summaryRequests = [];

    /// <summary>
    /// The streaming requests, in call order. A snapshot: the session thread appends while the
    /// test thread reads, and a shared <see cref="List{T}"/> enumerated across threads throws
    /// "Collection was modified".
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ChatMessage>> Requests
    {
        get
        {
            lock (_started)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>The options each streaming call arrived with, indexed like <see cref="Requests"/>.</summary>
    public IReadOnlyList<ChatOptions?> RequestOptions
    {
        get
        {
            lock (_started)
            {
                return [.. _requestOptions];
            }
        }
    }

    /// <summary>Usage the canned non-streaming response reports; null reports none.</summary>
    public UsageDetails? SummaryUsage { get; set; }

    /// <summary>The non-streaming requests (compaction summarization), in call order.</summary>
    public IReadOnlyList<IReadOnlyList<ChatMessage>> SummaryRequests
    {
        get
        {
            lock (_started)
            {
                return [.. _summaryRequests];
            }
        }
    }

    public GatedChatClient Enqueue(params ChatResponseUpdate[] updates)
    {
        _scripts.Enqueue(new Script(updates, null));
        return this;
    }

    public GatedChatClient EnqueueFailure(Exception failure, params ChatResponseUpdate[] before)
    {
        _scripts.Enqueue(new Script(before, failure));
        return this;
    }

    public async Task WaitForCallAsync(int call, TimeSpan? timeout = null)
    {
        TimeSpan budget = timeout ?? TimeSpan.FromSeconds(10);
        Task started;
        int observed;
        lock (_started)
        {
            started = Started(call).Task;
            observed = _requests.Count;
        }

        try
        {
            await started.WaitAsync(budget, TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"chat call {call} never started within {budget.TotalSeconds:0} seconds (started={observed})."
            );
        }
    }

    /// <summary>Blocks the given call until <see cref="Release"/> is called.</summary>
    public void Gate(int call)
    {
        lock (_release)
        {
            _release[call] = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }
    }

    public void Release(int call)
    {
        lock (_release)
        {
            if (_release.TryGetValue(call, out TaskCompletionSource? gate))
            {
                gate.TrySetResult();
            }
        }
    }

    /// <summary>Non-streaming calls (compaction summarization) always yield a canned summary.</summary>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        lock (_started)
        {
            _summaryRequests.Add([.. messages]);
        }

        return Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary of older turns"))
            {
                Usage = SummaryUsage,
            }
        );
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        TaskCompletionSource? gate;
        int call;
        lock (_started)
        {
            _requests.Add([.. messages]);
            _requestOptions.Add(options);
            call = _requests.Count;
            Started(call).TrySetResult();
        }

        lock (_release)
        {
            _release.TryGetValue(call, out gate);
        }

        if (gate is not null)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        Script script = _scripts.Dequeue();
        foreach (ChatResponseUpdate update in script.Updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }

        if (script.Failure is not null)
        {
            throw script.Failure;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private TaskCompletionSource Started(int call)
    {
        if (!_started.TryGetValue(call, out TaskCompletionSource? started))
        {
            _started[call] = started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        return started;
    }

    private sealed record Script(IReadOnlyList<ChatResponseUpdate> Updates, Exception? Failure);
}

/// <summary>Composes an interactive session over temp paths, a scripted console and a scripted
/// provider. Callers that need the real client factory (the runtime replay seam) pass
/// <c>factory</c> instead of a scripted client.</summary>
internal sealed class InteractiveSessionHost : IDisposable
{
    private readonly StringWriter _diagnostics = new();
    private readonly List<AgentEvent> _events = [];
    private readonly object _eventsGate = new();

    public InteractiveSessionHost(
        HookRunner? hooks = null,
        Func<AgentHarnessOptions, AgentHarnessOptions>? configureHarness = null,
        IChatClient? chat = null,
        IChatClientFactory? factory = null,
        Func<string, string?>? environment = null,
        IWorkspaceFiles? workspaceFiles = null,
        ToolResultRendererRegistry? toolResultRenderers = null,
        StatusWidgetRegistry? statusWidgets = null,
        ExtensionSeams? extensions = null,
        string? mcpConfigPath = null,
        string? mcpAgentsConfigPath = null,
        string? workingDirectory = null,
        LaunchOptions? launch = null,
        string? sessionsRoot = null,
        string? startSession = null,
        bool startWithPicker = false,
        ISecretPrompt? secretPrompt = null,
        string? modelsPath = null,
        IClipboardImageSource? clipboard = null,
        ITextClipboard? textClipboard = null,
        Func<DateTimeOffset>? clock = null,
        string? editorTempDirectory = null
    )
    {
        Scrollback = new TestConsole();
        Scrollback.Profile.Width = 80;
        Scrollback.Profile.Height = 24;
        Session = new InteractiveSession(
            new InteractiveSessionOptions
            {
                Factory = factory ?? new FakeChatClientFactory(chat ?? Client),
                SettingsPath = Temp.File("settings.json"),
                ModelsPath = modelsPath ?? Temp.File("models.json"),
                AuthPath = Temp.File("auth.json"),
                SessionDirectory = Temp.File("sessions"),
                WorkingDirectory = workingDirectory ?? Temp.Root,
                HistoryPath = Temp.File("history"),
                HomeDirectory = Temp.File("home"),
                Environment = environment ?? PrintModeTestSupport.Environment(),
                Console = Console,
                Scheduler = Scheduler,
                Scrollback = Scrollback,
                Hooks = hooks,
                ToolResultRenderers = toolResultRenderers,
                StatusWidgets = statusWidgets,
                Extensions = extensions ?? TestExtensionSeams.Empty(Temp),
                McpConfigPath = mcpConfigPath ?? Temp.File("global-mcp.json"),
                McpAgentsConfigPath = mcpAgentsConfigPath ?? Temp.File("agents-mcp.json"),
                Diagnostics = _diagnostics,
                ConfigureHarness = configureHarness,
                WorkspaceFiles = workspaceFiles,
                EventObserver = Observe,
                Launch = launch ?? new LaunchOptions(),
                StartSession = startSession,
                StartWithPicker = startWithPicker,
                SessionsRoot = sessionsRoot ?? Temp.File("sessions"),
                SecretPrompt = secretPrompt,
                Clipboard = clipboard,
                TextClipboard = textClipboard,
                Clock = clock,
                EditorTempDirectory = editorTempDirectory,
            }
        );
    }

    public TempDirectory Temp { get; } = new();

    public ScriptedConsoleIO Console { get; } = new();

    public ManualScheduler Scheduler { get; } = new();

    public GatedChatClient Client { get; } = new();

    public TestConsole Scrollback { get; }

    public InteractiveSession Session { get; }

    /// <summary>
    /// Every event of the session's runs in arrival order (the sequence validator's subject).
    /// A snapshot: the session's event pump appends on its own thread, and enumerating the live
    /// <see cref="List{T}"/> from the test thread throws "Collection was modified".
    /// </summary>
    public IReadOnlyList<AgentEvent> Events
    {
        get
        {
            lock (_eventsGate)
            {
                return [.. _events];
            }
        }
    }

    public string Diagnostics => _diagnostics.ToString();

    private void Observe(AgentEvent agentEvent)
    {
        lock (_eventsGate)
        {
            _events.Add(agentEvent);
        }
    }

    public Task RunAsync() => Session.RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Waits for the approval panel and then lets its type-ahead window expire: a key sent before
    /// the window closes decides nothing, which is the rule under test elsewhere. Tests that press
    /// keys on a prompt use this instead of a bare wait.
    /// </summary>
    public async Task WaitForApprovalAsync(TimeSpan? timeout = null)
    {
        await WaitUntilAsync(() => Session.PendingApproval is not null, timeout);
        Session.ExpireApprovalArmWindow();
    }

    public void Advance(int milliseconds) =>
        Scheduler.Advance(TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>
    /// Types a line and submits it, waiting after each step until the session consumed it. A
    /// frame is a paint of the live state, so advancing the scheduler between the typed text and
    /// the submit would paint the text on the input line — for a line that carries a secret the
    /// tests must observe only the frames the product emits after the submit.
    /// </summary>
    public async Task SendLineAsync(string text)
    {
        Console.SendText(text);
        await WaitUntilAsync(() => Session.Input.Text == text);
        Console.SendEnter();
        await WaitUntilAsync(() => Session.Input.Text.Length == 0);
    }

    public string LastFrame => Console.Writes.Count == 0 ? string.Empty : Console.Writes[^1];

    public string Frames => string.Concat(Console.Writes);

    /// <summary>
    /// Everything a timed-out wait needs to be readable after the fact. The message used to print
    /// only the last frame, so a sighting said "writes=2" and nothing about whether the session was
    /// mid-approval, refused, or stranded on a dead paint chain.
    /// </summary>
    public string Snapshot()
    {
        IReadOnlyList<string> writes = Console.Writes;
        var text = new System.Text.StringBuilder();
        text.Append(FormattableString.Invariant(
            $"running={Session.IsRunning} bashMode={Session.IsBashMode}"))
            .Append(FormattableString.Invariant(
                $" bashSilent={Session.IsBashSilent} bashRunning={Session.IsBashRunning}"))
            .Append(FormattableString.Invariant($" writes={writes.Count}"))
            .Append(FormattableString.Invariant(
                $" input=\"{Escape(Session.Input.Text)}\""))
            .Append(FormattableString.Invariant(
                $" pendingApproval={Session.PendingApproval?.ToolName ?? "<none>"}"))
            .Append(FormattableString.Invariant(
                $" approvalArmed={Session.ApprovalArmedForNextKey}"))
            .Append(FormattableString.Invariant(
                $" scheduledVt={Scheduler.PendingWorkCount}"));

        text.AppendLine().Append("writes:");
        for (var i = 0; i < writes.Count; i++)
        {
            string body = Escape(writes[i]);
            if (body.Length > 400)
            {
                body = body[..400] + $"…(+{writes[i].Length - 400} chars)";
            }

            text.AppendLine().Append(FormattableString.Invariant($"  [{i}] {body}"));
        }

        string scrollback = Scrollback.Output;
        string tail = scrollback.Length <= 600 ? scrollback : scrollback[^600..];
        text.AppendLine()
            .Append("scrollback:")
            .Append(Escape(tail));

        return text.ToString();
    }

    /// <summary>Escapes a rendered write so a terminal frame is legible in one log line.</summary>
    private static string Escape(string text) =>
        text.Replace("\u001b", "\\e", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>
    /// The harness's bound for a wait on a rendered frame or committed scrollback: in-process
    /// renders land in milliseconds, so a miss inside this window is a bug, not slowness.
    /// Spawn-dependent waits pass their own wider window explicitly.
    /// </summary>
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Waits for a frame that contains <paramref name="expected"/> while pumping the scheduler.
    /// A single <see cref="Advance"/> is not enough: the paint lands on a later pump tick, so a
    /// one-tick assert races the renderer (the one-generation-early read).
    /// </summary>
    public Task WaitForFrameAsync(string expected, TimeSpan? timeout = null) =>
        WaitUntilAsync(
            () =>
            {
                Advance(33);
                return LastFrame.Contains(expected, StringComparison.Ordinal);
            },
            timeout ?? TimeSpan.FromSeconds(5)
        );

    /// <summary>
    /// Waits until the committed scrollback contains <paramref name="required"/> (ordinal) and
    /// returns that snapshot. This is the only way to read scrollback: a bare read can land
    /// before the command that renders the text has committed it, which is how the tree and
    /// <c>/help</c> tests failed on slower runners.
    /// </summary>
    public Task<string> WaitForScrollbackAsync(string required, TimeSpan? timeout = null) =>
        WaitForScrollbackAsync(
            text => text.Contains(required, StringComparison.Ordinal),
            $"the text \"{required}\"",
            timeout
        );

    /// <summary>
    /// Waits until the committed scrollback satisfies <paramref name="predicate"/> and returns
    /// that snapshot — for assertions a single substring cannot express (row counts, ordering,
    /// two markers that must share one frame). <paramref name="what"/> names the expectation in
    /// the timeout's message.
    /// </summary>
    public async Task<string> WaitForScrollbackAsync(
        Func<string, bool> predicate,
        string what,
        TimeSpan? timeout = null
    )
    {
        ArgumentNullException.ThrowIfNull(predicate);
        TimeSpan budget = timeout ?? DefaultWait;
        DateTime deadline = DateTime.UtcNow + budget;
        while (true)
        {
            // Read under the live area's paint gate: a multi-line commit is written line by line,
            // so an unguarded read could observe a prefix of it.
            string snapshot = Session.ReadCommittedScrollback(() => Scrollback.Output);
            if (predicate(snapshot))
            {
                return snapshot;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"The scrollback never showed {what} within {budget.TotalSeconds:0} seconds."
                        + Environment.NewLine
                        + Snapshot()
                );
            }

            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Awaits <paramref name="action"/> to completion and only then reads the scrollback, so an
    /// absence assertion is made against a frame a finished event produced instead of against a
    /// read that merely happened to land after the previous commit. Use it for
    /// <c>DoesNotContain</c>: waiting for the absent text itself would be a contradiction.
    /// </summary>
    public async Task<string> ScrollbackAfterAsync(Func<Task> action, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        TimeSpan budget = timeout ?? DefaultWait;
        Task work = action();
        try
        {
            await work.WaitAsync(budget, TestContext.Current.CancellationToken);
        }
        catch (TimeoutException) when (!work.IsCompleted)
        {
            throw new TimeoutException(
                $"The event the scrollback read waits on did not finish within "
                    + $"{budget.TotalSeconds:0} seconds (running={Session.IsRunning} "
                    + $"writes={Console.Writes.Count})."
            );
        }

        return Session.ReadCommittedScrollback(() => Scrollback.Output);
    }

    public async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        // Real-async waits: yield the thread (never a busy spin) so the session's run task and
        // event pump get scheduled; bounded by wall time so slow runners fail with diagnostics
        // instead of hanging. The default suits in-process frames; spawn-dependent waits (a
        // child process starting on a cold CI runner) pass a wider window — the repo's
        // spawn-window idiom, mirroring the BashToolTests 30s windows.
        DateTime deadline = DateTime.UtcNow + (timeout ?? DefaultWait);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    "The condition never became true within "
                        + $"{((timeout ?? DefaultWait).TotalSeconds):0} seconds."
                        + Environment.NewLine
                        + Snapshot()
                );
            }

            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
    }

    public void Dispose()
    {
        Session.Dispose();
        _diagnostics.Dispose();
        Temp.Dispose();
    }
}
