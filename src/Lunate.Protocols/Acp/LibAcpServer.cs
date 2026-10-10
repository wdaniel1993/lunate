using System.Globalization;
using System.Text;
using Acp;
using Acp.JsonRpc;
using Acp.Schema;
using Acp.Streaming;
using Lunate.Agent;

namespace Lunate.Protocols.Acp;

/// <summary>
/// The <see cref="IAcpServer"/> adapter on LibAcp: initialize (protocol v1, honest text-only
/// capabilities), session/new (a harness per session, the session cwd as its workspace),
/// session/prompt (one run, text blocks only, streaming <c>session/update</c> via
/// <see cref="AcpEventMapper"/>), and session/cancel (cancels the in-flight run's token, or latches
/// onto a queued prompt so its run begins already cancelled).
/// Sessions run sequentially per connection — one active run at a time.
/// </summary>
internal sealed class LibAcpServer(Action<string>? log = null) : IAcpServer
{
    /// <inheritdoc />
    public async Task RunAsync(
        Stream input,
        Stream output,
        Func<string, AgentHarness> createHarness,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(createHarness);

        await using var stream = new NdJsonStream(input, output);
        await using var connection = new AgentSideConnection(
            side => new ConnectionAgent(side, createHarness, log),
            stream
        );
        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(shutdown.SetResult);
        await Task.WhenAny(connection.Closed, shutdown.Task).ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>One ACP connection: the session map, the run gate and the IAgent surface.</summary>
    private sealed class ConnectionAgent(
        AgentSideConnection connection,
        Func<string, AgentHarness> createHarness,
        Action<string>? log
    ) : IAgent
    {
        private readonly object sessionsGate = new();
        private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim runs = new(1, 1);

        public Task<InitializeResponse> InitializeAsync(
            InitializeRequest request,
            CancellationToken ct
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            Log(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"initialize: {request.ClientInfo?.Name ?? "unknown client"} {request.ClientInfo?.Version ?? "?"} (protocol {request.ProtocolVersion})"
                )
            );
            if (request.ProtocolVersion > Protocol.Version)
            {
                Log(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"initialize: client requested protocol {request.ProtocolVersion}; responding with {Protocol.Version}"
                    )
                );
            }

            LogUnsupportedClientCapabilities(request.ClientCapabilities);
            return Task.FromResult(
                new InitializeResponse
                {
                    ProtocolVersion = Protocol.Version,
                    AgentInfo = new Implementation { Name = "lunate", Version = ServerVersion },
                    AgentCapabilities = new AgentCapabilities
                    {
                        PromptCapabilities = new PromptCapabilities
                        {
                            Audio = false,
                            EmbeddedContext = false,
                            Image = false,
                        },
                    },
                }
            );
        }

        public Task<AuthenticateResponse?> AuthenticateAsync(
            AuthenticateRequest request,
            CancellationToken ct
        ) => throw RequestErrorException.MethodNotFound(AgentMethods.Authenticate);

