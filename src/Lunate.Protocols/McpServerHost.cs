using Lunate.Agent;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Lunate.Protocols;

/// <summary>
/// One configured MCP server. Construction spawns nothing; the first
/// <see cref="GetToolsAsync"/> starts the stdio process, initializes and lists the server's tools,
/// which are wrapped as <see cref="ITool"/>s named <c>server__tool</c>. Tool-list changes re-list
/// and surface the new set through the callback; <see cref="DisposeAsync"/> stops the process
/// idempotently.
/// </summary>
/// <remarks>
/// The client negotiates the stable 2025-11-25 protocol: under the 2026-07-28 revision a conforming
/// server only sends tool-list changes over a <c>subscriptions/listen</c> stream, which the SDK's
/// client does not expose yet, while the stable revision broadcasts them session-wide.
/// </remarks>
public sealed class McpServerHost : IAsyncDisposable
{
    private const string StableProtocolVersion = "2025-11-25";

    private static readonly TimeSpan DefaultCallTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly McpServerOptions options;
    private readonly Action<IReadOnlyList<ITool>>? onToolsChanged;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, McpToolAdapter> adapters = new(StringComparer.Ordinal);
    private McpClient? client;
    private IAsyncDisposable? listChangedRegistration;
    private IReadOnlyList<ITool> tools = [];
    private bool disposed;

    public McpServerHost(
        McpServerOptions options,
        Action<IReadOnlyList<ITool>>? onToolsChanged = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        this.onToolsChanged = onToolsChanged;
    }

    public string Name => options.Name;

    public bool Started { get; private set; }

    public async ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!Started)
            {
                await StartAsync(ct).ConfigureAwait(false);
            }

            return tools;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            await DisposeClientAsync().ConfigureAwait(false);
            tools = [];
            adapters.Clear();
            Started = false;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task StartAsync(CancellationToken ct)
    {
        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = options.Name,
                Command = options.Command,
                Arguments = [.. options.Arguments],
                WorkingDirectory = options.WorkingDirectory,
                EnvironmentVariables = options.Environment?.ToDictionary(
                    pair => pair.Key,
                    pair => (string?)pair.Value
                ),
                ShutdownTimeout = ShutdownTimeout,
            }
        );

        McpClient? started = null;
        try
        {
            started = await McpClient
                .CreateAsync(
                    transport,
                    new McpClientOptions
                    {
                        ClientInfo = new Implementation
                        {
                            Name = "lunate",
                            Version = ClientVersion,
                        },
                        ProtocolVersion = StableProtocolVersion,
                    },
                    loggerFactory: null,
                    cancellationToken: ct
                )
                .ConfigureAwait(false);

            listChangedRegistration = started.RegisterNotificationHandler(
                NotificationMethods.ToolListChangedNotification,
                (notification, token) =>
                {
                    _ = RefreshAsync(token);
                    return default;
                }
            );
            client = started;
            tools = await ListToolsAsync(ct).ConfigureAwait(false);
            Started = true;
        }
        catch (Exception exception)
        {
            await DisposeClientAsync().ConfigureAwait(false);
            if (exception is OperationCanceledException)
            {
                throw;
            }

            throw new InvalidOperationException(
                $"MCP server '{options.Name}' failed to start: {exception.Message}. Check the command and arguments in the server configuration.",
                exception
            );
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var acquired = false;
        try
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;
            if (disposed || client is null)
            {
                return;
            }

            var updated = await ListToolsAsync(ct).ConfigureAwait(false);
            onToolsChanged?.Invoke(updated);
        }
        catch (Exception)
        {
            // A failed refresh must not take the session down; the next call or notification retries.
        }
        finally
        {
            if (acquired)
            {
                gate.Release();
            }
        }
    }

    private async Task<IReadOnlyList<ITool>> ListToolsAsync(CancellationToken ct)
    {
        var listed = await client!.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        var callTimeout = options.CallTimeout ?? DefaultCallTimeout;
        var wrapped = new List<ITool>(listed.Count);

        foreach (var tool in listed)
        {
            var key = tool.ProtocolTool.Name;
            if (!adapters.TryGetValue(key, out var adapter))
            {
                adapter = new McpToolAdapter(client, options.Name, tool, callTimeout);
                adapters[key] = adapter;
            }

            wrapped.Add(adapter);
        }

        return wrapped;
    }

    private async ValueTask DisposeClientAsync()
    {
        if (listChangedRegistration is not null)
        {
            await listChangedRegistration.DisposeAsync().ConfigureAwait(false);
            listChangedRegistration = null;
        }

        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            client = null;
        }
    }

    private static string ClientVersion { get; } =
        typeof(McpServerHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
