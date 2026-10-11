using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The project <c>mcp.json</c> trust prompt: the sibling of <see cref="IExtensionTrustPrompt"/> for
/// repository-provided server configs. The question text names the config file.
/// </summary>
internal interface IMcpConfigTrustPrompt
{
    ValueTask<bool> ApproveAsync(string configPath, CancellationToken cancellationToken);
}

/// <summary>
/// The interactive trust prompt (line-based, mirroring the extension prompt): it asks on the mode's
/// diagnostics writer and only <c>y</c> or <c>yes</c> (case-insensitive) approves. The trust store
/// records an approval once per repository and content hash, so this is only reached when nothing
/// has approved the file.
/// </summary>
internal sealed class ConsoleMcpConfigTrustPrompt : IMcpConfigTrustPrompt
{
    private readonly TextWriter _errors;
    private readonly TextReader _input;

    internal ConsoleMcpConfigTrustPrompt(TextWriter errors, TextReader input)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(input);
        _errors = errors;
        _input = input;
    }

    public ValueTask<bool> ApproveAsync(string configPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        cancellationToken.ThrowIfCancellationRequested();
        _errors.WriteLine(
            $"lunate: project MCP config '{configPath}' runs commands from this repository. Start its servers? [y/N]"
        );
        string? answer = _input.ReadLine();
        return ValueTask.FromResult(Approves(answer));
    }

    private static bool Approves(string? answer)
    {
        string trimmed = answer?.Trim() ?? string.Empty;
        return trimmed.Equals("y", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The print- and ACP-mode trust prompt: repository-provided commands are never auto-approved off a
/// terminal. It refuses and the caller logs why, pointing at the interactive path that can approve.
/// </summary>
internal sealed class NonInteractiveMcpConfigTrustPrompt : IMcpConfigTrustPrompt
{
    public ValueTask<bool> ApproveAsync(string configPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
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
        IMcpConfigTrustPrompt prompt,
        IExtensionLog log,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(log);

        string contentHash = ExtensionTrustStore.ComputeFileContentHash(configPath);
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
                if (await prompt.ApproveAsync(configPath, ct).ConfigureAwait(false))
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
                    $"project MCP config '{configPath}' was not trusted; its servers were not started. Run an interactive session to approve it."
                );
                return false;
        }
    }
}
