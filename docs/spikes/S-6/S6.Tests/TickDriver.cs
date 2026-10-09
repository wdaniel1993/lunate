using System.Reflection;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;

namespace S6.Tests;

/// <summary>
/// Deterministic tick driver, mirroring XenoAtom.Terminal.UI's own internal
/// <c>TerminalAppTestDriver</c>. The public surface has no virtual clock, so
/// this reaches <c>TerminalApp.BeginRun/Tick/EndRun/SetUpdateCallback</c> by
/// reflection. This is the make-or-break question for deterministic tests:
/// if the internal hooks move, the driver breaks with the package.
/// </summary>
internal sealed class TickDriver : IDisposable
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly InMemoryTerminalBackend _backend;
    private readonly TerminalSession _session;
    private readonly TerminalApp _app;
    private readonly MethodInfo _beginRun;
    private readonly MethodInfo _tick;
    private readonly MethodInfo _endRun;
    private readonly long _step;
    private long _timestamp;

    public TickDriver(
        Visual root,
        TerminalHostKind hostKind = TerminalHostKind.Inline,
        TerminalSize? size = null
    )
    {
        _backend = new InMemoryTerminalBackend(size ?? new TerminalSize(80, 24));
        _session = XenoAtom.Terminal.Terminal.Open(
            _backend,
            new TerminalOptions { ImplicitStartInput = true },
            force: true
        );
        _app = new TerminalApp(
            root,
            _session.Instance,
            new TerminalAppOptions { HostKind = hostKind, LoopMode = TerminalLoopMode.Auto }
        );

        var callbackType = typeof(Func<TerminalRunningContext, TerminalLoopResult>);
        var setUpdate = typeof(TerminalApp).GetMethod("SetUpdateCallback", Flags, [callbackType])
            ?? throw new MissingMethodException("TerminalApp.SetUpdateCallback(Func<...>) not found.");
        setUpdate.Invoke(_app, [new Func<TerminalRunningContext, TerminalLoopResult>(_ => TerminalLoopResult.Continue)]);

        _beginRun = Require("BeginRun");
        _tick = Require("Tick");
        _endRun = Require("EndRun");

        _beginRun.Invoke(_app, null);
        _timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        _step = Math.Max(1, System.Diagnostics.Stopwatch.Frequency / 100);
    }

    public InMemoryTerminalBackend Backend => _backend;

    public TerminalApp App => _app;

    public TerminalInstance Terminal => _session.Instance;

    public void Tick(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _timestamp += _step;
            _tick.Invoke(_app, [_timestamp]);
        }
    }

    public void Dispose()
    {
        _endRun.Invoke(_app, null);
        _session.Dispose();
    }

    private static MethodInfo Require(string name) =>
        typeof(TerminalApp).GetMethod(name, Flags)
        ?? throw new MissingMethodException($"TerminalApp.{name} not found.");
}
