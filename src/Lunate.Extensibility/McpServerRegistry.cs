using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The MCP servers extensions registered. Registration is registration only: nothing is started and
/// nothing is spawned here; the registered process starts on the first tool listing. The first
/// registration per server name (ordinal) wins; a duplicate is refused and logged naming both
/// extensions, never silently shadowed.
/// </summary>
public sealed class McpServerRegistry
{
    private readonly IExtensionLog _log;
    private readonly object _gate = new();
    private readonly List<RegisteredMcpServer> _servers = [];

    public McpServerRegistry(IExtensionLog? log = null) => _log = log ?? NullExtensionLog.Instance;

    public void Register(string extensionId, McpServerRegistration registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Name);

        RegisteredMcpServer? existing;
        lock (_gate)
        {
            existing = _servers.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, registration.Name, StringComparison.Ordinal)
            );
            if (existing is null)
            {
                _servers.Add(new RegisteredMcpServer(extensionId, registration));
            }
        }

        if (existing is not null)
        {
            _log.Warn(
                $"extension MCP: server '{registration.Name}' from extension '{extensionId}' was refused because extension '{existing.ExtensionId}' registered it first; the first registration wins."
            );
        }
    }

    /// <summary>The registered servers in registration order.</summary>
    public IReadOnlyList<RegisteredMcpServer> List()
    {
        lock (_gate)
        {
            return [.. _servers];
        }
    }

    /// <summary>Removes every server the extension registered.</summary>
    public void Unregister(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        lock (_gate)
        {
            _servers.RemoveAll(server =>
                string.Equals(server.ExtensionId, extensionId, StringComparison.Ordinal)
            );
        }
    }
}

/// <summary>One registered MCP server with the extension that contributed it.</summary>
public sealed record RegisteredMcpServer(string ExtensionId, McpServerRegistration Registration)
{
    /// <summary>The server name; the key for first-registration-wins and its tools' namespace.</summary>
    public string Name => Registration.Name;
}
