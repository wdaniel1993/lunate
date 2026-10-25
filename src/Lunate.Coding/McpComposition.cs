using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>
/// Session-start composition of the configured MCP servers across the five standard files: the
/// user files <c>~/.lunate/mcp.json</c> and <c>~/.agents/mcp.json</c>, then the worktree's
/// <c>.lunate/mcp.json</c>, <c>.agents/mcp.json</c> and root <c>.mcp.json</c>, each project file
/// trust-gated. Entries merge by server name with the later file winning; every shadowed
/// definition is logged naming the server and both files. File errors and expansion warnings
/// become notices through the mode's interaction; a failed server becomes a warning naming it.
/// Nothing here starts before the caller's first listing. A denied project file reports the
/// per-server questions it skipped through the optional callback, so an interactive caller can
/// keep its <c>n of m</c> count truthful.
/// </summary>
internal static class McpComposition
{
    /// <summary>The production config path: <c>~/.lunate/mcp.json</c>.</summary>
    internal static string DefaultConfigPath { get; } =
        Path.Combine(ExtensionHostOptions.Default.StorePath, "mcp.json");

    /// <summary>The standard user-scope sibling: <c>~/.agents/mcp.json</c>.</summary>
    internal static string DefaultAgentsConfigPath { get; } =
        Path.Combine(LunateHome.Profile, ".agents", "mcp.json");

    /// <summary>The project config path inside a worktree; mirroring <c>.lunate/extensions</c>.</summary>
    internal static string ProjectConfigPath(string worktreeRoot) =>
        Path.Combine(worktreeRoot, ".lunate", "mcp.json");

    /// <summary>The dotagents-convention project config: <c>.agents/mcp.json</c>.</summary>
    internal static string ProjectAgentsConfigPath(string worktreeRoot) =>
        Path.Combine(worktreeRoot, ".agents", "mcp.json");

    /// <summary>The Claude Code-convention project config: the repo-root <c>.mcp.json</c>.</summary>
    internal static string ProjectRootConfigPath(string worktreeRoot) =>
        Path.Combine(worktreeRoot, ".mcp.json");

    /// <summary>
    /// The three project files in precedence order (highest first): <c>.lunate/mcp.json</c> >
    /// <c>.agents/mcp.json</c> > root <c>.mcp.json</c>.
    /// </summary>
    internal static IReadOnlyList<string> ProjectConfigPaths(string worktreeRoot) =>
        [
            ProjectConfigPath(worktreeRoot),
            ProjectAgentsConfigPath(worktreeRoot),
            ProjectRootConfigPath(worktreeRoot),
        ];

    internal static async Task<McpToolSet?> ComposeAsync(
        Workspace workspace,
        string? configPath,
        string? agentsConfigPath,
        ExtensionSeams? seams,
        IMcpConfigTrustPrompt prompt,
        IMcpServerApprovalPrompt serverPrompt,
        IExtensionLog log,
        Action<UserNotice> notify,
        CancellationToken ct,
        Action<int>? skippedServerQuestions = null
    )
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(serverPrompt);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(notify);

        List<McpServerOptions> servers = [];
        Dictionary<string, int> index = new(StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<string>> missingVariables = new(StringComparer.Ordinal);

        // Merge lowest-precedence first so the highest-precedence definition wins (later wins):
        // ~/.agents/mcp.json, then ~/.lunate/mcp.json, then root .mcp.json, .agents/mcp.json and
        // .lunate/mcp.json — Lunate's own files keep winning and every project file over every
        // user file.
        Add(McpConfig.Load(agentsConfigPath ?? DefaultAgentsConfigPath));
        Add(McpConfig.Load(configPath ?? DefaultConfigPath));

        string identity = workspace.GitCommonDir ?? workspace.WorktreeRoot;
        string worktree = workspace.WorktreeRoot;
        var store = new ExtensionTrustStore(
            new ExtensionHostOptions
            {
                StorePath = seams?.StorePath ?? ExtensionHostOptions.Default.StorePath,
            }
        );

        // Project scope: each file its own trust decision, then each server its own approval.
        foreach (string path in ProjectConfigPaths(worktree).Reverse())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            // The file is parsed before the trust gate so the question can name its servers;
            // nothing starts and errors are reported only when the file is trusted.
            McpConfigResult parsed = McpConfig.Load(path);
            bool trusted = await new McpConfigTrust(store)
                .EnsureTrustedAsync(identity, path, parsed.RawServers, prompt, log, ct)
                .ConfigureAwait(false);
            if (!trusted)
            {
                if (skippedServerQuestions is { } skippedCallback)
                {
                    int skipped = parsed.RawServers.Count(raw =>
                        store.EvaluateMcpServer(identity, McpConfigTrustHash.ComputeDefinition(raw))
                            is not ExtensionTrustDecision.Trusted
                    );
                    if (skipped > 0)
                    {
                        skippedCallback(skipped);
                    }
                }

                continue;
            }

            ReportErrors(parsed.Errors, notify);
            MergeFile(parsed, await ApproveServersAsync(path, parsed).ConfigureAwait(false));
        }

        NotifyWarnings(servers, missingVariables, notify);

