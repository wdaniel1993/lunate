using Lunate.Extensibility;

namespace Lunate.Coding;

/// <summary>
/// An extension MCP-server registration skipped because a configured server owns the name.
/// <see cref="ConfiguredSource"/> is the winning config file, for logging and `/mcp`.
/// </summary>
internal sealed record ShadowedMcpServer(string Name, string ExtensionId, string? ConfiguredSource);

/// <summary>
/// The configured-wins rule for MCP server names, shared by interactive, print and ACP: a
/// registration whose name matches a configured <c>mcp.json</c> server is kept out of the listing
/// (so no duplicate-name collision reaches the tool registry) and reported as shadowed.
/// </summary>
internal static class McpNameCollisions
{
    /// <summary>The configured server names mapped to their sources; null when none are composed.</summary>
    internal static IReadOnlyDictionary<string, string?>? ConfiguredServers(
        McpToolSet? configured
    ) =>
        configured?.Statuses.ToDictionary(
            status => status.Name,
            status => status.Source,
            StringComparer.Ordinal
        );

    internal static (
        IReadOnlyList<RegisteredMcpServer> Kept,
        IReadOnlyList<ShadowedMcpServer> Shadowed
    ) Filter(
        IReadOnlyList<RegisteredMcpServer> registrations,
        IReadOnlyDictionary<string, string?>? configuredServers
    )
    {
        ArgumentNullException.ThrowIfNull(registrations);
        if (configuredServers is null or { Count: 0 })
        {
            return (registrations, []);
        }

        List<RegisteredMcpServer> kept = [];
        List<ShadowedMcpServer> shadowed = [];
        foreach (RegisteredMcpServer registration in registrations)
        {
            if (configuredServers.TryGetValue(registration.Name, out string? source))
            {
                shadowed.Add(
                    new ShadowedMcpServer(registration.Name, registration.ExtensionId, source)
                );
            }
            else
            {
                kept.Add(registration);
            }
        }

        return (kept, shadowed);
    }
}
