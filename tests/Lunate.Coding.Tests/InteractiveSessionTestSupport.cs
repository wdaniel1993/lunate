using System.Reactive.Concurrency;
using System.Reactive.Linq;
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
    private readonly List<string> _writes = [];

    public bool IsInteractive { get; set; }

    public ConsoleSize Size { get; set; } = new(80, 24);

    public IObservable<ConsoleSize> Resized => Observable.Empty<ConsoleSize>();

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

    public void Send(KeyEvent key) => _keys.Writer.TryWrite(key);

    public void SendText(string text)
    {
        foreach (char character in text)
        {
            Send(new KeyEvent(KeyKind.Character, character.ToString(), false, false, false));
        }
    }

    public void SendEnter() => Send(new KeyEvent(KeyKind.Enter, null, false, false, false));

    public void SendEscape() => Send(new KeyEvent(KeyKind.Escape, null, false, false, false));

    public void SendCtrlC() =>
        Send(new KeyEvent(KeyKind.Character, "c", Ctrl: true, Shift: false, Alt: false));

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

    public IDisposable EnterRawMode() => NoopDisposable.Instance;

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose() { }
    }
}

/// <summary>A virtual-time scheduler the test advances by hand; no wall clock is ever read.</summary>
internal sealed class ManualScheduler : IScheduler
{
    private static readonly DateTimeOffset Base = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly PriorityQueue<ScheduledWork, (long Due, long Sequence)> _queue = new();
    private long _sequence;
    private DateTimeOffset _now = Base;

    public DateTimeOffset Now => _now;

    public IDisposable Schedule<TState>(
        TState state,
        Func<IScheduler, TState, IDisposable> action
    ) => Schedule(state, TimeSpan.Zero, action);

    public IDisposable Schedule<TState>(
        TState state,
        TimeSpan dueTime,
        Func<IScheduler, TState, IDisposable> action
    ) => Enqueue(state, _now + dueTime, action);

    public IDisposable Schedule<TState>(
        TState state,
        DateTimeOffset dueTime,
        Func<IScheduler, TState, IDisposable> action
    ) => Enqueue(state, dueTime, action);

    public void Advance(TimeSpan by)
    {
        DateTimeOffset target = _now + by;
        while (
            _queue.TryPeek(out ScheduledWork? work, out (long Due, long Sequence) priority)
            && priority.Due <= (target - Base).Ticks
        )
        {
            _queue.Dequeue();
            _now = Base + TimeSpan.FromTicks(priority.Due);
            work.Run(this);
        }

        _now = target;
    }

    private IDisposable Enqueue<TState>(
        TState state,
        DateTimeOffset due,
        Func<IScheduler, TState, IDisposable> action
    )
    {
        var work = new ScheduledWork(scheduler => action(scheduler, state));
        _queue.Enqueue(work, ((due - Base).Ticks, _sequence++));
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

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

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

    public Task WaitForCallAsync(int call)
    {
        lock (_started)
        {
            return Started(call).Task;
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
    ) =>
        Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary of older turns"))
        );

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
            Requests.Add([.. messages]);
            call = Requests.Count;
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

/// <summary>Composes an interactive session over temp paths, a scripted console and a scripted provider.</summary>
internal sealed class InteractiveSessionHost : IDisposable
{
    private readonly StringWriter _diagnostics = new();

    public InteractiveSessionHost(
        HookRunner? hooks = null,
        Func<AgentHarnessOptions, AgentHarnessOptions>? configureHarness = null,
        IChatClient? chat = null,
        Func<string, string?>? environment = null
    )
    {
        Scrollback = new TestConsole();
        Scrollback.Profile.Width = 80;
        Scrollback.Profile.Height = 24;
        Session = new InteractiveSession(
            new InteractiveSessionOptions
            {
                Factory = new FakeChatClientFactory(chat ?? Client),
                SettingsPath = Temp.File("settings.json"),
                ModelsPath = Temp.File("models.json"),
                AuthPath = Temp.File("auth.json"),
                SessionDirectory = Temp.File("sessions"),
                WorkingDirectory = Temp.Root,
                HistoryPath = Temp.File("history"),
                Environment = environment ?? PrintModeTestSupport.Environment(),
                Console = Console,
                Scheduler = Scheduler,
                Scrollback = Scrollback,
                Hooks = hooks,
                Diagnostics = _diagnostics,
                ConfigureHarness = configureHarness,
            }
        );
    }

    public TempDirectory Temp { get; } = new();

    public ScriptedConsoleIO Console { get; } = new();

    public ManualScheduler Scheduler { get; } = new();

    public GatedChatClient Client { get; } = new();

    public TestConsole Scrollback { get; }

    /// <summary>The plain scrollback text as the goldens pin it (TestConsole renders without ANSI).</summary>
    public string ScrollbackText => Scrollback.Output;

    public InteractiveSession Session { get; }

    public string Diagnostics => _diagnostics.ToString();

    public Task RunAsync() => Session.RunAsync(TestContext.Current.CancellationToken);

    public void Advance(int milliseconds) =>
        Scheduler.Advance(TimeSpan.FromMilliseconds(milliseconds));

    public string LastFrame => Console.Writes.Count == 0 ? string.Empty : Console.Writes[^1];

    public string Frames => string.Concat(Console.Writes);

    public async Task WaitUntilAsync(Func<bool> condition)
    {
        // Real-async waits: yield the thread (never a busy spin) so the session's run task and
        // event pump get scheduled; bounded by wall time so slow runners fail with diagnostics
        // instead of hanging.
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    "The condition never became true "
                        + $"(queued={Session.QueuedSteeringCount} running={Session.IsRunning} "
                        + $"writes={Console.Writes.Count})."
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
