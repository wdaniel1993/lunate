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

/// <summary>One server's live status: its state, listed tool count and failure reason.</summary>
internal sealed record McpServerStatus(
    string Name,
    McpServerState State,
    int ToolCount,
    string? Reason
);

/// <summary>
/// One session's configured MCP servers: a <see cref="McpServerHost"/> per option that starts
/// nothing until the first <see cref="ListToolsAsync"/>, which starts each server once, isolates a
/// failing server to a recorded status and returns the union of the listed tools.
/// <see cref="DisposeAsync"/> stops every server. Extension-registered and config-file servers
/// share this one listing, status and disposal path.
/// </summary>
internal sealed class McpToolSet : IAsyncDisposable
{
    private readonly List<Server> _servers;
    private readonly Action<string>? _log;
    private readonly Func<McpServerOptions, string> _describe;

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

    /// <summary>Lists every server's tools, starting each once; failures are isolated.</summary>
    internal async Task<IReadOnlyList<ITool>> ListToolsAsync(CancellationToken ct)
    {
        List<ITool> tools = [];
        foreach (Server server in _servers)
        {
            try
            {
                IReadOnlyList<ITool> listed = await server
                    .Host.GetToolsAsync(ct)
                    .ConfigureAwait(false);
                server.Status = new McpServerStatus(
                    server.Options.Name,
                    McpServerState.Started,
                    listed.Count,
                    null
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
                    exception.Message
                );
            }
        }

        return tools;
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

    private sealed class Server(McpServerOptions options, McpServerHost host)
    {
        public McpServerOptions Options { get; } = options;

        public McpServerHost Host { get; } = host;

        public McpServerStatus Status { get; set; } =
            new(options.Name, McpServerState.NotStarted, 0, null);
    }
}
