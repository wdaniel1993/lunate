using System.Globalization;
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

    /// <summary>
    /// How long a starting server may produce no output before it counts as hung. It is a stall
    /// bound, not a total startup budget: child output shows the process is alive and restarts it.
    /// </summary>
    internal static readonly TimeSpan DefaultStartupStallBound = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The bound of one liveness probe, short enough for command and call paths. It is a UI bound
    /// only: a probe that outlives it is <see cref="McpProbeOutcome.Unresponsive"/>, not a dead server.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly McpServerOptions options;
    private readonly TimeSpan startupStallBound;
    private readonly Action<IReadOnlyList<ITool>>? onToolsChanged;
    private readonly Action<string>? log;
    private readonly McpElicitationOutcome elicitationOutcome = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, McpToolAdapter> adapters = new(StringComparer.Ordinal);
    private McpClient? client;
    private IAsyncDisposable? listChangedRegistration;
    private IReadOnlyList<ITool> tools = [];
    private bool disposed;

    /// <param name="log">
    /// Receives the warning when an exposure override names a tool that is not listed. The spec
    /// invariant is that such an override is never silently ignored: production always supplies a
    /// log delegate — the internal MCP tool set enforces it at the only production construction
    /// path — and the parameter stays optional for tests, which legitimately omit it.
    /// </param>
    public McpServerHost(
        McpServerOptions options,
        Action<IReadOnlyList<ITool>>? onToolsChanged = null,
        Action<string>? log = null
    )
        : this(options, DefaultStartupStallBound, onToolsChanged, log) { }

    /// <summary>Test seam: the startup stall bound is otherwise the production default.</summary>
    internal McpServerHost(
        McpServerOptions options,
        TimeSpan startupStallBound,
        Action<IReadOnlyList<ITool>>? onToolsChanged = null,
        Action<string>? log = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        this.startupStallBound = startupStallBound;
        this.onToolsChanged = onToolsChanged;
        this.log = log;
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

    /// <summary>
    /// A bounded liveness probe: the SDK client hides the process, so a short tool-list round trip
    /// is the check. <see cref="McpProbeOutcome.Alive"/> when the server answered inside the probe
    /// bound; <see cref="McpProbeOutcome.Exited"/> when the call failed with a transport-level error
    /// (evidence the process is gone); <see cref="McpProbeOutcome.Unresponsive"/> when the probe
    /// bound elapsed without an answer or the host was never started or was disposed — no evidence
    /// either way, so it never counts as an exit. The cancellation token is the caller's;
    /// cancellation throws.
    /// </summary>
    public async ValueTask<McpProbeOutcome> ProbeAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (disposed || !Started || client is null)
            {
                return McpProbeOutcome.Unresponsive;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);
            try
            {
                await client.ListToolsAsync(cancellationToken: timeout.Token).ConfigureAwait(false);
                return McpProbeOutcome.Alive;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return McpProbeOutcome.Unresponsive;
            }
            catch (Exception)
            {
                return McpProbeOutcome.Exited;
            }
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
        using var liveness = new StartupLiveness(startupStallBound, ct);
        // With known credential values the child starts from an explicit environment Lunate builds
        // (see McpChildEnvironment); with none it keeps today's full inheritance. The declared env
        // map is the per-server opt-in in either case.
        IReadOnlyList<string> withheld = options.WithheldCredentialValues ?? [];
        bool withholds = withheld.Count > 0;
        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = options.Name,
                Command = options.Command,
                Arguments = [.. options.Arguments],
                WorkingDirectory = options.WorkingDirectory,
                EnvironmentVariables = withholds
                    ? McpChildEnvironment.BuildChildEnvironment(withheld, options.Environment)
                    : options.Environment?.ToDictionary(
                        pair => pair.Key,
                        pair => (string?)pair.Value
                    ),
                InheritEnvironmentVariables = !withholds,
                ShutdownTimeout = ShutdownTimeout,
                // stderr is the only child output the client can observe while it waits on the
                // protocol stream, so each line is the liveness signal that restarts the stall
                // bound. A slow server that keeps talking therefore outlives the bound; only a
                // silent one is reported hung.
                StandardErrorLines = _ => liveness.Touch(),
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
                        Capabilities = new ClientCapabilities
                        {
                            Elicitation = new ElicitationCapability { Form = new() },
                        },
                        Handlers = new McpClientHandlers
                        {
                            ElicitationHandler = (request, token) =>
                                McpElicitationHandler.HandleAsync(
                                    request,
                                    options.Name,
                                    elicitationOutcome,
                                    token
                                ),
                        },
                        // The SDK's fixed wall-clock window is replaced by our stall bound: it
                        // counts silence, not total elapsed time, so a live but slow server is
                        // never reported failed. See StartupLiveness.
                        InitializationTimeout = Timeout.InfiniteTimeSpan,
                    },
                    loggerFactory: null,
                    cancellationToken: liveness.Token
                )
                .ConfigureAwait(false);

            // The handshake is done: a late timer must not cancel the listing or anything after it.
            liveness.Complete();
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
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            if (liveness.Stalled)
            {
                throw new InvalidOperationException(
                    $"MCP server '{options.Name}' stopped responding while starting: it produced no output for {Describe(startupStallBound)}. Check that the configured command starts a server that does not wait for input, credentials or a lock before it answers the handshake.",
                    exception
                );
            }

            throw new InvalidOperationException(
                $"MCP server '{options.Name}' failed to start: {exception.Message}. Check the command and arguments in the server configuration.",
                exception
            );
        }
    }

    /// <summary>Culture-invariant span text for a message a model or a person reads.</summary>
    private static string Describe(TimeSpan span) =>
        string.Create(CultureInfo.InvariantCulture, $"{span.TotalSeconds:0.###} seconds");

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
            // The host is the source `GetToolsAsync` answers from: a refresh that only reported the
            // new set would still hand out the old one on every later call.
            tools = updated;
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
        var namespaceName = options.NamespaceName ?? options.Name;
        var unmatched = options.ExposureOverrides is { } overrides
            ? new HashSet<string>(overrides.Keys, StringComparer.Ordinal)
            : null;
        var wrapped = new List<ITool>(listed.Count);

        foreach (var tool in listed)
        {
            var key = tool.ProtocolTool.Name;
            unmatched?.Remove(key);
            if (!adapters.TryGetValue(key, out var adapter))
            {
                var exposure = options.Exposure;
                if (
                    options.ExposureOverrides is { } exposureOverrides
                    && exposureOverrides.TryGetValue(key, out var overridden)
                )
                {
                    exposure = overridden;
                }

                adapter = new McpToolAdapter(
                    client,
                    options.Name,
                    namespaceName,
                    exposure,
                    tool,
                    callTimeout,
                    log,
                    elicitationOutcome
                );
                adapters[key] = adapter;
            }

            wrapped.Add(adapter);
        }

        if (unmatched is { Count: > 0 })
        {
            foreach (string unknown in unmatched.Order(StringComparer.Ordinal))
            {
                log?.Invoke(
                    $"MCP server '{options.Name}': exposure override names unknown tool '{unknown}'. Check the tool name in the server configuration; the override is ignored."
                );
            }
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
