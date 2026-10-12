using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>
/// Session-start composition of the configured MCP servers: the global <c>mcp.json</c> plus the
/// worktree's project <c>.lunate/mcp.json</c> when its trust gate allows it. Entries merge by server
/// name with the project winning. File and entry errors become warnings through the mode's
/// interaction; a failed server becomes a warning naming it. Nothing here starts before the
/// caller's first listing.
/// </summary>
internal static class McpComposition
{
    /// <summary>The production config path: <c>~/.lunate/mcp.json</c>.</summary>
    internal static string DefaultConfigPath { get; } =
        Path.Combine(ExtensionHostOptions.Default.StorePath, "mcp.json");

    /// <summary>The project config path inside a worktree; mirroring <c>.lunate/extensions</c>.</summary>
    internal static string ProjectConfigPath(string worktreeRoot) =>
        Path.Combine(worktreeRoot, ".lunate", "mcp.json");

    internal static async Task<McpToolSet?> ComposeAsync(
        Workspace workspace,
        string? configPath,
        ExtensionSeams? seams,
        IMcpConfigTrustPrompt prompt,
        IExtensionLog log,
        Action<UserNotice> notify,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(notify);

        McpConfigResult global = McpConfig.Load(configPath ?? DefaultConfigPath);
        ReportErrors(global.Errors, notify);
        List<McpServerOptions> servers = [.. global.Servers];

        string projectPath = ProjectConfigPath(workspace.WorktreeRoot);
        if (File.Exists(projectPath))
        {
            // The file is parsed before the trust gate so the question can name its servers;
            // nothing starts and errors are reported only when the file is trusted.
            McpConfigResult project = McpConfig.Load(projectPath);
            var store = new ExtensionTrustStore(
                new ExtensionHostOptions
                {
                    StorePath = seams?.StorePath ?? ExtensionHostOptions.Default.StorePath,
                }
            );
            bool trusted = await new McpConfigTrust(store)
                .EnsureTrustedAsync(
                    workspace.GitCommonDir ?? workspace.WorktreeRoot,
                    workspace.WorktreeRoot,
                    projectPath,
                    project.Servers,
                    prompt,
                    log,
                    ct
                )
                .ConfigureAwait(false);
            if (trusted)
            {
                ReportErrors(project.Errors, notify);
                Merge(servers, project.Servers);
            }
        }

        return servers.Count == 0
            ? null
            : new McpToolSet(servers, log: log.Error, serverLog: log.Warn);
    }

    /// <summary>Reports every failed server as a warning naming it; the session continues.</summary>
    internal static void NotifyFailures(McpToolSet set, Action<UserNotice> notify)
    {
        foreach (McpServerStatus status in set.Statuses)
        {
            if (status.State is McpServerState.Failed)
            {
                notify(
                    new UserNotice(
                        NoticeLevel.Warning,
                        $"MCP server '{status.Name}' failed to start or list its tools: {status.Reason} The session continues without this server's tools."
                    )
                );
            }
        }
    }

    /// <summary>Merges project entries into the global list by name; a same-named entry wins.</summary>
    internal static void Merge(
        List<McpServerOptions> global,
        IReadOnlyList<McpServerOptions> project
    )
    {
        Dictionary<string, int> index = new(StringComparer.Ordinal);
        for (var position = 0; position < global.Count; position++)
        {
            index[global[position].Name] = position;
        }

        foreach (McpServerOptions server in project)
        {
            if (index.TryGetValue(server.Name, out int existing))
            {
                global[existing] = server;
            }
            else
            {
                index[server.Name] = global.Count;
                global.Add(server);
            }
        }
    }

    private static void ReportErrors(IReadOnlyList<string> errors, Action<UserNotice> notify)
    {
        foreach (string error in errors)
        {
            notify(new UserNotice(NoticeLevel.Warning, error));
        }
    }
}
