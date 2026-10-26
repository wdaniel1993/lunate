using Lunate.Agent;
using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>The lifecycle state of one configured MCP server.</summary>
internal enum McpServerState
{
    NotStarted,
    Started,
    Failed,
}

/// <summary>
/// One server's live status: its state, listed tool count, failure reason and source (the config
/// file it came from, or an extension attribution). <see cref="MissingVariables"/> names the
/// environment variables its definition referenced while unset and without a default. Never
/// carries environment values or headers.
/// </summary>
internal sealed record McpServerStatus(
    string Name,
    McpServerState State,
    int ToolCount,
    string? Reason,
    string? Source,
    IReadOnlyList<string>? MissingVariables = null
);

/// <summary>
/// One session's MCP servers: a <see cref="McpServerHost"/> per option that starts nothing until
/// the first <see cref="ListToolsAsync"/>, which starts each server once, isolates a failing server
/// to a recorded status and returns the union of the listed tools. <see cref="ObserveStatusesAsync"/>
/// liveness-checks started servers with a bounded probe and flips a server whose process exited (a
/// definite transport failure) to <see cref="McpServerState.Failed"/> with
/// <see cref="ServerExitedReason"/> — once per server, with one log line and one callback; a probe
/// timeout is inconclusive and leaves the server started. <see cref="DisposeAsync"/> stops every
/// server. Extension-registered
/// and config-file servers share this one listing, status and disposal path.
/// </summary>
internal sealed class McpToolSet : IAsyncDisposable
{
    /// <summary>The pinned failure reason of a server whose process exited after starting.</summary>
    internal const string ServerExitedReason = "the server process exited";

    private readonly List<Server> _servers = [];
    private readonly Action<string> _log;
    private readonly Func<McpServerOptions, string> _describe;
    private Action<McpServerStatus>? _onServerExited;
    private Action<string, IReadOnlyList<ITool>>? _onToolsChanged;

