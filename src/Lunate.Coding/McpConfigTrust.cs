using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>
/// The project <c>mcp.json</c> trust prompt: the sibling of <see cref="IExtensionTrustPrompt"/> for
/// repository-provided server configs. The question names the config file and its servers.
/// </summary>
internal interface IMcpConfigTrustPrompt
{
    ValueTask<bool> ApproveAsync(
        string configPath,
        IReadOnlyList<McpServerOptions> servers,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// The interactive mcp.json trust prompt: it builds the question (the path, one detail line per
/// server with its truncated command, and the count) and hands it to the session's live-area
/// question path. The trust store records an approval once per repository and file hash, so this is
/// only reached when nothing has approved the file.
/// </summary>
internal sealed class TuiMcpConfigTrustPrompt(TrustQuestion ask) : IMcpConfigTrustPrompt
{
    public ValueTask<bool> ApproveAsync(
        string configPath,
        IReadOnlyList<McpServerOptions> servers,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentNullException.ThrowIfNull(servers);
        cancellationToken.ThrowIfCancellationRequested();

        return ask(
            TrustFormat.McpQuestionTitle(configPath, servers.Count),
            TrustFormat.McpQuestionDetails(servers),
            cancellationToken
        );
    }
}

/// <summary>
/// The print- and ACP-mode trust prompt: repository-provided commands are never auto-approved off a
/// terminal. It refuses and the caller logs why, pointing at the interactive path that can approve.
/// </summary>
internal sealed class NonInteractiveMcpConfigTrustPrompt : IMcpConfigTrustPrompt
{
    public ValueTask<bool> ApproveAsync(
        string configPath,
        IReadOnlyList<McpServerOptions> servers,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentNullException.ThrowIfNull(servers);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}

/// <summary>
/// The project-file trust gate: the extension trust discipline (one-time per repository, re-prompted
/// when the content hash changes) over the distinct mcp.json record, so extension trust is neither
/// shared nor weakened.
/// </summary>
internal sealed class McpConfigTrust(ExtensionTrustStore store)
{
    internal async ValueTask<bool> EnsureTrustedAsync(
        string repositoryIdentity,
        string worktreePath,
        string configPath,
        IReadOnlyList<McpServerOptions> servers,
        IMcpConfigTrustPrompt prompt,
        IExtensionLog log,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(log);

        string contentHash = McpConfigTrustHash.ComputeFile(configPath);
        switch (store.EvaluateMcpConfig(repositoryIdentity, worktreePath, contentHash))
        {
            case ExtensionTrustDecision.Trusted:
                return true;
            case ExtensionTrustDecision.NewWorktree:
                store.RecordMcpConfigTrusted(
                    repositoryIdentity,
                    worktreePath,
                    contentHash,
                    DateTimeOffset.UtcNow
                );
                return true;
            default:
                if (await prompt.ApproveAsync(configPath, servers, ct).ConfigureAwait(false))
                {
                    store.RecordMcpConfigTrusted(
                        repositoryIdentity,
                        worktreePath,
                        contentHash,
                        DateTimeOffset.UtcNow
                    );
                    return true;
                }

                log.Warn(
                    $"project MCP config '{configPath}' was not trusted; its servers were not started. Run an interactive session or 'lunate trust' to approve it."
                );
                return false;
        }
    }
}
