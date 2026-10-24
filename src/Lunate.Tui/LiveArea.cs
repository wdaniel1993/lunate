using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using Spectre.Console;

namespace Lunate.Tui;

internal sealed class LiveArea : IDisposable
{
    internal static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);
    internal static readonly TimeSpan SpinnerInterval = TimeSpan.FromMilliseconds(120);

    private readonly IConsoleIO _console;
    private readonly IScheduler _scheduler;
    private readonly TerminalCapabilities _capabilities;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly FrameWriter _writer;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _postGate = new();
    private readonly object _paintGate = new();
    private RenderedFrame? _lastFrame;
    private readonly bool _readKeys;
    private readonly bool _ownsScrollback;
    private IAnsiConsole? _scrollback;
    private readonly Queue<Action<IAnsiConsole>> _deferredScrollback = new();
    private bool _suspended;
    private bool _started;
    private bool _disposed;
    private LiveAreaState _state = new();
    private bool _dirty;

    public LiveArea(
        IConsoleIO console,
        IScheduler scheduler,
        bool readKeys = true,
        IAnsiConsole? scrollback = null,
        TerminalCapabilities? capabilities = null
    )
    {
        _console = console;
        _scheduler = scheduler;
        _readKeys = readKeys;
        _scrollback = scrollback;
        _ownsScrollback = scrollback is null;
        _capabilities =
            capabilities ?? new TerminalCapabilities(ColorSystemSupport.Detect, Unicode: true);
        _writer = new FrameWriter(console);
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        LiveAreaState initial;
        lock (_postGate)
        {
            _state = _state with
            {
                Size = _console.Size,
                Color = _capabilities.Color,
                Unicode = _capabilities.Unicode,
            };
            initial = _state;
            _dirty = false;
        }

        RenderedFrame initialFrame = LiveAreaRenderer.Render(initial);
        _lastFrame = initialFrame;
        _writer.Paint(initialFrame);

        _subscriptions.Add(
            Observable.Interval(FrameInterval, _scheduler).Subscribe(_ => PaintPending())
        );
        _subscriptions.Add(_console.Resized.Subscribe(size => Post(new ResizeInput(size))));
        _subscriptions.Add(
            Observable
                .Interval(SpinnerInterval, _scheduler)
                .Subscribe(_ => Post(new SpinnerTick(_scheduler.Now)))
        );
        if (_readKeys)
        {
            _ = PumpKeysAsync(_cts.Token);
        }
    }

    /// <summary>
    /// Forces a full repaint of the live area from the frame last rendered: the region is erased and
    /// redrawn, so a terminal that scrolled or garbled the prompt recovers without touching the
    /// committed scrollback. A no-op before the first frame and while a full-screen surface is open.
    /// </summary>
    public void Redraw()
    {
        lock (_paintGate)
        {
            if (_suspended || _lastFrame is not { } frame)
            {
                return;
            }

            _writer.Clear();
            _writer.Paint(frame);
        }
    }

    public void PostKey(KeyEvent key) => Post(new KeyInput(key));

    public void AppendTail(string text) => Post(new TailInput(text));

    public void SetTail(string text) => Post(new TailSetInput(text));

    /// <summary>Sets the live thinking block's text; null clears the block.</summary>
    public void SetThinking(string? text) => Post(new ThinkingInput(text, _scheduler.Now));

    public void SetInput(InputLineState state) => SetInput(state, bashMode: false);

    public void SetInput(InputLineState state, bool bashMode) =>
        Post(new InputStateInput(state, bashMode));

    public void SetNotice(string? notice) => Post(new NoticeInput(notice));

    /// <summary>Sets the open masked secret prompt; null closes it.</summary>
    public void SetSecretPrompt(SecretPromptState? prompt) => Post(new SecretPromptInput(prompt));

    public void SetApproval(ApprovalPromptModel? prompt) => Post(new ApprovalInput(prompt));

    public void SetTrustPrompt(TrustPromptModel? prompt) => Post(new TrustPromptInput(prompt));

    public void SetLoading(string? text) => Post(new LoadingInput(text));

    public void SetPicker(SelectListModel? picker) => Post(new PickerInput(picker));

    /// <summary>Sets the open key-help overlay; null closes it.</summary>
    public void SetKeyHelp(KeyHelpModel? help) => Post(new KeyHelpInput(help));

    /// <summary>Sets the open autocomplete list; null closes it (design D4).</summary>
    public void SetAutocomplete(AutocompleteState? autocomplete) =>
        Post(new AutocompleteInput(autocomplete));

    public void SetTool(string? toolName) => Post(new ToolInput(toolName, _scheduler.Now));

    public void SetToolSummary(string? summary) => Post(new ToolSummaryInput(summary));

    public void SetRunning(bool running) => Post(new RunningInput(running, _scheduler.Now));

    public void SetCancellable(bool cancellable) => Post(new CancellableInput(cancellable));

    public void SetRunUsage(long? inputTokens, long? outputTokens) =>
        Post(new RunUsageInput(inputTokens, outputTokens));

    public void SetSubagentRunning(bool running) => Post(new SubagentInput(running));

    public void SetFooter(StatusFooterModel? footer, IReadOnlyList<string>? widgets = null) =>
        Post(new FooterInput(footer, widgets ?? []));

    /// <summary>Sets how many follow-ups wait behind the running turn; zero hides the indicator.</summary>
    public void SetQueued(int count, string? first = null) =>
        Post(new QueuedInput(Math.Max(0, count), first));

    /// <summary>
    /// Writes finished scrollback through the one writer: the live frame is cleared first and the
    /// next sample tick repaints it from the current state, so cursor math never desynchronizes.
    /// While suspended the commit queues; <see cref="SetSuspended"/> flushes the queue on resume.
    /// </summary>
    public void WriteScrollback(Action<IAnsiConsole> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_paintGate)
        {
            if (_suspended)
            {
                _deferredScrollback.Enqueue(write);
                return;
            }

            WriteScrollbackNow(write);
        }
    }

    /// <summary>
    /// Suspends painting while a full-screen surface (the transcript viewer) owns the terminal;
    /// resuming flushes the scrollback queued meanwhile, in order, and painting continues on the
    /// next sample tick.
    /// </summary>
    public void SetSuspended(bool suspended)
    {
        lock (_paintGate)
        {
            if (_suspended == suspended)
            {
                return;
            }

            _suspended = suspended;
            if (!suspended)
            {
                // Whatever changed while the surface was open paints on the next tick, even when
                // no new input arrives.
                lock (_postGate)
                {
                    _dirty = true;
                }
            }
            while (!suspended && _deferredScrollback.Count > 0)
            {
                WriteScrollbackNow(_deferredScrollback.Dequeue());
            }
        }
    }

    /// <summary>
    /// Reads the committed scrollback while no commit is in flight: a multi-line commit (the
    /// welcome header, <c>/help</c>, <c>/context</c>) writes one line at a time under the paint
    /// gate, so a caller could otherwise observe it half-written. The seam's snapshot is the
    /// output the commit completed, never a prefix of it.
    /// </summary>
    internal T ReadScrollback<T>(Func<T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        lock (_paintGate)
        {
            return read();
        }
    }

    private void WriteScrollbackNow(Action<IAnsiConsole> write)
    {
        _writer.Clear();
        write(Scrollback());
    }

    public void Dispose()
    {
        lock (_postGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _cts.Cancel();
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();

        // The render subscription is gone, so queue the final clear on the same
        // scheduler that paints frames; the terminating thread never touches
        // FrameWriter state concurrently. Dispose the CTS only after the pump has
        // been cancelled, which keeps its token valid for the in-flight reads.
        _scheduler.Schedule(() =>
        {
            lock (_paintGate)
            {
                _writer.Clear();
            }
        });
        _cts.Dispose();
    }

    private IAnsiConsole Scrollback()
    {
        _scrollback ??= ConsoleColorSystem.Create(
            _console.IsInteractive ? _capabilities.Color : ColorSystemSupport.NoColors,
            new ConsoleTextWriter(_console),
            ansi: _console.IsInteractive
        );

        // Spectre folds at the profile width; binding it per commit keeps later commits laid
        // out at the terminal width in effect at the time. An injected console keeps its width.
        if (_ownsScrollback)
        {
            _scrollback.Profile.Width = Math.Max(1, _console.Size.Columns);
        }

        return _scrollback;
    }

    /// <summary>
    /// Applies the input to the state synchronously. The state, not the input, is what the frame
    /// paints: an input posted between two frame ticks — or while a full-screen surface has the
    /// terminal — is still in the state when painting resumes, so nothing is lost by timing.
    /// </summary>
    private void Post(LiveAreaInput input)
    {
        lock (_postGate)
        {
            if (_disposed)
            {
                return;
            }

            LiveAreaState next = Reduce(_state, input);
            if (next == _state)
            {
                return;
            }

            _state = next;
            _dirty = true;
        }
    }

    private void PaintPending()
    {
        LiveAreaState state;
        lock (_postGate)
        {
            if (!_dirty)
            {
                return;
            }

            state = _state;
            _dirty = false;
        }

        lock (_paintGate)
        {
            if (_suspended)
            {
                // Keep the change pending: resuming must paint it without a further input.
                lock (_postGate)
                {
                    _dirty = true;
                }

                return;
            }

            RenderedFrame frame = LiveAreaRenderer.Render(state);
            _lastFrame = frame;
            _writer.Paint(frame);
        }
    }

    private async Task PumpKeysAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var key in _console.ReadKeysAsync(cancellationToken))
            {
                PostKey(key);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static LiveAreaState Reduce(LiveAreaState state, LiveAreaInput input) =>
        input switch
        {
            KeyInput key => state with { Input = InputLine.Apply(state.Input, key.Key) },
            InputStateInput inputState => state with
            {
                Input = inputState.State,
                BashMode = inputState.BashMode,
            },
            ResizeInput resize => state with { Size = resize.Size },
            SpinnerTick tick => state with
            {
                FrameNumber =
                    state.ToolName is not null || state.Running
                        ? state.FrameNumber + 1
                        : state.FrameNumber,
                Thinking = WithElapsedSeconds(state.Thinking, tick.At),
                ToolElapsedSeconds = state.ToolName is null
                    ? state.ToolElapsedSeconds
                    : SecondsSince(state.ToolStartedAt, tick.At),
                RunElapsedSeconds = state.Running
                    ? SecondsSince(state.RunStartedAt, tick.At)
                    : state.RunElapsedSeconds,
            },
            TailInput tail => state with { TailText = state.TailText + tail.Text },
            TailSetInput tail => state with { TailText = tail.Text },
            ThinkingInput { Text: null } => state with { Thinking = null },
            ThinkingInput { Text: { } text, At: var at } => state with
            {
                // The start sticks to the first fragment of the block; later fragments only grow
                // the text, so the elapsed counts from when the reasoning began.
                Thinking = state.Thinking is { } current
                    ? current with
                    {
                        Text = text,
                    }
                    : new ThinkingState { Text = text, StartedAt = at },
            },
            NoticeInput notice => state with { Notice = notice.Notice },
            SecretPromptInput secret => state with { SecretPrompt = secret.Prompt },
            ApprovalInput approval => state with { Approval = approval.Prompt },
            TrustPromptInput trust => state with { Trust = trust.Prompt },
            LoadingInput loading => state with { Loading = loading.Text },
            PickerInput picker => state with { Picker = picker.Picker },
            KeyHelpInput help => state with { KeyHelp = help.KeyHelp },
            AutocompleteInput autocomplete => state with { Autocomplete = autocomplete.State },
            ToolInput { Name: null } => state with
            {
                ToolName = null,
                ToolSummary = null,
                ToolStartedAt = default,
                ToolElapsedSeconds = 0,
                FrameNumber = 0,
            },
            ToolInput { Name: { } name } when state.ToolName == name => state,
            ToolInput { Name: { } name, At: var at } => state with
            {
                ToolName = name,
                ToolSummary = null,
                ToolStartedAt = at,
                ToolElapsedSeconds = 0,
                FrameNumber = 0,
            },
            ToolSummaryInput summary => state with { ToolSummary = summary.Summary },
            RunningInput { Running: false } => state with
            {
                Running = false,
                RunElapsedSeconds = 0,
            },
            RunningInput { Running: true } when state.Running => state,
            RunningInput { Running: true, At: var at } => state with
            {
                Running = true,
                RunStartedAt = at,
                RunElapsedSeconds = 0,
            },
            CancellableInput cancellable => state with { Cancellable = cancellable.Cancellable },
            RunUsageInput usage => state with
            {
                RunInputTokens = usage.InputTokens,
                RunOutputTokens = usage.OutputTokens,
            },
            SubagentInput subagent => state with { SubagentRunning = subagent.Running },
            FooterInput footer => state with
            {
                Footer = footer.Footer,
                FooterWidgets = footer.Widgets,
            },
            QueuedInput queued => state with
            {
                QueuedCount = queued.Count,
                QueuedFirst = queued.First,
            },
            _ => state,
        };

    /// <summary>Elapsed seconds come from the tick's clock, never a wall clock in the renderer.</summary>
    private static ThinkingState? WithElapsedSeconds(ThinkingState? thinking, DateTimeOffset at) =>
        thinking is not { } current
            ? null
            : current with
            {
                ElapsedSeconds = SecondsSince(current.StartedAt, at),
            };

    /// <summary>Whole seconds from the injected clock, clamped at zero.</summary>
    private static int SecondsSince(DateTimeOffset startedAt, DateTimeOffset at) =>
        (int)Math.Max(0, (at - startedAt).TotalSeconds);

    private abstract record LiveAreaInput;

    private sealed record KeyInput(KeyEvent Key) : LiveAreaInput;

    private sealed record InputStateInput(InputLineState State, bool BashMode) : LiveAreaInput;

    private sealed record ResizeInput(ConsoleSize Size) : LiveAreaInput;

    private sealed record SpinnerTick(DateTimeOffset At) : LiveAreaInput;

    private sealed record TailInput(string Text) : LiveAreaInput;

    private sealed record TailSetInput(string Text) : LiveAreaInput;

    private sealed record ThinkingInput(string? Text, DateTimeOffset At) : LiveAreaInput;

    private sealed record NoticeInput(string? Notice) : LiveAreaInput;

    private sealed record SecretPromptInput(SecretPromptState? Prompt) : LiveAreaInput;

    private sealed record ApprovalInput(ApprovalPromptModel? Prompt) : LiveAreaInput;

    private sealed record TrustPromptInput(TrustPromptModel? Prompt) : LiveAreaInput;

    private sealed record LoadingInput(string? Text) : LiveAreaInput;

    private sealed record PickerInput(SelectListModel? Picker) : LiveAreaInput;

    private sealed record KeyHelpInput(KeyHelpModel? KeyHelp) : LiveAreaInput;

    private sealed record AutocompleteInput(AutocompleteState? State) : LiveAreaInput;

    private sealed record ToolInput(string? Name, DateTimeOffset At) : LiveAreaInput;

    private sealed record ToolSummaryInput(string? Summary) : LiveAreaInput;

    private sealed record RunningInput(bool Running, DateTimeOffset At) : LiveAreaInput;

    private sealed record CancellableInput(bool Cancellable) : LiveAreaInput;

    private sealed record RunUsageInput(long? InputTokens, long? OutputTokens) : LiveAreaInput;

    private sealed record SubagentInput(bool Running) : LiveAreaInput;

    private sealed record FooterInput(StatusFooterModel? Footer, IReadOnlyList<string> Widgets)
        : LiveAreaInput;

    private sealed record QueuedInput(int Count, string? First) : LiveAreaInput;

    private sealed class ConsoleTextWriter(IConsoleIO console) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => console.Write(value.ToString());

        public override void Write(string? value)
        {
            if (value is not null)
            {
                console.Write(value);
            }
        }
    }
}