    internal McpToolSet(
        IReadOnlyList<McpServerOptions> servers,
        Action<string> log,
        Action<string>? serverLog = null,
        Func<McpServerOptions, string>? describe = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? missingVariables = null
    )
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _describe = describe ?? (options => $"MCP server '{options.Name}'");
        foreach (McpServerOptions options in servers)
        {
            Server? server = null;
            var host = new McpServerHost(
                options,
                // A server that announces a tool-list change re-lists; the updated set replaces this
                // set's view of that server and is reported through OnToolsChanged, so a consumer
                // that owns the visible tool registry can re-point it instead of serving the old set.
                onToolsChanged: tools => ToolsChanged(server, tools),
                log: serverLog ?? log
            );
            server = new Server(
                options,
                host,
                missingVariables?.GetValueOrDefault(options.Name) ?? []
            );
            _servers.Add(server);
        }
    }

    /// <summary>The per-server statuses in configuration order; live as listings happen.</summary>
    internal IReadOnlyList<McpServerStatus> Statuses =>
        [.. _servers.Select(server => server.Status)];

    /// <summary>
    /// Receives one callback per server that flips <see cref="McpServerState.Started"/> to
    /// <see cref="McpServerState.Failed"/> by the liveness probe; setting replaces the previous
    /// callback. Interactive mode wires it to its notice; other modes leave it null.
    /// </summary>
    internal void OnServerExited(Action<McpServerStatus>? callback) => _onServerExited = callback;

    /// <summary>
    /// Receives one callback per server whose tool list changed on the wire (its
    /// <c>tools/list_changed</c> notification), with the server's name and the set it now lists.
    /// Setting replaces the previous callback; a consumer that owns the visible tool registry uses
    /// it to re-point the tools of that server.
    /// </summary>
    internal void OnToolsChanged(Action<string, IReadOnlyList<ITool>>? callback) =>
        _onToolsChanged = callback;

    /// <summary>Lists every server's tools, starting each once; failures are isolated.</summary>
    internal async Task<IReadOnlyList<ITool>> ListToolsAsync(CancellationToken ct)
    {
        List<ITool> tools = [];
        foreach (Server server in _servers)
        {
            if (server.ExitNotified)
            {
                // The process exited; its status stays failed and its stale tools are not handed out.
                continue;
            }

            try
            {
                IReadOnlyList<ITool> listed = await server
                    .Host.GetToolsAsync(ct)
                    .ConfigureAwait(false);
                server.ListedTools = listed;
                server.Status = new McpServerStatus(
                    server.Options.Name,
                    McpServerState.Started,
                    listed.Count,
                    null,
                    server.Options.Source,
                    server.MissingVariables
                );
                tools.AddRange(listed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                server.Status = new McpServerStatus(
                    server.Options.Name,
                    McpServerState.Failed,
                    0,
                    exception.Message,
                    server.Options.Source,
                    server.MissingVariables
                );
                // The notices show a shortened reason; the log keeps the full one (stderr may
                // carry multiple lines).
                _log.Invoke(
                    $"{_describe(server.Options)} failed to start or list its tools: {exception.Message} The session continues without this server's tools."
                );
            }
        }

        return tools;
    }

    /// <summary>
    /// The tools listed from the servers matching <paramref name="predicate"/>, after a listing;
    /// used to identify the workspace-independent servers' tools for child runs in other roots.
    /// </summary>
    internal IReadOnlyList<ITool> ListedToolsFrom(Func<McpServerOptions, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return
        [
            .. _servers
                .Where(server => predicate(server.Options))
                .SelectMany(server => server.ListedTools),
        ];
    }

    /// <summary>
    /// Liveness-checks every started server with a bounded probe and flips a server whose process
    /// exited (a definite transport failure) to failed, logging and invoking
    /// <see cref="OnServerExited"/> once per server; a probe timeout is inconclusive and leaves the
    /// server started. Returns the current statuses.
    /// </summary>
    internal async Task<IReadOnlyList<McpServerStatus>> ObserveStatusesAsync(CancellationToken ct)
    {
        foreach (Server server in _servers)
        {
            if (server.Status.State is not McpServerState.Started || server.ExitNotified)
            {
                continue;
            }

            if (await server.Host.ProbeAsync(ct).ConfigureAwait(false) is McpProbeOutcome.Exited)
            {
                MarkExited(server);
            }
        }

        return Statuses;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Server server in _servers)
        {
            try
            {
                await server.Host.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _log.Invoke(
                    $"{_describe(server.Options)} failed to stop: {exception.Message} Check for a leftover child process."
                );
            }
        }
    }

    private void ToolsChanged(Server? server, IReadOnlyList<ITool> tools)
    {
        if (server is null || server.ExitNotified)
        {
            // A subscription replaced before construction finished, or a server already reported
            // dead: its tools are not part of this set any more.
            return;
        }

        server.ListedTools = tools;
        _onToolsChanged?.Invoke(server.Options.Name, tools);
    }

    private void MarkExited(Server server)
    {
        server.Status = new McpServerStatus(
            server.Options.Name,
            McpServerState.Failed,
            0,
            ServerExitedReason,
            server.Options.Source,
            server.MissingVariables
        );
        if (!server.TryMarkExitNotified())
        {
            return;
        }

        _log.Invoke(
            $"{_describe(server.Options)}: {ServerExitedReason}. The session continues without this server's tools."
        );
        _onServerExited?.Invoke(server.Status);
    }

    private sealed class Server(
        McpServerOptions options,
        McpServerHost host,
        IReadOnlyList<string> missingVariables
    )
    {
        private int _exitNotified;

        public McpServerOptions Options { get; } = options;

        public McpServerHost Host { get; } = host;

        /// <summary>The definition's unset variables without defaults; empty when none.</summary>
        public IReadOnlyList<string> MissingVariables { get; } = missingVariables;

        /// <summary>The tools the server's last listing returned; empty until listed.</summary>
        public IReadOnlyList<ITool> ListedTools { get; set; } = [];

        public bool ExitNotified => Volatile.Read(ref _exitNotified) == 1;

        public bool TryMarkExitNotified() => Interlocked.Exchange(ref _exitNotified, 1) == 0;

        public McpServerStatus Status { get; set; } =
            new(options.Name, McpServerState.NotStarted, 0, null, options.Source, missingVariables);
    }
}