        public Task<NewSessionResponse> NewSessionAsync(
            NewSessionRequest request,
            CancellationToken ct
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.Cwd))
            {
                throw RequestErrorException.InvalidParams(
                    additionalMessage: "session/new requires a working directory (cwd)."
                );
            }

            if (request.McpServers is { Count: > 0 })
            {
                Log(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"session/new: ignoring {request.McpServers.Count} client-provided MCP servers; not supported in v1"
                    )
                );
            }

            AgentHarness harness;
            try
            {
                harness = createHarness(request.Cwd);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log($"session/new: failed to create the session harness: {exception.Message}");
                throw RequestErrorException.InternalError(
                    new { details = exception.Message },
                    $"failed to create the session harness: {exception.Message}"
                );
            }

            string id = Guid.NewGuid().ToString("N");
            lock (sessionsGate)
            {
                sessions.Add(id, new Session(id, harness));
            }

            Log($"session/new: {id} (cwd {request.Cwd})");
            return Task.FromResult(new NewSessionResponse { SessionId = new SessionId(id) });
        }

        public async Task<PromptResponse> PromptAsync(PromptRequest request, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(request);
            Session session = RequireSession(request.SessionId);
            string prompt = PromptText(request, session);

            session.BeginPrompt();
            try
            {
                await runs.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    session.BeginRun(cancellation);
                    try
                    {
                        string stopReason = StopReasons.Stop;
                        string? runError = null;
                        await foreach (
                            AgentEvent agentEvent in session.Harness.RunAsync(
                                prompt,
                                cancellation.Token
                            )
                        )
                        {
                            switch (agentEvent)
                            {
                                case RunFinished finished:
                                    stopReason = finished.StopReason;
                                    break;
                                case RunError error:
                                    runError = error.Message;
                                    Log($"session/prompt {session.Id}: {error.Message}");
                                    break;
                                case RunStarted or TextMessageStart or TextMessageEnd:
                                    // Lifecycle markers; the prompt response and text chunks carry them.
                                    break;
                                default:
                                    await SendUpdateAsync(request.SessionId, agentEvent, ct)
                                        .ConfigureAwait(false);
                                    break;
                            }
                        }

                        return new PromptResponse
                        {
                            StopReason = runError is null
                                ? AcpEventMapper.MapStopReason(stopReason)
                                : AcpEventMapper.ErrorStopReason,
                        };
                    }
                    finally
                    {
                        session.EndRun();
                    }
                }
                finally
                {
                    runs.Release();
                }
            }
            finally
            {
                session.EndPrompt();
            }
        }

        public Task CancelAsync(CancelNotification notification, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(notification);
            Session? session = FindSession(notification.SessionId);
            if (session is null)
            {
                Log($"session/cancel: unknown session '{notification.SessionId.Value}'; ignored");
                return Task.CompletedTask;
            }

            session.Cancel();
            Log($"session/cancel: {session.Id}");
            return Task.CompletedTask;
        }

        private async Task SendUpdateAsync(
            SessionId sessionId,
            AgentEvent agentEvent,
            CancellationToken ct
        )
        {
            if (AcpEventMapper.Map(agentEvent) is not { } update)
            {
                Log($"session/prompt: not sent ({agentEvent.GetType().Name})");
                return;
            }

            await connection
                .SessionUpdateAsync(
                    new SessionNotification { SessionId = sessionId, Update = update },
                    ct
                )
                .ConfigureAwait(false);
        }

        private string PromptText(PromptRequest request, Session session)
        {
            var text = new StringBuilder();
            foreach (ContentBlock block in request.Prompt ?? [])
            {
                if (block is TextContent content)
                {
                    if (text.Length > 0)
                    {
                        text.Append('\n');
                    }

                    text.Append(content.Text);
                }
                else
                {
                    Log(
                        $"session/prompt {session.Id}: skipping non-text content block ({block.Type})"
                    );
                }
            }

            if (text.Length == 0)
            {
                throw RequestErrorException.InvalidParams(
                    additionalMessage: "session/prompt requires at least one text content block; the agent advertises text-only prompt capabilities."
                );
            }

            return text.ToString();
        }

        private Session RequireSession(SessionId? sessionId) =>
            FindSession(sessionId)
            ?? throw RequestErrorException.InvalidParams(
                additionalMessage: $"unknown session '{sessionId?.Value}'; create one with session/new first."
            );

        private Session? FindSession(SessionId? sessionId)
        {
            if (sessionId is null)
            {
                return null;
            }

            lock (sessionsGate)
            {
                return sessions.TryGetValue(sessionId.Value, out Session? session) ? session : null;
            }
        }

        private void LogUnsupportedClientCapabilities(ClientCapabilities? capabilities)
        {
            if (capabilities?.Fs is { } fs && (fs.ReadTextFile == true || fs.WriteTextFile == true))
            {
                Log("initialize: client offers its file system; not used in v1 (T-28)");
            }

            if (capabilities?.Terminal == true)
            {
                Log("initialize: client offers terminal support; not used in v1 (T-28)");
            }
        }

        private void Log(string message) => log?.Invoke(message);

        private static string ServerVersion { get; } =
            typeof(LibAcpServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// One ACP session: the harness, the dispatched prompts (queued behind the connection-wide run
    /// gate included) and the in-flight run's cancellation.
    /// </summary>
    private sealed class Session(string id, AgentHarness harness)
    {
        private readonly object gate = new();
        private CancellationTokenSource? active;
        private int prompts;
        private bool cancelPending;

        public string Id { get; } = id;

        public AgentHarness Harness { get; } = harness;

        public void BeginPrompt()
        {
            lock (gate)
            {
                prompts++;
            }
        }

        public void EndPrompt()
        {
            lock (gate)
            {
                prompts--;
                if (prompts == 0)
                {
                    cancelPending = false;
                }
            }
        }

        public void BeginRun(CancellationTokenSource cancellation)
        {
            bool cancel;
            lock (gate)
            {
                active = cancellation;
                cancel = cancelPending;
                cancelPending = false;
            }

            if (cancel)
            {
                cancellation.Cancel();
            }
        }

        public void EndRun()
        {
            lock (gate)
            {
                active = null;
            }
        }

        /// <summary>
        /// Cancels the in-flight run, or latches onto a dispatched prompt that is still queued
        /// (consumed by <see cref="BeginRun"/>, so the run begins already cancelled). A cancel
        /// with no prompt in flight stays a no-op.
        /// </summary>
        public void Cancel()
        {
            lock (gate)
            {
                if (active is not null)
                {
                    active.Cancel();
                }
                else if (prompts > 0)
                {
                    cancelPending = true;
                }
            }
        }
    }
}
