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
/// file it came from, or an extension attribution). Never carries environment values or headers.
/// </summary>
internal sealed record McpServerStatus(
    string Name,
    McpServerState State,
    int ToolCount,
    string? Reason,
    string? Source
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

    private readonly List<Server> _servers;
    private readonly Action<string>? _log;
    private readonly Func<McpServerOptions, string> _describe;
    private Action<McpServerStatus>? _onServerExited;

    internal McpToolSet(
        IReadOnlyList<McpServerOptions> servers,
        Action<string>? log = null,
        Action<string>? serverLog = null,
        Func<McpServerOptions, string>? describe = null
    )
    {
        ArgumentNullException.ThrowIfNull(servers);
        _log = log;
        _describe = describe ?? (options => $"MCP server '{options.Name}'");
        _servers =
        [
            .. servers.Select(options => new Server(
                options,
                new McpServerHost(options, log: serverLog ?? log)
            )),
        ];
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
                    server.Options.Source
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
                    server.Options.Source
                );
                // The notices show a shortened reason; the log keeps the full one (stderr may
                // carry multiple lines).
                _log?.Invoke(
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
                _log?.Invoke(
                    $"{_describe(server.Options)} failed to stop: {exception.Message} Check for a leftover child process."
                );
            }
        }
    }

    private void MarkExited(Server server)
    {
        server.Status = new McpServerStatus(
            server.Options.Name,
            McpServerState.Failed,
            0,
            ServerExitedReason,
            server.Options.Source
        );
        if (!server.TryMarkExitNotified())
        {
            return;
        }

        _log?.Invoke(
            $"{_describe(server.Options)}: {ServerExitedReason}. The session continues without this server's tools."
        );
        _onServerExited?.Invoke(server.Status);
    }

    private sealed class Server(McpServerOptions options, McpServerHost host)
    {
        private int _exitNotified;

        public McpServerOptions Options { get; } = options;

        public McpServerHost Host { get; } = host;

        /// <summary>The tools the server's last listing returned; empty until listed.</summary>
        public IReadOnlyList<ITool> ListedTools { get; set; } = [];

        public bool ExitNotified => Volatile.Read(ref _exitNotified) == 1;

        public bool TryMarkExitNotified() => Interlocked.Exchange(ref _exitNotified, 1) == 0;

        public McpServerStatus Status { get; set; } =
            new(options.Name, McpServerState.NotStarted, 0, null, options.Source);
    }
}
