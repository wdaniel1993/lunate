using System.Text.Json;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// A scripted in-memory child speaking the real wire: every start builds a fresh crossed channel
/// pair with a <see cref="RemoteWire"/> on the child side, so tests drive handshakes, hook
/// answers, service calls, crashes and shutdown deterministically. The child's answers are
/// delegates on this object and survive restarts.
/// </summary>
internal sealed class ScriptedChild : IAsyncDisposable
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly List<ChildSession> _sessions = [];

    public IProcessRunner Runner => new ScriptedRunner(this);

    public IReadOnlyList<ProcessSessionRequest> Requests
    {
        get
        {
            lock (_sessions)
            {
                return [.. _sessions.Select(session => session.Request)];
            }
        }
    }

    public int StartCount => Requests.Count;

    public ChildSession Current =>
        _sessions.Count > 0
            ? _sessions[^1]
            : throw new InvalidOperationException("the child has not started");

    public bool Started => _sessions.Count > 0;

    public bool HasExited => Started && Current.HasExited;

    public bool Killed => Started && Current.Killed;

    /// <summary>
    /// True advertises the <c>commands</c> capability in the default initialize answer; false
    /// models a child that serves hooks and services only (a version-1 child, or a fork).
    /// </summary>
    public bool AdvertisesCommands { get; set; } = true;

    /// <summary>
    /// Builds the initialize answer. Unset, the answer echoes the declared surface and the
    /// <see cref="AdvertisesCommands"/> flag — what a well-behaved child does.
    /// </summary>
    public Func<InitializeRequest, InitializeResult> Handshake
    {
        get => _handshake ?? DefaultHandshake;
        set => _handshake = value;
    }

    private Func<InitializeRequest, InitializeResult>? _handshake;

    private InitializeResult DefaultHandshake(InitializeRequest request) =>
        new(request.ProtocolVersion, request.Hooks, request.Services)
        {
            Commands = AdvertisesCommands,
        };

    /// <summary>
    /// Answers one hook request; the default returns null (as a child with no opinion may). A
    /// handler may hang on its cancellation token to model a silent child.
    /// </summary>
    public Func<
        string,
        JsonElement?,
        CancellationToken,
        ValueTask<JsonElement?>
    > HookAnswers { get; set; } =
        (method, _, _) => ValueTask.FromResult<JsonElement?>(DefaultAnswer(method));

    /// <summary>The no-opinion answer a well-behaved child gives each interceptor by default.</summary>
    private static JsonElement? DefaultAnswer(string method)
    {
        HookContract? contract = HookContracts.Find(method["hook/".Length..]);
        object? result = method switch
        {
            "hook/ToolCalling" => new ToolCallingResult.Proceed(null),
            "hook/ContextBuilding" => ContextBuildingResult.None,
            "hook/RunStarting" => new RunStartingResult.None(),
            "hook/MessageCompleted" => new MessageCompletedResult.Keep(),
            "hook/InputReceived" => new InputReceivedResult.PassThrough(),
            "hook/TurnEnded" => new TurnEndedResult.None(),
            "hook/Compacting" => new CompactingResult.UseDefault(),
            "hook/ProjectTrust" => new ProjectTrustResult.Allow(),
            "hook/ToolResultReady" => new ToolResultReadyResult(string.Empty, null),
            _ => null,
        };
        return contract is null || result is null
            ? null
            : HookResultCodec.EncodeResult(contract, result);
    }

    /// <summary>Every observer notification the child received, by method.</summary>
    public List<(string Method, JsonElement? Parameters)> Notifications { get; } = [];

    /// <summary>Answers one <c>service-call</c>; the default returns null.</summary>
    public Func<ServiceCallEnvelope, JsonElement?> ServiceAnswers { get; set; } = _ => null;

    /// <summary>Runs once per start, right after the handshake request.</summary>
    public Func<RemoteWire, Task>? OnStarted { get; set; }

    /// <summary>True exits the child when the shutdown request arrives (the default).</summary>
    public bool ExitOnShutdown { get; set; } = true;

    /// <summary>The last handshake the child performed.</summary>
    public InitializeRequest? LastHandshake { get; private set; }

    /// <summary>Services the child consumed from the host, by method.</summary>
    public List<(string Method, JsonElement? Arguments)> Consumed { get; } = [];

    /// <summary>Consumes one host service from the child side (the reentrant path).</summary>
    public async Task<JsonElement?> ConsumeServiceAsync(
        string name,
        string method,
        JsonElement? arguments,
        CancellationToken cancellationToken
    )
    {
        string wireMethod = RemoteProtocol.ServiceMethod(name, method);
        JsonElement? result = await Current
            .Wire.CallAsync(wireMethod, arguments, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        Consumed.Add((wireMethod, arguments));
        return result;
    }

    /// <summary>Ends the child as a crash: the host's stream ends and the process reads exited.</summary>
    public void Crash(int exitCode = 1) => Current.Crash(exitCode);

    /// <summary>Ends the child as a clean exit.</summary>
    public void Exit() => Current.Exit();

    public async ValueTask DisposeAsync()
    {
        foreach (ChildSession session in _sessions)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class ScriptedRunner(ScriptedChild child) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IProcessSession> StartSessionAsync(
            ProcessSessionRequest request,
            CancellationToken ct
        )
        {
            ChildSession session = child.BeginStart(request);
            return Task.FromResult<IProcessSession>(session);
        }
    }

    private ChildSession BeginStart(ProcessSessionRequest request)
    {
        var session = new ChildSession(this, request);
        lock (_sessions)
        {
            _sessions.Add(session);
        }

        return session;
    }

    /// <summary>One scripted child process: the wire end plus the host-visible process session.</summary>
    internal sealed class ChildSession : IProcessSession
    {
        private readonly ScriptedChild _child;
        private readonly (InMemoryLineChannel Host, InMemoryLineChannel Guest) _pair;

        public ChildSession(ScriptedChild child, ProcessSessionRequest request)
        {
            _child = child;
            Request = request;
            _pair = InMemoryLineChannel.Pair();
            Wire = new RemoteWire(_pair.Guest);
            Wire.RequestReceived += OnRequestAsync;
            Wire.NotificationReceived += (method, parameters, _) =>
            {
                lock (_child.Notifications)
                {
                    _child.Notifications.Add((method, parameters));
                }

                return ValueTask.CompletedTask;
            };
            Wire.Start();
        }

        public ProcessSessionRequest Request { get; }

        public RemoteWire Wire { get; }

        public bool HasExited { get; private set; }

        public int? ExitCode { get; private set; }

        public bool Killed { get; private set; }

        public void Crash(int exitCode = 1)
        {
            ExitCode = exitCode;
            HasExited = true;
            End();
        }

        public void Exit()
        {
            ExitCode = 0;
            HasExited = true;
            End();
        }

        /// <summary>Ends both streams, so the host reads EOF and the child wire's loop ends.</summary>
        private void End()
        {
            _pair.Host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _pair.Guest.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            await Wire.DisposeAsync().ConfigureAwait(false);
            await _pair.Host.DisposeAsync().ConfigureAwait(false);
            await _pair.Guest.DisposeAsync().ConfigureAwait(false);
        }

        private async ValueTask<JsonElement?> OnRequestAsync(
            string method,
            JsonElement? parameters,
            CancellationToken cancellationToken
        )
        {
            if (method == RemoteProtocol.InitializeMethod)
            {
                InitializeRequest request =
                    parameters!.Value.Deserialize<InitializeRequest>(Options)
                    ?? throw new InvalidOperationException("the initialize request did not parse");
                _child.LastHandshake = request;
                InitializeResult result = _child.Handshake(request);
                if (_child.OnStarted is { } started)
                {
                    _ = Task.Run(() => started(Wire), CancellationToken.None);
                }

                return JsonSerializer.SerializeToElement(result, Options);
            }

            if (method.StartsWith("hook/", StringComparison.Ordinal))
            {
                return await _child
                    .HookAnswers(method, parameters, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (method == RemoteProtocol.ServiceCallMethod)
            {
                ServiceCallEnvelope envelope =
                    parameters!.Value.Deserialize<ServiceCallEnvelope>(Options)
                    ?? throw new InvalidOperationException(
                        "the service-call envelope did not parse"
                    );
                return _child.ServiceAnswers(envelope);
            }

            if (method == RemoteProtocol.ShutdownMethod)
            {
                if (_child.ExitOnShutdown)
                {
                    Exit();
                }

                return null;
            }

            throw new RemoteCallException(-32601, $"the scripted child does not know '{method}'");
        }

        // IProcessSession: the host side of the crossed pair is transparent.

        public ValueTask<string?> ReadOutputLineAsync(CancellationToken cancellationToken) =>
            _pair.Host.ReadLineAsync(cancellationToken);

        public ValueTask<string?> ReadErrorLineAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask WriteInputLineAsync(string line, CancellationToken cancellationToken) =>
            _pair.Host.WriteLineAsync(line, cancellationToken);

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            while (!HasExited)
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }

            return ExitCode ?? 0;
        }

        public ValueTask KillAsync()
        {
            Killed = true;
            Exit();
            return ValueTask.CompletedTask;
        }
    }
}
