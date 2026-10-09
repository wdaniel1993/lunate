using System.Collections.Concurrent;
using System.Diagnostics;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace S6.Harness;

/// <summary>
/// Orchestrates the XenoAtom.Terminal.UI inline stack for the S-5 scenario:
/// <c>Terminal.Live</c> owns the live region, <c>Terminal.Write</c> writes
/// finished blocks above it, and a <see cref="LiveVisual"/> with a
/// <c>PromptEditor</c> is the live content.
/// </summary>
public sealed class XenoLiveApp : IDisposable
{
    private readonly TerminalSession _session;
    private readonly InMemoryTerminalBackend? _backend;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentQueue<AgentEvent> _events = new();
    private readonly Stopwatch _spinnerClock = Stopwatch.StartNew();
    private readonly TaskCompletionSource _stopped = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly List<string> _finishedBlocks = [];

    private TimeSpan _lastSpinner;
    private int _stopRequested;
    private TerminalApp? _app;

    public XenoLiveApp(
        TerminalSession session,
        InMemoryTerminalBackend? backend,
        Func<DateTimeOffset>? clock = null
    )
    {
        _session = session;
        _backend = backend;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Model = new LiveModel();
        Visual = new LiveVisual(Model, _clock);
        Visual.Refresh();
    }

    public LiveModel Model { get; }

    public LiveVisual Visual { get; }

    public TerminalInstance Instance => _session.Instance;

    public InMemoryTerminalBackend? Backend => _backend;

    public int Ticks { get; private set; }

    public int EventsProcessed { get; private set; }

    /// <summary>Markdown text of every finished block written above the live region.</summary>
    public IReadOnlyList<string> FinishedBlocks => _finishedBlocks;

    public static XenoLiveApp CreateHeadless(
        int width,
        int height,
        Func<DateTimeOffset>? clock = null
    )
    {
        var backend = new InMemoryTerminalBackend(new TerminalSize(width, height));
        var session = Terminal.Open(
            backend,
            new TerminalOptions { ImplicitStartInput = true, TreatControlCAsInput = true },
            force: true
        );
        return new XenoLiveApp(session, backend, clock);
    }

    public static XenoLiveApp CreateInteractive()
    {
        var session = Terminal.Open(
            options: new TerminalOptions { ImplicitStartInput = true, TreatControlCAsInput = true }
        );
        return new XenoLiveApp(session, backend: null);
    }

    public void PostEvent(AgentEvent value)
    {
        _events.Enqueue(value);
        _app?.Post(static () => { });
    }

    /// <summary>Pushes the script key into the headless backend (real input is the terminal's job).</summary>
    public void SendKey(ConsoleKeyInfo key)
    {
        if (_backend is null)
        {
            return;
        }

        if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            _backend.PushEvent(
                new TerminalKeyEvent
                {
                    Key = TerminalKey.Unknown,
                    Char = TerminalChar.CtrlC,
                    Modifiers = TerminalModifiers.Ctrl,
                }
            );
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Escape });
                return;
            case ConsoleKey.Enter:
                _backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });
                return;
            case ConsoleKey.Backspace:
                _backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Backspace });
                return;
        }

        if (key.KeyChar != '\0')
        {
            _backend.PushEvent(new TerminalTextEvent { Text = key.KeyChar.ToString() });
        }
    }

    public void Resize(int width, int height) =>
        _backend?.SetSize(new TerminalSize(width, height), raiseEvent: true);

    public void RequestStop()
    {
        Interlocked.Exchange(ref _stopRequested, 1);
        _app?.Post(static () => { });
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Instance.LiveAsync(Visual, OnUpdate, cancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _stopped.TrySetResult();
        }
    }

    public Task WaitForTicksAsync(int ticks, TimeSpan timeout) =>
        WaitForAsync(() => Ticks >= ticks, timeout);

    public Task WaitForAsync(Func<bool> condition, TimeSpan timeout) =>
        Task.Run(async () =>
        {
            var deadline = Stopwatch.StartNew();
            while (!condition())
            {
                if (deadline.Elapsed > timeout)
                {
                    throw new TimeoutException("Timed out waiting for the live loop.");
                }

                await Task.Delay(5).ConfigureAwait(false);
            }
        });

    public string GetOutputText() => _backend?.GetOutText() ?? string.Empty;

    /// <summary>
    /// Writes a fresh visual once through this app's terminal session and
    /// returns the captured screen. Used after the live loop has stopped; there
    /// is no public isolated TerminalInstance, so opening another global one
    /// while the loop runs would dispose this session.
    /// </summary>
    public string RenderVisualOnce(Visual visual, int width, int height)
    {
        var before = _backend?.GetOutText().Length ?? 0;
        Instance.Write(visual);
        var delta = _backend is null ? string.Empty : _backend.GetOutText()[before..];
        var screen = new AnsiScreen(width, height);
        screen.Apply(delta);
        return screen.GetText();
    }

    public string RenderLiveSnapshot(int width, int height)
    {
        var visual = new LiveVisual(Model, _clock);
        visual.Refresh();
        return RenderVisualOnce(visual, width, height);
    }

    public string RenderMarkdownBlock(string markdown, int width, int height) =>
        RenderVisualOnce(new MarkdownControl(markdown), width, height);

    public void Dispose()
    {
        _session.Dispose();
        _stopped.TrySetResult();
    }

    private TerminalLoopResult OnUpdate(TerminalRunningContext context)
    {
        _app ??= context.App;
        Ticks++;

        var changed = false;

        while (_events.TryDequeue(out var value))
        {
            EventsProcessed++;
            Model.ApplyEvent(value);
            changed = true;

            if (value is AgentEvent.ToolFinished finished)
            {
                var mark = finished.Ok ? "ok" : "failed";
                var markdown = $"**{finished.Tool}** {mark}: {finished.Summary}";
                _finishedBlocks.Add(markdown);
                context.Terminal.Write(new MarkdownControl(markdown));
            }
        }

        if (
            Model.ToolRunning
            && _spinnerClock.Elapsed - _lastSpinner >= TimeSpan.FromMilliseconds(120)
        )
        {
            _lastSpinner = _spinnerClock.Elapsed;
            Model.TickSpinner(_spinnerClock.Elapsed, out _);
            changed = true;
        }

        if (changed)
        {
            Visual.Refresh();
        }

        return Volatile.Read(ref _stopRequested) == 1 || Model.QuitRequested
            ? TerminalLoopResult.StopAndKeepVisual
            : TerminalLoopResult.Continue;
    }
}
