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
/// disposal. A server that fails to start or list is logged with the server and extension named and
/// the session continues without its tools. This is the single mapping the extension-host
/// composition reuses; config-file servers feed the same set.
/// </summary>
internal sealed class ExtensionMcpServers : IAsyncDisposable
{
    private readonly IExtensionLog _log;
    private readonly Dictionary<string, string> _extensionByServer;
    private readonly McpToolSet _set;

    internal ExtensionMcpServers(
        IExtensionLog log,
        IReadOnlyList<RegisteredMcpServer> registrations
    )
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(registrations);
        _log = log;
        _extensionByServer = registrations.ToDictionary(
            registration => registration.Name,
            registration => registration.ExtensionId,
            StringComparer.Ordinal
        );
        _set = new McpToolSet(
            [.. registrations.Select(registration => Map(registration.Registration))],
            log.Error,
            log.Warn,
            options =>
                $"extension MCP server '{options.Name}' from extension '{_extensionByServer[options.Name]}'"
        );
    }

    /// <summary>Lists every server's tools, starting each server once; failures log and skip it.</summary>
    internal async Task<IReadOnlyList<ITool>> ListToolsAsync(CancellationToken ct)
    {
        IReadOnlyList<ITool> tools = await _set.ListToolsAsync(ct).ConfigureAwait(false);
        foreach (McpServerStatus status in _set.Statuses)
        {
            if (status.State is McpServerState.Failed)
            {
                _log.Error(
                    $"extension MCP server '{status.Name}' from extension '{_extensionByServer[status.Name]}' failed to start or list its tools: {status.Reason} The session continues without this server's tools."
                );
            }
        }

        return tools;
    }

    public ValueTask DisposeAsync() => _set.DisposeAsync();

    private static McpServerOptions Map(McpServerRegistration registration) =>
        new(
            registration.Name,
            registration.Config.Command,
            registration.Config.Arguments,
            registration.Config.Environment,
            registration.Config.WorkingDirectory,
            registration.Config.CallTimeout,
            MapExposure(registration.Exposure),
            registration.ExposureOverrides?.ToDictionary(
                pair => pair.Key,
                pair => MapExposure(pair.Value),
                StringComparer.Ordinal
            ),
            NamespaceName: registration.Name
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
