using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Protocols;
using AgentExposure = Lunate.Agent.ToolExposure;
using ContractExposure = Lunate.Extensibility.Abstractions.ToolExposure;

namespace Lunate.Coding;

/// <summary>
/// Maps extension MCP-server registrations onto the shared <see cref="McpToolSet"/>: one host per
/// registration that starts nothing until the first <see cref="ListToolsAsync"/> and stops on
/// disposal. Each status carries the source <c>extension '{id}'</c>. A server that fails to start
/// or list is logged by the set with the server and extension named and the session continues
/// without its tools; a server whose process exited is logged once by the set and observable
/// through <see cref="Statuses"/>. This is the single mapping the extension-host composition
/// reuses; config-file servers feed the same set.
/// </summary>
internal sealed class ExtensionMcpServers : IAsyncDisposable
{
    private readonly Dictionary<string, string> _extensionByServer;
    private readonly McpToolSet _set;

    internal ExtensionMcpServers(
        IExtensionLog log,
        IReadOnlyList<RegisteredMcpServer> registrations
    )
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(registrations);
        _extensionByServer = registrations.ToDictionary(
            registration => registration.Name,
            registration => registration.ExtensionId,
            StringComparer.Ordinal
        );
        _set = new McpToolSet(
            [.. registrations.Select(registration => Map(registration))],
            log.Error,
            log.Warn,
            options =>
                $"extension MCP server '{options.Name}' from extension '{_extensionByServer[options.Name]}'"
        );
    }

    /// <summary>The per-server statuses in registration order; live as listings happen.</summary>
    internal IReadOnlyList<McpServerStatus> Statuses => _set.Statuses;

    /// <summary>Receives one callback per server whose process exited after starting.</summary>
    internal void OnServerExited(Action<McpServerStatus>? callback) =>
        _set.OnServerExited(callback);

    /// <summary>Lists every server's tools, starting each server once; failures log and skip it.</summary>
    internal async Task<IReadOnlyList<ITool>> ListToolsAsync(CancellationToken ct) =>
        await _set.ListToolsAsync(ct).ConfigureAwait(false);

    /// <summary>Liveness-checks every started server; an exited process flips to failed once.</summary>
    internal Task<IReadOnlyList<McpServerStatus>> ObserveStatusesAsync(CancellationToken ct) =>
        _set.ObserveStatusesAsync(ct);

    public ValueTask DisposeAsync() => _set.DisposeAsync();

    private static McpServerOptions Map(RegisteredMcpServer registration) =>
        new(
            registration.Name,
            registration.Registration.Config.Command,
            registration.Registration.Config.Arguments,
            registration.Registration.Config.Environment,
            registration.Registration.Config.WorkingDirectory,
            registration.Registration.Config.CallTimeout,
            MapExposure(registration.Registration.Exposure),
            registration.Registration.ExposureOverrides?.ToDictionary(
                pair => pair.Key,
                pair => MapExposure(pair.Value),
                StringComparer.Ordinal
            ),
            NamespaceName: registration.Name,
            Source: $"extension '{registration.ExtensionId}'"
        );

    private static AgentExposure MapExposure(ContractExposure exposure) =>
        exposure switch
        {
            ContractExposure.Direct => AgentExposure.Direct,
            ContractExposure.ModelOnly => AgentExposure.ModelOnly,
            ContractExposure.Programmatic => AgentExposure.Programmatic,
            ContractExposure.Deferred => AgentExposure.Deferred,
            ContractExposure.Hidden => AgentExposure.Hidden,
            _ => throw new ArgumentOutOfRangeException(nameof(exposure), exposure, null),
        };
}
