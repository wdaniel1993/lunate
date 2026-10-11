using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Protocols;
using AgentExposure = Lunate.Agent.ToolExposure;
using ContractExposure = Lunate.Extensibility.Abstractions.ToolExposure;

namespace Lunate.Coding;

/// <summary>
/// Maps extension MCP-server registrations to protocol hosts: one <see cref="McpServerHost"/> per
/// registration that starts nothing until the first <see cref="ListToolsAsync"/> and stops on
/// disposal. A server that fails to start or list is logged with the server and extension named and
/// the session continues without its tools. This is the single mapping the extension-host
/// composition reuses.
/// </summary>
internal sealed class ExtensionMcpServers : IAsyncDisposable
{
    private readonly IExtensionLog _log;
    private readonly List<Server> _servers;

    internal ExtensionMcpServers(
        IExtensionLog log,
        IReadOnlyList<RegisteredMcpServer> registrations
    )
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(registrations);
        _log = log;
        _servers =
        [
            .. registrations.Select(registration => new Server(
                registration,
                new McpServerHost(Map(registration.Registration), log: log.Warn)
            )),
        ];
    }

    /// <summary>Lists every server's tools, starting each server once; failures log and skip it.</summary>
    internal async Task<IReadOnlyList<ITool>> ListToolsAsync(CancellationToken ct)
    {
        List<ITool> tools = [];
        foreach (Server server in _servers)
        {
            try
            {
                tools.AddRange(await server.Host.GetToolsAsync(ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _log.Error(
                    $"extension MCP server '{server.Registration.Name}' from extension '{server.Registration.ExtensionId}' failed to start or list its tools: {exception.Message} The session continues without this server's tools."
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
                _log.Error(
                    $"extension MCP server '{server.Registration.Name}' failed to stop: {exception.Message} Check for a leftover child process."
                );
            }
        }
    }

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

    private sealed record Server(RegisteredMcpServer Registration, McpServerHost Host);
}