        return servers.Count == 0
            ? null
            : new McpToolSet(
                servers,
                log: log.Error,
                serverLog: log.Warn,
                missingVariables: missingVariables
            );

        void Add(McpConfigResult parsed)
        {
            ReportErrors(parsed.Errors, notify);
            MergeFile(parsed, parsed.Servers);
        }

        void MergeFile(McpConfigResult parsed, IReadOnlyList<McpServerOptions> chosen) =>
            MergeInto(
                servers,
                index,
                missingVariables,
                chosen,
                server => MissingVariablesOf(parsed, server),
                log.Warn
            );

        // One decision per unapproved project server, asked one at a time; an approved definition
        // is recorded for the repository, a refused one is skipped with the approval hint.
        async Task<IReadOnlyList<McpServerOptions>> ApproveServersAsync(
            string path,
            McpConfigResult parsed
        )
        {
            List<McpServerOptions> approved = [];
            for (var position = 0; position < parsed.RawServers.Count; position++)
            {
                McpServerOptions raw = parsed.RawServers[position];
                string definitionHash = McpConfigTrustHash.ComputeDefinition(raw);
                if (
                    store.EvaluateMcpServer(identity, definitionHash)
                    is not ExtensionTrustDecision.Trusted
                )
                {
                    if (!await serverPrompt.ApproveAsync(raw, ct).ConfigureAwait(false))
                    {
                        log.Warn(
                            $"server '{raw.Name}' from project MCP config '{path}' is not approved; it was not started. Run an interactive session or 'lunate trust' to approve it."
                        );
                        continue;
                    }

                    store.RecordMcpServerTrusted(
                        identity,
                        worktree,
                        definitionHash,
                        DateTimeOffset.UtcNow
                    );
                }

                approved.Add(parsed.Servers[position]);
            }

            return approved;
        }
    }

    /// <summary>The longest a failure reason may be inside a live-area notice.</summary>
    private const int MaxReasonLength = 100;

    /// <summary>
    /// One bounded single-line rendering of an MCP failure reason: whitespace runs (spaces, tabs,
    /// CR, LF) collapse to one space, and a result longer than <c>MaxReasonLength</c> characters is
    /// clipped with an ellipsis. Null, empty and whitespace-only reasons become the empty string so
    /// callers keep their own wording. A notice is one live-area line: the raw reason may be
    /// multi-line and arbitrarily long, which would break the frame and push later entries out of
    /// <c>/mcp</c> under clipping. The full reason stays in the session log.
    /// </summary>
    internal static string ShortReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        string collapsed = string.Join(
            ' ',
            reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        );
        return collapsed.Length > MaxReasonLength ? collapsed[..MaxReasonLength] + "…" : collapsed;
    }

    /// <summary>
    /// The one notice per MCP server whose process exited after starting; the status carries the
    /// pinned reason. The interactive session wires this to the live area.
    /// </summary>
    internal static UserNotice ServerExitedNotice(McpServerStatus status) =>
        new(
            NoticeLevel.Warning,
            $"MCP server '{status.Name}' failed: {ShortReason(status.Reason)}. The session continues without this server's tools."
        );

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
                        $"MCP server '{status.Name}' failed to start or list its tools: {ShortReason(status.Reason)} The session continues without this server's tools."
                    )
                );
            }
        }
    }

    /// <summary>
    /// Merges later-file entries by name: a same-named entry wins, its shadowed definition is
    /// logged naming the server and both source files, and the missing-variable map follows the
    /// winning definition (a shadowed server's warnings are dropped with it).
    /// </summary>
    internal static void MergeInto(
        List<McpServerOptions> servers,
        Dictionary<string, int> index,
        Dictionary<string, IReadOnlyList<string>> missingVariables,
        IReadOnlyList<McpServerOptions> incoming,
        Func<McpServerOptions, IReadOnlyList<string>> missingVariablesOf,
        Action<string> log
    )
    {
        foreach (McpServerOptions server in incoming)
        {
            if (index.TryGetValue(server.Name, out int existing))
            {
                log(
                    $"server '{server.Name}' from '{server.Source}' replaces the same name from '{servers[existing].Source}'"
                );
                servers[existing] = server;
            }
            else
            {
                index[server.Name] = servers.Count;
                servers.Add(server);
            }

            missingVariables[server.Name] = missingVariablesOf(server);
        }
    }

    private static IReadOnlyList<string> MissingVariablesOf(
        McpConfigResult parsed,
        McpServerOptions server
    ) =>
        [
            .. parsed
                .Warnings.Where(warning =>
                    string.Equals(warning.Server, server.Name, StringComparison.Ordinal)
                )
                .Select(warning => warning.Variable),
        ];

    private static void NotifyWarnings(
        IReadOnlyList<McpServerOptions> servers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> missingVariables,
        Action<UserNotice> notify
    )
    {
        foreach (McpServerOptions server in servers)
        {
            if (!missingVariables.TryGetValue(server.Name, out IReadOnlyList<string>? variables))
            {
                continue;
            }

            foreach (string variable in variables)
            {
                notify(
                    new UserNotice(
                        NoticeLevel.Warning,
                        new McpServerWarning(server.Name, variable).Message
                    )
                );
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
