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
    private readonly Subject<LiveAreaInput> _stimuli = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly FrameWriter _writer;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _postGate = new();
    private readonly object _paintGate = new();
    private readonly bool _readKeys;
    private IAnsiConsole? _scrollback;
    private bool _started;
    private bool _disposed;

    public LiveArea(
        IConsoleIO console,
        IScheduler scheduler,
        bool readKeys = true,
        IAnsiConsole? scrollback = null
    )
    {
        _console = console;
        _scheduler = scheduler;
        _readKeys = readKeys;
        _scrollback = scrollback;
        _writer = new FrameWriter(console);
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        var initial = new LiveAreaState { Size = _console.Size };
        _writer.Paint(LiveAreaRenderer.Render(initial));

        var states = _stimuli
            .Scan(initial, Reduce)
            .StartWith(initial)
            .DistinctUntilChanged()
            .Skip(1);
        _subscriptions.Add(
            states
                .Sample(FrameInterval, _scheduler)
                .Subscribe(state =>
                {
                    lock (_paintGate)
                    {
                        _writer.Paint(LiveAreaRenderer.Render(state));
                    }
                })
        );
        _subscriptions.Add(_console.Resized.Subscribe(size => Post(new ResizeInput(size))));
        _subscriptions.Add(
            Observable.Interval(SpinnerInterval, _scheduler).Subscribe(_ => Post(new SpinnerTick()))
        );
        if (_readKeys)
        {
            _ = PumpKeysAsync(_cts.Token);
        }
    }

    public void PostKey(KeyEvent key) => Post(new KeyInput(key));

    public void AppendTail(string text) => Post(new TailInput(text));

    public void SetTail(string text) => Post(new TailSetInput(text));

    public void SetInput(InputLineState state) => Post(new InputStateInput(state));

    public void SetNotice(string? notice) => Post(new NoticeInput(notice));

    public void SetApproval(ApprovalPromptModel? prompt) => Post(new ApprovalInput(prompt));

    public void SetPicker(SelectListModel? picker) => Post(new PickerInput(picker));

    public void SetTool(string? toolName) => Post(new ToolInput(toolName));

    public void SetFooter(StatusFooterModel? footer) => Post(new FooterInput(footer));

    /// <summary>
    /// Writes finished scrollback through the one writer: the live frame is cleared first and the
    /// next sample tick repaints it from the current state, so cursor math never desynchronizes.
    /// </summary>
    public void WriteScrollback(Action<IAnsiConsole> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_paintGate)
        {
            _writer.Clear();
            write(Scrollback());
        }
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
        _stimuli.Dispose();

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

    private IAnsiConsole Scrollback() =>
        _scrollback ??= AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = _console.IsInteractive ? AnsiSupport.Yes : AnsiSupport.No,
                ColorSystem = _console.IsInteractive
                    ? ColorSystemSupport.Detect
                    : ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(new ConsoleTextWriter(_console)),
            }
        );

    private void Post(LiveAreaInput input)
    {
        lock (_postGate)
        {
            if (_disposed)
            {
                return;
            }

            _stimuli.OnNext(input);
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
            InputStateInput inputState => state with { Input = inputState.State },
            ResizeInput resize => state with { Size = resize.Size },
            SpinnerTick => state.ToolName is null
                ? state
                : state with
                {
                    FrameNumber = state.FrameNumber + 1,
                },
            TailInput tail => state with { TailText = state.TailText + tail.Text },
            TailSetInput tail => state with { TailText = tail.Text },
            NoticeInput notice => state with { Notice = notice.Notice },
            ApprovalInput approval => state with { Approval = approval.Prompt },
            PickerInput picker => state with { Picker = picker.Picker },
            ToolInput tool => state with { ToolName = tool.Name, FrameNumber = 0 },
            FooterInput footer => state with { Footer = footer.Footer },
            _ => state,
        };

    private abstract record LiveAreaInput;

    private sealed record KeyInput(KeyEvent Key) : LiveAreaInput;

    private sealed record InputStateInput(InputLineState State) : LiveAreaInput;

    private sealed record ResizeInput(ConsoleSize Size) : LiveAreaInput;

    private sealed record SpinnerTick : LiveAreaInput;

    private sealed record TailInput(string Text) : LiveAreaInput;

    private sealed record TailSetInput(string Text) : LiveAreaInput;

    private sealed record NoticeInput(string? Notice) : LiveAreaInput;

    private sealed record ApprovalInput(ApprovalPromptModel? Prompt) : LiveAreaInput;

    private sealed record PickerInput(SelectListModel? Picker) : LiveAreaInput;

    private sealed record ToolInput(string? Name) : LiveAreaInput;

    private sealed record FooterInput(StatusFooterModel? Footer) : LiveAreaInput;

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
